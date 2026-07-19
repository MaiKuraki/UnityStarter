using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Core
{
   internal class GameplayTagRegistrationError
   {
      public string Message { get; }
      public IGameplayTagSource Source { get; }
      public string TagName { get; }

      public GameplayTagRegistrationError(string message, IGameplayTagSource source, string tagName)
      {
         Message = message;
         Source = source;
         TagName = tagName;
      }
   }

   public class GameplayTagRegistrationContext
   {
      internal const int DefaultMaxRegistrationAttemptCount = GameplayTagUtility.MaxRegisteredTagCount * 2;
      internal const int DefaultMaxRetainedDiagnosticCount = 128;

      private readonly List<GameplayTagDefinition> m_Definition = new();
      private readonly Dictionary<string, GameplayTagDefinition> m_TagsByName = new(StringComparer.Ordinal);
      private readonly List<GameplayTagRegistrationError> m_RegistrationErrors = new();
      private readonly int m_MaxRegisteredTagCount;
      private readonly int m_MaxRegistrationAttemptCount;
      private readonly int m_MaxRetainedDiagnosticCount;
      private GameplayTagRegistrationError m_TerminalRegistrationError;
      private int m_RegistrationAttemptCount;
      private int m_TotalRegistrationErrorCount;
      private int m_SuppressedRegistrationErrorCount;

      /// <summary>
      /// True after a terminal registration budget error. Sources should stop enumerating input when this becomes true.
      /// </summary>
      public bool IsRegistrationTerminated => m_TerminalRegistrationError != null;

      public GameplayTagRegistrationContext()
         : this(
            GameplayTagUtility.MaxRegisteredTagCount,
            DefaultMaxRegistrationAttemptCount,
            DefaultMaxRetainedDiagnosticCount)
      { }

      internal GameplayTagRegistrationContext(
         int maxRegisteredTagCount,
         int maxRegistrationAttemptCount,
         int maxRetainedDiagnosticCount)
      {
         if (maxRegisteredTagCount <= 0 || maxRegisteredTagCount > GameplayTagUtility.MaxRegisteredTagCount)
            throw new ArgumentOutOfRangeException(nameof(maxRegisteredTagCount));
         if (maxRegistrationAttemptCount < maxRegisteredTagCount)
            throw new ArgumentOutOfRangeException(nameof(maxRegistrationAttemptCount));
         if (maxRetainedDiagnosticCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxRetainedDiagnosticCount));

         m_MaxRegisteredTagCount = maxRegisteredTagCount;
         m_MaxRegistrationAttemptCount = maxRegistrationAttemptCount;
         m_MaxRetainedDiagnosticCount = maxRetainedDiagnosticCount;
      }

      public bool RegisterTag(string name, string description, GameplayTagFlags flags, IGameplayTagSource source = null)
      {
         return RegisterTagInternal(name, description, flags, source);
      }

      private bool RegisterTagInternal(string name, string description, GameplayTagFlags flags, IGameplayTagSource source)
      {
         if (!TryBeginRegistrationAttempt(name, source))
            return false;

         if (!GameplayTagUtility.IsNameValid(name, out string errorMessage))
         {
            AddRegistrationError(errorMessage, source, name);
            return false;
         }

         if (m_TagsByName.TryGetValue(name, out GameplayTagDefinition existingDefinition))
         {
            if (string.IsNullOrEmpty(existingDefinition.Description) && !string.IsNullOrEmpty(description))
               existingDefinition.Description = description;

            if (source != null)
               existingDefinition.AddSource(source);

            return true;
         }

         if (m_TagsByName.Count >= m_MaxRegisteredTagCount)
         {
            TerminateRegistration(
               $"Registry tag count cannot exceed {m_MaxRegisteredTagCount}.",
               source,
               name);
            return false;
         }

         GameplayTagDefinition definition = new(name, description, flags);

         if (source != null)
            definition.AddSource(source);

         m_TagsByName.Add(name, definition);
         m_Definition.Add(definition);

         return true;
      }

      internal List<GameplayTagDefinition> GenerateDefinitions(
         bool addNoneTag,
         IReadOnlyDictionary<string, int> preferredRuntimeIndices = null)
      {
         if (HasRegistrationErrors || !RegisterMissingParents())
            return null;

         SortDefinitions(preferredRuntimeIndices);
         if (addNoneTag)
         {
            RegisterNoneTag();
         }
         SetTagRuntimeIndices();
         FillParentsAndChildren();
         SetHierarchyTags();

         return m_Definition;
      }

      private void RegisterNoneTag()
      {
         m_Definition.Insert(0, GameplayTagDefinition.NoneTagDefinition);
      }

      private bool RegisterMissingParents()
      {
         int remainingCapacity = m_MaxRegisteredTagCount - m_TagsByName.Count;
         Dictionary<string, GameplayTagFlags> missingParents = new(StringComparer.Ordinal);

         for (int definitionIndex = 0; definitionIndex < m_Definition.Count; definitionIndex++)
         {
            GameplayTagDefinition definition = m_Definition[definitionIndex];
            GameplayTagFlags flags = definition.Flags;
            string parentTagName = GameplayTagUtility.GetParentNameUnchecked(definition.TagName);
            while (!string.IsNullOrEmpty(parentTagName))
            {
               if (m_TagsByName.TryGetValue(parentTagName, out GameplayTagDefinition parentTag))
               {
                  flags |= parentTag.Flags;
                  parentTagName = GameplayTagUtility.GetParentNameUnchecked(parentTagName);
                  continue;
               }

               if (missingParents.TryGetValue(parentTagName, out GameplayTagFlags pendingFlags))
               {
                  flags |= pendingFlags;
                  missingParents[parentTagName] = flags;
                  parentTagName = GameplayTagUtility.GetParentNameUnchecked(parentTagName);
                  continue;
               }

               if (missingParents.Count >= remainingCapacity)
               {
                  TerminateRegistration(
                     $"Registry tag count including implicit parents cannot exceed {m_MaxRegisteredTagCount}.",
                     null,
                     definition.TagName);
                  return false;
               }

               missingParents.Add(parentTagName, flags);
               parentTagName = GameplayTagUtility.GetParentNameUnchecked(parentTagName);
            }
         }

         foreach (KeyValuePair<string, GameplayTagFlags> pair in missingParents)
         {
            GameplayTagDefinition definition = new(pair.Key, string.Empty, pair.Value);
            m_TagsByName.Add(pair.Key, definition);
            m_Definition.Add(definition);
         }

         return true;
      }

      private void SortDefinitions(IReadOnlyDictionary<string, int> preferredRuntimeIndices)
      {
         if (preferredRuntimeIndices == null || preferredRuntimeIndices.Count == 0)
         {
            m_Definition.Sort((a, b) => string.Compare(a.TagName, b.TagName, StringComparison.Ordinal));
            return;
         }

         m_Definition.Sort((a, b) =>
         {
            bool hasA = preferredRuntimeIndices.TryGetValue(a.TagName, out int indexA);
            bool hasB = preferredRuntimeIndices.TryGetValue(b.TagName, out int indexB);
            if (hasA && hasB)
               return indexA.CompareTo(indexB);
            if (hasA)
               return -1;
            if (hasB)
               return 1;
            return string.Compare(a.TagName, b.TagName, StringComparison.Ordinal);
         });
      }

      private void FillParentsAndChildren()
      {
         Dictionary<GameplayTagDefinition, List<GameplayTagDefinition>> childrenLists = new();

         // Skip the first tag definition which is the "None" tag
         for (int i = 1; i < m_Definition.Count; i++)
         {
            GameplayTagDefinition definition = m_Definition[i];
            string immediateParentName = GameplayTagUtility.GetParentNameUnchecked(definition.TagName);
            if (!string.IsNullOrEmpty(immediateParentName) && m_TagsByName.TryGetValue(immediateParentName, out GameplayTagDefinition immediateParent))
            {
               definition.SetParent(immediateParent);
            }

            string parentTagName = immediateParentName;
            while (!string.IsNullOrEmpty(parentTagName))
            {
               GameplayTagDefinition parentDefinition = m_TagsByName[parentTagName];
               if (!childrenLists.TryGetValue(parentDefinition, out List<GameplayTagDefinition> children))
               {
                  children = new();
                  childrenLists.Add(parentDefinition, children);
               }

               children.Add(definition);
               parentTagName = GameplayTagUtility.GetParentNameUnchecked(parentTagName);
            }
         }

         foreach ((GameplayTagDefinition definition, List<GameplayTagDefinition> children) in childrenLists)
         {
            definition.SetChildren(children);
         }
      }

      private void SetHierarchyTags()
      {
         for (int i = 1; i < m_Definition.Count; i++)
         {
            GameplayTagDefinition definition = m_Definition[i];

            ReadOnlySpan<GameplayTag> parentHierarchy = definition.ParentTagDefinition != null
               ? definition.ParentTagDefinition.HierarchyTags
               : ReadOnlySpan<GameplayTag>.Empty;

            GameplayTag[] hierarchyTags = new GameplayTag[parentHierarchy.Length + 1];
            parentHierarchy.CopyTo(hierarchyTags);
            hierarchyTags[parentHierarchy.Length] = definition.Tag;

            definition.SetHierarchyTags(hierarchyTags);
         }
      }

      private void SetTagRuntimeIndices()
      {
         for (int i = 0; i < m_Definition.Count; i++)
            m_Definition[i].SetRuntimeIndex(i);
      }

      internal IEnumerable<GameplayTagRegistrationError> GetRegistrationErrors()
      {
         for (int i = 0; i < m_RegistrationErrors.Count; i++)
            yield return m_RegistrationErrors[i];

         if (m_TerminalRegistrationError != null)
            yield return m_TerminalRegistrationError;
      }

      internal bool HasRegistrationErrors => m_TotalRegistrationErrorCount != 0;
      internal int RegistrationErrorCount => m_TotalRegistrationErrorCount;
      internal int SuppressedRegistrationErrorCount => m_SuppressedRegistrationErrorCount;
      internal int RegistrationAttemptCount => m_RegistrationAttemptCount;
      internal int RegisteredTagCount => m_TagsByName.Count;

      private bool TryBeginRegistrationAttempt(string name, IGameplayTagSource source)
      {
         if (IsRegistrationTerminated)
            return false;

         if (m_RegistrationAttemptCount >= m_MaxRegistrationAttemptCount)
         {
            TerminateRegistration(
               $"Gameplay tag registration attempts cannot exceed {m_MaxRegistrationAttemptCount} per registry candidate.",
               source,
               name);
            return false;
         }

         m_RegistrationAttemptCount++;
         return true;
      }

      private void AddRegistrationError(string message, IGameplayTagSource source, string tagName)
      {
         m_TotalRegistrationErrorCount++;
         if (m_RegistrationErrors.Count < m_MaxRetainedDiagnosticCount)
         {
            m_RegistrationErrors.Add(new GameplayTagRegistrationError(message, source, tagName));
         }
         else
         {
            m_SuppressedRegistrationErrorCount++;
         }
      }

      private void TerminateRegistration(string message, IGameplayTagSource source, string tagName)
      {
         if (IsRegistrationTerminated)
            return;

         m_TerminalRegistrationError = new GameplayTagRegistrationError(message, source, tagName);
         m_TotalRegistrationErrorCount++;
      }
   }
}
