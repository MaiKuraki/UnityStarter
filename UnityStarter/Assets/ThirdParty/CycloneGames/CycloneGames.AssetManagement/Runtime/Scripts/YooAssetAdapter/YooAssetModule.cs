#if YOOASSET_PRESENT
using System;
using System.Collections.Generic;
using YooAsset;
using CycloneGames.Logger;

namespace CycloneGames.AssetManagement.Runtime
{
	public sealed class YooAssetModule : IAssetModule
	{
		private readonly Dictionary<string, YooAssetPackage> _packages = new Dictionary<string, YooAssetPackage>(StringComparer.Ordinal);
		private bool _initialized;

		public bool Initialized => _initialized;

		public void Initialize(AssetModuleOptions options = default)
		{
			if (_initialized) return;
			YooAssets.Initialize(ToYooLogger(options.Logger));
			if (options.OperationSystemMaxTimeSliceMs > 0)
			{
				YooAssets.SetOperationSystemMaxTimeSlice(options.OperationSystemMaxTimeSliceMs);
			}
			HandleTracker.Enabled = options.EnableHandleTracking;
			// Note: BundleLoadingMaxConcurrency can only be applied per package through InitializeParameters.
			_initialized = true;
		}

		private static YooAsset.ILogger ToYooLogger(UnityEngine.ILogger logger)
		{
			if (logger != null) return new UnityToYooLoggerAdapter(logger);
			return new CycloneToYooLoggerAdapter();
		}

		public void Destroy()
		{
			if (!_initialized) return;
			YooAssets.Destroy();
			_packages.Clear();
			_initialized = false;
		}

		public IAssetPackage CreatePackage(string packageName)
		{
			if (string.IsNullOrEmpty(packageName)) throw new ArgumentException("[YooAssetModule] Package name is null or empty", nameof(packageName));
			if (!_initialized) throw new InvalidOperationException("[YooAssetModule] Asset module not initialized");
			if (_packages.ContainsKey(packageName)) throw new InvalidOperationException($"[YooAssetModule] Package already exists: {packageName}");

			var yooPackage = YooAssets.CreatePackage(packageName);
			var wrapped = new YooAssetPackage(yooPackage);
			_packages.Add(packageName, wrapped);
			return wrapped;
		}

		public IAssetPackage GetPackage(string packageName)
		{
			if (string.IsNullOrEmpty(packageName)) return null;
			_packages.TryGetValue(packageName, out var pkg);
			return pkg;
		}

		public bool RemovePackage(string packageName)
		{
			if (string.IsNullOrEmpty(packageName)) return false;
			if (!_packages.TryGetValue(packageName, out var pkg)) return false;
			if (pkg.IsAlive)
			{
				return false; // must DestroyAsync before remove
			}
			YooAssets.RemovePackage(((YooAssetPackage)pkg).Raw);
			_packages.Remove(packageName);
			return true;
		}

		public IReadOnlyList<string> GetAllPackageNames()
		{
			var names = new List<string>(_packages.Count);
			foreach (var kv in _packages)
			{
				names.Add(kv.Key);
			}
			return names;
		}
	}

	internal sealed class UnityToYooLoggerAdapter : YooAsset.ILogger
	{
		private readonly UnityEngine.ILogger _inner;
		public UnityToYooLoggerAdapter(UnityEngine.ILogger inner) { _inner = inner; }
		public void Log(string message) => _inner.Log(message);
		public void Warning(string message) => _inner.LogWarning(string.Empty, message);
		public void Error(string message) => _inner.LogError(string.Empty, message);
		public void Exception(System.Exception exception) => _inner.LogException(exception);
	}

	internal sealed class CycloneToYooLoggerAdapter : YooAsset.ILogger
	{
		public void Log(string message)
		{
			CLogger.LogDebug(message, "YooAsset");
		}

		public void Warning(string message)
		{
			CLogger.LogWarning(message, "YooAsset");
		}

		public void Error(string message)
		{
			CLogger.LogError(message, "YooAsset");
		}

		public void Exception(System.Exception exception)
		{
			CLogger.LogError(exception?.ToString() ?? "<null>", "YooAsset");
		}
	}
}
#endif // YOOASSET_PRESENT