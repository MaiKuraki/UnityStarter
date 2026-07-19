using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    internal sealed class DynamicAtlasThreadGuard
    {
        private readonly int _ownerThreadId;

        internal DynamicAtlasThreadGuard()
        {
            if (!PlayerLoopHelper.IsMainThread)
            {
                throw new InvalidOperationException("DynamicAtlasService must be created on the Unity main thread.");
            }

            _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        internal bool IsOwnerThread =>
            PlayerLoopHelper.IsMainThread &&
            Thread.CurrentThread.ManagedThreadId == _ownerThreadId;

        internal void ThrowIfNotOwnerThread(string operation)
        {
            if (!IsOwnerThread)
            {
                throw new InvalidOperationException($"Dynamic atlas operation '{operation}' must run on its owner Unity thread.");
            }
        }
    }
}
