// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Threading;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Central main-thread contract for Unity-facing audio operations.
    /// Locks do not make Unity objects safe to access from worker threads.
    /// </summary>
    internal static class AudioRuntimeThreadGuard
    {
        private static int mainThreadId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            CaptureCurrentThread();
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void CaptureEditorMainThread()
        {
            CaptureCurrentThread();
        }
#endif

        internal static void CaptureCurrentThread()
        {
            Volatile.Write(ref mainThreadId, Thread.CurrentThread.ManagedThreadId);
        }

        internal static bool IsMainThread
        {
            get
            {
                int capturedThreadId = Volatile.Read(ref mainThreadId);
                return capturedThreadId != 0 && capturedThreadId == Thread.CurrentThread.ManagedThreadId;
            }
        }

        internal static void EnsureMainThread(string operation)
        {
            int capturedThreadId = Volatile.Read(ref mainThreadId);
            if (capturedThreadId == 0)
            {
                throw new InvalidOperationException(
                    $"{operation} cannot run before the Unity main thread has been captured.");
            }

            if (capturedThreadId != Thread.CurrentThread.ManagedThreadId)
            {
                throw new InvalidOperationException(
                    $"{operation} must run on the Unity main thread. Marshal the request at the composition boundary.");
            }
        }
    }
}
