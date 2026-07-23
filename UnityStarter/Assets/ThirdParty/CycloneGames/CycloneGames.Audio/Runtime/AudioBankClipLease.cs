// Copyright (c) CycloneGames
// Licensed under the MIT License.

using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.Audio.Runtime
{
    internal static class AudioClipHandleRelease
    {
        internal static void Safe(IAudioClipHandle handle)
        {
            if (handle == null) return;

            try
            {
                handle.Release();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }

    /// <summary>
    /// Caller-owned residency lease for external clips referenced by an AudioBank.
    /// Dispose the lease on the Unity main thread when the owning scene or feature unloads.
    /// </summary>
    public interface IAudioBankClipLease : IDisposable
    {
        AudioBank Bank { get; }
        int RequestedCount { get; }
        int LoadedCount { get; }
        int FailedCount { get; }
        bool IsDisposed { get; }
    }

    /// <summary>
    /// Optional capability for callers that require deterministic external clip residency.
    /// Kept separate from IAudioService so existing service implementations remain compatible.
    /// </summary>
    public interface IAudioBankClipLeaseProvider
    {
        UniTask<IAudioBankClipLease> AcquireBankClipLeaseAsync(
            AudioBank bank,
            CancellationToken cancellationToken = default);
    }

    public sealed class AudioBankClipLease : IAudioBankClipLease
    {
        private IAudioClipHandle[] handles;
        private long reservedDecodedBytes;
        private readonly int lifecycleGeneration;

        internal AudioBankClipLease(
            AudioBank bank,
            int requestedCount,
            IAudioClipHandle[] retainedHandles,
            int failedCount,
            long reservedDecodedBytes,
            int lifecycleGeneration)
        {
            Bank = bank;
            RequestedCount = requestedCount;
            handles = retainedHandles ?? Array.Empty<IAudioClipHandle>();
            LoadedCount = handles.Length;
            FailedCount = failedCount;
            this.reservedDecodedBytes = Math.Max(0L, reservedDecodedBytes);
            this.lifecycleGeneration = lifecycleGeneration;
        }

        public AudioBank Bank { get; private set; }
        public int RequestedCount { get; }
        public int LoadedCount { get; }
        public int FailedCount { get; }
        public bool IsDisposed => handles == null;

        public void Dispose()
        {
            AudioRuntimeThreadGuard.EnsureMainThread(nameof(AudioBankClipLease) + ".Dispose");
            IAudioClipHandle[] ownedHandles = handles;
            if (ownedHandles == null) return;

            handles = null;
            Bank = null;
            long bytesToRelease = reservedDecodedBytes;
            reservedDecodedBytes = 0L;
            AudioManager.ReleaseActiveBankClipLeaseBytes(bytesToRelease, lifecycleGeneration);
            for (int i = 0; i < ownedHandles.Length; i++)
            {
                AudioClipHandleRelease.Safe(ownedHandles[i]);
                ownedHandles[i] = null;
            }
        }
    }
}
