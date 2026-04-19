using System;
using System.Collections.Generic;
using System.Reflection;

namespace CycloneGames.GameplayTags.Runtime
{
    public static class GameplayTagManager
    {
        private readonly struct TagRange
        {
            public readonly int Start;
            public readonly int Count;

            public TagRange(int start, int count)
            {
                Start = start;
                Count = count;
            }
        }

        private readonly struct PendingRegistration
        {
            public readonly string Name;
            public readonly string Description;
            public readonly GameplayTagFlags Flags;

            public PendingRegistration(string name, string description, GameplayTagFlags flags)
            {
                Name = name;
                Description = description;
                Flags = flags;
            }
        }

        public static bool HasBeenReloaded => s_HasBeenReloaded;

        private static Dictionary<string, GameplayTagDefinition> s_TagDefinitionsByName = new(StringComparer.Ordinal);
        private static List<GameplayTagDefinition> s_TagsDefinitionsList = new();
        private static List<PendingRegistration> s_PendingDynamicRegistrations = new();
        private static GameplayTag[] s_Tags;
        private static string[] s_TagNames;
        private static string[] s_TagDescriptions;
        private static string[] s_TagLabels;
        private static GameplayTagFlags[] s_TagFlags;
        private static int[] s_TagHierarchyLevels;
        private static int[] s_TagParentRuntimeIndices;
        private static int[] s_FlatParentRuntimeIndices;
        private static int[] s_FlatChildRuntimeIndices;
        private static int[] s_FlatHierarchyRuntimeIndices;
        private static TagRange[] s_ParentTagRanges;
        private static TagRange[] s_ChildTagRanges;
        private static TagRange[] s_HierarchyTagRanges;
        private static GameplayTag[] s_FlatParentTags;
        private static GameplayTag[] s_FlatChildTags;
        private static GameplayTag[] s_FlatHierarchyTags;
        private static volatile bool s_IsInitialized;
        private static bool s_HasBeenReloaded;
        private static readonly object s_InitLock = new();

        public static ReadOnlySpan<GameplayTag> GetAllTags()
        {
            InitializeIfNeeded();
            return new ReadOnlySpan<GameplayTag>(s_Tags);
        }

        internal static int GetRegisteredTagCount()
        {
            InitializeIfNeeded();
            return s_TagsDefinitionsList.Count;
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

        internal static GameplayTag GetTagFromRuntimeIndex(int runtimeIndex)
        {
            if (runtimeIndex <= 0)
            {
                return GameplayTag.None;
            }

            int arrayIndex = runtimeIndex - 1;
            if (s_Tags != null && arrayIndex < s_Tags.Length)
            {
                return s_Tags[arrayIndex];
            }

            GameplayTagDefinition definition = GetDefinitionFromRuntimeIndex(runtimeIndex);
            return new GameplayTag(definition);
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

            lock (s_InitLock)
            {
                if (s_IsInitialized)
                {
                    return;
                }

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
                IEnumerable<IGameplayTagSource> projectSources = GameplayTagRuntimePlatform.EnumerateProjectTagSources != null
                    ? GameplayTagRuntimePlatform.EnumerateProjectTagSources()
                    : Array.Empty<IGameplayTagSource>();
                foreach (IGameplayTagSource source in projectSources)
                {
                    source.RegisterTags(context);
                }
#else
                // Register tags from the build-time data asset.
                new BuildGameplayTagSource().RegisterTags(context);
#endif
                for (int i = 0; i < s_PendingDynamicRegistrations.Count; i++)
                {
                    PendingRegistration pending = s_PendingDynamicRegistrations[i];
                    context.RegisterTag(pending.Name, pending.Description, pending.Flags);
                }

                foreach (GameplayTagRegistrationError error in context.GetRegistrationErrors())
                {
                    GameplayTagLogger.LogError($"Failed to register gameplay tag \"{error.TagName}\": {error.Message} (Source: {error.Source?.Name ?? "Unknown"})");
                }

                s_TagsDefinitionsList = context.GenerateDefinitions(true);

                s_TagDefinitionsByName.Clear();
                foreach (GameplayTagDefinition definition in s_TagsDefinitionsList)
                {
                    s_TagDefinitionsByName[definition.TagName] = definition;
                }

                RebuildTagArray();
                s_IsInitialized = true;
            }
        }
        
        private static void RebuildTagArray()
        {
            int totalTagCount = s_TagsDefinitionsList.Count;
            int tagCount = totalTagCount - 1;
            if (tagCount < 0) tagCount = 0;

            s_Tags = new GameplayTag[tagCount];
            for (int i = 0; i < tagCount; i++)
            {
                s_Tags[i] = s_TagsDefinitionsList[i + 1].Tag;
            }

            s_TagNames = new string[totalTagCount];
            s_TagDescriptions = new string[totalTagCount];
            s_TagLabels = new string[totalTagCount];
            s_TagFlags = new GameplayTagFlags[totalTagCount];
            s_TagHierarchyLevels = new int[totalTagCount];
            s_TagParentRuntimeIndices = new int[totalTagCount];
            s_ParentTagRanges = new TagRange[totalTagCount];
            s_ChildTagRanges = new TagRange[totalTagCount];
            s_HierarchyTagRanges = new TagRange[totalTagCount];

            int totalParentTags = 0;
            int totalChildTags = 0;
            int totalHierarchyTags = 0;
            for (int i = 0; i < totalTagCount; i++)
            {
                GameplayTagDefinition definition = s_TagsDefinitionsList[i];
                totalParentTags += definition.ParentTags.Length;
                totalChildTags += definition.ChildTags.Length;
                totalHierarchyTags += definition.HierarchyTags.Length;
            }

            s_FlatParentTags = new GameplayTag[totalParentTags];
            s_FlatChildTags = new GameplayTag[totalChildTags];
            s_FlatHierarchyTags = new GameplayTag[totalHierarchyTags];
            s_FlatParentRuntimeIndices = new int[totalParentTags];
            s_FlatChildRuntimeIndices = new int[totalChildTags];
            s_FlatHierarchyRuntimeIndices = new int[totalHierarchyTags];

            int parentOffset = 0;
            int childOffset = 0;
            int hierarchyOffset = 0;
            for (int i = 0; i < totalTagCount; i++)
            {
                GameplayTagDefinition definition = s_TagsDefinitionsList[i];
                s_TagNames[i] = definition.TagName;
                s_TagDescriptions[i] = definition.Description;
                s_TagLabels[i] = definition.Label;
                s_TagFlags[i] = definition.Flags;
                s_TagHierarchyLevels[i] = definition.HierarchyLevel;
                s_TagParentRuntimeIndices[i] = definition.ParentTagDefinition != null ? definition.ParentTagDefinition.RuntimeIndex : 0;

                ReadOnlySpan<GameplayTag> parentTags = definition.ParentTags;
                parentTags.CopyTo(s_FlatParentTags.AsSpan(parentOffset, parentTags.Length));
                CopyRuntimeIndices(parentTags, s_FlatParentRuntimeIndices, parentOffset);
                s_ParentTagRanges[i] = new TagRange(parentOffset, parentTags.Length);
                parentOffset += parentTags.Length;

                ReadOnlySpan<GameplayTag> childTags = definition.ChildTags;
                childTags.CopyTo(s_FlatChildTags.AsSpan(childOffset, childTags.Length));
                CopyRuntimeIndices(childTags, s_FlatChildRuntimeIndices, childOffset);
                s_ChildTagRanges[i] = new TagRange(childOffset, childTags.Length);
                childOffset += childTags.Length;

                ReadOnlySpan<GameplayTag> hierarchyTags = definition.HierarchyTags;
                hierarchyTags.CopyTo(s_FlatHierarchyTags.AsSpan(hierarchyOffset, hierarchyTags.Length));
                CopyRuntimeIndices(hierarchyTags, s_FlatHierarchyRuntimeIndices, hierarchyOffset);
                s_HierarchyTagRanges[i] = new TagRange(hierarchyOffset, hierarchyTags.Length);
                hierarchyOffset += hierarchyTags.Length;
            }

            for (int i = 1; i < totalTagCount; i++)
            {
                s_TagsDefinitionsList[i].OptimizeRuntimeStorage();
            }
        }

        private static void CopyRuntimeIndices(ReadOnlySpan<GameplayTag> tags, int[] destination, int startIndex)
        {
            for (int i = 0; i < tags.Length; i++)
            {
                destination[startIndex + i] = tags[i].RuntimeIndex;
            }
        }

        internal static string GetTagName(int runtimeIndex)
        {
            GameplayTagDefinition definition = GetDefinitionFromRuntimeIndex(runtimeIndex);
            return s_TagNames != null && runtimeIndex >= 0 && runtimeIndex < s_TagNames.Length ? s_TagNames[runtimeIndex] : definition.TagName;
        }

        internal static string GetTagDescription(int runtimeIndex)
        {
            GameplayTagDefinition definition = GetDefinitionFromRuntimeIndex(runtimeIndex);
            return s_TagDescriptions != null && runtimeIndex >= 0 && runtimeIndex < s_TagDescriptions.Length ? s_TagDescriptions[runtimeIndex] : definition.Description;
        }

        internal static string GetTagLabel(int runtimeIndex)
        {
            GameplayTagDefinition definition = GetDefinitionFromRuntimeIndex(runtimeIndex);
            return s_TagLabels != null && runtimeIndex >= 0 && runtimeIndex < s_TagLabels.Length ? s_TagLabels[runtimeIndex] : definition.Label;
        }

        internal static GameplayTagFlags GetTagFlags(int runtimeIndex)
        {
            GameplayTagDefinition definition = GetDefinitionFromRuntimeIndex(runtimeIndex);
            return s_TagFlags != null && runtimeIndex >= 0 && runtimeIndex < s_TagFlags.Length ? s_TagFlags[runtimeIndex] : definition.Flags;
        }

        internal static int GetTagHierarchyLevel(int runtimeIndex)
        {
            GameplayTagDefinition definition = GetDefinitionFromRuntimeIndex(runtimeIndex);
            return s_TagHierarchyLevels != null && runtimeIndex >= 0 && runtimeIndex < s_TagHierarchyLevels.Length ? s_TagHierarchyLevels[runtimeIndex] : definition.HierarchyLevel;
        }

        internal static int GetParentRuntimeIndex(int runtimeIndex)
        {
            GameplayTagDefinition definition = GetDefinitionFromRuntimeIndex(runtimeIndex);
            return s_TagParentRuntimeIndices != null && runtimeIndex >= 0 && runtimeIndex < s_TagParentRuntimeIndices.Length
                ? s_TagParentRuntimeIndices[runtimeIndex]
                : (definition.ParentTagDefinition != null ? definition.ParentTagDefinition.RuntimeIndex : 0);
        }

        internal static bool IsLeafTag(int runtimeIndex)
        {
            if (runtimeIndex <= 0)
            {
                return false;
            }

            return s_ChildTagRanges != null && runtimeIndex < s_ChildTagRanges.Length
                ? s_ChildTagRanges[runtimeIndex].Count == 0
                : GetDefinitionFromRuntimeIndex(runtimeIndex).ChildTags.Length == 0;
        }

        internal static ReadOnlySpan<GameplayTag> GetParentTagsSpan(int runtimeIndex)
        {
            if (runtimeIndex <= 0 || s_FlatParentTags == null || s_ParentTagRanges == null || runtimeIndex >= s_ParentTagRanges.Length)
            {
                return ReadOnlySpan<GameplayTag>.Empty;
            }

            TagRange range = s_ParentTagRanges[runtimeIndex];
            return new ReadOnlySpan<GameplayTag>(s_FlatParentTags, range.Start, range.Count);
        }

        internal static ReadOnlySpan<GameplayTag> GetChildTagsSpan(int runtimeIndex)
        {
            if (runtimeIndex <= 0 || s_FlatChildTags == null || s_ChildTagRanges == null || runtimeIndex >= s_ChildTagRanges.Length)
            {
                return ReadOnlySpan<GameplayTag>.Empty;
            }

            TagRange range = s_ChildTagRanges[runtimeIndex];
            return new ReadOnlySpan<GameplayTag>(s_FlatChildTags, range.Start, range.Count);
        }

        internal static ReadOnlySpan<GameplayTag> GetHierarchyTagsSpan(int runtimeIndex)
        {
            if (runtimeIndex <= 0 || s_FlatHierarchyTags == null || s_HierarchyTagRanges == null || runtimeIndex >= s_HierarchyTagRanges.Length)
            {
                return ReadOnlySpan<GameplayTag>.Empty;
            }

            TagRange range = s_HierarchyTagRanges[runtimeIndex];
            return new ReadOnlySpan<GameplayTag>(s_FlatHierarchyTags, range.Start, range.Count);
        }

        internal static ReadOnlySpan<int> GetParentRuntimeIndicesSpan(int runtimeIndex)
        {
            if (runtimeIndex <= 0 || s_FlatParentRuntimeIndices == null || s_ParentTagRanges == null || runtimeIndex >= s_ParentTagRanges.Length)
            {
                return ReadOnlySpan<int>.Empty;
            }

            TagRange range = s_ParentTagRanges[runtimeIndex];
            return new ReadOnlySpan<int>(s_FlatParentRuntimeIndices, range.Start, range.Count);
        }

        internal static ReadOnlySpan<int> GetChildRuntimeIndicesSpan(int runtimeIndex)
        {
            if (runtimeIndex <= 0 || s_FlatChildRuntimeIndices == null || s_ChildTagRanges == null || runtimeIndex >= s_ChildTagRanges.Length)
            {
                return ReadOnlySpan<int>.Empty;
            }

            TagRange range = s_ChildTagRanges[runtimeIndex];
            return new ReadOnlySpan<int>(s_FlatChildRuntimeIndices, range.Start, range.Count);
        }

        internal static ReadOnlySpan<int> GetHierarchyRuntimeIndicesSpan(int runtimeIndex)
        {
            if (runtimeIndex <= 0 || s_FlatHierarchyRuntimeIndices == null || s_HierarchyTagRanges == null || runtimeIndex >= s_HierarchyTagRanges.Length)
            {
                return ReadOnlySpan<int>.Empty;
            }

            TagRange range = s_HierarchyTagRanges[runtimeIndex];
            return new ReadOnlySpan<int>(s_FlatHierarchyRuntimeIndices, range.Start, range.Count);
        }

        internal static bool IsChildOf(int runtimeIndex, int parentRuntimeIndex)
        {
            if (runtimeIndex <= parentRuntimeIndex || runtimeIndex <= 0 || parentRuntimeIndex <= 0)
            {
                return false;
            }

            ReadOnlySpan<int> parentIndices = GetParentRuntimeIndicesSpan(runtimeIndex);
            for (int i = 0; i < parentIndices.Length; i++)
            {
                if (parentIndices[i] == parentRuntimeIndex)
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsParentOf(int runtimeIndex, int childRuntimeIndex)
        {
            if (runtimeIndex <= 0 || childRuntimeIndex <= runtimeIndex)
            {
                return false;
            }

            ReadOnlySpan<int> childIndices = GetChildRuntimeIndicesSpan(runtimeIndex);
            for (int i = 0; i < childIndices.Length; i++)
            {
                if (childIndices[i] == childRuntimeIndex)
                {
                    return true;
                }
            }

            return false;
        }

        public static void RegisterDynamicTags(IEnumerable<string> tags)
        {
            if (tags == null) return;

            foreach (string tag in tags)
            {
                if (string.IsNullOrEmpty(tag)) continue;
                RegisterDynamicTag(tag, string.Empty, GameplayTagFlags.None);
            }
        }

        public static void RegisterDynamicTagsFromType(Type targetType)
        {
            if (targetType == null)
            {
                return;
            }

            FieldInfo[] fields = targetType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!field.IsLiteral || field.IsInitOnly || field.FieldType != typeof(string))
                {
                    continue;
                }

                string tagName = (string)field.GetValue(null);
                if (!string.IsNullOrEmpty(tagName))
                {
                    RegisterDynamicTag(tagName, field.Name, GameplayTagFlags.None);
                }
            }
        }

        public static void RegisterDynamicTagsFromAssembly(Assembly assembly)
        {
            if (assembly == null)
            {
                return;
            }

            foreach (GameplayTagAttribute attribute in assembly.GetCustomAttributes<GameplayTagAttribute>())
            {
                RegisterDynamicTag(attribute.TagName, attribute.Description, attribute.Flags);
            }

            foreach (RegisterGameplayTagsFromAttribute fromAttribute in assembly.GetCustomAttributes<RegisterGameplayTagsFromAttribute>())
            {
                RegisterDynamicTagsFromType(fromAttribute.TargetType);
            }
        }

        public static void RegisterDynamicTag(string name, string description = null, GameplayTagFlags flags = GameplayTagFlags.None)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (s_InitLock)
            {
                if (s_IsInitialized)
                {
                    AppendDynamicTagUnsafe(name, description, flags);
                    return;
                }

                for (int i = 0; i < s_PendingDynamicRegistrations.Count; i++)
                {
                    if (string.Equals(s_PendingDynamicRegistrations[i].Name, name, StringComparison.Ordinal))
                    {
                        return;
                    }
                }

                s_PendingDynamicRegistrations.Add(new PendingRegistration(name, description, flags));
            }
        }

        private static void AppendDynamicTagUnsafe(string name, string description, GameplayTagFlags flags)
        {
            if (!GameplayTagUtility.IsNameValid(name, out string errorMessage))
            {
                GameplayTagLogger.LogError($"Failed to register gameplay tag \"{name}\": {errorMessage}");
                return;
            }

            if (s_TagDefinitionsByName.ContainsKey(name))
            {
                return;
            }

            AppendMissingParentsUnsafe(name, flags);

            GameplayTagDefinition definition = new(name, description, flags);
            definition.SetRuntimeIndex(s_TagsDefinitionsList.Count);
            s_TagsDefinitionsList.Add(definition);
            s_TagDefinitionsByName.Add(definition.TagName, definition);

            RebuildHierarchyPreservingIndicesUnsafe();
        }

        private static void AppendMissingParentsUnsafe(string tagName, GameplayTagFlags flags)
        {
            string parentName = GameplayTagUtility.GetParentNameUnchecked(tagName);
            while (!string.IsNullOrEmpty(parentName))
            {
                if (s_TagDefinitionsByName.ContainsKey(parentName))
                {
                    parentName = GameplayTagUtility.GetParentNameUnchecked(parentName);
                    continue;
                }

                GameplayTagDefinition parentDefinition = new(parentName, string.Empty, flags);
                parentDefinition.SetRuntimeIndex(s_TagsDefinitionsList.Count);
                s_TagsDefinitionsList.Add(parentDefinition);
                s_TagDefinitionsByName.Add(parentName, parentDefinition);
                parentName = GameplayTagUtility.GetParentNameUnchecked(parentName);
            }
        }

        private static void RebuildHierarchyPreservingIndicesUnsafe()
        {
            Dictionary<GameplayTagDefinition, List<GameplayTagDefinition>> childrenMap = new();

            for (int i = 1; i < s_TagsDefinitionsList.Count; i++)
            {
                GameplayTagDefinition definition = s_TagsDefinitionsList[i];
                string immediateParentName = GameplayTagUtility.GetParentNameUnchecked(definition.TagName);
                definition.SetParent(!string.IsNullOrEmpty(immediateParentName) && s_TagDefinitionsByName.TryGetValue(immediateParentName, out GameplayTagDefinition immediateParent)
                    ? immediateParent
                    : null);

                string ancestorName = immediateParentName;
                while (!string.IsNullOrEmpty(ancestorName))
                {
                    GameplayTagDefinition ancestorDefinition = s_TagDefinitionsByName[ancestorName];
                    if (!childrenMap.TryGetValue(ancestorDefinition, out List<GameplayTagDefinition> children))
                    {
                        children = new List<GameplayTagDefinition>();
                        childrenMap.Add(ancestorDefinition, children);
                    }

                    children.Add(definition);
                    ancestorName = GameplayTagUtility.GetParentNameUnchecked(ancestorName);
                }
            }

            for (int i = 1; i < s_TagsDefinitionsList.Count; i++)
            {
                GameplayTagDefinition definition = s_TagsDefinitionsList[i];
                if (childrenMap.TryGetValue(definition, out List<GameplayTagDefinition> children))
                {
                    definition.SetChildren(children);
                }
                else
                {
                    definition.SetChildren(new List<GameplayTagDefinition>());
                }

                ReadOnlySpan<GameplayTag> parentHierarchy = definition.ParentTagDefinition != null
                    ? definition.ParentTagDefinition.HierarchyTags
                    : ReadOnlySpan<GameplayTag>.Empty;

                GameplayTag[] hierarchyTags = new GameplayTag[parentHierarchy.Length + 1];
                parentHierarchy.CopyTo(hierarchyTags);
                hierarchyTags[parentHierarchy.Length] = definition.Tag;
                definition.SetHierarchyTags(hierarchyTags);
            }

            RebuildTagArray();
        }

        public static void ReloadTags()
        {
            lock (s_InitLock)
            {
                s_IsInitialized = false;
                s_TagDefinitionsByName.Clear();
                s_TagsDefinitionsList.Clear();
                s_Tags = Array.Empty<GameplayTag>();
                s_TagNames = null;
                s_TagDescriptions = null;
                s_TagLabels = null;
                s_TagFlags = null;
                s_TagHierarchyLevels = null;
                s_TagParentRuntimeIndices = null;
                s_ParentTagRanges = null;
                s_ChildTagRanges = null;
                s_HierarchyTagRanges = null;
                s_FlatParentRuntimeIndices = null;
                s_FlatChildRuntimeIndices = null;
                s_FlatHierarchyRuntimeIndices = null;
                s_FlatParentTags = null;
                s_FlatChildTags = null;
                s_FlatHierarchyTags = null;
            }

            InitializeIfNeeded();

            s_HasBeenReloaded = true;

            if (GameplayTagRuntimePlatform.IsRuntimePlaying())
            {
                GameplayTagLogger.LogWarning("Gameplay tags have been reloaded at runtime." +
                               " Existing data structures using gameplay tags may not work as expected." +
                               " A domain reload is required.");
            }
        }
    }
}
