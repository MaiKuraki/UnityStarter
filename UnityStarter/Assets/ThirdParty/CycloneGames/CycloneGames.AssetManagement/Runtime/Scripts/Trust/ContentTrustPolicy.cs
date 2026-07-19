namespace CycloneGames.AssetManagement.Runtime.Trust
{
    /// <summary>
    /// Defines the trust boundary applied by a content verifier.
    /// Both policies require SHA-256 for every manifest entry.
    /// </summary>
    public enum ContentTrustPolicy : byte
    {
        /// <summary>
        /// Requires a manifest signature verifier and a valid signature before file verification.
        /// </summary>
        RequireSignature = 0,

        /// <summary>
        /// Verifies SHA-256 file integrity without requiring a manifest signature.
        /// This policy must be selected explicitly.
        /// </summary>
        IntegrityOnly = 1,
    }
}
