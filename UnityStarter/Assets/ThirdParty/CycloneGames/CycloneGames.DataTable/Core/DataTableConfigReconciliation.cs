namespace CycloneGames.DataTable
{
    /// <summary>
    /// Result of comparing a local configuration identity with a peer's identity.
    /// </summary>
    public enum DataTableConfigReconciliationStatus
    {
        /// <summary>Fingerprints match; both ends declare identical configuration content.</summary>
        Same = 0,

        /// <summary>Local revision is newer; the peer should resynchronize from a newer source.</summary>
        PeerStale = 1,

        /// <summary>Peer revision is newer; the local end should resynchronize before proceeding.</summary>
        PeerAhead = 2,

        /// <summary>
        /// Revisions are equal but content differs. This is a deterministic rejection: the two
        /// ends can never agree, so the product must refuse the session rather than resync.
        /// </summary>
        Mismatch = 3,
    }

    /// <summary>
    /// Pure classification of two configuration identities (fingerprint + revision). Transport,
    /// handshake timing, and the response to each status are product decisions; this type only
    /// guarantees that every endpoint classifies the same pair of identities identically.
    /// </summary>
    public static class DataTableConfigReconciliation
    {
        /// <summary>
        /// Classifies a peer relative to the local end. Equal fingerprints always win over
        /// revision order: republishing identical content under a newer sequence is not a
        /// divergence. Differing fingerprints with equal sequences are a
        /// <see cref="DataTableConfigReconciliationStatus.Mismatch"/>; otherwise the higher
        /// sequence is treated as the newer side. Whether a higher sequence is trusted is a
        /// product-layer trust decision (rollback protection), outside this classification.
        /// </summary>
        public static DataTableConfigReconciliationStatus Classify(
            DataTableConfigFingerprint localFingerprint,
            DataTableRevision localRevision,
            DataTableConfigFingerprint peerFingerprint,
            DataTableRevision peerRevision)
        {
            if (localFingerprint.Equals(peerFingerprint))
            {
                return DataTableConfigReconciliationStatus.Same;
            }

            if (localRevision.Sequence == peerRevision.Sequence)
            {
                return DataTableConfigReconciliationStatus.Mismatch;
            }

            return localRevision.Sequence > peerRevision.Sequence
                ? DataTableConfigReconciliationStatus.PeerStale
                : DataTableConfigReconciliationStatus.PeerAhead;
        }
    }
}
