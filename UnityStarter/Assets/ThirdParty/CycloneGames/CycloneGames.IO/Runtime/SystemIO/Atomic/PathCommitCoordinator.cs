using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace CycloneGames.IO
{
    internal static class PathCommitCoordinator
    {
        private static readonly ConcurrentDictionary<string, Entry> Entries =
            new ConcurrentDictionary<string, Entry>(PathComparer);

        internal static int EntryCount => Entries.Count;

        internal static IDisposable Acquire(string destinationPath)
        {
            string key = Path.GetFullPath(destinationPath);
            while (true)
            {
                Entry entry = Entries.GetOrAdd(key, CreateEntry);
                lock (entry.StateGate)
                {
                    if (entry.IsRemoved)
                    {
                        continue;
                    }

                    entry.ReferenceCount++;
                }

                Monitor.Enter(entry.CommitGate);
                return new Lease(key, entry);
            }
        }

        private static StringComparer PathComparer =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        private static Entry CreateEntry(string _)
        {
            return new Entry();
        }

        private static void Release(string key, Entry entry)
        {
            Monitor.Exit(entry.CommitGate);
            lock (entry.StateGate)
            {
                entry.ReferenceCount--;
                if (entry.ReferenceCount != 0)
                {
                    return;
                }

                entry.IsRemoved = true;
                Entries.TryRemove(key, out _);
            }
        }

        private sealed class Entry
        {
            internal object StateGate { get; } = new object();

            internal object CommitGate { get; } = new object();

            internal int ReferenceCount { get; set; }

            internal bool IsRemoved { get; set; }
        }

        private sealed class Lease : IDisposable
        {
            private readonly string _key;
            private Entry _entry;

            internal Lease(string key, Entry entry)
            {
                _key = key;
                _entry = entry;
            }

            public void Dispose()
            {
                Entry entry = Interlocked.Exchange(ref _entry, null);
                if (entry != null)
                {
                    Release(_key, entry);
                }
            }
        }
    }
}
