using System;

namespace CycloneGames.Persistence
{
    /// <summary>
    /// One registered entry: a format token and a caller-supplied value.
    /// </summary>
    public readonly struct PersistenceFormatRegistration<TEntry>
    {
        public PersistenceFormatRegistration(PersistenceCodecId format, TEntry entry)
        {
            if (string.IsNullOrEmpty(format.Value))
            {
                throw new ArgumentException(
                    "A format registration requires a valid format token.",
                    nameof(format));
            }

            Format = format;
            Entry = entry;
        }

        public PersistenceCodecId Format { get; }

        public TEntry Entry { get; }
    }

    /// <summary>
    /// Exact-match lookup from a format token to a registered entry. Reads no files and writes
    /// no records.
    /// </summary>
    public sealed class PersistenceFormatRegistry<TEntry>
    {
        private readonly byte[][] _tokens;
        private readonly TEntry[] _entries;

        public PersistenceFormatRegistry(
            params PersistenceFormatRegistration<TEntry>[] registrations)
        {
            if (registrations == null)
            {
                throw new ArgumentNullException(nameof(registrations));
            }

            _tokens = new byte[registrations.Length][];
            _entries = new TEntry[registrations.Length];
            for (int index = 0; index < registrations.Length; index++)
            {
                PersistenceFormatRegistration<TEntry> registration = registrations[index];
                byte[] token = Ascii(registration.Format.Value);
                for (int previous = 0; previous < index; previous++)
                {
                    if (TokensEqual(_tokens[previous], token))
                    {
                        throw new ArgumentException(
                            "A format registry cannot contain two entries for one token.",
                            nameof(registrations));
                    }
                }

                _tokens[index] = token;
                _entries[index] = registration.Entry;
            }
        }

        public int Count => _entries.Length;

        /// <summary>
        /// Looks up a token read by <see cref="PersistenceRecordHeader.TryReadFormatToken"/>.
        /// The comparison is allocation-free over a contiguous array.
        /// </summary>
        public bool TryGet(ReadOnlySpan<byte> token, out TEntry entry)
        {
            for (int index = 0; index < _tokens.Length; index++)
            {
                if (token.SequenceEqual(_tokens[index]))
                {
                    entry = _entries[index];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        public bool TryGet(PersistenceCodecId format, out TEntry entry)
        {
            string value = format.Value;
            for (int index = 0; index < _tokens.Length; index++)
            {
                if (TokensEqual(_tokens[index], value))
                {
                    entry = _entries[index];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        private static bool TokensEqual(byte[] token, ReadOnlySpan<byte> other)
        {
            return token.AsSpan().SequenceEqual(other);
        }

        private static bool TokensEqual(byte[] token, string value)
        {
            if (value == null || token.Length != value.Length)
            {
                return false;
            }

            for (int index = 0; index < token.Length; index++)
            {
                if (token[index] != (byte)value[index])
                {
                    return false;
                }
            }

            return true;
        }

        private static byte[] Ascii(string value)
        {
            var bytes = new byte[value.Length];
            for (int index = 0; index < value.Length; index++)
            {
                bytes[index] = (byte)value[index];
            }

            return bytes;
        }
    }
}
