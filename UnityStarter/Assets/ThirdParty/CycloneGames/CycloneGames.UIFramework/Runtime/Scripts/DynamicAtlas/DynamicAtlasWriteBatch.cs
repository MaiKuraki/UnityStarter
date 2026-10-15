using System;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Deferred-upload scope created by <see cref="IDynamicAtlas.BeginBatch"/>. Texture
    /// uploads flush when the outermost batch is disposed. Disposal must run on the owner
    /// thread; a dispose attempted on a worker thread throws and leaves the batch reusable.
    /// </summary>
    public sealed class DynamicAtlasWriteBatch : IDisposable
    {
        private DynamicAtlasService _owner;
        private readonly long _batchEpoch;

        internal DynamicAtlasWriteBatch(DynamicAtlasService owner, long batchEpoch)
        {
            _owner = owner;
            _batchEpoch = batchEpoch;
        }

        public void Dispose()
        {
            DynamicAtlasService owner = _owner;
            if (owner == null)
            {
                return;
            }

            // EndBatch runs the owner-thread guard before mutating batch state, so a
            // dispose attempted on a worker thread throws here and leaves this batch
            // reusable on the owner thread without consuming batch depth.
            owner.EndBatch(_batchEpoch);
            _owner = null;
        }
    }
}
