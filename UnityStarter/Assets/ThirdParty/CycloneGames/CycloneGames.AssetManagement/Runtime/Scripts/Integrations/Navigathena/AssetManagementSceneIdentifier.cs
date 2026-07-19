#if CYCLONEGAMES_HAS_NAVIGATHENA
using System;
using System.Threading;

using UnityEngine.SceneManagement;

using Cysharp.Threading.Tasks;
using MackySoft.Navigathena.SceneManagement;

namespace CycloneGames.AssetManagement.Runtime.Integrations.Navigathena
{
    /// <summary>
    /// A provider-agnostic scene identifier backed by an explicitly owned asset package.
    /// </summary>
    public sealed class AssetManagementSceneIdentifier : ISceneIdentifier
    {
        private readonly IAssetSceneLoader _sceneLoader;
        private readonly string _location;
        private readonly LoadSceneParameters _loadParameters;
        private readonly SceneActivationMode _activationMode;
        private readonly string _bucket;

        public AssetManagementSceneIdentifier(
            IAssetSceneLoader sceneLoader,
            string location,
            LoadSceneMode loadSceneMode = LoadSceneMode.Additive,
            bool activateOnLoad = true,
            string bucket = null)
            : this(
                sceneLoader,
                location,
                new LoadSceneParameters(loadSceneMode),
                activateOnLoad ? SceneActivationMode.ActivateOnLoad : SceneActivationMode.Manual,
                bucket)
        {
        }

        public AssetManagementSceneIdentifier(
            IAssetSceneLoader sceneLoader,
            string location,
            LoadSceneParameters loadParameters,
            SceneActivationMode activationMode = SceneActivationMode.ActivateOnLoad,
            string bucket = null)
        {
            _sceneLoader = sceneLoader ?? throw new ArgumentNullException(nameof(sceneLoader));
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("Scene location cannot be null or empty.", nameof(location));
            }

            _location = location;
            _loadParameters = loadParameters;
            _activationMode = activationMode;
            _bucket = bucket;
        }

        public MackySoft.Navigathena.SceneManagement.ISceneHandle CreateHandle()
        {
            return new NavigathenaSceneHandleAdapter(
                _sceneLoader,
                _location,
                _loadParameters,
                _activationMode,
                _bucket);
        }
    }

    /// <summary>
    /// Bridges an AssetManagement scene ownership lease into a Navigathena transition handle.
    /// </summary>
    public sealed class NavigathenaSceneHandleAdapter : MackySoft.Navigathena.SceneManagement.ISceneHandle
    {
        private readonly IAssetSceneLoader _sceneLoader;
        private readonly string _location;
        private readonly LoadSceneParameters _loadParameters;
        private readonly SceneActivationMode _activationMode;
        private readonly string _bucket;
        private ISceneHandle _sceneHandle;
        private bool _loadStarted;
        private bool _unloadStarted;
        private bool _providerUnloadCompleted;
        private bool _unloaded;
        private UniTask<Scene> _loadTask;
        private UniTask _unloadTask;

        public NavigathenaSceneHandleAdapter(
            ISceneHandle sceneHandle,
            IAssetSceneLoader sceneLoader)
        {
            _sceneHandle = sceneHandle ?? throw new ArgumentNullException(nameof(sceneHandle));
            _sceneLoader = sceneLoader ?? throw new ArgumentNullException(nameof(sceneLoader));
        }

        internal NavigathenaSceneHandleAdapter(
            IAssetSceneLoader sceneLoader,
            string location,
            LoadSceneParameters loadParameters,
            SceneActivationMode activationMode,
            string bucket)
        {
            _sceneLoader = sceneLoader ?? throw new ArgumentNullException(nameof(sceneLoader));
            _location = location ?? throw new ArgumentNullException(nameof(location));
            _loadParameters = loadParameters;
            _activationMode = activationMode;
            _bucket = bucket;
        }

        public Scene Scene
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return _sceneHandle != null ? _sceneHandle.Scene : default;
            }
        }

        public UniTask<Scene> Load(
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (_unloaded || _unloadStarted)
            {
                throw new ObjectDisposedException(nameof(NavigathenaSceneHandleAdapter));
            }

            if (_loadStarted)
            {
                throw new InvalidOperationException("A Navigathena scene handle can only be loaded once.");
            }

            cancellationToken.ThrowIfCancellationRequested();
            _loadStarted = true;
            _loadTask = AssetOperationBroadcast.Create(LoadCoreAsync(progress, cancellationToken));
            return _loadTask;
        }

        private async UniTask<Scene> LoadCoreAsync(
            IProgress<float> progress,
            CancellationToken cancellationToken)
        {
            try
            {
                if (_sceneHandle == null)
                {
                    _sceneHandle = _sceneLoader.LoadSceneAsync(
                        _location,
                        _loadParameters,
                        _activationMode,
                        bucket: _bucket);
                }

                if (_sceneHandle.ActivationMode == SceneActivationMode.Manual)
                {
                    await _sceneHandle.ActivateAsync(cancellationToken);
                }
                else
                {
                    while (!_sceneHandle.IsDone)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        progress?.Report(_sceneHandle.Progress);
                        await UniTask.Yield(cancellationToken);
                    }
                }

                await _sceneHandle.Task;
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(1f);
                return _sceneHandle.Scene;
            }
            catch (Exception loadFailure) when (
                loadFailure is not OutOfMemoryException &&
                loadFailure is not AccessViolationException)
            {
                if (_sceneHandle != null)
                {
                    try
                    {
                        await EnsureUnloadStarted();
                    }
                    catch (Exception cleanupFailure) when (
                        cleanupFailure is not OutOfMemoryException &&
                        cleanupFailure is not AccessViolationException)
                    {
                        throw new AggregateException(
                            "Scene load failed and deterministic cleanup also failed.",
                            loadFailure,
                            cleanupFailure);
                    }
                }
                else
                {
                    _unloaded = true;
                }

                throw;
            }
        }

        public async UniTask Unload(
            IProgress<float> progress = null,
            CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            UniTask task = EnsureUnloadStarted();
            if (cancellationToken.CanBeCanceled)
            {
                await task.AttachExternalCancellation(cancellationToken);
            }
            else
            {
                await task;
            }

            progress?.Report(1f);
        }

        private UniTask EnsureUnloadStarted()
        {
            if (_unloaded)
            {
                return UniTask.CompletedTask;
            }

            if (_sceneHandle == null)
            {
                _unloaded = true;
                return UniTask.CompletedTask;
            }

            if (!_unloadStarted)
            {
                _unloadStarted = true;
                _unloadTask = AssetOperationBroadcast.Create(UnloadCoreAsync());
            }

            return _unloadTask;
        }

        private async UniTask UnloadCoreAsync()
        {
            try
            {
                if (!_providerUnloadCompleted)
                {
                    await _sceneLoader.UnloadSceneAsync(_sceneHandle);
                    _providerUnloadCompleted = true;
                }

                // Navigathena owns the caller lease. Provider unload and caller-wrapper release are separate
                // contracts, so release the wrapper only after authoritative unload succeeds. If Dispose faults,
                // retain the handle and retry only that idempotent caller release on the next Unload call.
                _sceneHandle.Dispose();
                _sceneHandle = null;
                _unloaded = true;
            }
            finally
            {
                _unloadStarted = false;
            }
        }
    }
}
#endif
