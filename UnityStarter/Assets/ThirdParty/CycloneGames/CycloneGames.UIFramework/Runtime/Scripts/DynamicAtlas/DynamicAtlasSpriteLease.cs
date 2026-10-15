using System;
using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Non-allocating value-type handle to an atlas sprite reference. The handle wraps a
    /// pooled record and only releases its entry while the record still carries the rental
    /// id captured by the handle, so stale copies and double disposal are no-ops.
    /// </summary>
    public readonly struct DynamicAtlasSpriteLease : IDisposable
    {
        internal sealed class LeaseRecord
        {
            internal DynamicAtlasService Owner;
            internal string Key;
            internal long EntryGeneration;
            internal Sprite Sprite;
            internal long RentalId;

            internal void Reset()
            {
                Owner = null;
                Key = null;
                EntryGeneration = 0;
                Sprite = null;
                RentalId = 0;
            }
        }

        private readonly LeaseRecord _record;
        private readonly long _rentalId;

        internal DynamicAtlasSpriteLease(LeaseRecord record, long rentalId)
        {
            _record = record;
            _rentalId = rentalId;
        }

        /// <summary>
        /// False only for a default (unassigned) handle. A disposed handle still reports
        /// true here; query <see cref="IsDisposed"/> for the release state.
        /// </summary>
        public bool IsValid => _record != null;

        /// <summary>The entry key, or null once the handle is stale or disposed.</summary>
        public string Key => IsCurrent ? _record.Key : null;

        /// <summary>The leased sprite, or null once the handle is stale or disposed.</summary>
        public Sprite Sprite => IsCurrent ? _record.Sprite : null;

        /// <summary>True when the handle no longer releases an entry (disposed, stale, or default).</summary>
        public bool IsDisposed => !IsCurrent;

        private bool IsCurrent =>
            _record != null && _rentalId != 0 && _record.RentalId == _rentalId;

        /// <summary>
        /// Releases exactly one entry reference. Idempotent; a stale or default handle is a
        /// no-op. Must run on the owner thread.
        /// </summary>
        public void Dispose()
        {
            LeaseRecord record = _record;
            if (record == null || _rentalId == 0 || record.RentalId != _rentalId)
            {
                return;
            }

            record.Owner.ReleaseLease(record);
        }
    }
}
