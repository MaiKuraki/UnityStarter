using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;

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
		/// <param name="options">Provider-independent module default cache tuning.</param>
		UniTask InitializeAsync(AssetManagementOptions options = default);

		/// <summary>
		/// Destroys the module and releases all resources asynchronously.
		/// Production composition roots should prefer this method so provider cleanup can finish deterministically.
		/// </summary>
		UniTask DestroyAsync();

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

	}

	/// <summary>
	/// Internal handle lease used by the cache to pin shared provider handles.
	/// Public callers use IDisposable only; Retain/Release must stay inside the ownership layer.
	/// </summary>
	internal interface IReferenceCounted : IDisposable
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

	internal interface IAssetBackendLifetime
	{
		bool IsDisposed { get; }
	}

	/// <summary>
	/// Optional contract implemented by cacheable asset handles to report an approximate
	/// runtime memory footprint (bytes). The cache uses this for memory-budget eviction in
	/// addition to entry-count limits. Non-asset handles (scene/instantiate) do not implement this.
	/// </summary>
	internal interface IAssetMemoryFootprint
	{
		long EstimateRuntimeBytes();
	}

	/// <summary>
	/// Abstraction of a loaded asset package. Unity-facing methods are main-thread-affine unless a member explicitly
	/// states otherwise. Provider-specific implementations must not imply cross-thread safety with collection locks.
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

		// --- Asset Loading ---
		IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object;

		/// <summary>
		/// Instantiates a prefab using a successfully completed, active <see cref="GameObject"/> lease owned by
		/// this package. The returned instance handle has independent ownership and must be disposed.
		/// </summary>
		/// <exception cref="ArgumentException">
		/// The handle is null, disposed, not an AssetManagement caller lease, or belongs to another package.
		/// </exception>
		/// <exception cref="InvalidOperationException">
		/// The prefab load has not completed successfully or has no asset value.
		/// </exception>
		IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true);

		// --- Query ---
		/// <summary>
		/// Returns true if an asset of type <typeparamref name="TAsset"/> at <paramref name="location"/> is
		/// currently held by the cache (either retained/in-use or pooled idle). Does not trigger a load and
		/// does not affect reference counts or LRU ordering.
		/// </summary>
		bool IsAssetCached<TAsset>(string location) where TAsset : UnityEngine.Object;

		// --- Maintenance ---
		UniTask UnloadUnusedAssetsAsync();

		/// <summary>
		/// Overrides the cache's idle (RefCount == 0) memory budget at runtime, in bytes. Pass a positive
		/// value to set an explicit budget, or 0 to restore the automatic platform-aware default. Immediately
		/// evicts idle handles to honor the new budget. Lets a host project tune memory pressure on the fly
		/// (e.g. tighten before a heavy scene) without modifying the asset module.
		/// </summary>
		void SetCacheIdleMemoryBudget(long maxIdleBytes);

		/// <summary>
		/// Evicts idle (RefCount == 0) cached handles matched by <paramref name="policy"/>, disposing each and
		/// returning how many were evicted. Active (in-use) handles are never touched.
		/// The package carries no timer or frame driver; callers own the retention schedule and rule composition.
		/// </summary>
		int TrimIdleCache(AssetCacheRetentionPolicy policy);

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

	/// <summary>Optional synchronous asset operations. Addressables intentionally does not implement this capability.</summary>
	public interface IAssetSyncOperations
	{
		IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location, string bucket = null, string tag = null, string owner = null) where TAsset : UnityEngine.Object;
	}

	/// <summary>Optional asynchronous bulk/sub-asset loading capability.</summary>
	public interface IAssetBulkLoader
	{
		IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object;
	}

	/// <summary>Optional raw-file loading capability for provider-owned file content.</summary>
	public interface IAssetRawFileLoader
	{
		IRawFileHandle LoadRawFileSync(string location, string bucket = null, string tag = null, string owner = null);
		IRawFileHandle LoadRawFileAsync(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default);
	}

	/// <summary>Optional asynchronous scene loading and unloading capability.</summary>
	public interface IAssetSceneLoader
	{
		/// <summary>
		/// Loads a scene with explicit Unity load parameters. Any local 2D or 3D physics world is owned
		/// by the loaded scene and follows that scene's lifetime.
		/// </summary>
		ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneParameters loadParameters, SceneActivationMode activationMode, int priority = 100, string bucket = null);
		ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100, string bucket = null);
		/// <summary>
		/// Unloads an owned scene. Cancellation is accepted only before provider mutation starts; once started,
		/// the operation completes deterministically so provider and wrapper ownership cannot diverge.
		/// </summary>
		UniTask UnloadSceneAsync(ISceneHandle sceneHandle, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Optional low-frequency provider catalog query contract. Querying by catalog tag may scan provider manifests and
	/// allocate inside provider SDKs, so callers must keep this out of gameplay/UI hot paths and use explicit lifetime
	/// buckets for loads. Catalog tags are provider-side labels, not the runtime cache metadata tags passed to load APIs.
	/// </summary>
	public interface IAssetCatalogQuery
	{
		/// <summary>
		/// Fills <paramref name="results"/> with provider-normalized load locations for assets that match
		/// <paramref name="tag"/>. Implementations clear the list before adding results.
		/// </summary>
		UniTask<bool> TryGetAssetLocationsByTagAsync(string tag, List<string> results, CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Caller-owned provider download operation. The creator transfers one ownership lease to the caller.
	/// <see cref="Dispose"/> is idempotent and retires wrapper ownership exactly once. A provider operation that
	/// supports abort may be stopped immediately; otherwise the adapter retains internal ownership until that
	/// operation reaches a terminal state. Provider handles with an explicit release contract are released only at
	/// that provider-safe terminal point.
	/// </summary>
	public interface IDownloader : IDisposable
	{
		bool IsDone { get; }
		bool Succeed { get; }
		float Progress { get; }
		int TotalDownloadCount { get; }
		int CurrentDownloadCount { get; }
		long TotalDownloadBytes { get; }
		long CurrentDownloadBytes { get; }
		string Error { get; }

		/// <summary>
		/// Resolves the bounded download plan and total byte estimate without starting payload writes.
		/// Must be idempotent. Normal completion means provider preparation succeeded; provider failures fault
		/// the task and caller cancellation throws <see cref="OperationCanceledException"/>. Totals are
		/// authoritative only after this task succeeds.
		/// </summary>
		UniTask PrepareAsync(CancellationToken cancellationToken = default);

		/// <summary>
		/// Starts or joins the memoized provider download. Normal completion means the provider download
		/// succeeded. Provider failures fault the task. The passed token cancels only this caller's wait.
		/// <see cref="Cancel"/> or <see cref="Dispose"/> cancels every caller-visible wait for this downloader;
		/// physical provider abort is capability-specific. An adapter whose provider cannot abort must drain the
		/// operation to a terminal state before releasing it. Either form of caller-visible cancellation causes
		/// <see cref="OperationCanceledException"/> for the affected wait.
		/// <see cref="Succeed"/> and <see cref="Error"/> are retained for state inspection and diagnostics,
		/// not as substitutes for awaiting this method.
		/// </summary>
		UniTask StartAsync(CancellationToken cancellationToken = default);
		void Cancel();
	}

	public interface IOperation
	{
		bool IsDone { get; }
		float Progress { get; }
		/// <summary>
		/// Provider diagnostic text. This value may be empty or become available only after completion;
		/// callers must await <see cref="Task"/> to determine whether the operation succeeded.
		/// </summary>
		string Error { get; }
		/// <summary>
		/// Memoized broadcast completion task that is safe for repeated and concurrent awaiters. Successful
		/// completion means the provider operation succeeded. Provider failures fault this task;
		/// <see cref="Error"/> remains diagnostic context and is never a substitute for observing task completion.
		/// </summary>
		UniTask Task { get; }
		void WaitForAsyncComplete();
	}

	public interface IAssetHandle<out TAsset> : IOperation, IDisposable where TAsset : UnityEngine.Object
	{
		TAsset Asset { get; }
		UnityEngine.Object AssetObject { get; }
	}

	public interface IAllAssetsHandle<out TAsset> : IOperation, IDisposable where TAsset : UnityEngine.Object
	{
		IReadOnlyList<TAsset> Assets { get; }
	}

	public interface IInstantiateHandle : IOperation, IDisposable
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

	/// <summary>
	/// Provider-normalized scene operation and ownership handle.
	/// For <see cref="SceneActivationMode.Manual"/>, call <see cref="ActivateAsync"/> to resume and await
	/// activation. The inherited <see cref="IOperation.Task"/> is the provider load completion and is not a
	/// provider-neutral pre-activation readiness barrier; some providers keep it pending until activation resumes.
	/// Scene handles never enter asset idle-cache retention. Cache trim and low-memory maintenance do not unload scenes.
	/// <see cref="IDisposable.Dispose"/> must be idempotent and releases only the caller's wrapper ownership;
	/// authoritative scene lifetime remains with <see cref="IAssetSceneLoader.UnloadSceneAsync"/> and package shutdown.
	/// </summary>
	public interface ISceneHandle : IOperation, IDisposable
	{
		string ScenePath { get; }
		Scene Scene { get; }
		/// <summary>
		/// Scene handles use explicit unload semantics.
		/// Calling <see cref="IDisposable.Dispose"/> only releases
		/// the caller's ownership of the handle wrapper; it does NOT unload the scene itself.
		/// Use <see cref="IAssetSceneLoader.UnloadSceneAsync"/> as the scene-capability unload path.
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
		/// Cancellation is accepted only before activation starts; an in-progress activation completes deterministically.
		/// </summary>
		UniTask ActivateAsync(CancellationToken cancellationToken = default);
	}

	/// <summary>
	/// Handle for raw file operations. Raw files are non-compressed files suitable for JSON, text, binary data, etc.
	/// Thread-safe for read operations after loading completes. Dispose must be called on the main thread.
	/// </summary>
	public interface IRawFileHandle : IOperation, IDisposable
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
	/// Provider-independent module configuration.
	/// </summary>
	public readonly struct AssetManagementOptions
	{
		private readonly byte _configured;

		/// <summary>
		/// Module-wide default cache tuning applied to every package. A default value resolves to the conservative
		/// platform fallback; a per-package <see cref="AssetPackageInitOptions.CacheTuningOverride"/> takes precedence.
		/// </summary>
		public readonly AssetCacheTuning DefaultCacheTuning;

		public AssetManagementOptions(AssetCacheTuning defaultCacheTuning = default)
		{
			_configured = 1;
			DefaultCacheTuning = defaultCacheTuning.Normalized();
		}

		public static AssetManagementOptions Default => new AssetManagementOptions();

		internal AssetManagementOptions Normalized()
		{
			return _configured == 0 ? Default : this;
		}
	}

	/// <summary>
	/// Provider-agnostic initialization parameters for a package.
	/// </summary>
	public readonly struct AssetPackageInitOptions
	{
		public readonly object ProviderOptions;

		/// <summary>
		/// Optional package-specific cache tuning. Null uses the module default.
		/// </summary>
		public readonly AssetCacheTuning? CacheTuningOverride;

		public AssetPackageInitOptions(
			object providerOptions = null,
			AssetCacheTuning? cacheTuningOverride = null)
		{
			ProviderOptions = providerOptions;
			CacheTuningOverride = cacheTuningOverride;
		}
	}

}
