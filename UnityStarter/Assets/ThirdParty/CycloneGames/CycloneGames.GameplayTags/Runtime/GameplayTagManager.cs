using System;
using System.Collections.Generic;
using System.Reflection;

namespace CycloneGames.GameplayTags.Runtime
{
    public static class GameplayTagManager
    {
        private static Dictionary<string, GameplayTagDefinition> s_TagDefinitionsByName = new Dictionary<string, GameplayTagDefinition>();
        // Use a list internally for easier modification, and an array for the public-facing, immutable data.
        private static List<GameplayTagDefinition> s_TagsDefinitionsList = new List<GameplayTagDefinition>();
        private static GameplayTag[] s_Tags;
        private static bool s_IsInitialized;
        
        public static ReadOnlySpan<GameplayTag> GetAllTags()
        {
            InitializeIfNeeded();
            return new ReadOnlySpan<GameplayTag>(s_Tags);
        }

        internal static GameplayTagDefinition GetDefinitionFromRuntimeIndex(int runtimeIndex)
        {
            InitializeIfNeeded();
            // PERF: Direct array access is extremely fast.
            // Add bounds check for stability, though valid indices should always be passed.
            if (runtimeIndex < 0 || runtimeIndex >= s_TagsDefinitionsList.Count)
            {
                // UnityEngine.Debug.LogError($"Invalid runtimeIndex {runtimeIndex} requested. Max index is {s_TagsDefinitionsList.Count - 1}.");
                return s_TagsDefinitionsList[0]; // Return "None" tag definition.
            }
            return s_TagsDefinitionsList[runtimeIndex];
        }

        public static GameplayTag RequestTag(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return GameplayTag.None;
            }

            InitializeIfNeeded();
            if (s_TagDefinitionsByName.TryGetValue(name, out GameplayTagDefinition definition))
            {
                return definition.Tag;
            }

            // Do not warn here anymore, as this is a common case.
            // Let calling code decide if a warning is needed.
            return GameplayTag.None;
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
            
            // This whole block runs only once.
            s_IsInitialized = true; // Set early to prevent re-entrancy.

            GameplayTagRegistrationContext context = new GameplayTagRegistrationContext();
            
            // Discover tags from attributes in all loaded assemblies.
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (GameplayTagAttribute attribute in assembly.GetCustomAttributes<GameplayTagAttribute>())
                    {
                        try
                        {
                            context.RegisterTag(attribute.TagName, attribute.Description, attribute.Flags);
                        }
                        catch (Exception e)
                        {
#if UNITY_2017_1_OR_NEWER
                            UnityEngine.Debug.LogError($"Failed to register tag '{attribute.TagName}' from assembly '{assembly.FullName}'. Reason: {e.Message}");
#endif
                        }
                    }
                }
                catch (Exception e)
                {
                    // Some dynamic assemblies can't be introspected. This is fine, just ignore them.
#if UNITY_2017_1_OR_NEWER
                    UnityEngine.Debug.LogWarning($"Could not load attributes from assembly '{assembly.FullName}'. This may be expected for some system assemblies. Error: {e.Message}");
#endif
                }
            }
            
            s_TagsDefinitionsList = context.GenerateDefinitions(true);
            s_TagDefinitionsByName.Clear();
            foreach (GameplayTagDefinition definition in s_TagsDefinitionsList)
            {
                // Also add the "None" tag to the dictionary for completeness, even though it has an empty name.
                s_TagDefinitionsByName[definition.TagName] = definition;
            }

            RebuildTagArray();
        }

        private static void RebuildTagArray()
        {
            // OPTIMIZATION: Avoid LINQ to prevent GC allocation.
            // Create the public-facing array of tags, skipping the 'None' tag at index 0.
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
            var context = new GameplayTagRegistrationContext(s_TagsDefinitionsList);

            foreach (string tag in tags)
            {
                if (string.IsNullOrEmpty(tag)) continue;
                context.RegisterTag(tag, string.Empty, GameplayTagFlags.None);
            }
            
            // Finalize registration
            s_TagsDefinitionsList = context.GenerateDefinitions(false); // Do not add another "None" tag.
            // Re-map all definitions by name
            s_TagDefinitionsByName.Clear();
            foreach (var definition in s_TagsDefinitionsList)
            {
                s_TagDefinitionsByName[definition.TagName] = definition;
            }
            RebuildTagArray();
        }

        public static void RegisterDynamicTag(string name, string description = null, GameplayTagFlags flags = GameplayTagFlags.None)
        {
            if (string.IsNullOrEmpty(name)) return;
            
            InitializeIfNeeded();
            
            // If tag already exists, do nothing.
            if (s_TagDefinitionsByName.ContainsKey(name))
            {
                return;
            }
            
            var context = new GameplayTagRegistrationContext(s_TagsDefinitionsList);
            context.RegisterTag(name, description, flags);
            
            s_TagsDefinitionsList = context.GenerateDefinitions(false);
            s_TagDefinitionsByName.Clear();
            foreach (var definition in s_TagsDefinitionsList)
            {
                s_TagDefinitionsByName[definition.TagName] = definition;
            }
            RebuildTagArray();
        }
    }
}