using System.Runtime.CompilerServices;
using UnityEngine;

namespace CycloneGames.Logger
{
    public sealed class UnityLogger : ILogger
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogTrace(in string message) => UnityEngine.Debug.Log($"TRACE: {message}");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogDebug(in string message) => UnityEngine.Debug.Log($"DEBUG: {message}");

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogInfo(in string message) => UnityEngine.Debug.Log(message);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogWarning(in string message) => UnityEngine.Debug.LogWarning(message);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogError(in string message) => UnityEngine.Debug.LogError(message);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogFatal(in string message) => UnityEngine.Debug.LogError($"FATAL: {message}");

        public void Dispose() { }
    }
}