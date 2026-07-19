namespace CycloneGames.AssetManagement.Runtime.Trust
{
    public enum ContentTrustFailure : byte
    {
        None = 0,
        InvalidManifest = 1,
        InvalidEntry = 2,
        SignatureRejected = 3,
        PathEscapesRoot = 4,
        MissingFile = 5,
        SizeMismatch = 6,
        UnsupportedHashAlgorithm = 7,
        InvalidExpectedHash = 8,
        HashComputationFailed = 9,
        HashMismatch = 10,
        IoError = 11,
        SignatureRequired = 12,
        HashAlgorithmRejected = 13,
    }
}
