using System;

namespace CycloneGames.Persistence
{
    /// <summary>
    /// Wire format identity stored in a persistence record, compared by ordinal equality.
    /// </summary>
    public readonly struct PersistenceCodecId : IEquatable<PersistenceCodecId>
    {
        internal const int MaximumTokenLength = 64;

        private readonly string _value;

        public PersistenceCodecId(string value)
        {
            Validate(value, nameof(value));
            _value = value;
        }

        public string Value => _value ?? string.Empty;

        public bool Equals(PersistenceCodecId other)
        {
            return string.Equals(_value, other._value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is PersistenceCodecId other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _value == null ? 0 : StringComparer.Ordinal.GetHashCode(_value);
        }

        public override string ToString()
        {
            return Value;
        }

        public static bool operator ==(PersistenceCodecId left, PersistenceCodecId right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(PersistenceCodecId left, PersistenceCodecId right)
        {
            return !left.Equals(right);
        }

        // Mirrored by PersistenceRecord.IsValidIdentifier. Both define the same grammar so a
        // token accepted here can always be parsed back from a record.
        internal static void Validate(string value, string parameterName)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaximumTokenLength)
            {
                throw new ArgumentException(
                    $"A format token must contain 1 to {MaximumTokenLength} ASCII characters.",
                    parameterName);
            }

            if (!IsAlphaNumeric(value[0]) || !IsAlphaNumeric(value[value.Length - 1]))
            {
                throw new ArgumentException(
                    "A format token must begin and end with a lowercase ASCII letter or digit.",
                    parameterName);
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!IsAlphaNumeric(character)
                    && character != '-'
                    && character != '_'
                    && character != '.')
                {
                    throw new ArgumentException(
                        "A format token contains an unsupported character.",
                        parameterName);
                }
            }
        }

        private static bool IsAlphaNumeric(char character)
        {
            return (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9');
        }
    }
}
