#if YOOASSET_PRESENT
using System;
using System.Collections.Generic;
using UnityEngine;
using YooAsset;

namespace CycloneGames.AssetManagement.Runtime
{
	internal sealed class YooAssetHandle<TAsset> : IAssetHandle<TAsset> where TAsset : UnityEngine.Object
	{
		private readonly Action<int> _onDispose;
		private readonly int _id;
		internal readonly AssetHandle Raw;

		public YooAssetHandle(Action<int> onDispose, int id, AssetHandle raw)
		{
			_onDispose = onDispose;
			_id = id;
			Raw = raw;
		}

		public bool IsDone => Raw == null || Raw.IsDone;
		public float Progress => Raw?.Progress ?? 0f;
		public string Error => Raw?.LastError ?? string.Empty;
		public void WaitForAsyncComplete() => Raw?.WaitForAsyncComplete();

		public TAsset Asset => Raw != null ? Raw.GetAssetObject<TAsset>() : null;
		public UnityEngine.Object AssetObject => Raw?.AssetObject;

		public void Dispose()
		{
			Raw?.Dispose();
			_onDispose?.Invoke(_id);
			HandleTracker.Unregister(_id);
		}
	}

	internal sealed class YooAllAssetsHandle<TAsset> : IAllAssetsHandle<TAsset> where TAsset : UnityEngine.Object
	{
		private readonly Action<int> _onDispose;
		private readonly int _id;
		internal readonly AllAssetsHandle Raw;
		private System.Collections.Generic.IReadOnlyList<TAsset> _cachedAssets; // cache to avoid per-access allocations

		public YooAllAssetsHandle(Action<int> onDispose, int id, AllAssetsHandle raw)
		{
			_onDispose = onDispose;
			_id = id;
			Raw = raw;
		}

		public bool IsDone => Raw == null || Raw.IsDone;
		public float Progress => Raw?.Progress ?? 0f;
		public string Error => Raw?.LastError ?? string.Empty;
		public void WaitForAsyncComplete() => Raw?.WaitForAsyncComplete();

		public IReadOnlyList<TAsset> Assets
		{
			get
			{
				if (_cachedAssets != null) return _cachedAssets;
				if (Raw == null) return Array.Empty<TAsset>();
				var objs = Raw.AllAssetObjects;
				if (objs == null || objs.Count == 0) return Array.Empty<TAsset>();
				// Only cache when operation is done to avoid capturing incomplete lists
				if (Raw.IsDone)
				{
					var list = new List<TAsset>(objs.Count);
					for (int i = 0; i < objs.Count; i++) list.Add(objs[i] as TAsset);
					_cachedAssets = list;
					return _cachedAssets;
				}
				// Not done yet: produce a transient empty list to avoid incorrect partial caching
				return Array.Empty<TAsset>();
			}
		}

		public void Dispose()
		{
			Raw?.Dispose();
			_onDispose?.Invoke(_id);
			HandleTracker.Unregister(_id);
		}
	}

	internal sealed class YooInstantiateHandle : IInstantiateHandle
	{
		private readonly Action<int> _onDispose;
		private readonly int _id;
		internal readonly InstantiateOperation Raw;

		public YooInstantiateHandle(Action<int> onDispose, int id, InstantiateOperation raw)
		{
			_onDispose = onDispose;
			_id = id;
			Raw = raw;
		}

		public bool IsDone => Raw == null || Raw.IsDone;
		public float Progress => Raw?.Progress ?? 0f;
		public string Error => Raw?.Error ?? string.Empty;
		public void WaitForAsyncComplete() { /* not supported for scene handle in this YooAsset version */ }

		public GameObject Instance => Raw?.Result;

		public void Dispose()
		{
			_onDispose?.Invoke(_id);
			HandleTracker.Unregister(_id);
		}
	}

	internal sealed class YooSceneHandle : ISceneHandle
	{
		private readonly Action<int> _onDispose;
		private readonly int _id;
		internal readonly SceneHandle Raw;

		public YooSceneHandle(Action<int> onDispose, int id, SceneHandle raw)
		{
			_onDispose = onDispose;
			_id = id;
			Raw = raw;
		}

		public bool IsDone => Raw == null || Raw.IsDone;
		public float Progress => Raw?.Progress ?? 0f;
		public string Error => Raw?.LastError ?? string.Empty;
		public void WaitForAsyncComplete() { /* this YooAsset version has no SceneHandle.WaitForAsyncComplete */ }

		public string ScenePath => Raw?.SceneName;

		public void Dispose()
		{
			Raw?.Dispose();
			_onDispose?.Invoke(_id);
			HandleTracker.Unregister(_id);
		}
	}

	internal sealed class YooDownloader : IDownloader
	{
		private readonly ResourceDownloaderOperation _op;

		public YooDownloader(ResourceDownloaderOperation op)
		{
			_op = op;
		}

		public bool IsDone => _op == null || _op.IsDone;
		public bool Succeed => _op != null && _op.Status == EOperationStatus.Succeed;
		public float Progress => _op?.Progress ?? 1f;
		public int TotalDownloadCount => _op?.TotalDownloadCount ?? 0;
		public int CurrentDownloadCount => _op?.CurrentDownloadCount ?? 0;
		public long TotalDownloadBytes => _op?.TotalDownloadBytes ?? 0;
		public long CurrentDownloadBytes => _op?.CurrentDownloadBytes ?? 0;
		public string Error => _op?.Error ?? string.Empty;

		public void Begin() => _op?.BeginDownload();

		public async System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken = default)
		{
			Begin();
			while (!IsDone)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					_op?.CancelDownload();
					throw new System.OperationCanceledException(cancellationToken);
				}
				await YieldUtil.Next(cancellationToken);
			}
		}

		public void Pause() => _op?.PauseDownload();
		public void Resume() => _op?.ResumeDownload();
		public void Cancel() => _op?.CancelDownload();

		public void Combine(IDownloader other)
		{
			if (_op == null) return;
			if (other is YooDownloader yd && yd._op != null)
			{
				_op.Combine(yd._op);
			}
		}
	}
}
#endif // YOOASSET_PRESENT