#if YOOASSET_PRESENT
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace CycloneGames.AssetManagement.Runtime
{
	public sealed class YooAssetHandle<TAsset> : IAssetHandle<TAsset> where TAsset : UnityEngine.Object
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
		public UniTask Task => Raw?.Task.AsUniTask() ?? UniTask.CompletedTask;
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

	public sealed class YooAllAssetsHandle<TAsset> : IAllAssetsHandle<TAsset> where TAsset : UnityEngine.Object
	{
		// Private utility class to wrap a list of Objects as a read-only list of TAsset, avoiding GC allocation of a new list.
		private sealed class ReadOnlyListAdapter<T> : IReadOnlyList<T> where T : UnityEngine.Object
		{
			private readonly IReadOnlyList<UnityEngine.Object> _source;

			public ReadOnlyListAdapter(IReadOnlyList<UnityEngine.Object> source)
			{
				_source = source ?? throw new ArgumentNullException(nameof(source));
			}

			public T this[int index] => _source[index] as T;
			public int Count => _source.Count;
			public System.Collections.Generic.IEnumerator<T> GetEnumerator()
			{
				foreach (var item in _source)
				{
					yield return item as T;
				}
			}
			System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
		}
		
		private readonly Action<int> _onDispose;
		private readonly int _id;
		internal readonly AllAssetsHandle Raw;
		private IReadOnlyList<TAsset> _cachedAssets;
		private IReadOnlyList<UnityEngine.Object> _assetObjects;

		public YooAllAssetsHandle(Action<int> onDispose, int id, AllAssetsHandle raw)
		{
			_onDispose = onDispose;
			_id = id;
			Raw = raw;
		}

		public bool IsDone => Raw == null || Raw.IsDone;
		public float Progress => Raw?.Progress ?? 0f;
		public string Error => Raw?.LastError ?? string.Empty;
		public UniTask Task => Raw?.Task.AsUniTask() ?? UniTask.CompletedTask;
		public void WaitForAsyncComplete() => Raw?.WaitForAsyncComplete();

		public IReadOnlyList<TAsset> Assets
		{
			get
			{
				if (_cachedAssets != null) return _cachedAssets;
				if (Raw == null || !Raw.IsDone) return Array.Empty<TAsset>();

				if (_assetObjects == null)
				{
					_assetObjects = Raw.AllAssetObjects;
				}
				
				if (_assetObjects == null || _assetObjects.Count == 0)
				{
					_cachedAssets = Array.Empty<TAsset>();
					return _cachedAssets;
				}

				_cachedAssets = new ReadOnlyListAdapter<TAsset>(_assetObjects);
				return _cachedAssets;
			}
		}

		public void Dispose()
		{
			Raw?.Dispose();
			_onDispose?.Invoke(_id);
			HandleTracker.Unregister(_id);
		}
	}

	public sealed class YooInstantiateHandle : IInstantiateHandle
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
		public UniTask Task => Raw?.Task.AsUniTask() ?? UniTask.CompletedTask;
		public void WaitForAsyncComplete() { /* not supported for scene handle in this YooAsset version */ }

		public GameObject Instance => Raw?.Result;

		public void Dispose()
		{
			_onDispose?.Invoke(_id);
			HandleTracker.Unregister(_id);
		}
	}

	public sealed class YooSceneHandle : ISceneHandle
	{
		private readonly Action<int> _onDispose;
		private readonly int _id;
		public readonly SceneHandle Raw;

		public YooSceneHandle(Action<int> onDispose, int id, SceneHandle raw)
		{
			_onDispose = onDispose;
			_id = id;
			Raw = raw;
		}

		public bool IsDone => Raw == null || Raw.IsDone;
		public float Progress => Raw?.Progress ?? 0f;
		public string Error => Raw?.LastError ?? string.Empty;
		public UniTask Task => Raw?.Task.AsUniTask() ?? UniTask.CompletedTask;
		public void WaitForAsyncComplete() { /* YooAsset has no SceneHandle.WaitForAsyncComplete */ }

		public string ScenePath => Raw?.SceneName;
		public Scene Scene => Raw.SceneObject;

		public void Dispose()
		{
			Raw?.Dispose();
			_onDispose?.Invoke(_id);
			HandleTracker.Unregister(_id);
		}
	}

	public sealed class YooDownloader : IDownloader
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

		public async UniTask StartAsync(System.Threading.CancellationToken cancellationToken = default)
		{
			Begin();
			while (!IsDone)
			{
				if (cancellationToken.IsCancellationRequested)
				{
					_op?.CancelDownload();
					throw new OperationCanceledException(cancellationToken);
				}
				await UniTask.Yield(cancellationToken);
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
