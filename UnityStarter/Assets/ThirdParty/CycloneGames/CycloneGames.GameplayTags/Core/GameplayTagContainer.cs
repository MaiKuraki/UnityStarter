using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;

namespace CycloneGames.GameplayTags.Core
{
   public struct GameplayTagContainerIndices
   {
      public readonly bool IsCreated => Explicit != null && Implicit != null;
      public readonly bool IsEmpty => !IsCreated || Explicit.Count == 0;
      public readonly int TagCount => IsCreated ? Implicit.Count : 0;
      public readonly int ExplicitTagCount => IsCreated ? Explicit.Count : 0;

      internal List<int> Explicit { get; private set; }
      internal List<int> Implicit { get; private set; }

      public static void Create(ref GameplayTagContainerIndices indices)
      {
         if (indices.IsCreated)
            return;

         indices = new GameplayTagContainerIndices
         {
            Explicit = new(),
            Implicit = new()
         };
      }

      public static GameplayTagContainerIndices Create()
      {
         return new GameplayTagContainerIndices
         {
            Explicit = new(),
            Implicit = new()
         };
      }

      internal readonly void Clear()
      {
         Explicit?.Clear();
         Implicit?.Clear();
      }

      internal readonly void CopyTo(in GameplayTagContainerIndices other)
      {
         other.Clear();
         other.Explicit.AddRange(this.Explicit);
         other.Implicit.AddRange(this.Implicit);
      }
   }

   public interface IReadOnlyGameplayTagContainer : IEnumerable<GameplayTag>
   {
      bool IsEmpty { get; }
      int ExplicitTagCount { get; }
      int TagCount { get; }
      GameplayTagContainerIndices Indices { get; }

      GameplayTagEnumerator GetTags();
      GameplayTagEnumerator GetExplicitTags();
      void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags);
      void GetChildTags(GameplayTag tag, List<GameplayTag> childTags);
      void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags);
      void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags);
      bool ContainsRuntimeIndex(int runtimeIndex, bool explicitOnly);
   }

   public interface IGameplayTagContainer : IReadOnlyGameplayTagContainer
   {
      void AddTag(GameplayTag gameplayTag);
      void RemoveTag(GameplayTag gameplayTag);
      void AddTags<T>(in T other) where T : IReadOnlyGameplayTagContainer;
      void RemoveTags<T>(in T other) where T : IReadOnlyGameplayTagContainer;
      void Clear();
   }

   internal interface IGameplayTagRuntimeIndexView
   {
      int RuntimeIndexEpoch { get; }
   }

   [Serializable]
   [DebuggerTypeProxy(typeof(GameplayTagContainerDebugView))]
   [DebuggerDisplay("{DebuggerDisplay,nq}")]
   public class GameplayTagContainer : IGameplayTagContainer, IGameplayTagRuntimeIndexView, IEnumerable<GameplayTag>
   {
      private const int k_BitsetShrinkRatio = 4;

      public static GameplayTagContainer Empty => new();

      public bool IsEmpty
      {
         get
         {
            EnsureRuntimeStateInitialized();
            return m_Indices.IsEmpty;
         }
      }

      public int ExplicitTagCount
      {
         get
         {
            EnsureRuntimeStateInitialized();
            return m_Indices.ExplicitTagCount;
         }
      }

      public int TagCount
      {
         get
         {
            EnsureRuntimeStateInitialized();
            return m_Indices.TagCount;
         }
      }

      public GameplayTagContainerIndices Indices
      {
         get
         {
            EnsureRuntimeStateInitialized();
            return m_Indices;
         }
      }

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      private string DebuggerDisplay => $"Count (Explicit, Total) = ({ExplicitTagCount}, {TagCount})";

      // Kept public so Unity can serialize it without requiring Unity-specific attributes in the core assembly.
      public List<string> m_SerializedExplicitTags;

      private GameplayTagContainerIndices m_Indices = new();
      private int[] m_ExplicitBitset = Array.Empty<int>();
      private int[] m_ImplicitBitset = Array.Empty<int>();
      private bool m_SerializedDirty;
      private int m_RuntimeIndexEpoch;

      int IGameplayTagRuntimeIndexView.RuntimeIndexEpoch
      {
         get
         {
            EnsureRuntimeStateInitialized();
            return m_RuntimeIndexEpoch;
         }
      }

      public GameplayTagContainer()
      { }

      public GameplayTagContainer(IReadOnlyGameplayTagContainer other)
      {
         Copy(this, other);
      }

      public GameplayTagContainer Clone()
      {
         GameplayTagContainer clone = new();
         Copy(clone, this);
         return clone;
      }

      public static void Copy<T>(GameplayTagContainer dest, in T src) where T : IReadOnlyGameplayTagContainer
      {
         if (src is GameplayTagContainer sourceContainer && ReferenceEquals(dest, sourceContainer))
            return;
         GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(src);
         dest.Clear();

         if (src.IsEmpty)
            return;

         GameplayTagContainerIndices.Create(ref dest.m_Indices);
         src.Indices.CopyTo(dest.m_Indices);
         dest.m_RuntimeIndexEpoch = GameplayTagManager.CurrentRuntimeIndexEpoch;
         dest.SyncSerializedExplicitTagsWithRuntime();
      }

      public static GameplayTagContainer Intersection<T, U>(in T lhs, in U rhs) where T : IReadOnlyGameplayTagContainer where U : IReadOnlyGameplayTagContainer
      {
         GameplayTagContainer intersection = new();
         intersection.AddIntersection(lhs, rhs);
         return intersection;
      }

      public static void Intersection<T, U>(GameplayTagContainer output, in T lhs, in U rhs) where T : IReadOnlyGameplayTagContainer where U : IReadOnlyGameplayTagContainer
      {
         if (output == null)
            throw new ArgumentNullException(nameof(output));

         if (!output.IsEmpty)
            throw new ArgumentException("Output container must be empty.", nameof(output));

         output.AddIntersection(lhs, rhs);
      }

      internal void AddIntersection<T, U>(in T lhs, in U rhs) where T : IReadOnlyGameplayTagContainer where U : IReadOnlyGameplayTagContainer
      {
         static void OrderedListIntersection(List<int> a, List<int> b, List<int> dst)
         {
            int i = 0;
            int j = 0;
            while (i < a.Count && j < b.Count)
            {
               int aElement = a[i];
               int bElement = b[j];
               if (aElement == bElement)
               {
                  dst.Add(aElement);
                  i++;
                  j++;
                  continue;
               }

               if (aElement < bElement)
               {
                  i++;
                  continue;
               }

               j++;
            }
         }

         GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(lhs);
         GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(rhs);
         if (lhs.IsEmpty || rhs.IsEmpty)
            return;

         EnsureRuntimeStateInitialized();
         if (!m_Indices.IsCreated)
            m_Indices = GameplayTagContainerIndices.Create();

         OrderedListIntersection(lhs.Indices.Explicit, rhs.Indices.Explicit, m_Indices.Explicit);
         FillImplicitTags();
         SyncSerializedExplicitTagsWithRuntime();
      }

      public static GameplayTagContainer Union<T, U>(in T lhs, in U rhs) where T : IReadOnlyGameplayTagContainer where U : IReadOnlyGameplayTagContainer
      {
         static void OrderedListUnion(List<int> a, List<int> b, List<int> dst)
         {
            dst.Capacity = Math.Max(dst.Capacity, a.Count + b.Count);

            int i = 0;
            int j = 0;
            while (i < a.Count && j < b.Count)
            {
               int aElement = a[i];
               int bElement = b[j];
               if (aElement == bElement)
               {
                  dst.Add(aElement);
                  i++;
                  j++;
                  continue;
               }

               if (aElement < bElement)
               {
                  dst.Add(aElement);
                  i++;
                  continue;
               }

               dst.Add(bElement);
               j++;
            }

            for (; i < a.Count; i++)
               dst.Add(a[i]);

            for (; j < b.Count; j++)
               dst.Add(b[j]);
         }

         GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(lhs);
         GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(rhs);
         GameplayTagContainer union = new();
         GameplayTagContainerIndices.Create(ref union.m_Indices);
         union.m_RuntimeIndexEpoch = GameplayTagManager.CurrentRuntimeIndexEpoch;

         if (lhs.IsEmpty && rhs.IsEmpty)
            return union;

         if (lhs.IsEmpty)
            return new GameplayTagContainer(rhs);

         if (rhs.IsEmpty)
            return new GameplayTagContainer(lhs);

         OrderedListUnion(lhs.Indices.Explicit, rhs.Indices.Explicit, union.m_Indices.Explicit);
         OrderedListUnion(lhs.Indices.Implicit, rhs.Indices.Implicit, union.m_Indices.Implicit);
         union.RebuildBitsetsFromIndices();
         union.SyncSerializedExplicitTagsWithRuntime();

         return union;
      }

      public void GetDiffExplicitTags<T>(T other, List<GameplayTag> added, List<GameplayTag> removed) where T : IReadOnlyGameplayTagContainer
      {
         GameplayTagContainerUtility.EnsureCurrentRuntimeIndexEpoch(other);
         GameplayTagContainerIndices otherIndices = other.Indices;
         List<int> currentContainerTagIndices = Indices.Explicit;
         List<int> otherContainerTagIndices = otherIndices.Explicit;

         int currentIndex = 0;
         int otherIndex = 0;

         while (currentIndex < Indices.ExplicitTagCount && otherIndex < otherIndices.ExplicitTagCount)
         {
            int currentTagIndex = currentContainerTagIndices[currentIndex];
            int otherTagIndex = otherContainerTagIndices[otherIndex];

            if (currentTagIndex == otherTagIndex)
            {
               currentIndex++;
               otherIndex++;
               continue;
            }

            if (currentTagIndex < otherTagIndex)
            {
               added.Add(GameplayTagManager.GetDefinitionFromRuntimeIndex(currentTagIndex).Tag);
               currentIndex++;
               continue;
            }

            removed.Add(GameplayTagManager.GetDefinitionFromRuntimeIndex(otherTagIndex).Tag);
            otherIndex++;
         }

         for (; currentIndex < Indices.ExplicitTagCount; currentIndex++)
            added.Add(GameplayTagManager.GetDefinitionFromRuntimeIndex(currentContainerTagIndices[currentIndex]).Tag);

         for (; otherIndex < otherIndices.ExplicitTagCount; otherIndex++)
            removed.Add(GameplayTagManager.GetDefinitionFromRuntimeIndex(otherContainerTagIndices[otherIndex]).Tag);
      }

      public GameplayTagEnumerator GetExplicitTags()
      {
         EnsureRuntimeStateInitialized();
         return new GameplayTagEnumerator(m_Indices.Explicit);
      }

      public GameplayTagEnumerator GetTags()
      {
         EnsureRuntimeStateInitialized();
         return new GameplayTagEnumerator(m_Indices.Implicit);
      }

      public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags)
      {
         EnsureRuntimeStateInitialized();
         GameplayTagContainerUtility.GetParentTags(m_Indices.Implicit, tag, parentTags);
      }

      public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags)
      {
         EnsureRuntimeStateInitialized();
         GameplayTagContainerUtility.GetChildTags(m_Indices.Implicit, tag, childTags);
      }

      public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags)
      {
         EnsureRuntimeStateInitialized();
         GameplayTagContainerUtility.GetParentTags(m_Indices.Explicit, tag, parentTags);
      }

      public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags)
      {
         EnsureRuntimeStateInitialized();
         GameplayTagContainerUtility.GetChildTags(m_Indices.Explicit, tag, childTags);
      }

      public void Clear()
      {
         int currentEpoch = GameplayTagManager.CurrentRuntimeIndexEpoch;
         if (m_Indices.IsCreated && m_RuntimeIndexEpoch != 0 && m_RuntimeIndexEpoch != currentEpoch)
         {
            m_Indices = GameplayTagContainerIndices.Create();
            m_RuntimeIndexEpoch = currentEpoch;
            m_ExplicitBitset = Array.Empty<int>();
            m_ImplicitBitset = Array.Empty<int>();
            m_SerializedExplicitTags?.Clear();
            m_SerializedDirty = false;
            return;
         }

         EnsureRuntimeStateInitialized();
         m_Indices.Clear();
         m_ExplicitBitset = Array.Empty<int>();
         m_ImplicitBitset = Array.Empty<int>();
         m_SerializedExplicitTags?.Clear();
      }

      public void AddTag(GameplayTag tag)
      {
         if (tag.IsNone || !tag.IsValid)
            throw new ArgumentException("Cannot add an invalid gameplay tag.", nameof(tag));

         EnsureRuntimeStateInitialized();
         GameplayTagContainerIndices.Create(ref m_Indices);
         int index = BinarySearchUtility.Search(m_Indices.Explicit, tag.RuntimeIndex);
         if (index >= 0)
            return;

         m_Indices.Explicit.Insert(~index, tag.RuntimeIndex);
         RebuildExplicitBitsetIfNeeded();
         AddImplicitTagsFor(tag);
         SyncSerializedExplicitTagsWithRuntime();
      }

      public void AddTags<T>(in T container) where T : IReadOnlyGameplayTagContainer
      {
         if (container == null || container.IsEmpty)
            return;

         EnsureRuntimeStateInitialized();
         GameplayTagContainerIndices.Create(ref m_Indices);

         bool changed = false;
         foreach (GameplayTag tag in container.GetExplicitTags())
         {
            if (!tag.IsValid)
               continue;

            if (BinarySearchUtility.Search(m_Indices.Explicit, tag.RuntimeIndex) >= 0)
               continue;

            m_Indices.Explicit.Add(tag.RuntimeIndex);
            changed = true;
         }

         if (!changed)
            return;

         m_Indices.Explicit.Sort();
         RebuildExplicitBitsetIfNeeded();
         FillImplicitTags();
         SyncSerializedExplicitTagsWithRuntime();
      }

      public void RemoveTag(GameplayTag tag)
      {
         if (tag.IsNone || !tag.IsValid)
            throw new ArgumentException("Cannot remove an invalid gameplay tag.", nameof(tag));

         EnsureRuntimeStateInitialized();
         if (!m_Indices.IsCreated)
            return;

         int index = BinarySearchUtility.Search(m_Indices.Explicit, tag.RuntimeIndex);
         if (index < 0)
         {
            GameplayTagUtility.WarnNotExplicitlyAddedTagRemoval(tag);
            return;
         }

         m_Indices.Explicit.RemoveAt(index);
         RebuildExplicitBitsetIfNeeded();
         FillImplicitTags();
         SyncSerializedExplicitTagsWithRuntime();
      }

      public void RemoveTags<T>(in T other) where T : IReadOnlyGameplayTagContainer
      {
         EnsureRuntimeStateInitialized();
         if (!m_Indices.IsCreated || other == null || other.IsEmpty)
            return;

         bool changed = false;
         foreach (GameplayTag tag in other.GetExplicitTags())
         {
            int removeIndex = BinarySearchUtility.Search(m_Indices.Explicit, tag.RuntimeIndex);
            if (removeIndex < 0)
            {
               GameplayTagUtility.WarnNotExplicitlyAddedTagRemoval(tag);
               continue;
            }

            m_Indices.Explicit.RemoveAt(removeIndex);
            changed = true;
         }

         if (!changed)
            return;

         RebuildExplicitBitsetIfNeeded();
         FillImplicitTags();
         SyncSerializedExplicitTagsWithRuntime();
      }

      public bool ContainsRuntimeIndex(int runtimeIndex, bool explicitOnly)
      {
         EnsureRuntimeStateInitialized();
         if (runtimeIndex <= 0)
            return false;

         int[] bitset = explicitOnly ? m_ExplicitBitset : m_ImplicitBitset;
         if (bitset != null && bitset.Length > 0)
            return TryCheckBit(bitset, runtimeIndex);

         List<int> indices = explicitOnly ? m_Indices.Explicit : m_Indices.Implicit;
         return indices != null && indices.Count > 0 && BinarySearchUtility.Search(indices, runtimeIndex) >= 0;
      }

      private void EnsureRuntimeStateInitialized()
      {
         int currentEpoch = GameplayTagManager.CurrentRuntimeIndexEpoch;
         if (m_Indices.IsCreated && m_RuntimeIndexEpoch == currentEpoch)
            return;

         if (m_Indices.IsCreated && !m_Indices.IsEmpty &&
             (m_SerializedExplicitTags == null || m_SerializedExplicitTags.Count == 0))
         {
            throw new InvalidOperationException(
               "GameplayTagContainer runtime indices crossed an incompatible registry epoch without serialized tag names.");
         }

         m_Indices = GameplayTagContainerIndices.Create();
         m_ExplicitBitset = Array.Empty<int>();
         m_ImplicitBitset = Array.Empty<int>();
         m_RuntimeIndexEpoch = currentEpoch;

         if (m_SerializedExplicitTags == null || m_SerializedExplicitTags.Count == 0)
            return;

         for (int i = 0; i < m_SerializedExplicitTags.Count; i++)
         {
            GameplayTag tag = GameplayTagManager.RequestTag(m_SerializedExplicitTags[i], false);
            if (!tag.IsValid)
               continue;

            if (BinarySearchUtility.Search(m_Indices.Explicit, tag.RuntimeIndex) >= 0)
               continue;

            m_Indices.Explicit.Add(tag.RuntimeIndex);
         }

         if (m_Indices.Explicit.Count > 1)
            m_Indices.Explicit.Sort();

         RebuildExplicitBitsetIfNeeded();
         FillImplicitTags();
      }

      private void AddImplicitTagsFor(GameplayTag tag)
      {
         ReadOnlySpan<int> tagIndices = GameplayTagManager.GetHierarchyRuntimeIndicesSpan(tag.RuntimeIndex);
         for (int i = tagIndices.Length - 1; i >= 0; i--)
         {
            int parentRuntimeIndex = tagIndices[i];
            int index = BinarySearchUtility.Search(m_Indices.Implicit, parentRuntimeIndex);
            if (index >= 0)
               break;

            m_Indices.Implicit.Insert(~index, parentRuntimeIndex);
         }

         RebuildImplicitBitsetIfNeeded();
      }

      private void FillImplicitTags()
      {
         m_Indices.Implicit.Clear();

         for (int i = 0; i < m_Indices.Explicit.Count; i++)
         {
            ReadOnlySpan<int> hierarchyTagIndices = GameplayTagManager.GetHierarchyRuntimeIndicesSpan(m_Indices.Explicit[i]);
            for (int j = 0; j < hierarchyTagIndices.Length; j++)
            {
                int runtimeIndex = hierarchyTagIndices[j];
               if (BinarySearchUtility.Search(m_Indices.Implicit, runtimeIndex) >= 0)
                  continue;

               m_Indices.Implicit.Add(runtimeIndex);
            }
         }

         if (m_Indices.Implicit.Count > 1)
            m_Indices.Implicit.Sort();

         RebuildImplicitBitsetIfNeeded();
      }

      private void RebuildBitsetsFromIndices()
      {
         RebuildExplicitBitsetIfNeeded();
         RebuildImplicitBitsetIfNeeded();
      }

      private static bool TryCheckBit(int[] bitset, int runtimeIndex)
      {
         int wordIndex = runtimeIndex >> 5;
         return bitset != null &&
                wordIndex < bitset.Length &&
                (bitset[wordIndex] & (1 << (runtimeIndex & 31))) != 0;
      }

      private static void SetBit(int[] bitset, int runtimeIndex)
      {
         if (runtimeIndex <= 0)
            return;

         int wordIndex = runtimeIndex >> 5;
         if (wordIndex >= bitset.Length)
            return;

         bitset[wordIndex] |= 1 << (runtimeIndex & 31);
      }

      private static void ClearBitset(int[] bitset)
      {
         if (bitset != null && bitset.Length > 0)
            Array.Clear(bitset, 0, bitset.Length);
      }

      private void RebuildExplicitBitsetIfNeeded()
      {
         RebuildBitsetIfNeeded(m_Indices.Explicit, ref m_ExplicitBitset);
      }

      private void RebuildImplicitBitsetIfNeeded()
      {
         RebuildBitsetIfNeeded(m_Indices.Implicit, ref m_ImplicitBitset);
      }

      private static void RebuildBitsetIfNeeded(List<int> indices, ref int[] bitset)
      {
         if (indices == null || indices.Count == 0)
         {
            bitset = Array.Empty<int>();
            return;
         }

         int maxRuntimeIndex = indices[indices.Count - 1];
         if (!GameplayTagContainerUtility.ShouldUseBitset(indices.Count, maxRuntimeIndex, out int requiredLength))
         {
            bitset = Array.Empty<int>();
            return;
         }

         if (bitset.Length < requiredLength || bitset.Length > requiredLength * k_BitsetShrinkRatio)
            bitset = new int[requiredLength];
         ClearBitset(bitset);
         for (int i = 0; i < indices.Count; i++)
            SetBit(bitset, indices[i]);
      }

      private void SyncSerializedExplicitTagsWithRuntime()
      {
         m_SerializedDirty = true;
         if (m_SerializedExplicitTags != null)
         {
            FlushSerializedState();
         }
      }

      /// <summary>
      /// Called by serialization (OnBeforeSerialize) or when explicit serialized state is needed.
      /// Only rebuilds the serialized list if dirty.
      /// </summary>
      public void FlushSerializedState()
      {
         if (!m_SerializedDirty)
            return;

         EnsureRuntimeStateInitialized();

         m_SerializedDirty = false;

         if (!m_Indices.IsCreated)
            return;

         if (m_Indices.Explicit.Count == 0)
         {
            m_SerializedExplicitTags?.Clear();
            return;
         }

         m_SerializedExplicitTags ??= new List<string>(m_Indices.Explicit.Count);
         m_SerializedExplicitTags.Clear();

         for (int i = 0; i < m_Indices.Explicit.Count; i++)
         {
            GameplayTagDefinition definition = GameplayTagManager.GetDefinitionFromRuntimeIndex(m_Indices.Explicit[i]);
            if (!definition.IsNone())
               m_SerializedExplicitTags.Add(definition.TagName);
         }
      }

      [EditorBrowsable(EditorBrowsableState.Never)]
      public void Add(GameplayTag tag)
      {
         AddTag(tag);
      }

      public IEnumerator<GameplayTag> GetEnumerator()
      {
         EnsureRuntimeStateInitialized();
         return GetTags();
      }

      IEnumerator IEnumerable.GetEnumerator()
      {
         return GetEnumerator();
      }
   }
}
