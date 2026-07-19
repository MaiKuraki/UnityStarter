namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>Terminal decision for one authority-owned activation attempt.</summary>
    public enum GASAuthorityActivationStatus : byte
    {
        Invalid = 0,
        Activated = 1,
        MissingOrStaleGrant = 2,
        WrongExecutionPolicy = 3,
        AbilityRejected = 4,
        RuntimeUnavailable = 5
    }

    /// <summary>Allocation-free result returned by the authoritative Runtime execution boundary.</summary>
    public readonly struct GASAuthorityActivationResult
    {
        public readonly GASAuthorityActivationStatus Status;
        public readonly ulong AuthoritativeStateVersion;

        public GASAuthorityActivationResult(
            GASAuthorityActivationStatus status,
            ulong authoritativeStateVersion)
        {
            Status = status;
            AuthoritativeStateVersion = authoritativeStateVersion;
        }

        public bool Activated => Status == GASAuthorityActivationStatus.Activated;
    }
}
