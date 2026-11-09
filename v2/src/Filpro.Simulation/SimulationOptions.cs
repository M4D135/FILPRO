namespace Filpro.Simulation;

/// <summary>Réglages du monde fictif. Déterministe pour une même graine : deux lances donnent le même Accueil.</summary>
public sealed record SimulationOptions(int Seed = 20261023, int ProfileCount = 6, int BackupCount = 5, int TaskCount = 4, int JournalCount = 8)
{
    public static SimulationOptions Default { get; } = new();

    public SimulationOptions Validate()
    {
        if (ProfileCount is < 0 or > 200 || BackupCount is < 0 or > 200 || TaskCount is < 0 or > 200 || JournalCount is < 0 or > 200)
        {
            throw new ArgumentOutOfRangeException(nameof(Seed), "les compteurs du monde fictif doivent être entre 0 et 200");
        }
        return this;
    }
}
