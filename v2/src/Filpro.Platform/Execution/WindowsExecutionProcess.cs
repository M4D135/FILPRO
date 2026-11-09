using System.Diagnostics;
using System.Text;
using Filpro.Core;

namespace Filpro.Platform;

public sealed record WindowsExecutionOptions(string UsmtRoot, string StateDirectory,
    IReadOnlyDictionary<string, string> ToolSha256, Func<CancellationToken, Task> VerifyPackAsync);
public sealed record ExecutionProcessRequest(string Executable, IReadOnlyList<string> Arguments, string OutputDirectory, string Name,
    string? ProgressPath = null, Action<UsmtProgressSnapshot>? Progress = null);
public sealed record ExecutionProcessResult(int ExitCode, string StandardOutputPath, string StandardErrorPath, bool OutputTruncated, int? IgnoredErrors = null);
public interface IExecutionProcessRunner
{
    Task<ExecutionProcessResult> RunAsync(ExecutionProcessRequest request, CancellationToken cancellationToken);
}

/// <summary>Exécutables fermés du pack vérifié, arguments séparés, aucun shell ni résolution PATH.</summary>
public sealed class WindowsExecutionProcess(WindowsExecutionOptions options, ExecutionFileSystem fileSystem) : IExecutionProcessRunner
{
    private static readonly string[] Allowed = ["scanstate.exe", "loadstate.exe", "usmtutils.exe"];
    public async Task<ExecutionProcessResult> RunAsync(ExecutionProcessRequest request, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) throw new FilproOperationException("platform.windows", "L'exécution USMT nécessite Windows.");
        cancellationToken.ThrowIfCancellationRequested();
        if (!Allowed.Contains(request.Executable, StringComparer.Ordinal) || request.Arguments.Count > 1000
            || request.Arguments.Any(a => a is null || a.Contains('\0') || a.Length > 29000)
            || request.Arguments.Sum(a => (long)a.Length + 3) > 30000)
            throw new FilproOperationException("execution.command", "Invocation native hors du contrat fermé.");
        StoragePathPolicy.RequireSegment(request.Name);
        var root = StoragePathPolicy.NormalizeWindows(options.UsmtRoot);
        using var directoryLease = fileSystem.HoldDirectory(root);
        await options.VerifyPackAsync(cancellationToken).ConfigureAwait(false);
        if (!options.ToolSha256.TryGetValue(request.Executable, out var hash) || string.IsNullOrWhiteSpace(hash))
            throw new FilproOperationException("pack.integrity", "Empreinte de l'exécutable USMT absente du pack vérifié.");
        var executable = Path.Combine(root, request.Executable);
        var expected = await fileSystem.DescribeAsync(root, request.Executable, cancellationToken).ConfigureAwait(false);
        if (!expected.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new FilproOperationException("pack.integrity", "Exécutable USMT modifié depuis la validation du pack.");
        // Ce handle interdit modification/suppression entre le dernier hash et le lancement du processus.
        using var executableLease = StorageFileIdentity.OpenRead(executable);
        var lockedHash = Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(executableLease, cancellationToken).ConfigureAwait(false));
        if (!lockedHash.Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new FilproOperationException("pack.integrity", "Exécutable USMT substitué avant lancement.");
        using var outputLease = fileSystem.HoldDirectory(request.OutputDirectory);
        var stdout = Path.Combine(request.OutputDirectory, request.Name + "-stdout.txt");
        var stderr = Path.Combine(request.OutputDirectory, request.Name + "-stderr.txt");
        await using var output = new FileStream(stdout, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous);
        await using var error = new FileStream(stderr, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 65536, FileOptions.Asynchronous);
        var start = new ProcessStartInfo
        {
            FileName = executable, WorkingDirectory = root, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in request.Arguments) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        cancellationToken.ThrowIfCancellationRequested();
        if (!process.Start()) throw new FilproOperationException("execution.process", "Le processus USMT n'a pas démarré.");
        var outTask = DrainAsync(process.StandardOutput.BaseStream, output);
        var errTask = DrainAsync(process.StandardError.BaseStream, error);
        int? ignoredErrors = null;
        try
        {
            var completed = process.WaitForExitAsync(cancellationToken);
            while (!completed.IsCompleted)
            {
                ObserveProgress();
                await Task.WhenAny(completed, Task.Delay(500, cancellationToken)).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            await completed.ConfigureAwait(false);
            ObserveProgress();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // USMT doit terminer l'étape native pour décharger ses ruches utilisateur.
            // L'arrêt reste demandé, sans état terminal prématuré ; l'attente peut être longue.
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            if (!process.HasExited)
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(outTask, errTask).ConfigureAwait(false);
            await output.FlushAsync(CancellationToken.None).ConfigureAwait(false);
            await error.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new(process.ExitCode, stdout, stderr, outTask.Result || errTask.Result, ignoredErrors);

        void ObserveProgress()
        {
            if (request.ProgressPath is null || request.Progress is null) return;
            if (!StoragePathPolicy.IsWithin(request.ProgressPath, request.OutputDirectory, false))
                throw new FilproOperationException("execution.progress", "Journal de progression hors du dossier de l'opération.");
            try
            {
                fileSystem.RequireFile(request.ProgressPath);
                using var stream = new FileStream(request.ProgressPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                StorageFileIdentity.RequireSingleLink(stream);
                var length = stream.Length;
                var offset = Math.Max(0, length - 65536); stream.Position = offset;
                var buffer = new byte[(int)(length - offset)]; stream.ReadExactly(buffer);
                var text = DecodeProgress(buffer);
                if (offset > 0 && text.IndexOf('\n') is var newline && newline >= 0) text = text[(newline + 1)..];
                var progress = UsmtProgressReader.Parse(text);
                if (progress.IgnoredErrors.HasValue) ignoredErrors = Math.Max(ignoredErrors ?? 0, progress.IgnoredErrors.Value);
                request.Progress(progress);
            }
            catch (IOException) { /* Un journal natif ouvert exclusivement signifie mesure inconnue, pas réussite. */ }
        }
    }

    private static string DecodeProgress(byte[] bytes)
    {
        if (bytes.Length >= 2 && (bytes[0] == 0xff && bytes[1] == 0xfe || bytes[1] == 0 && bytes[0] != 0))
            return Encoding.Unicode.GetString(bytes).TrimStart('\uFEFF');
        if (bytes.Length >= 2 && bytes[0] == 0xfe && bytes[1] == 0xff)
            return Encoding.BigEndianUnicode.GetString(bytes).TrimStart('\uFEFF');
        return Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF');
    }

    public static IEnumerable<string> ReadCatalogOutput(string path)
    {
        using var stream = StorageFileIdentity.OpenRead(path);
        StorageFileIdentity.RequireSingleLink(stream);
        var prefix = new byte[4]; var count = stream.Read(prefix); stream.Position = 0;
        Encoding encoding = count >= 2 && prefix[1] == 0 && prefix[0] != 0
            ? new UnicodeEncoding(false, false, true) : new UTF8Encoding(false, true);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        string? line;
        while ((line = reader.ReadLine()) is not null) yield return line;
    }

    private static async Task<bool> DrainAsync(Stream source, Stream destination)
    {
        const long limit = 64L * 1024 * 1024;
        long total = 0;
        var failed = false;
        var buffer = new byte[32768];
        int read;
        while ((read = await source.ReadAsync(buffer, CancellationToken.None).ConfigureAwait(false)) != 0)
        {
            if (!failed && total < limit)
            {
                try { await destination.WriteAsync(buffer.AsMemory(0, (int)Math.Min(read, limit - total)), CancellationToken.None).ConfigureAwait(false); }
                catch (IOException) { failed = true; } // Continuer à drainer même si le support des journaux est plein.
            }
            total += read; // Toujours drainer : arrêter la lecture peut bloquer USMT.
        }
        return failed || total > limit;
    }
}
