using System.Threading;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    internal static class DynamicAtlasThreadGuard
    {
        private static int _mainThreadId = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static bool IsMainThread
        {
            get
            {
                if (_mainThreadId < 0)
                {
                    _mainThreadId = Thread.CurrentThread.ManagedThreadId;
                }
                return Thread.CurrentThread.ManagedThreadId == _mainThreadId;
            }
        }

        public static bool EnsureMainThread(string caller)
        {
            if (IsMainThread) return true;

            CLogger.LogError($"[DynamicAtlas] {caller} must be called on Unity main thread.");
            return false;
        }
    }
}
