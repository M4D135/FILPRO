namespace Filpro.Core;

/// <summary>Réserve les démarrages jusqu'à remise au coordinateur et exclut une remise au worker de mise à jour.</summary>
public sealed class ApplicationOperationGate
{
    private readonly object gate = new();
    private int starting;
    private bool updating;
    private bool closing;
    public bool UpdateReserved { get { lock (gate) return updating; } }

    public IDisposable EnterNativeStart()
    {
        lock (gate)
        {
            if (closing) throw new FilproOperationException("session.closing", "La session se ferme.");
            if (updating) throw new FilproOperationException("update.busy", "Une installation est en cours de confirmation ; aucun nouveau travail sur les profils.");
            starting++;
            return new Lease(() => { lock (gate) starting--; });
        }
    }
    public IDisposable EnterUpdateHandoff(Func<bool> applicationIsIdle)
    {
        ArgumentNullException.ThrowIfNull(applicationIsIdle);
        lock (gate)
        {
            if (closing || updating || starting != 0 || !applicationIsIdle())
                throw new FilproOperationException("update.busy", "Attendre la fin des opérations et de leurs démarrages avant de remplacer l’application.");
            updating = true;
            return new Lease(() => { lock (gate) updating = false; });
        }
    }
    public void Close() { lock (gate) closing = true; }
    private sealed class Lease(Action release) : IDisposable
    {
        private Action? pending = release;
        public void Dispose() => Interlocked.Exchange(ref pending, null)?.Invoke();
    }
}
