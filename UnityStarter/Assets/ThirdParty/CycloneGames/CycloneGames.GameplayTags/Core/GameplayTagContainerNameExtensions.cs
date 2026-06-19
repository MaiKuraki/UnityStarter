using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Core
{
   public static class GameplayTagContainerNameExtensions
   {
      public static GameplayTagRequirements CreateRequirementsFromTagNames(
         IEnumerable<string> forbiddenTagNames,
         IEnumerable<string> requiredTagNames,
         bool ignoreMissing = false)
      {
         return new GameplayTagRequirements(
            FromTagNames(forbiddenTagNames, ignoreMissing),
            FromTagNames(requiredTagNames, ignoreMissing));
      }

      public static GameplayTagRequirements CreateRequirementsFromTagNames(
         IReadOnlyList<string> forbiddenTagNames,
         IReadOnlyList<string> requiredTagNames,
         bool ignoreMissing = false)
      {
         return new GameplayTagRequirements(
            FromTagNames(forbiddenTagNames, ignoreMissing),
            FromTagNames(requiredTagNames, ignoreMissing));
      }

      public static GameplayTagContainer FromTagNames(IEnumerable<string> tagNames, bool ignoreMissing = false)
      {
         GameplayTagContainer container = new();
         container.AddTagNames(tagNames, ignoreMissing);
         return container;
      }

      public static GameplayTagContainer FromTagNames(IReadOnlyList<string> tagNames, bool ignoreMissing = false)
      {
         GameplayTagContainer container = new();
         container.AddTagNames(tagNames, ignoreMissing);
         return container;
      }

      public static GameplayTagContainer FromDelimitedTagNames(string tagNames, char separator = ',', bool ignoreMissing = false)
      {
         GameplayTagContainer container = new();
         container.AddDelimitedTagNames(tagNames, separator, ignoreMissing);
         return container;
      }

      public static void AddTagNames(this GameplayTagContainer container, IEnumerable<string> tagNames, bool ignoreMissing = false)
      {
         if (container == null)
         {
            throw new ArgumentNullException(nameof(container));
         }

         if (tagNames == null)
         {
            return;
         }

         if (tagNames is IReadOnlyList<string> tagNameList)
         {
            AddTagNames(container, tagNameList, ignoreMissing);
            return;
         }

         foreach (string tagName in tagNames)
         {
            AddTagName(container, tagName, ignoreMissing);
         }
      }

      public static void AddDelimitedTagNames(this GameplayTagContainer container, string tagNames, char separator = ',', bool ignoreMissing = false)
      {
         if (container == null)
         {
            throw new ArgumentNullException(nameof(container));
         }

         if (string.IsNullOrWhiteSpace(tagNames))
         {
            return;
         }

         int segmentStart = 0;
         for (int i = 0; i <= tagNames.Length; i++)
         {
            if (i != tagNames.Length && tagNames[i] != separator)
            {
               continue;
            }

            string tagName = tagNames.Substring(segmentStart, i - segmentStart).Trim();
            AddTagName(container, tagName, ignoreMissing);
            segmentStart = i + 1;
         }
      }

      public static void AddTagNames(this GameplayTagContainer container, IReadOnlyList<string> tagNames, bool ignoreMissing = false)
      {
         if (container == null)
         {
            throw new ArgumentNullException(nameof(container));
         }

         if (tagNames == null)
         {
            return;
         }

         for (int i = 0; i < tagNames.Count; i++)
         {
            AddTagName(container, tagNames[i], ignoreMissing);
         }
      }

      public static bool TryAddTagName(this GameplayTagContainer container, string tagName)
      {
         if (container == null)
         {
            throw new ArgumentNullException(nameof(container));
         }

         if (!GameplayTagManager.TryRequestTag(tagName, out GameplayTag tag))
         {
            return false;
         }

         container.AddTag(tag);
         return true;
      }

      private static void AddTagName(GameplayTagContainer container, string tagName, bool ignoreMissing)
      {
         if (string.IsNullOrWhiteSpace(tagName))
         {
            if (ignoreMissing)
            {
               return;
            }

            throw new ArgumentException("Gameplay tag name cannot be empty.", nameof(tagName));
         }

         if (!GameplayTagManager.TryRequestTag(tagName, out GameplayTag tag))
         {
            if (ignoreMissing)
            {
               return;
            }

            throw new InvalidOperationException($"Gameplay tag \"{tagName}\" is not registered.");
         }

         container.AddTag(tag);
      }
   }
}
