using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.AssetManagement.Runtime.Batch;
using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime.Preload
{
	public sealed class PreloadRunner : MonoBehaviour
	{
		public PreloadManifest Manifest;
		public IAssetPackage Package;
		public float Progress { get; private set; }
		public bool IsDone { get; private set; }
		public string Error { get; private set; }
		private readonly List<IAssetHandle<Object>> _retained = new List<IAssetHandle<Object>>(8);
		// Ties every preload operation to the runner's lifetime so OnDestroy cancels in-flight work.
		private CancellationTokenSource _lifetimeCts;

		/// <summary>
		/// Maximum number of handles to dispose per frame during cleanup.
		/// Prevents frame spikes when releasing large preload batches.
		/// </summary>
		private const int RELEASE_BATCH_SIZE = 8;

		public async UniTask RunAsync(CancellationToken cancellationToken = default)
		{
			IsDone = false; Progress = 0f; Error = null;
			if (Manifest == null || Package == null) { IsDone = true; return; }

			// Supersede any previous run and create a lifetime-linked token so cancellation from either the
			// caller or OnDestroy aborts the load batch and its progress loop deterministically.
			_lifetimeCts?.Cancel();
			_lifetimeCts?.Dispose();
			_lifetimeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			var token = _lifetimeCts.Token;

			var group = new GroupOperation();
			_retained.Clear();
			try
			{
				for (int i = 0; i < Manifest.Assets.Count; i++)
				{
					var entry = Manifest.Assets[i];
					var op = Package.LoadAssetAsync<Object>(entry.Location, cancellationToken: token); // warm and retain until after scene switch
					group.Add(op, Manifest.UseUniformWeights ? 1f : entry.Weight);
					_retained.Add(op);
				}

				// Run progress updates in the background without blocking the main await.
				UpdateProgress(group, token).Forget();

				await group.StartAsync(token);
				Error = group.Error;
			}
			catch (System.OperationCanceledException)
			{
				// Cancellation (e.g. component destroyed mid-preload) is not an error.
				// OnDestroy / ReleaseRetainedAsync handles releasing any retained handles.
			}
			finally
			{
				IsDone = true;
			}
		}

		private async UniTaskVoid UpdateProgress(IGroupOperation group, CancellationToken cancellationToken)
		{
			while (!group.IsDone && !cancellationToken.IsCancellationRequested)
			{
				Progress = group.Progress;
				await UniTask.Yield(cancellationToken);
			}
			// Ensure progress is set to 100% when done.
			if (group.IsDone) Progress = 1f;
		}

		private void OnDestroy()
		{
			_lifetimeCts?.Cancel();
			_lifetimeCts?.Dispose();
			_lifetimeCts = null;

			// Release remaining handles synchronously on destroy.
			// Most handles should already be released by ReleaseRetainedAsync;
			// this is a safety net for any stragglers.
			for (int i = 0; i < _retained.Count; i++)
			{
				_retained[i]?.Dispose();
			}
			_retained.Clear();
		}

		/// <summary>
		/// Releases retained handles spread across multiple frames to avoid frame spikes.
		/// Call this instead of waiting for OnDestroy when you want amortized cleanup
		/// (e.g. after the loading screen is shown but before the runner is destroyed).
		/// </summary>
		public async UniTask ReleaseRetainedAsync(CancellationToken cancellationToken = default)
		{
			if (_retained.Count == 0) return;

			// Link to the runner lifetime (without replacing it) so OnDestroy can abort an in-progress drain.
			var lifetimeToken = _lifetimeCts?.Token ?? CancellationToken.None;
			using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetimeToken))
			{
				try
				{
					int count = 0;
					for (int i = _retained.Count - 1; i >= 0; i--)
					{
						_retained[i]?.Dispose();
						_retained[i] = null;
						count++;

						if (count >= RELEASE_BATCH_SIZE)
						{
							count = 0;
							await UniTask.Yield(linked.Token);
						}
					}
					_retained.Clear();
				}
				catch (System.OperationCanceledException) { /* OnDestroy will clean up */ }
			}
		}
	}
}