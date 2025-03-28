using System;
using System.Collections.Generic;
using System.Linq;

namespace CycloneGames.GameplayTags
{
   internal class GameplayTagRegistrationContext
   {
      private List<GameplayTagDefinition> m_Definition = new();
      private Dictionary<string, GameplayTagDefinition> m_TagsByName = new();

      public void RegisterTag(string name, string description = null, GameplayTagFlags flags = GameplayTagFlags.None)
      {
         GameplayTagUtility.ValidateName(name);

         if (m_TagsByName.ContainsKey(name))
         {
            return;
         }

         GameplayTagDefinition definition = new(name, description, flags);

         m_TagsByName.Add(name, definition);
         m_Definition.Add(definition);
      }

      public GameplayTagDefinition[] GenerateDefinitions(bool bAutoAddNoneTag = false)
      {
         RegisterMissingParents();
         SortDefinitionsAlphabetically();
         if(bAutoAddNoneTag) RegisterNoneTag();
         SetTagRuntimeIndices();
         FillParentsAndChildren();
         SetHierarchyTags();

         return m_Definition.ToArray();
      }

      private void RegisterNoneTag()
      {
         m_Definition.Insert(0, GameplayTagDefinition.CreateNoneTagDefinition());
      }

      private void RegisterMissingParents()
      {
         List<GameplayTagDefinition> definitions = new(m_Definition);
         foreach (GameplayTagDefinition definition in definitions)
         {
            string[] parentTagNames = GameplayTagUtility.GetHeirarchyNames(definition.TagName);

            GameplayTagFlags flags = definition.Flags;
            foreach (string parentTagName in Enumerable.Reverse(parentTagNames))
            {
               if (m_TagsByName.TryGetValue(parentTagName, out GameplayTagDefinition parentTag))
               {
                  flags |= parentTag.Flags;
                  continue;
               }

               RegisterTag(parentTagName, string.Empty, flags);
            }
         }
      }

      private void SortDefinitionsAlphabetically()
      {
         m_Definition.Sort((a, b) => string.Compare(a.TagName, b.TagName, StringComparison.OrdinalIgnoreCase));
      }

      private void FillParentsAndChildren()
      {
         Dictionary<GameplayTagDefinition, List<GameplayTagDefinition>> childrenLists = new();

         // Skip the first tag definition which is the "None" tag
         for (int i = 1; i < m_Definition.Count; i++)
         {
            GameplayTagDefinition definition = m_Definition[i];
            string[] parentTagNames = GameplayTagUtility.GetHeirarchyNames(definition.TagName);
            for (int j = 0; j < parentTagNames.Length - 1; j++)
            {
               string parentTagName = parentTagNames[j];
               GameplayTagDefinition parentDefinition = m_TagsByName[parentTagName];
               if (!childrenLists.TryGetValue(parentDefinition, out List<GameplayTagDefinition> children))
               {
                  children = new();
                  childrenLists.Add(parentDefinition, children);
               }

               children.Add(definition);
            }
         }

         foreach ((GameplayTagDefinition definition, List<GameplayTagDefinition> children) in childrenLists)
         {
            definition.SetChildren(children);
            foreach (GameplayTagDefinition child in children)
            {
               child.SetParent(definition);
            }
         }
      }

      private void SetHierarchyTags()
      {
         for (int i = 1; i < m_Definition.Count; i++)
         {
            GameplayTagDefinition definition = m_Definition[i];

            List<GameplayTag> hierarcyTags = new();

            if (definition.ParentTagDefinition != null)
            {
               hierarcyTags.AddRange(definition.ParentTagDefinition.HierarchyTags.ToArray());
            }

            hierarcyTags.Add(definition.Tag);
            definition.SetHierarchyTags(hierarcyTags.ToArray());
         }
      }

      private void SetTagRuntimeIndices()
      {
         for (int i = 0; i < m_Definition.Count; i++)
         {
            m_Definition[i].SetRuntimeIndex(i);
         }
      }
   }
}