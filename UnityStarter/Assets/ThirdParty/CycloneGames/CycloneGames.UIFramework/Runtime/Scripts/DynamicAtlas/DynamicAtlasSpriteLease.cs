using System;
using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    public sealed class DynamicAtlasSpriteLease : IDisposable
    {
        private DynamicAtlasService _owner;
        private readonly long _entryGeneration;

        public string Key { get; }
        public Sprite Sprite { get; private set; }
        public bool IsDisposed => _owner == null;

        internal DynamicAtlasSpriteLease(
            DynamicAtlasService owner,
            string key,
            long entryGeneration,
            Sprite sprite)
        {
            _owner = owner;
            Key = key;
            _entryGeneration = entryGeneration;
            Sprite = sprite;
        }

        public void Dispose()
        {
            DynamicAtlasService owner = _owner;
            if (owner == null)
            {
                return;
            }

            owner.ReleaseLease(Key, _entryGeneration);
            _owner = null;
            Sprite = null;
        }
    }

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

            if (!owner.IsOwnerThread)
            {
                owner.EndBatch(_batchEpoch);
                return;
            }

            _owner = null;
            owner.EndBatch(_batchEpoch);
        }
    }
}
