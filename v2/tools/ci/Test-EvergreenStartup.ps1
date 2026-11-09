#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory)][string] $Application, [Parameter(Mandatory)][string] $Report)
$ErrorActionPreference = 'Stop'
$exe = (Get-Item -LiteralPath $Application).FullName
$dir = Split-Path -Parent $exe
if (Test-Path -LiteralPath $Report) { throw 'Le rapport de démarrage existe déjà.' }
$result = [ordered]@{
    status = 'Failed'; reason = $null; sourceCommit = $env:GITHUB_SHA; startedUtc = [DateTime]::UtcNow.ToString('o')
    exe = $exe; exeSha256 = (Get-FileHash -LiteralPath $exe).Hash.ToLowerInvariant(); hostPid = $null
    interactive = [Environment]::UserInteractive; sdkInitialized = $false; browserPid = $null; browserExe = $null
    pageText = $null; testProcessStopped = $null; forcedClose = $false
}
$process = $null
try {
    if ((Test-Path -LiteralPath (Join-Path $dir 'wad/pack-manifest.json')) -or
        (Test-Path -LiteralPath (Join-Path $dir 'wad/runtime/webview2/msedgewebview2.exe'))) { throw 'Le contrôle exige le build source, sans manifeste ni moteur embarqué.' }
    $approved = foreach ($view in @('Registry32', 'Registry64')) {
        $machine = [Microsoft.Win32.RegistryKey]::OpenBaseKey('LocalMachine', $view)
        try {
            $key = $machine.OpenSubKey('SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}')
            if ($null -eq $key) { continue }
            try { $version = [string]$key.GetValue('pv') } finally { $key.Dispose() }
            if ($version -notmatch '^[1-9][0-9]*\.\d+\.\d+\.\d+$') { continue }
            foreach ($root in @('ProgramFilesX86', 'ProgramFiles')) {
                $path = Join-Path ([Environment]::GetFolderPath($root)) "Microsoft/EdgeWebView/Application/$version/msedgewebview2.exe"
                if (Test-Path -LiteralPath $path -PathType Leaf) { [IO.Path]::GetFullPath($path) }
            }
        } finally { $machine.Dispose() }
    }
    if (-not $approved) { $result.status = 'Unavailable'; $result.reason = 'Aucun Evergreen machine installé détecté sur le runner.'; return }
    if ([Security.Principal.WindowsIdentity]::GetCurrent().IsSystem) { $result.status = 'Unavailable'; $result.reason = 'WebView2 ne prend pas en charge le compte SYSTEM.'; return }

    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    $type = [Windows.Automation.AutomationElement]::ControlTypeProperty
    $document = [Windows.Automation.PropertyCondition]::new($type, [Windows.Automation.ControlType]::Document)
    # En démonstration, la configuration du pack est proposée au démarrage dans une fenêtre modale qui rend la coque
    # inerte : son sous-titre prouve la même page tant qu'elle est ouverte, le bandeau ensuite.
    $expected = @('Démonstration — données fictives.', 'Démonstration — aucun fichier réel modifié')
    $text = [Windows.Automation.OrCondition]::new([Windows.Automation.Condition[]]@($expected | ForEach-Object {
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $_) }))
    $log = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'FILPRO/logs/filpro.log'
    $previous = if ([IO.File]::Exists($log)) { (Get-Item -LiteralPath $log).Length } else { 0 }
    $process = Start-Process -FilePath $exe -WorkingDirectory $dir -ArgumentList '--simulation' -PassThru
    $result.hostPid = $process.Id
    $pattern = 'WebView2 initialisé \(FILPRO ' + $process.Id + ', navigateur ([0-9]+)\)\.'
    $scope = [Windows.Automation.TreeScope]::Descendants
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt 75) {
        if ($process.HasExited) { throw "FILPRO a quitté avant la vérification (code $($process.ExitCode))." }
        if (-not $result.sdkInitialized -and [IO.File]::Exists($log)) {
            $stream = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
            try {
                $stream.Position = $previous
                $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8)
                try { $fresh = $reader.ReadToEnd() } finally { $reader.Dispose() }
            } finally { $stream.Dispose() }
            if ($fresh -match $pattern) {
                $browserId = [int]$Matches[1]
                $browser = Get-CimInstance Win32_Process -Filter "ProcessId=$browserId"
                if (-not $browser -or $approved -notcontains $browser.ExecutablePath) { throw 'Le navigateur du SDK ne correspond pas à un Evergreen machine installé.' }
                $result.sdkInitialized = $true; $result.browserPid = $browserId; $result.browserExe = $browser.ExecutablePath
            }
        }
        $process.Refresh()
        if ($result.sdkInitialized -and $process.MainWindowHandle -ne [IntPtr]::Zero) {
            try {
                $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
                if ($window.Current.ProcessId -ne $process.Id) { throw 'Fenêtre étrangère.' }
                $page = $window.FindFirst($scope, $document)
                if ($page -and ($label = $page.FindFirst($scope, $text)) -and $label.Current.ControlType -eq [Windows.Automation.ControlType]::Text -and -not $label.Current.IsOffscreen) {
                    $result.pageText = $label.Current.Name; $result.status = 'PageObserved'; return
                }
            } catch [Windows.Automation.ElementNotAvailableException] { } # fenêtre/page encore en construction
        }
        Start-Sleep -Milliseconds 500
    }
    if (-not $result.interactive -and $result.sdkInitialized) {
        $result.status = 'SdkOnly'; $result.reason = 'SDK et exécutable confirmés. Texte inaccessible dans la session non interactive du runner.'; return
    }
    # Constat joint à l'échec, sans effet sur le verdict : fenêtre, document, libellé, textes visibles et capture.
    $diagnostic = [ordered]@{ window = $null; document = $false; label = $null; texts = @(); screenshot = $null; error = $null }
    try {
        $process.Refresh()
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
            $window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
            $diagnostic.window = [string]$window.Current.BoundingRectangle
            $page = $window.FindFirst($scope, $document)
            $diagnostic.document = $null -ne $page
            $label = if ($page) { $page.FindFirst($scope, $text) }
            if ($label) { $diagnostic.label = [ordered]@{ controlType = $label.Current.ControlType.ProgrammaticName; offscreen = $label.Current.IsOffscreen; bounds = [string]$label.Current.BoundingRectangle } }
            $textType = [Windows.Automation.PropertyCondition]::new($type, [Windows.Automation.ControlType]::Text)
            $diagnostic.texts = @(if ($page) { $page.FindAll($scope, $textType) | Select-Object -First 25 | ForEach-Object { $_.Current.Name } })
        }
        Add-Type -AssemblyName System.Drawing, System.Windows.Forms
        $screen = [Windows.Forms.SystemInformation]::VirtualScreen
        $bitmap = [Drawing.Bitmap]::new($screen.Width, $screen.Height)
        try {
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try { $graphics.CopyFromScreen($screen.Location, [Drawing.Point]::Empty, $screen.Size) } finally { $graphics.Dispose() }
            $diagnostic.screenshot = [IO.Path]::ChangeExtension($Report, '.png')
            $bitmap.Save($diagnostic.screenshot, [Drawing.Imaging.ImageFormat]::Png)
        } finally { $bitmap.Dispose() }
    } catch { $diagnostic.error = $_.Exception.Message }
    $result.diagnostic = $diagnostic
    throw 'Le texte de la page de démonstration et le navigateur attendu n''ont pas été observés ensemble dans le délai.'
} catch {
    $result.reason = $_.Exception.Message
    throw
} finally {
    try {
        if ($null -ne $process) {
            if (-not $process.HasExited) { $null = $process.CloseMainWindow() }
            if (-not $process.WaitForExit(10000)) { $result.forcedClose = $true; $process.Kill(); $process.WaitForExit() }
            $result.testProcessStopped = $process.HasExited; $process.Dispose()
        }
    } catch { $result.status = 'Failed'; $result.reason = 'Fermeture du processus de test impossible : ' + $_.Exception.Message; throw }
    finally {
        $result.finishedUtc = [DateTime]::UtcNow.ToString('o')
        [IO.File]::WriteAllText($Report, ($result | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
        Write-Host "Evergreen : $($result.status) — $($result.reason)"
        if ($result.status -in @('Unavailable', 'SdkOnly')) { Write-Warning $result.reason }
    }
}
