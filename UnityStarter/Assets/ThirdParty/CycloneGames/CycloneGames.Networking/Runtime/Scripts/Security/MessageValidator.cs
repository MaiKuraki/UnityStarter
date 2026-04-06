using System.Runtime.CompilerServices;

namespace CycloneGames.Networking.Security
{
    /// <summary>
    /// Validates incoming message payloads before deserialization.
    /// Prevents buffer overflow attacks, oversized messages, and malformed packets.
    /// 
    /// NOTE: This performs structural validation only (size, message ID range).
    /// It does NOT provide cryptographic integrity verification (HMAC/signatures).
    /// For authentication and tamper protection, use transport-level encryption (TLS/DTLS)
    /// or add application-level HMAC verification on top of this validator.
    /// </summary>
    public sealed class MessageValidator
    {
        public int MaxPayloadSize { get; set; }
        public int MinPayloadSize { get; set; }
        public ushort MaxMessageId { get; set; }

        public MessageValidator(
            int maxPayloadSize = NetworkConstants.MaxMTU,
            int minPayloadSize = 2,
            ushort maxMessageId = NetworkConstants.MaxMessageId)
        {
            MaxPayloadSize = maxPayloadSize;
            MinPayloadSize = minPayloadSize;
            MaxMessageId = maxMessageId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ValidationResult Validate(ushort messageId, int payloadSize)
        {
            if (messageId > MaxMessageId)
                return ValidationResult.InvalidMessageId;

            if (payloadSize < MinPayloadSize)
                return ValidationResult.PayloadTooSmall;

            if (payloadSize > MaxPayloadSize)
                return ValidationResult.PayloadTooLarge;

            return ValidationResult.Valid;
        }

        /// <summary>
        /// Validate raw buffer integrity. Checks for minimum header presence.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ValidateBuffer(byte[] buffer, int offset, int length)
        {
            if (buffer == null) return false;
            if (offset < 0 || length <= 0) return false;
            if (offset + length > buffer.Length) return false;
            return length >= MinPayloadSize && length <= MaxPayloadSize;
        }
    }

    public enum ValidationResult : byte
    {
        Valid,
        InvalidMessageId,
        PayloadTooSmall,
        PayloadTooLarge,
        MalformedHeader
    }
}
