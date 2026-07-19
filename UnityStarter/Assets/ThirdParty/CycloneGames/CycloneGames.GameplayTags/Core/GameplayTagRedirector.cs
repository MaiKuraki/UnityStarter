using System;
using System.Collections.Generic;
using System.Threading;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>Copy-on-write redirect table that resolves renamed gameplay tag aliases.</summary>
   public static class GameplayTagRedirector
   {
      public const int MaxRedirectCount = 4096;

      private sealed class RedirectTable
      {
         public readonly Dictionary<string, string> Entries;
         public readonly ulong ManifestHash;

         public RedirectTable(Dictionary<string, string> entries)
         {
            Entries = entries;
            if (entries.Count == 0)
            {
               ManifestHash = 0;
               return;
            }

            string[] keys = new string[entries.Count];
            entries.Keys.CopyTo(keys, 0);
            Array.Sort(keys, StringComparer.Ordinal);
            ulong hash = GameplayTagUtility.FnvOffsetBasis64;
            for (int i = 0; i < keys.Length; i++)
            {
               hash = GameplayTagUtility.CombineStableHash(hash, GameplayTagUtility.ComputeStableIdUnchecked(keys[i]));
               hash = GameplayTagUtility.CombineStableHash(hash, GameplayTagUtility.ComputeStableIdUnchecked(entries[keys[i]]));
            }
            ManifestHash = hash;
         }
      }

      private static readonly object s_Gate = new();
      private static RedirectTable s_Table = new(new Dictionary<string, string>(StringComparer.Ordinal));

      internal static ulong CurrentManifestHash => Volatile.Read(ref s_Table).ManifestHash;

      public static void AddRedirect(string oldName, string newName)
      {
         GameplayTagUtility.ValidateName(oldName);
         GameplayTagUtility.ValidateName(newName);
         if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return;

         lock (s_Gate)
         {
            Dictionary<string, string> next = new(Volatile.Read(ref s_Table).Entries, StringComparer.Ordinal);
            if (!next.ContainsKey(oldName) && next.Count >= MaxRedirectCount)
               throw new InvalidOperationException($"Gameplay tag redirect count cannot exceed {MaxRedirectCount}.");

            string finalTarget = ResolveChain(newName, next);
            if (string.Equals(finalTarget, oldName, StringComparison.Ordinal))
               throw new InvalidOperationException($"Circular gameplay tag redirect detected: '{oldName}' -> '{newName}'.");

            next[oldName] = finalTarget;
            string[] keys = new string[next.Count];
            int keyCount = 0;
            foreach (KeyValuePair<string, string> pair in next)
            {
               if (!string.Equals(pair.Key, oldName, StringComparison.Ordinal) &&
                   string.Equals(pair.Value, oldName, StringComparison.Ordinal))
               {
                  keys[keyCount++] = pair.Key;
               }
            }

            for (int i = 0; i < keyCount; i++)
               next[keys[i]] = finalTarget;

            ValidateAcyclic(next);
            Volatile.Write(ref s_Table, new RedirectTable(next));
         }
      }

      /// <summary>Adds an entire redirect batch atomically.</summary>
      public static void AddRedirects(IEnumerable<KeyValuePair<string, string>> redirects)
      {
         if (redirects == null)
            throw new ArgumentNullException(nameof(redirects));

         if (redirects is ICollection<KeyValuePair<string, string>> collection &&
             collection.Count > MaxRedirectCount)
         {
            throw new InvalidOperationException(
               $"Gameplay tag redirect batch cannot contain more than {MaxRedirectCount} entries.");
         }

         if (redirects is IReadOnlyCollection<KeyValuePair<string, string>> readOnlyCollection &&
             readOnlyCollection.Count > MaxRedirectCount)
         {
            throw new InvalidOperationException(
               $"Gameplay tag redirect batch cannot contain more than {MaxRedirectCount} entries.");
         }

         List<KeyValuePair<string, string>> batch = new();
         foreach (KeyValuePair<string, string> pair in redirects)
         {
            if (batch.Count == MaxRedirectCount)
            {
               throw new InvalidOperationException(
                  $"Gameplay tag redirect batch cannot contain more than {MaxRedirectCount} entries.");
            }

            GameplayTagUtility.ValidateName(pair.Key);
            GameplayTagUtility.ValidateName(pair.Value);
            batch.Add(pair);
         }

         if (batch.Count == 0)
            return;

         lock (s_Gate)
         {
            Dictionary<string, string> next = new(Volatile.Read(ref s_Table).Entries, StringComparer.Ordinal);
            for (int i = 0; i < batch.Count; i++)
            {
               KeyValuePair<string, string> pair = batch[i];
               if (!string.Equals(pair.Key, pair.Value, StringComparison.Ordinal))
                  next[pair.Key] = pair.Value;
               if (next.Count > MaxRedirectCount)
                  throw new InvalidOperationException($"Gameplay tag redirect count cannot exceed {MaxRedirectCount}.");
            }

            ValidateAcyclic(next);

            string[] keys = new string[next.Count];
            next.Keys.CopyTo(keys, 0);
            for (int i = 0; i < keys.Length; i++)
               next[keys[i]] = ResolveChain(next[keys[i]], next);

            Volatile.Write(ref s_Table, new RedirectTable(next));
         }
      }

      public static string Resolve(string tagName)
      {
         if (string.IsNullOrEmpty(tagName))
            return tagName;
         Dictionary<string, string> snapshot = Volatile.Read(ref s_Table).Entries;
         return snapshot.TryGetValue(tagName, out string target) ? target : tagName;
      }

      public static bool HasRedirect(string tagName)
      {
         return !string.IsNullOrEmpty(tagName) && Volatile.Read(ref s_Table).Entries.ContainsKey(tagName);
      }

      public static bool RemoveRedirect(string oldName)
      {
         if (string.IsNullOrEmpty(oldName))
            return false;

         lock (s_Gate)
         {
            Dictionary<string, string> current = Volatile.Read(ref s_Table).Entries;
            if (!current.ContainsKey(oldName))
               return false;
            Dictionary<string, string> next = new(current, StringComparer.Ordinal);
            bool removed = next.Remove(oldName);
            Volatile.Write(ref s_Table, new RedirectTable(next));
            return removed;
         }
      }

      public static void ClearAll()
      {
         lock (s_Gate)
            Volatile.Write(ref s_Table, new RedirectTable(new Dictionary<string, string>(StringComparer.Ordinal)));
      }

      public static IReadOnlyDictionary<string, string> GetAllRedirects()
      {
         return new Dictionary<string, string>(Volatile.Read(ref s_Table).Entries, StringComparer.Ordinal);
      }

      private static void ValidateAcyclic(Dictionary<string, string> redirects)
      {
         foreach (string key in redirects.Keys)
            ResolveChain(key, redirects);
      }

      private static string ResolveChain(string name, Dictionary<string, string> redirects)
      {
         HashSet<string> visited = new(StringComparer.Ordinal);
         string current = name;
         while (redirects.TryGetValue(current, out string next))
         {
            if (!visited.Add(current))
               throw new InvalidOperationException($"Circular gameplay tag redirect detected from '{name}'.");
            current = next;
         }
         return current;
      }
   }
}
