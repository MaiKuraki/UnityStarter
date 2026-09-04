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

      internal static int CurrentCount => Volatile.Read(ref s_Table).Entries.Count;

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

            // No whole-table pass here. The old table was already acyclic, and this edit maps oldName to a
            // fully resolved terminal and rewrites everything that pointed at oldName to that same
            // terminal, so the only cycle it could introduce is oldName reaching itself - which
            // ResolveChain already rejected above.
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

            // Flattening resolves every chain and ResolveChain rejects cycles as it walks, so this single
            // pass is both the normalization and the acyclicity check. There is no separate whole-table
            // validation, which is what used to make a batch cost one full walk per entry.
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

      private static string ResolveChain(string name, Dictionary<string, string> redirects)
      {
         // A chain that walks more entries than the table holds must have revisited one, so a depth
         // budget is a complete cycle detector. The HashSet this replaces allocated once per call, and
         // validation used to call it once per entry, which turned a batch import into a quadratic
         // allocation storm on the critical path of a cold start.
         string current = name;
         int budget = redirects.Count + 1;
         while (redirects.TryGetValue(current, out string next))
         {
            if (--budget == 0)
               throw new InvalidOperationException($"Circular gameplay tag redirect detected from '{name}'.");

            current = next;
         }

         return current;
      }
   }
}
