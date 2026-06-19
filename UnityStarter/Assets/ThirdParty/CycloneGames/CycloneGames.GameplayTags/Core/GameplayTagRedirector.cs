using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// Handles tag renaming and migration. When tags are renamed, old names are
   /// redirected to new names transparently during resolution.
   /// Redirect chains are flattened (A→B→C becomes A→C) for O(1) lookup.
   /// </summary>
   public static class GameplayTagRedirector
   {
      private static Dictionary<string, string> s_Redirects = new(StringComparer.Ordinal);
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
            Dictionary<string, string> redirects = new(s_Redirects, StringComparer.Ordinal);

            // Flatten redirect chain: follow newName to its final destination
            string finalTarget = ResolveChain(newName, redirects);

            // Check for circular redirect
            if (string.Equals(finalTarget, oldName, StringComparison.Ordinal))
            {
               GameplayTagLogger.LogWarning($"Circular tag redirect detected: '{oldName}' → '{newName}' → '{oldName}'. Redirect ignored.");
               return;
            }

            redirects[oldName] = finalTarget;

            // Re-flatten any existing redirects that pointed to oldName
            List<string> keysToUpdate = null;
            foreach (var kvp in redirects)
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
                  redirects[keysToUpdate[i]] = finalTarget;
            }

            s_Redirects = redirects;
         }
      }

      /// <summary>
      /// Batch-register multiple redirects.
      /// </summary>
      public static void AddRedirects(IEnumerable<KeyValuePair<string, string>> redirects)
      {
         if (redirects == null) return;
         foreach (var kvp in redirects)
         {
            if (!string.IsNullOrEmpty(kvp.Key) && !string.IsNullOrEmpty(kvp.Value))
               AddRedirect(kvp.Key, kvp.Value);
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

         // Copy-on-write table snapshot. Reads may see the previous table briefly,
         // but never a Dictionary being mutated concurrently.
         Dictionary<string, string> redirects = s_Redirects;
         return redirects.TryGetValue(tagName, out string redirected) ? redirected : tagName;
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
            if (!s_Redirects.ContainsKey(oldName))
               return false;

            Dictionary<string, string> redirects = new(s_Redirects, StringComparer.Ordinal);
            bool removed = redirects.Remove(oldName);
            s_Redirects = redirects;
            return removed;
         }
      }

      /// <summary>
      /// Clear all registered redirects.
      /// </summary>
      public static void ClearAll()
      {
         lock (s_Lock)
         {
            s_Redirects = new Dictionary<string, string>(StringComparer.Ordinal);
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

      private static string ResolveChain(string name, Dictionary<string, string> redirects)
      {
         const int maxHops = 16;
         string current = name;
         for (int i = 0; i < maxHops; i++)
         {
            if (!redirects.TryGetValue(current, out string next))
               return current;
            current = next;
         }

         GameplayTagLogger.LogWarning($"Tag redirect chain exceeded {maxHops} hops starting from '{name}'. Possible circular reference.");
         return current;
      }
   }
}
