using System;
using System.Collections.Generic;
using CycloneGames.Logger;

namespace CycloneGames.AssetManagement
{
	/// <summary>
	/// Enabled via AssetModuleOptions.EnableHandleTracking. Editor defaults to true.
	/// </summary>
	internal static class HandleTracker
	{
		private sealed class Record
		{
			public readonly string Descriptor;
			public readonly string PackageName;
			public readonly string CreationStack;
			public Record(string descriptor, string packageName, string creationStack)
			{
				Descriptor = descriptor;
				PackageName = packageName;
				CreationStack = creationStack;
			}
		}

		private static readonly Dictionary<int, Record> _records = new Dictionary<int, Record>(256);
		private static readonly object _lock = new object();

		public static bool Enabled { get; set; }

		public static void Register(int id, string packageName, string descriptor)
		{
			if (!Enabled) return;
			string stack = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			stack = Environment.StackTrace;
#endif
			lock (_lock)
			{
				_records[id] = new Record(descriptor ?? string.Empty, packageName ?? string.Empty, stack);
			}
		}

		public static void Unregister(int id)
		{
			if (!Enabled) return;
			lock (_lock)
			{
				_records.Remove(id);
			}
		}

		public static void ReportLeaks(string packageName)
		{
			if (!Enabled) return;
			List<KeyValuePair<int, Record>> leaked = null;
			lock (_lock)
			{
				foreach (var kv in _records)
				{
					if (string.Equals(kv.Value.PackageName, packageName, StringComparison.Ordinal))
					{
						(leaked ??= new List<KeyValuePair<int, Record>>()).Add(kv);
					}
				}
			}
			if (leaked == null || leaked.Count == 0) return;

			CLogger.LogWarning($"[AssetManagement] Detected {leaked.Count} undisposed handles in package '{packageName}'.");
			for (int i = 0; i < leaked.Count; i++)
			{
				var rec = leaked[i].Value;
				CLogger.LogWarning($"[AssetManagement] Leak {i + 1}/{leaked.Count}: '{rec.Descriptor}'\nStack:{rec.CreationStack}");
			}
		}
	}
}