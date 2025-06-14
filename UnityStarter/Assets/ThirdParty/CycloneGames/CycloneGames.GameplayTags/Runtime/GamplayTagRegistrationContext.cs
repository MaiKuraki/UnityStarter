using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Runtime
{
    internal class GameplayTagRegistrationContext
    {
        private List<GameplayTagDefinition> m_Definitions;
        private Dictionary<string, GameplayTagDefinition> m_TagsByName;

        public GameplayTagRegistrationContext()
        {
            m_Definitions = new List<GameplayTagDefinition>();
            m_TagsByName = new Dictionary<string, GameplayTagDefinition>();
        }

        // This is crucial for the dynamic tag registration to work without losing static tags.
        public GameplayTagRegistrationContext(List<GameplayTagDefinition> existingDefinitions)
        {
            m_Definitions = new List<GameplayTagDefinition>(existingDefinitions.Count);
            m_TagsByName = new Dictionary<string, GameplayTagDefinition>(existingDefinitions.Count);

            // We must copy the definitions, excluding the "None" tag which will be handled later.
            foreach (var def in existingDefinitions)
            {
                if (def.RuntimeIndex == 0) continue; // Skip the "None" tag

                // We add them to our internal lists to be processed.
                m_Definitions.Add(def);
                m_TagsByName.Add(def.TagName, def);
            }
        }

        public void RegisterTag(string name, string description = null, GameplayTagFlags flags = GameplayTagFlags.None)
        {
            GameplayTagUtility.ValidateName(name);
            if (m_TagsByName.TryGetValue(name, out var existingDef))
            {
                // If a tag is registered multiple times, update its description if the new one is provided
                // and the old one was empty. This allows placeholder parents to get descriptions later.
                if (!string.IsNullOrEmpty(description) && string.IsNullOrEmpty(existingDef.Description))
                {
                    // To update it, we need to remove the old and add a new one, as GameplayTagDefinition is immutable.
                    var updatedDef = new GameplayTagDefinition(name, description, flags);
                    m_TagsByName[name] = updatedDef;

                    int index = m_Definitions.IndexOf(existingDef);
                    if (index != -1)
                    {
                        m_Definitions[index] = updatedDef;
                    }
                }
                return;
            }

            var definition = new GameplayTagDefinition(name, description, flags);
            m_TagsByName.Add(name, definition);
            m_Definitions.Add(definition);
        }

        public List<GameplayTagDefinition> GenerateDefinitions(bool bAutoAddNoneTag = false)
        {
            RegisterMissingParents();
            SortDefinitionsAlphabetically();
            if (bAutoAddNoneTag)
            {
                RegisterNoneTag();
            }
            SetTagRuntimeIndices();
            FillParentsAndChildren();
            SetHierarchyTags();

            return m_Definitions;
        }

        private void RegisterNoneTag()
        {
            m_Definitions.Insert(0, GameplayTagDefinition.CreateNoneTagDefinition());
        }

        private void RegisterMissingParents()
        {
            // Use a temporary list to avoid modifying the collection while iterating.
            var tempDefinitions = new List<GameplayTagDefinition>(m_Definitions);

            foreach (GameplayTagDefinition definition in tempDefinitions)
            {
                // We only need to check the direct parent. The recursion will handle grandparents.
                if (GameplayTagUtility.TryGetParentName(definition.TagName, out string parentName))
                {
                    if (!m_TagsByName.ContainsKey(parentName))
                    {
                        // Recursively register the parent. This ensures the entire chain is created.
                        RegisterTag(parentName, "Auto-generated parent tag.", GameplayTagFlags.None);
                    }
                }
            }
        }

        private void SortDefinitionsAlphabetically()
        {
            m_Definitions.Sort((a, b) => string.Compare(a.TagName, b.TagName, StringComparison.Ordinal));
        }

        private void FillParentsAndChildren()
        {
            var childrenLists = new Dictionary<GameplayTagDefinition, List<GameplayTagDefinition>>();

            foreach (var definition in m_Definitions)
            {
                if (definition.RuntimeIndex == 0)
                {
                    continue;
                }

                if (GameplayTagUtility.TryGetParentName(definition.TagName, out string parentName))
                {
                    if (m_TagsByName.TryGetValue(parentName, out var parentDefinition))
                    {
                        definition.SetParent(parentDefinition);

                        if (!childrenLists.TryGetValue(parentDefinition, out var children))
                        {
                            children = new List<GameplayTagDefinition>();
                            childrenLists[parentDefinition] = children;
                        }
                        children.Add(definition);
                    }
                }
            }

            foreach (var (parent, children) in childrenLists)
            {
                parent.SetChildren(children);
            }
        }

        private void SetHierarchyTags()
        {
            foreach (var definition in m_Definitions)
            {
                if (definition.RuntimeIndex == 0) // Skip "None" tag
                {
                    definition.SetHierarchyTags(Array.Empty<GameplayTag>());
                    continue;
                }
                ;

                var hierarchyTags = new List<GameplayTag>();
                var current = definition;
                while (current != null)
                {
                    hierarchyTags.Add(current.Tag);
                    current = current.ParentTagDefinition;
                }

                // The list is currently [Child, Parent, Grandparent]. We need to reverse it.
                hierarchyTags.Reverse();
                definition.SetHierarchyTags(hierarchyTags.ToArray());
            }
        }

        private void SetTagRuntimeIndices()
        {
            // This assigns the final, sorted index to each tag definition.
            for (int i = 0; i < m_Definitions.Count; i++)
            {
                m_Definitions[i].SetRuntimeIndex(i);
            }
        }
    }
}