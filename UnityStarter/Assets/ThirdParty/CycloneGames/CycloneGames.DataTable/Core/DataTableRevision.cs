using System;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Immutable content identity used for anti-replay publication. Sequence is strictly
    /// monotonic within a store; Id is a stable non-empty revision identifier or content hash.
    /// </summary>
    public readonly struct DataTableRevision : IEquatable<DataTableRevision>
    {
        public const int MaxIdLength = 256;

        internal static readonly DataTableRevision None = new DataTableRevision(
            sequence: 0,
            id: string.Empty,
            skipValidation: true);

        public DataTableRevision(long sequence, string id)
            : this(sequence, id, skipValidation: false)
        {
        }

        private DataTableRevision(long sequence, string id, bool skipValidation)
        {
            if (!skipValidation)
            {
                if (sequence <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(sequence),
                        sequence,
                        "A publishable data-table revision sequence must be greater than zero.");
                }

                ValidateId(id);
            }

            Sequence = sequence;
            Id = id;
        }

        public long Sequence { get; }

        public string Id { get; }

        public bool IsPublishable => Sequence > 0 && !string.IsNullOrEmpty(Id);

        public bool Equals(DataTableRevision other)
        {
            return Sequence == other.Sequence && string.Equals(Id, other.Id, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is DataTableRevision other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Sequence.GetHashCode() * 397) ^ (Id == null ? 0 : StringComparer.Ordinal.GetHashCode(Id));
            }
        }

        public override string ToString()
        {
            return IsPublishable ? $"{Sequence}:{Id}" : "None";
        }

        public static bool operator ==(DataTableRevision left, DataTableRevision right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(DataTableRevision left, DataTableRevision right)
        {
            return !left.Equals(right);
        }

        private static void ValidateId(string id)
        {
            if (id == null)
            {
                throw new ArgumentNullException(nameof(id));
            }

            if (id.Length == 0 || string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("A data-table revision Id must not be empty or whitespace.", nameof(id));
            }

            if (id.Length > MaxIdLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(id),
                    id.Length,
                    $"A data-table revision Id cannot exceed {MaxIdLength} UTF-16 code units.");
            }

            if (char.IsWhiteSpace(id[0]) || char.IsWhiteSpace(id[id.Length - 1]))
            {
                throw new ArgumentException(
                    "A data-table revision Id must not have leading or trailing whitespace.",
                    nameof(id));
            }

            for (int i = 0; i < id.Length; i++)
            {
                char value = id[i];
                if (char.IsControl(value) || char.IsSurrogate(value))
                {
                    throw new ArgumentException(
                        "A data-table revision Id must not contain control characters or surrogate code units.",
                        nameof(id));
                }
            }
        }
    }
}
