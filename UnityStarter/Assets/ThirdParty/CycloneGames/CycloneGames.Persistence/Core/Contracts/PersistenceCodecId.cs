using System;

namespace CycloneGames.Persistence
{
    /// <summary>
    /// Stable, versioned ASCII identifier stored in Persistence Record V1.
    /// </summary>
    public readonly struct PersistenceCodecId : IEquatable<PersistenceCodecId>
    {
        public const int MaximumLength = 64;

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

        internal static void Validate(string value, string parameterName)
        {
            if (string.IsNullOrEmpty(value) || value.Length > MaximumLength)
            {
                throw new ArgumentException(
                    $"A codec identifier must contain 1 to {MaximumLength} ASCII characters.",
                    parameterName);
            }

            if (!IsAlphaNumeric(value[0]) || !IsAlphaNumeric(value[value.Length - 1]))
            {
                throw new ArgumentException(
                    "A codec identifier must begin and end with a lowercase ASCII letter or digit.",
                    parameterName);
            }

            bool hasVersionSeparator = false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character == '/')
                {
                    if (hasVersionSeparator)
                    {
                        throw new ArgumentException(
                            "A codec identifier must contain exactly one version separator.",
                            parameterName);
                    }

                    hasVersionSeparator = true;
                    continue;
                }

                if (!IsAlphaNumeric(character)
                    && character != '-'
                    && character != '_'
                    && character != '.')
                {
                    throw new ArgumentException(
                        "A codec identifier contains an unsupported character.",
                        parameterName);
                }
            }

            if (!hasVersionSeparator)
            {
                throw new ArgumentException(
                    "A codec identifier must include a version separator, for example 'yaml/1'.",
                    parameterName);
            }
        }

        private static bool IsAlphaNumeric(char character)
        {
            return (character >= 'a' && character <= 'z')
                || (character >= '0' && character <= '9');
        }
    }
}
