using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// An immutable, published view of a complete gameplay tag registry.
   /// </summary>
   /// <remarks>
   /// <para>
   /// A snapshot is built once and never mutated. Writers assemble a candidate off to the side and
   /// publish it with a single <see cref="System.Threading.Volatile.Write"/>, so readers capture one
   /// reference and then touch only arrays - no locks, no version checks, no torn reads.
   /// </para>
   /// <para>
   /// Every per-tag datum lives in a flat array indexed by runtime index. Index 0 is reserved for
   /// <see cref="GameplayTag.None"/>. There are no per-tag managed objects, which is what removes the
   /// previous per-tag allocation cost and keeps the whole registry in a handful of contiguous blocks.
   /// </para>
   /// <para>
   /// Hierarchy is stored as a single compressed sparse row pool. For tag <c>i</c>,
   /// <c>HierarchyPool[HierarchyOffsets[i] .. HierarchyOffsets[i+1])</c> is the chain from the root down
   /// to <c>i</c> inclusive, ascending. The parent chain is the same slice with the last element
   /// dropped, so one array serves ancestor tests, hierarchy level, and tree walking:
   /// <list type="bullet">
   /// <item><description>level = <c>HierarchyOffsets[i+1] - HierarchyOffsets[i]</c></description></item>
   /// <item><description>parent = <c>HierarchyPool[HierarchyOffsets[i+1] - 2]</c>, or 0 at level 1</description></item>
   /// <item><description>ancestors = the slice minus its final element</description></item>
   /// </list>
   /// </para>
   /// <para>
   /// Slices are ascending because the build guarantees a parent always receives a lower index than any
   /// of its descendants. That guarantee is asserted during construction, so a violated invariant fails
   /// loudly at build time instead of corrupting queries at runtime.
   /// </para>
   /// </remarks>
   public sealed class TagDataSnapshot
   {
      /// <summary>
      /// Slices at or below this length are scanned linearly rather than binary searched. A contiguous
      /// run of this many integers fits in one or two cache lines, which beats the branch mispredictions
      /// of a binary search at these sizes.
      /// </summary>
      internal const int LinearScanLimit = 8;

      private const string NoneTagName = "<None>";

      /// <summary>Incremented on every publication.</summary>
      public int Generation { get; }

      /// <summary>
      /// Incremented only when existing runtime indices may have been reassigned or removed.
      /// Cached indices remain valid as long as this value is unchanged.
      /// </summary>
      public int RuntimeIndexEpoch { get; }

      /// <summary>Registered tags, excluding <see cref="GameplayTag.None"/>.</summary>
      public int TagCount { get; }

      /// <summary>Registered tags including <see cref="GameplayTag.None"/> at index 0.</summary>
      public int TotalTagCount { get; }

      /// <summary>
      /// Order-independent hash of the tag manifest, used by replication handshakes to detect that two
      /// peers disagree about the registry. Derived from sorted names, so it is stable across processes
      /// and platforms.
      /// </summary>
      public ulong RegistryManifestHash { get; }

      internal readonly string[] Names;
      internal readonly string[] Descriptions;
      internal readonly int[] ParentIndices;
      internal readonly GameplayTagFlags[] Flags;
      internal readonly ulong[] StableIds;
      internal readonly int[] HierarchyOffsets;
      internal readonly int[] HierarchyPool;
      internal readonly int[] ChildOffsets;
      internal readonly int[] ChildPool;
      internal readonly Dictionary<string, int> NameToIndex;

      private Dictionary<ulong, int> m_StableIdToIndex;

      internal TagDataSnapshot(
         string[] names,
         string[] descriptions,
         GameplayTagFlags[] flags,
         int[] parentIndices,
         int generation,
         int runtimeIndexEpoch)
      {
         if (names == null)
            throw new ArgumentNullException(nameof(names));
         if (descriptions == null)
            throw new ArgumentNullException(nameof(descriptions));
         if (flags == null)
            throw new ArgumentNullException(nameof(flags));
         if (parentIndices == null)
            throw new ArgumentNullException(nameof(parentIndices));
         int total = names.Length;
         if (total == 0)
            throw new ArgumentException("A registry must contain at least the None tag.", nameof(names));
         if (descriptions.Length != total || flags.Length != total || parentIndices.Length != total)
            throw new ArgumentException("All per-tag arrays must have the same length.");
         if (!string.Equals(names[0], NoneTagName, StringComparison.Ordinal))
            throw new ArgumentException("Index 0 must be the None tag.", nameof(names));
         if (parentIndices[0] != 0)
            throw new ArgumentException("The None tag must have no parent.", nameof(parentIndices));

         Names = names;
         Descriptions = descriptions;
         Flags = flags;
         ParentIndices = parentIndices;
         Generation = generation;
         RuntimeIndexEpoch = runtimeIndexEpoch;
         TotalTagCount = total;
         TagCount = total - 1;

         StableIds = new ulong[total];
         NameToIndex = new Dictionary<string, int>(Math.Max(0, total - 1), StringComparer.Ordinal);
         for (int i = 1; i < total; i++)
         {
            string name = names[i];
            StableIds[i] = GameplayTagUtility.ComputeStableIdUnchecked(name);
            NameToIndex[name] = i;
         }

         RegistryManifestHash = ComputeManifestHash(names, StableIds, total);
         BuildHierarchy(parentIndices, total, out int[] hierarchyOffsets, out int[] hierarchyPool);
         HierarchyOffsets = hierarchyOffsets;
         HierarchyPool = hierarchyPool;
         BuildChildren(parentIndices, total, out int[] childOffsets, out int[] childPool);
         ChildOffsets = childOffsets;
         ChildPool = childPool;
      }

      private TagDataSnapshot(int generation, int runtimeIndexEpoch)
      {
         Names = new[] { NoneTagName };
         Descriptions = new[] { string.Empty };
         Flags = new[] { GameplayTagFlags.None };
         ParentIndices = new[] { 0 };
         StableIds = new[] { 0UL };
         HierarchyOffsets = new[] { 0, 0 };
         HierarchyPool = Array.Empty<int>();
         ChildOffsets = new[] { 0, 0 };
         ChildPool = Array.Empty<int>();
         NameToIndex = new Dictionary<string, int>(0, StringComparer.Ordinal);
         m_StableIdToIndex = new Dictionary<ulong, int>(0);
         Generation = generation;
         RuntimeIndexEpoch = runtimeIndexEpoch;
         TotalTagCount = 1;
         TagCount = 0;
         RegistryManifestHash = GameplayTagUtility.FnvOffsetBasis64;
      }

      /// <summary>
      /// A registry containing only <see cref="GameplayTag.None"/>. Used before initialization and while
      /// a registry is being rebuilt, so a lookup performed during a build resolves to nothing rather
      /// than recursing into a second build.
      /// </summary>
      internal static TagDataSnapshot CreateEmpty(int generation, int runtimeIndexEpoch)
         => new(generation, runtimeIndexEpoch);

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static GameplayTag FromIndex(int runtimeIndex) => new(runtimeIndex);

      /// <summary>The number of hierarchy segments of the tag at <paramref name="runtimeIndex"/>.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal int GetHierarchyLevel(int runtimeIndex)
         => (uint)runtimeIndex < (uint)TotalTagCount
            ? HierarchyOffsets[runtimeIndex + 1] - HierarchyOffsets[runtimeIndex]
            : 0;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public string GetName(int runtimeIndex)
         => (uint)runtimeIndex < (uint)TotalTagCount ? Names[runtimeIndex] : string.Empty;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal string GetDescription(int runtimeIndex)
         => (uint)runtimeIndex < (uint)TotalTagCount ? Descriptions[runtimeIndex] ?? string.Empty : string.Empty;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal ulong GetStableId(int runtimeIndex)
         => (uint)runtimeIndex < (uint)TotalTagCount ? StableIds[runtimeIndex] : 0UL;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal GameplayTagFlags GetFlags(int runtimeIndex)
         => (uint)runtimeIndex < (uint)TotalTagCount ? Flags[runtimeIndex] : GameplayTagFlags.None;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public int GetParentIndex(int runtimeIndex)
         => (uint)runtimeIndex < (uint)TotalTagCount ? ParentIndices[runtimeIndex] : 0;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal bool IsLeaf(int runtimeIndex)
         => runtimeIndex > 0 && (uint)runtimeIndex < (uint)TotalTagCount
            && ChildOffsets[runtimeIndex] == ChildOffsets[runtimeIndex + 1];

      /// <summary>
      /// True when <paramref name="ancestorIndex"/> is a strict ancestor of
      /// <paramref name="descendantIndex"/>.
      /// </summary>
      internal bool IsAncestorOf(int ancestorIndex, int descendantIndex)
      {
         if (ancestorIndex <= 0 || descendantIndex <= 0 || ancestorIndex == descendantIndex)
            return false;
         if ((uint)descendantIndex >= (uint)TotalTagCount)
            return false;

         int start = HierarchyOffsets[descendantIndex];
         int length = HierarchyOffsets[descendantIndex + 1] - start - 1;
         if (length <= 0)
            return false;

         if (length <= LinearScanLimit)
         {
            int end = start + length;
            for (int i = start; i < end; i++)
            {
               if (HierarchyPool[i] == ancestorIndex)
                  return true;
            }

            return false;
         }

         return BinarySearchUtility.Contains(HierarchyPool, start, length, ancestorIndex);
      }

      /// <summary>
      /// The number of leading hierarchy segments shared by two tags. 0 when they share no root.
      /// </summary>
      internal int MatchesTagDepth(int indexA, int indexB)
      {
         if (indexA <= 0 || indexB <= 0)
            return 0;
         if (indexA == indexB)
            return GetHierarchyLevel(indexA);
         if ((uint)indexA >= (uint)TotalTagCount || (uint)indexB >= (uint)TotalTagCount)
            return 0;

         int startA = HierarchyOffsets[indexA];
         int startB = HierarchyOffsets[indexB];
         int shared = Math.Min(
            HierarchyOffsets[indexA + 1] - startA,
            HierarchyOffsets[indexB + 1] - startB);

         int depth = 0;
         while (depth < shared && HierarchyPool[startA + depth] == HierarchyPool[startB + depth])
            depth++;

         return depth;
      }

      internal void AppendAncestors(int runtimeIndex, List<GameplayTag> destination)
      {
         if (destination == null || runtimeIndex <= 0 || (uint)runtimeIndex >= (uint)TotalTagCount)
            return;

         int start = HierarchyOffsets[runtimeIndex];
         int end = HierarchyOffsets[runtimeIndex + 1] - 1;
         for (int i = start; i < end; i++)
            destination.Add(new GameplayTag(HierarchyPool[i]));
      }

      internal void AppendChildren(int runtimeIndex, List<GameplayTag> destination)
      {
         if (destination == null || runtimeIndex < 0 || (uint)runtimeIndex >= (uint)TotalTagCount)
            return;

         int start = ChildOffsets[runtimeIndex];
         int end = ChildOffsets[runtimeIndex + 1];
         for (int i = start; i < end; i++)
            destination.Add(new GameplayTag(ChildPool[i]));
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal bool TryGetIndex(string name, out int runtimeIndex)
      {
         if (string.IsNullOrEmpty(name))
         {
            runtimeIndex = 0;
            return false;
         }

         return NameToIndex.TryGetValue(name, out runtimeIndex);
      }

      internal bool TryGetIndex(ulong stableId, out int runtimeIndex)
      {
         if (stableId == 0UL)
         {
            runtimeIndex = 0;
            return false;
         }

         Dictionary<ulong, int> map = Volatile.Read(ref m_StableIdToIndex);
         if (map == null)
         {
            map = BuildStableIdMap();
            Dictionary<ulong, int> published = Interlocked.CompareExchange(ref m_StableIdToIndex, map, null);
            if (published != null)
               map = published;
         }

         return map.TryGetValue(stableId, out runtimeIndex);
      }

      private Dictionary<ulong, int> BuildStableIdMap()
      {
         Dictionary<ulong, int> map = new(TagCount);
         for (int i = 1; i < TotalTagCount; i++)
         {
            ulong stableId = StableIds[i];
            if (map.TryGetValue(stableId, out int existing))
            {
               throw new InvalidOperationException(
                  $"Gameplay tag stable ID collision between '{Names[existing]}' and '{Names[i]}'.");
            }

            map.Add(stableId, i);
         }

         return map;
      }

      private static ulong ComputeManifestHash(string[] names, ulong[] stableIds, int total)
      {
         int[] ordered = new int[Math.Max(0, total - 1)];
         for (int i = 1; i < total; i++)
            ordered[i - 1] = i;

         Array.Sort(ordered, (a, b) => string.Compare(names[a], names[b], StringComparison.Ordinal));

         ulong hash = GameplayTagUtility.FnvOffsetBasis64;
         for (int i = 0; i < ordered.Length; i++)
            hash = GameplayTagUtility.CombineStableHash(hash, stableIds[ordered[i]]);

         return hash;
      }

      private static void BuildHierarchy(
         int[] parentIndices,
         int total,
         out int[] offsets,
         out int[] pool)
      {
         offsets = new int[total + 1];
         for (int i = 1; i < total; i++)
         {
            int parent = parentIndices[i];
            if (parent < 0 || parent >= i)
            {
               throw new InvalidOperationException(
                  $"Gameplay tag hierarchy invariant violated: tag {i} has parent {parent}, " +
                  "which is not a lower index. A parent must always be assigned a lower runtime index " +
                  "than its descendants.");
            }

            int inherited = parent > 0 ? offsets[parent + 1] - offsets[parent] : 0;
            offsets[i + 1] = offsets[i] + inherited + 1;
         }

         pool = new int[offsets[total]];
         for (int i = 1; i < total; i++)
         {
            int start = offsets[i];
            int parent = parentIndices[i];
            if (parent > 0)
            {
               int parentStart = offsets[parent];
               int inherited = offsets[parent + 1] - parentStart;
               if (inherited > 0)
                  Array.Copy(pool, parentStart, pool, start, inherited);
            }

            pool[offsets[i + 1] - 1] = i;
         }
      }

      /// <summary>
      /// Builds the direct-children compressed sparse row. Roots are children of nothing, so the None
      /// entry at index 0 stays empty and its child range collapses to zero width.
      /// </summary>
      private static void BuildChildren(
         int[] parentIndices,
         int total,
         out int[] offsets,
         out int[] pool)
      {
         offsets = new int[total + 1];
         for (int i = 1; i < total; i++)
         {
            int parent = parentIndices[i];
            if (parent > 0)
               offsets[parent + 1]++;
         }

         for (int i = 1; i <= total; i++)
            offsets[i] += offsets[i - 1];

         pool = new int[offsets[total]];
         int[] cursor = new int[total];
         for (int i = 1; i < total; i++)
         {
            int parent = parentIndices[i];
            if (parent > 0)
               pool[offsets[parent] + cursor[parent]++] = i;
         }
      }
   }
}
