using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.GameplayTags.Runtime
{
   /// <summary>
   /// Immutable snapshot of all tag data. Published atomically via Volatile.Write
   /// to guarantee thread-safe reads without locks (Copy-on-Write / Snapshot pattern).
   /// Readers capture a reference once, then access arrays freely.
   /// </summary>
   public sealed class TagDataSnapshot
   {
      internal readonly struct TagRange
      {
         public readonly int Start;
         public readonly int Count;

         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         public TagRange(int start, int count)
         {
            Start = start;
            Count = count;
         }
      }

      /// <summary>Total number of tag definitions including the None tag at index 0.</summary>
      internal readonly int TotalTagCount;

      // Core flat arrays — all indexed by runtimeIndex (0 = None)
      internal readonly GameplayTag[] Tags;
      internal readonly string[] TagNames;
      internal readonly string[] TagDescriptions;
      internal readonly string[] TagLabels;
      internal readonly GameplayTagFlags[] TagFlags;
      internal readonly int[] TagHierarchyLevels;
      internal readonly int[] TagParentRuntimeIndices;

      // Hierarchy flat arrays (contiguous memory, cache-friendly)
      internal readonly GameplayTag[] FlatParentTags;
      internal readonly GameplayTag[] FlatChildTags;
      internal readonly GameplayTag[] FlatHierarchyTags;
      internal readonly int[] FlatParentRuntimeIndices;
      internal readonly int[] FlatChildRuntimeIndices;
      internal readonly int[] FlatHierarchyRuntimeIndices;

      // Range indexes — locate hierarchy slices for a given runtimeIndex
      internal readonly TagRange[] ParentTagRanges;
      internal readonly TagRange[] ChildTagRanges;
      internal readonly TagRange[] HierarchyTagRanges;

      // Lookup structures
      internal readonly Dictionary<string, int> NameToRuntimeIndex;
      internal readonly GameplayTagDefinition[] Definitions;

      internal TagDataSnapshot(List<GameplayTagDefinition> definitions, Dictionary<string, GameplayTagDefinition> definitionsByName)
      {
         TotalTagCount = definitions.Count;
         int tagCount = Math.Max(0, TotalTagCount - 1);

         // Build name→index lookup
         NameToRuntimeIndex = new Dictionary<string, int>(TotalTagCount, StringComparer.Ordinal);
         for (int i = 0; i < TotalTagCount; i++)
         {
            GameplayTagDefinition def = definitions[i];
            if (!def.IsNone())
               NameToRuntimeIndex[def.TagName] = def.RuntimeIndex;
         }

         // Build Tags array (excludes None at index 0)
         Tags = new GameplayTag[tagCount];
         for (int i = 0; i < tagCount; i++)
            Tags[i] = definitions[i + 1].Tag;

         // Build per-index flat arrays
         TagNames = new string[TotalTagCount];
         TagDescriptions = new string[TotalTagCount];
         TagLabels = new string[TotalTagCount];
         TagFlags = new GameplayTagFlags[TotalTagCount];
         TagHierarchyLevels = new int[TotalTagCount];
         TagParentRuntimeIndices = new int[TotalTagCount];

         ParentTagRanges = new TagRange[TotalTagCount];
         ChildTagRanges = new TagRange[TotalTagCount];
         HierarchyTagRanges = new TagRange[TotalTagCount];

         Definitions = new GameplayTagDefinition[TotalTagCount];

         // Count totals for hierarchy flat arrays
         int totalParentTags = 0, totalChildTags = 0, totalHierarchyTags = 0;
         for (int i = 0; i < TotalTagCount; i++)
         {
            GameplayTagDefinition def = definitions[i];
            totalParentTags += def.ParentTags.Length;
            totalChildTags += def.ChildTags.Length;
            totalHierarchyTags += def.HierarchyTags.Length;
         }

         FlatParentTags = new GameplayTag[totalParentTags];
         FlatChildTags = new GameplayTag[totalChildTags];
         FlatHierarchyTags = new GameplayTag[totalHierarchyTags];
         FlatParentRuntimeIndices = new int[totalParentTags];
         FlatChildRuntimeIndices = new int[totalChildTags];
         FlatHierarchyRuntimeIndices = new int[totalHierarchyTags];

         // Fill all arrays in a single pass
         int parentOffset = 0, childOffset = 0, hierarchyOffset = 0;
         for (int i = 0; i < TotalTagCount; i++)
         {
            GameplayTagDefinition def = definitions[i];
            Definitions[i] = def;
            TagNames[i] = def.TagName;
            TagDescriptions[i] = def.Description;
            TagLabels[i] = def.Label;
            TagFlags[i] = def.Flags;
            TagHierarchyLevels[i] = def.HierarchyLevel;
            TagParentRuntimeIndices[i] = def.ParentTagDefinition?.RuntimeIndex ?? 0;

            ReadOnlySpan<GameplayTag> parentTags = def.ParentTags;
            parentTags.CopyTo(FlatParentTags.AsSpan(parentOffset, parentTags.Length));
            CopyRuntimeIndices(parentTags, FlatParentRuntimeIndices, parentOffset);
            ParentTagRanges[i] = new TagRange(parentOffset, parentTags.Length);
            parentOffset += parentTags.Length;

            ReadOnlySpan<GameplayTag> childTags = def.ChildTags;
            childTags.CopyTo(FlatChildTags.AsSpan(childOffset, childTags.Length));
            CopyRuntimeIndices(childTags, FlatChildRuntimeIndices, childOffset);
            ChildTagRanges[i] = new TagRange(childOffset, childTags.Length);
            childOffset += childTags.Length;

            ReadOnlySpan<GameplayTag> hierarchyTags = def.HierarchyTags;
            hierarchyTags.CopyTo(FlatHierarchyTags.AsSpan(hierarchyOffset, hierarchyTags.Length));
            CopyRuntimeIndices(hierarchyTags, FlatHierarchyRuntimeIndices, hierarchyOffset);
            HierarchyTagRanges[i] = new TagRange(hierarchyOffset, hierarchyTags.Length);
            hierarchyOffset += hierarchyTags.Length;
         }

         // After snapshot is built, optimize definition storage
         for (int i = 1; i < TotalTagCount; i++)
            definitions[i].OptimizeRuntimeStorage();
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal bool TryGetRuntimeIndex(string name, out int runtimeIndex)
      {
         return NameToRuntimeIndex.TryGetValue(name, out runtimeIndex);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal ReadOnlySpan<GameplayTag> GetParentTagsSpan(int runtimeIndex)
      {
         if ((uint)runtimeIndex >= (uint)ParentTagRanges.Length)
            return ReadOnlySpan<GameplayTag>.Empty;

         TagRange range = ParentTagRanges[runtimeIndex];
         return new ReadOnlySpan<GameplayTag>(FlatParentTags, range.Start, range.Count);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal ReadOnlySpan<GameplayTag> GetChildTagsSpan(int runtimeIndex)
      {
         if ((uint)runtimeIndex >= (uint)ChildTagRanges.Length)
            return ReadOnlySpan<GameplayTag>.Empty;

         TagRange range = ChildTagRanges[runtimeIndex];
         return new ReadOnlySpan<GameplayTag>(FlatChildTags, range.Start, range.Count);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal ReadOnlySpan<GameplayTag> GetHierarchyTagsSpan(int runtimeIndex)
      {
         if ((uint)runtimeIndex >= (uint)HierarchyTagRanges.Length)
            return ReadOnlySpan<GameplayTag>.Empty;

         TagRange range = HierarchyTagRanges[runtimeIndex];
         return new ReadOnlySpan<GameplayTag>(FlatHierarchyTags, range.Start, range.Count);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal ReadOnlySpan<int> GetParentRuntimeIndicesSpan(int runtimeIndex)
      {
         if ((uint)runtimeIndex >= (uint)ParentTagRanges.Length)
            return ReadOnlySpan<int>.Empty;

         TagRange range = ParentTagRanges[runtimeIndex];
         return new ReadOnlySpan<int>(FlatParentRuntimeIndices, range.Start, range.Count);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal ReadOnlySpan<int> GetChildRuntimeIndicesSpan(int runtimeIndex)
      {
         if ((uint)runtimeIndex >= (uint)ChildTagRanges.Length)
            return ReadOnlySpan<int>.Empty;

         TagRange range = ChildTagRanges[runtimeIndex];
         return new ReadOnlySpan<int>(FlatChildRuntimeIndices, range.Start, range.Count);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal ReadOnlySpan<int> GetHierarchyRuntimeIndicesSpan(int runtimeIndex)
      {
         if ((uint)runtimeIndex >= (uint)HierarchyTagRanges.Length)
            return ReadOnlySpan<int>.Empty;

         TagRange range = HierarchyTagRanges[runtimeIndex];
         return new ReadOnlySpan<int>(FlatHierarchyRuntimeIndices, range.Start, range.Count);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal bool IsChildOf(int runtimeIndex, int parentRuntimeIndex)
      {
         if (runtimeIndex <= parentRuntimeIndex || runtimeIndex <= 0 || parentRuntimeIndex <= 0)
            return false;

         ReadOnlySpan<int> parentIndices = GetParentRuntimeIndicesSpan(runtimeIndex);
         return BinarySearchUtility.SearchSpan(parentIndices, parentRuntimeIndex) >= 0;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal bool IsParentOf(int runtimeIndex, int childRuntimeIndex)
      {
         if (runtimeIndex <= 0 || childRuntimeIndex <= runtimeIndex)
            return false;

         ReadOnlySpan<int> childIndices = GetChildRuntimeIndicesSpan(runtimeIndex);
         return BinarySearchUtility.SearchSpan(childIndices, childRuntimeIndex) >= 0;
      }

      /// <summary>
      /// Returns the number of matching hierarchy levels between two tags.
      /// Returns 0 if no common ancestor, 1 if they share a root, etc.
      /// </summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal int MatchesTagDepth(int runtimeIndexA, int runtimeIndexB)
      {
         if (runtimeIndexA <= 0 || runtimeIndexB <= 0)
            return 0;

         if (runtimeIndexA == runtimeIndexB)
            return (uint)runtimeIndexA < (uint)TagHierarchyLevels.Length ? TagHierarchyLevels[runtimeIndexA] : 0;

         ReadOnlySpan<int> hierarchyA = GetHierarchyRuntimeIndicesSpan(runtimeIndexA);
         ReadOnlySpan<int> hierarchyB = GetHierarchyRuntimeIndicesSpan(runtimeIndexB);

         int minLen = Math.Min(hierarchyA.Length, hierarchyB.Length);
         int depth = 0;
         for (int i = 0; i < minLen; i++)
         {
            if (hierarchyA[i] == hierarchyB[i])
               depth++;
            else
               break;
         }

         return depth;
      }

      private static void CopyRuntimeIndices(ReadOnlySpan<GameplayTag> tags, int[] destination, int startIndex)
      {
         for (int i = 0; i < tags.Length; i++)
            destination[startIndex + i] = tags[i].RuntimeIndex;
      }
   }
}
