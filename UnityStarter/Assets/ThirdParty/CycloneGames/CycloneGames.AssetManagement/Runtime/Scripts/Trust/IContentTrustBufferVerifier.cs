using System;

namespace CycloneGames.AssetManagement.Runtime.Trust
{
    /// <summary>
    /// Optional verifier boundary for downloaded or pooled memory buffers that should not be copied into a new byte array.
    /// </summary>
    public interface IContentTrustBufferVerifier : IContentTrustVerifier
    {
        ContentTrustVerificationResult VerifyBytes(ReadOnlySpan<byte> bytes, in ContentTrustFileEntry entry);
    }
}
