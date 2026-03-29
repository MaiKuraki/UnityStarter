using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace CycloneGames.GameplayTags.Runtime
{
    public static class GameplayTagManager
    {
        public static bool HasBeenReloaded => s_HasBeenReloaded;

        private static Dictionary<string, GameplayTagDefinition> s_TagDefinitionsByName = new();
        private static List<GameplayTagDefinition> s_TagsDefinitionsList = new();
        private static GameplayTag[] s_Tags;
        private static volatile bool s_IsInitialized;
        private static bool s_HasBeenReloaded;
        private static readonly object s_InitLock = new();

        public static ReadOnlySpan<GameplayTag> GetAllTags()
        {
            InitializeIfNeeded();
            return new ReadOnlySpan<GameplayTag>(s_Tags);
        }

        internal static GameplayTagDefinition GetDefinitionFromRuntimeIndex(int runtimeIndex)
        {
            InitializeIfNeeded();
            if (runtimeIndex < 0 || runtimeIndex >= s_TagsDefinitionsList.Count)
            {
                return s_TagsDefinitionsList[0]; // Return "None" tag definition.
            }
            return s_TagsDefinitionsList[runtimeIndex];
        }

        public static GameplayTag RequestTag(string name, bool logWarningIfNotFound = true)
        {
            if (string.IsNullOrEmpty(name))
            {
                return GameplayTag.None;
            }

            if (TryRequestTag(name, out GameplayTag tag))
            {
                return tag;
            }

            if (logWarningIfNotFound)
            {
                GameplayTagLogger.LogWarning($"No tag registered with name \"{name}\".");
            }

            return GameplayTagDefinition.CreateInvalidDefinition(name).Tag;
        }

        public static bool TryRequestTag(string name, out GameplayTag tag)
        {
            if (string.IsNullOrEmpty(name))
            {
                tag = GameplayTag.None;
                return false;
            }

            InitializeIfNeeded();
            if (s_TagDefinitionsByName.TryGetValue(name, out GameplayTagDefinition definition))
            {
                tag = definition.Tag;
                return true;
            }

            tag = GameplayTag.None;
            return false;
        }

        public static void InitializeIfNeeded()
        {
            if (s_IsInitialized)
            {
                return;
            }
            
            s_IsInitialized = true;

            var context = new GameplayTagRegistrationContext();

#if UNITY_EDITOR
            // Register tags from all assemblies with the GameplayTagAttribute attribute.
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                new AssemblyGameplayTagSource(assembly).RegisterTags(context);
                
                // Scans the assembly for attributes pointing to static classes with tag definitions.
                foreach (RegisterGameplayTagsFromAttribute fromAttribute in assembly.GetCustomAttributes<RegisterGameplayTagsFromAttribute>())
                {
                    Type targetType = fromAttribute.TargetType;
                    if (targetType == null) continue;

                    var fields = targetType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    foreach (var field in fields)
                    {
                        if (field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
                        {
                            string tagName = (string)field.GetValue(null);
                            if (!string.IsNullOrEmpty(tagName))
                            {
                                context.RegisterTag(tagName, description: tagName, flags: GameplayTagFlags.None);
                            }
                        }
                    }
                }
            }

            // Register tags from all JSON files in the ProjectSettings/GameplayTags directory.
            foreach (IGameplayTagSource source in FileGameplayTagSource.GetAllFileSources())
            {
                source.RegisterTags(context);
            }
#else
            // Register tags from the GameplayTags file in StreamingAssets.   
            new BuildGameplayTagSource().RegisterTags(context);
#endif

            foreach (GameplayTagRegistrationError error in context.GetRegistrationErrors())
            {
                Debug.LogError($"Failed to register gameplay tag \"{error.TagName}\": {error.Message} (Source: {error.Source?.Name ?? "Unknown"})");
            }

            s_TagsDefinitionsList = context.GenerateDefinitions(true);
            
            s_TagDefinitionsByName.Clear();
            foreach (GameplayTagDefinition definition in s_TagsDefinitionsList)
            {
                s_TagDefinitionsByName[definition.TagName] = definition;
            }
            
            RebuildTagArray();
        }
        
        private static void RebuildTagArray()
        {
            int tagCount = s_TagsDefinitionsList.Count - 1;
            if (tagCount < 0) tagCount = 0;

            s_Tags = new GameplayTag[tagCount];
            for (int i = 0; i < tagCount; i++)
            {
                s_Tags[i] = s_TagsDefinitionsList[i + 1].Tag;
            }
        }

        public static void RegisterDynamicTags(IEnumerable<string> tags)
        {
            if (tags == null) return;
            InitializeIfNeeded();

            foreach (string tag in tags)
            {
                if (string.IsNullOrEmpty(tag)) continue;
                RegisterDynamicTag(tag, string.Empty, GameplayTagFlags.None, false);
            }
            
            RebuildTagHierarchyAndArray();
        }

        public static void RegisterDynamicTag(string name, string description = null, GameplayTagFlags flags = GameplayTagFlags.None)
        {
            RegisterDynamicTag(name, description, flags, true);
        }

        private static void RegisterDynamicTag(string name, string description, GameplayTagFlags flags, bool rebuildHierarchy)
        {
            if (string.IsNullOrEmpty(name)) return;
            InitializeIfNeeded();

            if (s_TagDefinitionsByName.ContainsKey(name))
            {
                return;
            }

            var newDefinition = new GameplayTagDefinition(name, description, flags);
            s_TagDefinitionsByName.Add(name, newDefinition);
            s_TagsDefinitionsList.Add(newDefinition);
            
            if (rebuildHierarchy)
            {
                RebuildTagHierarchyAndArray();
            }
        }

        private static void RebuildTagHierarchyAndArray()
        {
            // Auto-register missing parent definitions for dynamically added tags
            int initialCount = s_TagsDefinitionsList.Count;
            for (int i = 0; i < initialCount; i++)
            {
                GameplayTagDefinition definition = s_TagsDefinitionsList[i];
                if (definition.IsNone()) continue;

                string parentName = GameplayTagUtility.GetParentName(definition.TagName);
                while (!string.IsNullOrEmpty(parentName))
                {
                    if (s_TagDefinitionsByName.ContainsKey(parentName))
                        break;

                    var parentDef = new GameplayTagDefinition(parentName, string.Empty, GameplayTagFlags.None);
                    s_TagDefinitionsByName[parentName] = parentDef;
                    s_TagsDefinitionsList.Add(parentDef);

                    parentName = GameplayTagUtility.GetParentName(parentName);
                }
            }

            s_TagsDefinitionsList.Sort((a, b) => string.Compare(a.TagName, b.TagName, StringComparison.Ordinal));

            for (int i = 0; i < s_TagsDefinitionsList.Count; i++)
                s_TagsDefinitionsList[i].SetRuntimeIndex(i);

            s_TagDefinitionsByName.Clear();
            foreach (GameplayTagDefinition definition in s_TagsDefinitionsList)
                s_TagDefinitionsByName[definition.TagName] = definition;

            // Build all-descendant children map (matches initialization behavior in GameplayTagRegistrationContext)
            var childrenMap = new Dictionary<GameplayTagDefinition, List<GameplayTagDefinition>>();
            for (int i = 0; i < s_TagsDefinitionsList.Count; i++)
            {
                GameplayTagDefinition definition = s_TagsDefinitionsList[i];
                if (definition.IsNone()) continue;

                string[] hierarchyNames = GameplayTagUtility.GetHeirarchyNames(definition.TagName);
                for (int j = 0; j < hierarchyNames.Length - 1; j++)
                {
                    if (s_TagDefinitionsByName.TryGetValue(hierarchyNames[j], out var ancestorDef))
                    {
                        if (!childrenMap.TryGetValue(ancestorDef, out var children))
                        {
                            children = new List<GameplayTagDefinition>();
                            childrenMap[ancestorDef] = children;
                        }
                        children.Add(definition);
                    }
                }
            }

            foreach (var kvp in childrenMap)
            {
                kvp.Key.SetChildren(kvp.Value);
                foreach (GameplayTagDefinition child in kvp.Value)
                    child.SetParent(kvp.Key);
            }

            foreach (GameplayTagDefinition definition in s_TagsDefinitionsList)
            {
                if (definition.IsNone()) continue;

                if (!childrenMap.ContainsKey(definition))
                    definition.SetChildren(new List<GameplayTagDefinition>());

                // Build hierarchy tags bottom-up without List allocation
                int depth = 0;
                GameplayTagDefinition current = definition;
                while (current != null && !current.IsNone())
                {
                    depth++;
                    current = current.ParentTagDefinition;
                }

                var hierarchyTags = new GameplayTag[depth];
                current = definition;
                for (int k = depth - 1; k >= 0; k--)
                {
                    hierarchyTags[k] = current.Tag;
                    current = current.ParentTagDefinition;
                }
                definition.SetHierarchyTags(hierarchyTags);
            }

            RebuildTagArray();
        }

        public static void ReloadTags()
        {
            s_IsInitialized = false;
            s_TagDefinitionsByName.Clear();
            s_TagsDefinitionsList.Clear();

            InitializeIfNeeded();

            s_HasBeenReloaded = true;

            if (Application.isPlaying)
            {
                Debug.LogWarning("Gameplay tags have been reloaded at runtime." +
                               " Existing data structures using gameplay tags may not work as expected." +
                               " A domain reload is required.");
            }
        }
    }
}
