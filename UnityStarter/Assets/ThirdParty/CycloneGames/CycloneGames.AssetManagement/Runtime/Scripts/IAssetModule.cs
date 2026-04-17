using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CycloneGames.AssetManagement.Runtime
{
	/// <summary>
	/// Abstraction of the asset system. Designed for DI and provider-agnostic usage.
	/// </summary>
	public interface IAssetModule
	{
		bool Initialized { get; }

		/// <summary>
		/// Initializes the module asynchronously. Idempotent. Safe to call multiple times.
		/// </summary>
		/// <param name="options">Global options (time slice, concurrency, logger etc.).</param>
		UniTask InitializeAsync(AssetManagementOptions options = default);

		/// <summary>
		/// Destroys the module and releases all resources.
		/// </summary>
		void Destroy();

		/// <summary>
		/// Creates a new logical package. Package must be initialized via <see cref="IAssetPackage.InitializeAsync"/> before use.
		/// </summary>
		IAssetPackage CreatePackage(string packageName);

		/// <summary>
		/// Gets a created package; returns null if not found.
		/// </summary>
		IAssetPackage GetPackage(string packageName);

		/// <summary>
		/// Removes a package. Only allowed after the package has been destroyed.
		/// </summary>
		UniTask<bool> RemovePackageAsync(string packageName);

		/// <summary>
		/// Returns a snapshot of existing package names.
		/// </summary>
		IReadOnlyList<string> GetAllPackageNames();

		/// <summary>
		/// Creates a patch service for the specified package to manage the update workflow.
		/// </summary>
		IPatchService CreatePatchService(string packageName);
	}

	/// <summary>
	/// Base interface for handles supporting explicit Automatic Reference Counting (ARC).
	/// Required for sharing resources safely between multiple consumers (e.g. UI layers, Object Pools).
	/// </summary>
	public interface IReferenceCounted : IDisposable
	{
		int RefCount { get; }
		
		/// <summary>
		/// Increments the reference count securely.
		/// </summary>
		void Retain();
		
		/// <summary>
		/// Decrements the reference count. If RefCount reaches 0, the handle is released (to a cache or destroyed).
		/// </summary>
		void Release();
	}

	internal interface IInternalCacheable
	{
		void ForceDispose();
	}

	/// <summary>
	/// Abstraction of a package (catalog + bundles). Provider specific implementation should be zero-GC in hot paths.
	/// </summary>
	public interface IAssetPackage
	{
		string Name { get; }

		/// <summary>
		/// Initializes the package.
		/// Provider-specific parameters are carried in <see cref="AssetPackageInitOptions.ProviderOptions"/>.
		/// </summary>
		UniTask<bool> InitializeAsync(AssetPackageInitOptions options, CancellationToken cancellationToken = default);

		/// <summary>
		/// Destroys the package and releases all provider resources.
		/// </summary>
		UniTask DestroyAsync();

		// --- Update & Download ---
		UniTask<string> RequestPackageVersionAsync(bool appendTimeTicks = true, int timeoutSeconds = 60, CancellationToken cancellationToken = default);
		UniTask<bool> UpdatePackageManifestAsync(string packageVersion, int timeoutSeconds = 60, CancellationToken cancellationToken = default);
		UniTask<bool> ClearCacheFilesAsync(ClearCacheMode clearMode = ClearCacheMode.All, object clearParam = null, CancellationToken cancellationToken = default);

		// Downloaders based on ACTIVE manifest
		IDownloader CreateDownloaderForAll(int downloadingMaxNumber, int failedTryAgain);
		IDownloader CreateDownloaderForTags(string[] tags, int downloadingMaxNumber, int failedTryAgain);
		IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain);

		// Pre-download for a SPECIFIC manifest version (without switching active manifest)
		UniTask<IDownloader> CreatePreDownloaderForAllAsync(string packageVersion, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default);
		UniTask<IDownloader> CreatePreDownloaderForTagsAsync(string packageVersion, string[] tags, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default);
		UniTask<IDownloader> CreatePreDownloaderForLocationsAsync(string packageVersion, string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default);

		// --- Asset Loading ---
		IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location, string bucket = null, string tag = null, string owner = null) where TAsset : UnityEngine.Object;
		IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object;

		/// <summary>
		/// Loads all sub-assets for a location (e.g., sprites in an atlas).
		/// </summary>
		IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object;

		/// <summary>
		/// Loads a raw file synchronously. Returns null on error.
		/// Raw files are not compressed and suitable for JSON, text files, binary data, etc.
		/// </summary>
		IRawFileHandle LoadRawFileSync(string location, string bucket = null, string tag = null, string owner = null);

		/// <summary>
		/// Loads a raw file asynchronously.
		/// Raw files are not compressed and suitable for JSON, text files, binary data, etc.
		/// </summary>
		IRawFileHandle LoadRawFileAsync(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default);

		/// <summary>
		/// Instantiates a prefab synchronously using a previously loaded handle. Returns null on error.
		/// </summary>
		GameObject InstantiateSync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false);

		/// <summary>
		/// Instantiates a prefab asynchronously using a previously loaded handle.
		/// </summary>
		IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true);

		// --- Scene Loading ---
		ISceneHandle LoadSceneSync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, string bucket = null);
		ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode, SceneActivationMode activationMode, int priority = 100, string bucket = null);
		ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100, string bucket = null);
		UniTask UnloadSceneAsync(ISceneHandle sceneHandle);

		// --- Maintenance ---
		UniTask UnloadUnusedAssetsAsync();

		/// <summary>
		/// Forces the evaluation and clearing of handles associated with a specific logical bucket group. 
		/// Handles belonging to this bucket that have RefCount == 0 will be immediately purged, bypassing LRU delays.
		/// </summary>
		void ClearBucket(string bucket);

		/// <summary>
		/// Clears every idle handle whose bucket matches the specified prefix or any of its descendants.
		/// Useful for hierarchical lifetime domains such as "UI.Scene" or "Gameplay.Levels.BossRoom".
		/// </summary>
		void ClearBucketsByPrefix(string bucketPrefix);
	}

	public interface IDownloader
	{
		bool IsDone { get; }
		bool Succeed { get; }
		float Progress { get; }
		int TotalDownloadCount { get; }
		int CurrentDownloadCount { get; }
		long TotalDownloadBytes { get; }
		long CurrentDownloadBytes { get; }
		string Error { get; }

		void Begin();
		UniTask StartAsync(CancellationToken cancellationToken = default);
		void Pause();
		void Resume();
		void Cancel();
		void Combine(IDownloader other);
	}

	public interface IOperation
	{
		bool IsDone { get; }
		float Progress { get; }
		string Error { get; }
		UniTask Task { get; }
		void WaitForAsyncComplete();
	}

	public interface IAssetHandle<out TAsset> : IOperation, IReferenceCounted where TAsset : UnityEngine.Object
	{
		TAsset Asset { get; }
		UnityEngine.Object AssetObject { get; }
	}

	public interface IAllAssetsHandle<out TAsset> : IOperation, IReferenceCounted where TAsset : UnityEngine.Object
	{
		IReadOnlyList<TAsset> Assets { get; }
	}

	public interface IInstantiateHandle : IOperation, IReferenceCounted
	{
		GameObject Instance { get; }
	}

	/// <summary>
	/// Defines whether a scene should become interactive as soon as loading completes,
	/// or remain suspended until <see cref="ISceneHandle.ActivateAsync"/> is called.
	/// </summary>
	public enum SceneActivationMode : byte
	{
		ActivateOnLoad = 0,
		Manual = 1,
	}

	/// <summary>
	/// Runtime state of a scene handle's activation lifecycle.
	/// This is a provider-normalized state rather than a guaranteed one-to-one mirror of the underlying SDK.
	/// Providers that do not expose an observable "loaded but not yet activatable" phase may remain in
	/// <see cref="Loading"/> until <see cref="ISceneHandle.ActivateAsync"/> completes.
	/// </summary>
	public enum SceneActivationState : byte
	{
		Loading = 0,
		/// <summary>
		/// The scene load is complete and the provider is waiting for an explicit activation step.
		/// This state is only reported when the underlying provider exposes that barrier explicitly.
		/// </summary>
		WaitingForActivation = 1,
		Activated = 2,
	}

	public interface ISceneHandle : IOperation, IReferenceCounted
	{
		string ScenePath { get; }
		Scene Scene { get; }
		/// <summary>
		/// Scene handles use explicit unload semantics.
		/// Calling <see cref="IReferenceCounted.Release"/> / <see cref="IDisposable.Dispose"/> only releases
		/// the caller's ownership of the handle wrapper; it does NOT unload the scene itself.
		/// Use <see cref="IAssetPackage.UnloadSceneAsync"/> as the single cross-provider unload path.
		/// </summary>
		SceneActivationMode ActivationMode { get; }
		/// <summary>
		/// Current normalized activation state.
		/// This value is best-effort across providers and should not be used as the sole source of truth
		/// for gameplay sequencing when <see cref="Task"/> or <see cref="ActivateAsync"/> completion is available.
		/// </summary>
		SceneActivationState ActivationState { get; }
		/// <summary>
		/// Whether this provider accepts scenes loaded with <see cref="SceneActivationMode.Manual"/>.
		/// This does not imply that every intermediate activation state can be observed.
		/// </summary>
		bool SupportsManualActivation { get; }

		/// <summary>
		/// Activates a scene that was loaded with <see cref="SceneActivationMode.Manual"/>.
		/// This call must be idempotent: if the scene is already activated it should complete immediately.
		/// Implementations may internally resume suspended loading rather than calling a distinct provider API
		/// literally named "activate".
		/// If manual activation is unsupported, implementations should throw <see cref="NotSupportedException"/>.
		/// </summary>
		UniTask ActivateAsync(CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Handle for raw file operations. Raw files are non-compressed files suitable for JSON, text, binary data, etc.
	/// Thread-safe for read operations after loading completes. Dispose must be called on the main thread.
	/// </summary>
	public interface IRawFileHandle : IOperation, IReferenceCounted
	{
		/// <summary>
		/// Gets the file path. Returns empty string if not available.
		/// </summary>
		string FilePath { get; }

		/// <summary>
		/// Reads the file contents as text. Returns empty string if not loaded or error occurred.
		/// Thread-safe: Can be called from any thread after IsDone is true.
		/// </summary>
		string ReadText();

		/// <summary>
		/// Reads the file contents as bytes. Returns null if not loaded or error occurred.
		/// Thread-safe: Can be called from any thread after IsDone is true.
		/// </summary>
		byte[] ReadBytes();
	}

	/// <summary>
	/// Global configuration for the module.
	/// </summary>
	public readonly struct AssetManagementOptions
	{
		public readonly long OperationSystemMaxTimeSliceMs;
		public readonly int BundleLoadingMaxConcurrency;
		public readonly ILogger Logger;
		public readonly bool EnableHandleTracking;

		public AssetManagementOptions(long operationSystemMaxTimeSliceMs = 16, int bundleLoadingMaxConcurrency = int.MaxValue, ILogger logger = null, bool enableHandleTracking = true)
		{
			OperationSystemMaxTimeSliceMs = operationSystemMaxTimeSliceMs < 10 ? 10 : operationSystemMaxTimeSliceMs;
			BundleLoadingMaxConcurrency = bundleLoadingMaxConcurrency;
			Logger = logger;
			EnableHandleTracking = enableHandleTracking;
		}
	}

	/// <summary>
	/// Provider-agnostic initialization parameters for a package.
	/// </summary>
	public readonly struct AssetPackageInitOptions
	{
		public readonly AssetPlayMode PlayMode;
		public readonly object ProviderOptions;
		public readonly int? BundleLoadingMaxConcurrencyOverride;

		public AssetPackageInitOptions(AssetPlayMode playMode, object providerOptions, int? bundleLoadingMaxConcurrencyOverride = null)
		{
			PlayMode = playMode;
			ProviderOptions = providerOptions;
			BundleLoadingMaxConcurrencyOverride = bundleLoadingMaxConcurrencyOverride;
		}
	}

	public enum AssetPlayMode
	{
		EditorSimulate,
		Offline,
		Host,
		Web,
		Custom
	}

	public enum ClearCacheMode
	{
		/// <summary>
		/// Clear all cached files, including asset bundles and manifests.
		/// </summary>
		All,
		/// <summary>
		/// Clear only the cached files that are no longer in use by the current manifest.
		/// </summary>
		Unused,
		/// <summary>
		/// Clear cached files associated with specific tags. The tags should be provided via the `clearParam`.
		/// </summary>
		ByTags
	}
}
