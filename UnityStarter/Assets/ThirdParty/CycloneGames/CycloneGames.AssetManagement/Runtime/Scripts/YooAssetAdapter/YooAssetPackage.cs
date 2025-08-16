using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace CycloneGames.AssetManagement
{
	internal sealed class YooAssetPackage : IAssetPackage
	{
		private readonly ResourcePackage _raw;
		private int _nextId = 1;
		private bool _initialized;

		public string Name => _raw.PackageName;
		public bool IsAlive => _initialized;
		public ResourcePackage Raw => _raw;

		public YooAssetPackage(ResourcePackage raw)
		{
			_raw = raw ?? throw new ArgumentNullException(nameof(raw));
		}

		public async Task<bool> InitializeAsync(AssetPackageInitOptions options, CancellationToken cancellationToken = default)
		{
			if (_initialized) return true;
			if (options.ProviderOptions == null) throw new ArgumentNullException(nameof(options.ProviderOptions));

			InitializeParameters initParams = CreateInitParams(options);
			var op = _raw.InitializeAsync(initParams);
			while (!op.IsDone)
			{
				if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
				await YieldUtil.Next(cancellationToken);
			}
			_initialized = op.Status == EOperationStatus.Succeed;
			return _initialized;
		}

		private static InitializeParameters CreateInitParams(AssetPackageInitOptions options)
		{
			InitializeParameters p;
			switch (options.PlayMode)
			{
				case AssetPlayMode.EditorSimulate:
					p = (EditorSimulateModeParameters)options.ProviderOptions;
					break;
				case AssetPlayMode.Offline:
					p = (OfflinePlayModeParameters)options.ProviderOptions;
					break;
				case AssetPlayMode.Host:
					p = (HostPlayModeParameters)options.ProviderOptions;
					break;
				case AssetPlayMode.Web:
					p = (WebPlayModeParameters)options.ProviderOptions;
					break;
				case AssetPlayMode.Custom:
					p = (CustomPlayModeParameters)options.ProviderOptions;
					break;
				default:
					throw new ArgumentOutOfRangeException();
			}

			if (options.BundleLoadingMaxConcurrencyOverride.HasValue)
			{
				p.BundleLoadingMaxConcurrency = options.BundleLoadingMaxConcurrencyOverride.Value;
			}
			return p;
		}

		public async Task DestroyAsync()
		{
			if (!_initialized) return;
			_initialized = false;
			HandleTracker.ReportLeaks(_raw.PackageName);
			var op = _raw.DestroyAsync();
			while (!op.IsDone) await YieldUtil.Next();
		}

		// --- Update & Download ---
		public async Task<string> RequestPackageVersionAsync(bool appendTimeTicks = true, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
		{
			var op = _raw.RequestPackageVersionAsync(appendTimeTicks, timeoutSeconds);
			while (!op.IsDone)
			{
				if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
				await YieldUtil.Next(cancellationToken);
			}
			if (op.Status == EOperationStatus.Succeed) return op.PackageVersion;
			return null;
		}

		public async Task<bool> UpdatePackageManifestAsync(string packageVersion, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
		{
			var op = _raw.UpdatePackageManifestAsync(packageVersion, timeoutSeconds);
			while (!op.IsDone)
			{
				if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
				await YieldUtil.Next(cancellationToken);
			}
			return op.Status == EOperationStatus.Succeed;
		}

		public async Task<bool> ClearCacheFilesAsync(string clearMode, object clearParam = null, CancellationToken cancellationToken = default)
		{
			var op = _raw.ClearCacheFilesAsync(clearMode, clearParam);
			while (!op.IsDone)
			{
				if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
				await YieldUtil.Next(cancellationToken);
			}
			return op.Status == EOperationStatus.Succeed;
		}

		public IDownloader CreateDownloaderForAll(int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60)
		{
			var op = _raw.CreateResourceDownloader(downloadingMaxNumber, failedTryAgain, timeoutSeconds);
			return new YooDownloader(op);
		}

		public IDownloader CreateDownloaderForTags(string[] tags, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60)
		{
			if (tags == null || tags.Length == 0) return new YooDownloader(null);
			var op = _raw.CreateResourceDownloader(tags, downloadingMaxNumber, failedTryAgain, timeoutSeconds);
			return new YooDownloader(op);
		}

		public IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60)
		{
			if (locations == null || locations.Length == 0) return new YooDownloader(null);
			var op = _raw.CreateBundleDownloader(locations, recursiveDownload, downloadingMaxNumber, failedTryAgain, timeoutSeconds);
			return new YooDownloader(op);
		}

		public async Task<IDownloader> CreatePreDownloaderForAllAsync(string packageVersion, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
		{
			var pre = _raw.PreDownloadContentAsync(packageVersion, timeoutSeconds);
			while (!pre.IsDone)
			{
				if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
				await YieldUtil.Next(cancellationToken);
			}
			var d = pre.CreateResourceDownloader(downloadingMaxNumber, failedTryAgain, timeoutSeconds);
			return new YooDownloader(d);
		}

		public async Task<IDownloader> CreatePreDownloaderForTagsAsync(string packageVersion, string[] tags, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
		{
			var pre = _raw.PreDownloadContentAsync(packageVersion, timeoutSeconds);
			while (!pre.IsDone)
			{
				if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
				await YieldUtil.Next(cancellationToken);
			}
			var d = pre.CreateResourceDownloader(tags, downloadingMaxNumber, failedTryAgain, timeoutSeconds);
			return new YooDownloader(d);
		}

		public async Task<IDownloader> CreatePreDownloaderForLocationsAsync(string packageVersion, string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
		{
			var pre = _raw.PreDownloadContentAsync(packageVersion, timeoutSeconds);
			while (!pre.IsDone)
			{
				if (cancellationToken.IsCancellationRequested) throw new OperationCanceledException(cancellationToken);
				await YieldUtil.Next(cancellationToken);
			}
			var d = pre.CreateBundleDownloader(locations, recursiveDownload, downloadingMaxNumber, failedTryAgain, timeoutSeconds);
			return new YooDownloader(d);
		}

		public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location) where TAsset : UnityEngine.Object
		{
			var handle = _raw.LoadAssetSync<TAsset>(location);
			var wrapped = new YooAssetHandle<TAsset>(RegisterHandle(out int id), id, handle);
			HandleTracker.Register(id, _raw.PackageName, $"Asset {typeof(TAsset).Name} : {location}");
			return wrapped;
		}

		public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location) where TAsset : UnityEngine.Object
		{
			var handle = _raw.LoadAssetAsync<TAsset>(location);
			var wrapped = new YooAssetHandle<TAsset>(RegisterHandle(out int id), id, handle);
			HandleTracker.Register(id, _raw.PackageName, $"AssetAsync {typeof(TAsset).Name} : {location}");
			return wrapped;
		}

		public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location) where TAsset : UnityEngine.Object
		{
			var handle = _raw.LoadAllAssetsAsync<TAsset>(location);
			var wrapped = new YooAllAssetsHandle<TAsset>(RegisterHandle(out int id), id, handle);
			HandleTracker.Register(id, _raw.PackageName, $"AllAssets {typeof(TAsset).Name} : {location}");
			return wrapped;
		}

		public GameObject InstantiateSync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false)
		{
			if (handle is YooAssetHandle<GameObject> h)
			{
				return h.Raw.InstantiateSync(parent, worldPositionStays);
			}
			return null;
		}

		public IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true)
		{
			if (handle is YooAssetHandle<GameObject> h)
			{
				var op = h.Raw.InstantiateAsync(parent, worldPositionStays, setActive);
				var wrapped = new YooInstantiateHandle(RegisterHandle(out int id), id, op);
				HandleTracker.Register(id, _raw.PackageName, $"InstantiateAsync : {h.Raw?.GetAssetInfo()?.AssetPath}");
				return wrapped;
			}
			return null;
		}

		public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100)
		{
			// Map to YooAsset signature: (location, sceneMode, physicsMode, suspendLoad, priority)
			bool suspendLoad = !activateOnLoad;
			var op = _raw.LoadSceneAsync(sceneLocation, loadMode, LocalPhysicsMode.None, suspendLoad, (uint)Mathf.Max(0, priority));
			var h = new YooSceneHandle(RegisterHandle(out int id), id, op);
			HandleTracker.Register(id, _raw.PackageName, $"SceneAsync : {sceneLocation}");
			return h;
		}

		public ISceneHandle LoadSceneSync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single)
		{
			var op = _raw.LoadSceneSync(sceneLocation, loadMode, LocalPhysicsMode.None);
			var h = new YooSceneHandle(RegisterHandle(out int id), id, op);
			HandleTracker.Register(id, _raw.PackageName, $"SceneSync : {sceneLocation}");
			return h;
		}

		public async Task UnloadSceneAsync(ISceneHandle sceneHandle)
		{
			if (sceneHandle is YooSceneHandle sh)
			{
				var op = sh.Raw.UnloadAsync();
				while (!op.IsDone) await YieldUtil.Next();
			}
		}

		public async Task UnloadUnusedAssetsAsync()
		{
			var op = _raw.UnloadUnusedAssetsAsync();
			while (!op.IsDone) await YieldUtil.Next();
		}

		// --- Handle registry ---
		private Action<int> RegisterHandle(out int id)
		{
			id = _nextId++;
			return UnregisterHandle;
		}

		private void UnregisterHandle(int id)
		{
			// No-op: current implementation does not store handles. Method kept for future tracking needs.
		}
	}
}