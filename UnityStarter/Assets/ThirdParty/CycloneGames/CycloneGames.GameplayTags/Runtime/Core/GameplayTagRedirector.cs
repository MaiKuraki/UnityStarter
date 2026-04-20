using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Runtime
{
   /// <summary>
   /// Handles tag renaming and migration. When tags are renamed, old names are
   /// redirected to new names transparently during resolution.
   /// Redirect chains are flattened (A→B→C becomes A→C) for O(1) lookup.
   /// </summary>
   public static class GameplayTagRedirector
   {
      private static readonly Dictionary<string, string> s_Redirects = new(StringComparer.Ordinal);
      private static readonly object s_Lock = new();

      /// <summary>
      /// Register a tag redirect from oldName to newName.
      /// If oldName already redirects somewhere, the chain is flattened.
      /// </summary>
      public static void AddRedirect(string oldName, string newName)
      {
         if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName))
            return;
         if (string.Equals(oldName, newName, StringComparison.Ordinal))
            return;

         lock (s_Lock)
         {
            // Flatten redirect chain: follow newName to its final destination
            string finalTarget = ResolveChainLocked(newName);

            // Check for circular redirect
            if (string.Equals(finalTarget, oldName, StringComparison.Ordinal))
            {
               GameplayTagLogger.LogWarning($"Circular tag redirect detected: '{oldName}' → '{newName}' → '{oldName}'. Redirect ignored.");
               return;
            }

            s_Redirects[oldName] = finalTarget;

            // Re-flatten any existing redirects that pointed to oldName
            List<string> keysToUpdate = null;
            foreach (var kvp in s_Redirects)
            {
               if (string.Equals(kvp.Value, oldName, StringComparison.Ordinal) &&
                   !string.Equals(kvp.Key, oldName, StringComparison.Ordinal))
               {
                  keysToUpdate ??= new List<string>();
                  keysToUpdate.Add(kvp.Key);
               }
            }

            if (keysToUpdate != null)
            {
               for (int i = 0; i < keysToUpdate.Count; i++)
                  s_Redirects[keysToUpdate[i]] = finalTarget;
            }
         }
      }

      /// <summary>
      /// Batch-register multiple redirects.
      /// </summary>
      public static void AddRedirects(IEnumerable<KeyValuePair<string, string>> redirects)
      {
         if (redirects == null) return;
         lock (s_Lock)
         {
            foreach (var kvp in redirects)
            {
               if (!string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value))
                  AddRedirect(kvp.Key, kvp.Value);
            }
         }
      }

      /// <summary>
      /// Resolve a tag name through the redirect table.
      /// Returns the original name if no redirect exists.
      /// O(1) lookup since chains are pre-flattened.
      /// </summary>
      public static string Resolve(string tagName)
      {
         if (string.IsNullOrEmpty(tagName))
            return tagName;

         // Lock-free read — safe because Dictionary reads are thread-safe
         // when there are no concurrent writes, and we only write under lock.
         // For full thread-safety during concurrent writes, we'd need a ConcurrentDictionary.
         // The lock ensures writes are serialized; reads may see stale data briefly, which is acceptable.
         return s_Redirects.TryGetValue(tagName, out string redirected) ? redirected : tagName;
      }

      /// <summary>
      /// Check if a tag name has a redirect registered.
      /// </summary>
      public static bool HasRedirect(string tagName)
      {
         if (string.IsNullOrEmpty(tagName))
            return false;
         return s_Redirects.ContainsKey(tagName);
      }

      /// <summary>
      /// Remove a redirect for the given old name.
      /// </summary>
      public static bool RemoveRedirect(string oldName)
      {
         if (string.IsNullOrEmpty(oldName))
            return false;
         lock (s_Lock)
         {
            return s_Redirects.Remove(oldName);
         }
      }

      /// <summary>
      /// Clear all registered redirects.
      /// </summary>
      public static void ClearAll()
      {
         lock (s_Lock)
         {
            s_Redirects.Clear();
         }
      }

      /// <summary>
      /// Get all current redirects (for serialization/debugging).
      /// </summary>
      public static IReadOnlyDictionary<string, string> GetAllRedirects()
      {
         lock (s_Lock)
         {
            return new Dictionary<string, string>(s_Redirects, StringComparer.Ordinal);
         }
      }

      private static string ResolveChainLocked(string name)
      {
         const int maxHops = 16;
         string current = name;
         for (int i = 0; i < maxHops; i++)
         {
            if (!s_Redirects.TryGetValue(current, out string next))
               return current;
            current = next;
         }

         GameplayTagLogger.LogWarning($"Tag redirect chain exceeded {maxHops} hops starting from '{name}'. Possible circular reference.");
         return current;
      }
   }
}
