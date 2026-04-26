using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.GameplayTags.Core
{
    /// <summary>
    /// Central manager for the GameplayTag system. Uses an immutable Snapshot/Copy-on-Write
    /// model for thread-safe lock-free reads. All mutation happens under lock and publishes
    /// a new snapshot atomically via Volatile.Write.
    /// </summary>
    public static class GameplayTagManager
    {
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

        /// <summary>True if tags have been reloaded since last domain reload (HybridCLR aware).</summary>
        public static bool HasBeenReloaded
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref s_HasBeenReloaded);
        }

        /// <summary>
        /// The current immutable snapshot of all tag data. Thread-safe lock-free read.
        /// Readers should capture this once and use the reference for the duration of their operation.
        /// </summary>
        public static TagDataSnapshot Snapshot
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                TagDataSnapshot snap = Volatile.Read(ref s_Snapshot);
                if (snap != null) return snap;
                InitializeIfNeeded();
                return Volatile.Read(ref s_Snapshot);
            }
        }

        // Mutable state — only accessed under s_InitLock
        private static Dictionary<string, GameplayTagDefinition> s_TagDefinitionsByName = new(StringComparer.Ordinal);
        private static List<GameplayTagDefinition> s_TagsDefinitionsList = new();
        private static List<PendingRegistration> s_PendingDynamicRegistrations = new();

        // Immutable snapshot — published via Volatile.Write, read via Volatile.Read
        private static TagDataSnapshot s_Snapshot;
        private static volatile bool s_IsInitialized;
        private static bool s_HasBeenReloaded;
        private static readonly object s_InitLock = new();

        // Defer tree change broadcasting (batch pattern)
        private static int s_DeferTreeChangeBroadcastCount;
        private static bool s_DeferredTreeChangeNeeded;

        /// <summary>Event fired when the tag tree changes (after snapshot is published).</summary>
        public static event Action OnGameplayTagTreeChanged;

        // --- Public API ---

        public static ReadOnlySpan<GameplayTag> GetAllTags()
        {
            return new ReadOnlySpan<GameplayTag>(Snapshot.Tags);
        }

        internal static int GetRegisteredTagCount()
        {
            return Snapshot.TotalTagCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static GameplayTagDefinition GetDefinitionFromRuntimeIndex(int runtimeIndex)
        {
            TagDataSnapshot snap = Snapshot;
            if ((uint)runtimeIndex >= (uint)snap.TotalTagCount)
                return snap.Definitions[0]; // "None" tag
            return snap.Definitions[runtimeIndex];
        }

        /// <summary>Get a GameplayTag by its runtime index. O(1). Returns GameplayTag.None if invalid.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GameplayTag GetTagFromRuntimeIndex(int runtimeIndex)
        {
            if (runtimeIndex <= 0) return GameplayTag.None;
            TagDataSnapshot snap = Snapshot;
            int arrayIndex = runtimeIndex - 1;
            if ((uint)arrayIndex < (uint)snap.Tags.Length)
                return snap.Tags[arrayIndex];
            return new GameplayTag(GetDefinitionFromRuntimeIndex(runtimeIndex));
        }

        public static GameplayTag RequestTag(string name, bool logWarningIfNotFound = true)
        {
            if (string.IsNullOrEmpty(name))
                return GameplayTag.None;

            // Apply redirect
            name = GameplayTagRedirector.Resolve(name);

            if (TryRequestTag(name, out GameplayTag tag))
                return tag;

            if (logWarningIfNotFound)
                GameplayTagLogger.LogWarning($"No tag registered with name \"{name}\".");

            return GameplayTagDefinition.CreateInvalidDefinition(name).Tag;
        }

        public static bool TryRequestTag(string name, out GameplayTag tag)
        {
            if (string.IsNullOrEmpty(name))
            {
                tag = GameplayTag.None;
                return false;
            }

            // Apply redirect
            name = GameplayTagRedirector.Resolve(name);

            TagDataSnapshot snap = Snapshot;
            if (snap.TryGetRuntimeIndex(name, out int runtimeIndex))
            {
                tag = GetTagFromRuntimeIndex(runtimeIndex);
                return true;
            }

            tag = GameplayTag.None;
            return false;
        }

        // --- Snapshot-delegated accessors (lock-free reads) ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string GetTagName(int runtimeIndex)
        {
            TagDataSnapshot snap = Snapshot;
            return (uint)runtimeIndex < (uint)snap.TagNames.Length ? snap.TagNames[runtimeIndex] : string.Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string GetTagDescription(int runtimeIndex)
        {
            TagDataSnapshot snap = Snapshot;
            return (uint)runtimeIndex < (uint)snap.TagDescriptions.Length ? snap.TagDescriptions[runtimeIndex] : string.Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static string GetTagLabel(int runtimeIndex)
        {
            TagDataSnapshot snap = Snapshot;
            return (uint)runtimeIndex < (uint)snap.TagLabels.Length ? snap.TagLabels[runtimeIndex] : string.Empty;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static GameplayTagFlags GetTagFlags(int runtimeIndex)
        {
            TagDataSnapshot snap = Snapshot;
            return (uint)runtimeIndex < (uint)snap.TagFlags.Length ? snap.TagFlags[runtimeIndex] : GameplayTagFlags.None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetTagHierarchyLevel(int runtimeIndex)
        {
            TagDataSnapshot snap = Snapshot;
            return (uint)runtimeIndex < (uint)snap.TagHierarchyLevels.Length ? snap.TagHierarchyLevels[runtimeIndex] : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int GetParentRuntimeIndex(int runtimeIndex)
        {
            TagDataSnapshot snap = Snapshot;
            return (uint)runtimeIndex < (uint)snap.TagParentRuntimeIndices.Length ? snap.TagParentRuntimeIndices[runtimeIndex] : 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsLeafTag(int runtimeIndex)
        {
            if (runtimeIndex <= 0) return false;
            TagDataSnapshot snap = Snapshot;
            return (uint)runtimeIndex < (uint)snap.ChildTagRanges.Length && snap.ChildTagRanges[runtimeIndex].Count == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ReadOnlySpan<GameplayTag> GetParentTagsSpan(int runtimeIndex)
        {
            if (runtimeIndex <= 0) return ReadOnlySpan<GameplayTag>.Empty;
            return Snapshot.GetParentTagsSpan(runtimeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ReadOnlySpan<GameplayTag> GetChildTagsSpan(int runtimeIndex)
        {
            if (runtimeIndex <= 0) return ReadOnlySpan<GameplayTag>.Empty;
            return Snapshot.GetChildTagsSpan(runtimeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ReadOnlySpan<GameplayTag> GetHierarchyTagsSpan(int runtimeIndex)
        {
            if (runtimeIndex <= 0) return ReadOnlySpan<GameplayTag>.Empty;
            return Snapshot.GetHierarchyTagsSpan(runtimeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ReadOnlySpan<int> GetParentRuntimeIndicesSpan(int runtimeIndex)
        {
            if (runtimeIndex <= 0) return ReadOnlySpan<int>.Empty;
            return Snapshot.GetParentRuntimeIndicesSpan(runtimeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ReadOnlySpan<int> GetChildRuntimeIndicesSpan(int runtimeIndex)
        {
            if (runtimeIndex <= 0) return ReadOnlySpan<int>.Empty;
            return Snapshot.GetChildRuntimeIndicesSpan(runtimeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static ReadOnlySpan<int> GetHierarchyRuntimeIndicesSpan(int runtimeIndex)
        {
            if (runtimeIndex <= 0) return ReadOnlySpan<int>.Empty;
            return Snapshot.GetHierarchyRuntimeIndicesSpan(runtimeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsChildOf(int runtimeIndex, int parentRuntimeIndex)
        {
            return Snapshot.IsChildOf(runtimeIndex, parentRuntimeIndex);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsParentOf(int runtimeIndex, int childRuntimeIndex)
        {
            return Snapshot.IsParentOf(runtimeIndex, childRuntimeIndex);
        }

        /// <summary>
        /// Returns the number of matching hierarchy levels between two tags.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int MatchesTagDepth(in GameplayTag a, in GameplayTag b)
        {
            return Snapshot.MatchesTagDepth(a.RuntimeIndex, b.RuntimeIndex);
        }

        // --- Initialization ---

        public static void InitializeIfNeeded()
        {
            if (s_IsInitialized) return;

            lock (s_InitLock)
            {
                if (s_IsInitialized) return;

                var context = new GameplayTagRegistrationContext();

#if UNITY_EDITOR
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    new AssemblyGameplayTagSource(assembly).RegisterTags(context);

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
                                    context.RegisterTag(tagName, description: tagName, flags: GameplayTagFlags.None);
                            }
                        }
                    }
                }

                IEnumerable<IGameplayTagSource> projectSources = GameplayTagRuntimePlatform.EnumerateProjectTagSources != null
                    ? GameplayTagRuntimePlatform.EnumerateProjectTagSources()
                    : Array.Empty<IGameplayTagSource>();
                foreach (IGameplayTagSource source in projectSources)
                    source.RegisterTags(context);
#else
                new BuildGameplayTagSource().RegisterTags(context);
#endif
                for (int i = 0; i < s_PendingDynamicRegistrations.Count; i++)
                {
                    PendingRegistration pending = s_PendingDynamicRegistrations[i];
                    context.RegisterTag(pending.Name, pending.Description, pending.Flags);
                }

                foreach (GameplayTagRegistrationError error in context.GetRegistrationErrors())
                    GameplayTagLogger.LogError($"Failed to register gameplay tag \"{error.TagName}\": {error.Message} (Source: {error.Source?.Name ?? "Unknown"})");

                s_TagsDefinitionsList = context.GenerateDefinitions(true);

                s_TagDefinitionsByName.Clear();
                foreach (GameplayTagDefinition definition in s_TagsDefinitionsList)
                    s_TagDefinitionsByName[definition.TagName] = definition;

                // Build and atomically publish immutable snapshot
                var snapshot = new TagDataSnapshot(s_TagsDefinitionsList, s_TagDefinitionsByName);
                Volatile.Write(ref s_Snapshot, snapshot);
                s_IsInitialized = true;
            }

            BroadcastTreeChanged();
        }

        // --- Dynamic registration (HybridCLR hot-update friendly) ---

        public static void RegisterDynamicTags(IEnumerable<string> tags)
        {
            if (tags == null) return;
            foreach (string tag in tags)
            {
                if (!string.IsNullOrEmpty(tag))
                    RegisterDynamicTag(tag, string.Empty, GameplayTagFlags.None);
            }
        }

        public static void RegisterDynamicTagsFromType(Type targetType)
        {
            if (targetType == null) return;

            FieldInfo[] fields = targetType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!field.IsLiteral || field.IsInitOnly || field.FieldType != typeof(string))
                    continue;

                string tagName = (string)field.GetValue(null);
                if (!string.IsNullOrEmpty(tagName))
                    RegisterDynamicTag(tagName, field.Name, GameplayTagFlags.None);
            }
        }

        public static void RegisterDynamicTagsFromAssembly(Assembly assembly)
        {
            if (assembly == null) return;

            foreach (GameplayTagAttribute attribute in assembly.GetCustomAttributes<GameplayTagAttribute>())
                RegisterDynamicTag(attribute.TagName, attribute.Description, attribute.Flags);

            foreach (RegisterGameplayTagsFromAttribute fromAttribute in assembly.GetCustomAttributes<RegisterGameplayTagsFromAttribute>())
                RegisterDynamicTagsFromType(fromAttribute.TargetType);
        }

        public static void RegisterDynamicTag(string name, string description = null, GameplayTagFlags flags = GameplayTagFlags.None)
        {
            if (string.IsNullOrEmpty(name)) return;
            lock (s_InitLock)
            {
                if (s_IsInitialized)
                {
                    AppendDynamicTagLocked(name, description, flags);
                    return;
                }

                for (int i = 0; i < s_PendingDynamicRegistrations.Count; i++)
                {
                    if (string.Equals(s_PendingDynamicRegistrations[i].Name, name, StringComparison.Ordinal))
                        return;
                }

                s_PendingDynamicRegistrations.Add(new PendingRegistration(name, description, flags));
            }
        }

        private static void AppendDynamicTagLocked(string name, string description, GameplayTagFlags flags)
        {
            if (!GameplayTagUtility.IsNameValid(name, out string errorMessage))
            {
                GameplayTagLogger.LogError($"Failed to register gameplay tag \"{name}\": {errorMessage}");
                return;
            }

            if (s_TagDefinitionsByName.ContainsKey(name))
                return;

            AppendMissingParentsLocked(name, flags);

            GameplayTagDefinition definition = new(name, description, flags);
            definition.SetRuntimeIndex(s_TagsDefinitionsList.Count);
            s_TagsDefinitionsList.Add(definition);
            s_TagDefinitionsByName.Add(definition.TagName, definition);

            RebuildHierarchyAndPublishSnapshotLocked();
        }

        private static void AppendMissingParentsLocked(string tagName, GameplayTagFlags flags)
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

        private static void RebuildHierarchyAndPublishSnapshotLocked()
        {
            // Rebuild parent/child/hierarchy relationships
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
                    definition.SetChildren(children);
                else
                    definition.SetChildren(new List<GameplayTagDefinition>());

                ReadOnlySpan<GameplayTag> parentHierarchy = definition.ParentTagDefinition != null
                    ? definition.ParentTagDefinition.HierarchyTags
                    : ReadOnlySpan<GameplayTag>.Empty;

                GameplayTag[] hierarchyTags = new GameplayTag[parentHierarchy.Length + 1];
                parentHierarchy.CopyTo(hierarchyTags);
                hierarchyTags[parentHierarchy.Length] = definition.Tag;
                definition.SetHierarchyTags(hierarchyTags);
            }

            // Build new immutable snapshot and publish atomically
            var newSnapshot = new TagDataSnapshot(s_TagsDefinitionsList, s_TagDefinitionsByName);
            Volatile.Write(ref s_Snapshot, newSnapshot);

            BroadcastTreeChanged();
        }

        // --- Reload (editor / HybridCLR hot-reload) ---

        public static void ReloadTags()
        {
            lock (s_InitLock)
            {
                // Clear all mutable state under lock
                s_IsInitialized = false;
                s_TagDefinitionsByName.Clear();
                s_TagsDefinitionsList.Clear();

                // Do NOT null out s_Snapshot yet — readers may still be using it.
                // They hold a reference to the old snapshot which remains valid.

                // Re-initialize completely (builds new snapshot inside lock)
                var context = new GameplayTagRegistrationContext();

#if UNITY_EDITOR
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    new AssemblyGameplayTagSource(assembly).RegisterTags(context);

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
                                    context.RegisterTag(tagName, description: tagName, flags: GameplayTagFlags.None);
                            }
                        }
                    }
                }

                IEnumerable<IGameplayTagSource> projectSources = GameplayTagRuntimePlatform.EnumerateProjectTagSources != null
                    ? GameplayTagRuntimePlatform.EnumerateProjectTagSources()
                    : Array.Empty<IGameplayTagSource>();
                foreach (IGameplayTagSource source in projectSources)
                    source.RegisterTags(context);
#else
                new BuildGameplayTagSource().RegisterTags(context);
#endif
                for (int i = 0; i < s_PendingDynamicRegistrations.Count; i++)
                {
                    PendingRegistration pending = s_PendingDynamicRegistrations[i];
                    context.RegisterTag(pending.Name, pending.Description, pending.Flags);
                }

                foreach (GameplayTagRegistrationError error in context.GetRegistrationErrors())
                    GameplayTagLogger.LogError($"Failed to register gameplay tag \"{error.TagName}\": {error.Message} (Source: {error.Source?.Name ?? "Unknown"})");

                s_TagsDefinitionsList = context.GenerateDefinitions(true);

                s_TagDefinitionsByName.Clear();
                foreach (GameplayTagDefinition definition in s_TagsDefinitionsList)
                    s_TagDefinitionsByName[definition.TagName] = definition;

                // Atomically publish new snapshot
                var snapshot = new TagDataSnapshot(s_TagsDefinitionsList, s_TagDefinitionsByName);
                Volatile.Write(ref s_Snapshot, snapshot);
                Volatile.Write(ref s_HasBeenReloaded, true);
                s_IsInitialized = true;
            }

            if (GameplayTagRuntimePlatform.IsRuntimePlaying())
            {
                GameplayTagLogger.LogWarning("Gameplay tags have been reloaded at runtime." +
                               " Existing data structures using gameplay tags may not work as expected." +
                               " A domain reload is required.");
            }

            BroadcastTreeChanged();
        }

        // --- Deferred tree change broadcasting (batch pattern) ---

        /// <summary>
        /// Push a defer scope. While deferred, tree change broadcasts are suppressed.
        /// Call PopDeferOnGameplayTagTreeChangedBroadcast to release.
        /// </summary>
        public static void PushDeferOnGameplayTagTreeChangedBroadcast()
        {
            Interlocked.Increment(ref s_DeferTreeChangeBroadcastCount);
        }

        /// <summary>
        /// Pop a defer scope. If the count reaches 0 and a change was deferred, broadcast now.
        /// </summary>
        public static void PopDeferOnGameplayTagTreeChangedBroadcast()
        {
            int count = Interlocked.Decrement(ref s_DeferTreeChangeBroadcastCount);
            if (count <= 0 && Volatile.Read(ref s_DeferredTreeChangeNeeded))
            {
                Volatile.Write(ref s_DeferredTreeChangeNeeded, false);
                OnGameplayTagTreeChanged?.Invoke();
            }
        }

        private static void BroadcastTreeChanged()
        {
            if (Volatile.Read(ref s_DeferTreeChangeBroadcastCount) > 0)
            {
                Volatile.Write(ref s_DeferredTreeChangeNeeded, true);
                return;
            }

            OnGameplayTagTreeChanged?.Invoke();
        }
    }
}
