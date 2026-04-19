using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CycloneGames.GameplayTags.Runtime
{
   public delegate void OnTagCountChangedDelegate(GameplayTag gameplayTag, int newCount);

   public enum GameplayTagEventType
   {
      NewOrRemoved,
      AnyCountChange
   }

   internal struct PendingTagChange
   {
      public const byte NotifyAnyCount = 1;
      public const byte NotifyNewOrRemoved = 2;

      public int RuntimeIndex;
      public int NewCount;
      public byte Flags;
   }

   internal struct GameplayTagDelegateInfo
   {
      public OnTagCountChangedDelegate OnAnyChange;
      public OnTagCountChangedDelegate OnNewOrRemove;
   }

   public interface IGameplayTagCountContainer : IGameplayTagContainer
   {
      /// <summary>
      /// Event that is called when the count of any tag changes.
      /// </summary>
      event OnTagCountChangedDelegate OnAnyTagCountChange;

      /// <summary>
      /// Eve that is called when any tag is added or removed.
      /// </summary>
      event OnTagCountChangedDelegate OnAnyTagNewOrRemove;

      /// <summary>
      /// Gets the count of a specific tag (the number of times it has been explicitly added).
      /// </summary>
      int GetExplicitTagCount(GameplayTag tag);

      /// <summary>
      /// Gets the count of a specific tag.
      /// </summary>
      int GetTagCount(GameplayTag tag);

      /// <summary>
      /// Registers a callback for a tag event.
      /// </summary>
      /// <param name="callback">The callback to register.</param>
      /// <param name="tag">The gameplay tag.</param>
      /// <param name="eventType">The type of event.</param>
      void RegisterTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback);

      /// <summary>
      /// Removes a callback for a tag event.
      /// </summary>
      /// <param name="callback">The callback to remove.</param>
      /// <param name="tag">The gameplay tag.</param>
      /// <param name="eventType">The type of event.</param>
      void RemoveTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback);

      /// <summary>
      /// Removes all callbacks for tag events.
      /// </summary>
      void RemoveAllTagEventCallbacks();
   }

   [DebuggerDisplay("{DebuggerDisplay,nq}")]
   [DebuggerTypeProxy(typeof(GameplayTagContainerDebugView))]
   public class GameplayTagCountContainer : IGameplayTagCountContainer
   {
      /// <inheritdoc />
      public bool IsEmpty => m_Indices.Explicit.Count == 0;

      /// <inheritdoc />
      public int ExplicitTagCount => m_Indices.Explicit.Count;

      /// <inheritdoc />
      public int TagCount => m_Indices.Implicit.Count;

      /// <inheritdoc />
      public GameplayTagContainerIndices Indices => m_Indices;

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "It's used for debugging")]
      private string DebuggerDisplay => $"Count (Explicit, Total) = ({ExplicitTagCount}, {TagCount})";

      public event OnTagCountChangedDelegate OnAnyTagNewOrRemove;
      public event OnTagCountChangedDelegate OnAnyTagCountChange;

      private Dictionary<int, GameplayTagDelegateInfo> m_TagDelegateInfoMap = new();
      private int[] m_TagCountByIndex = Array.Empty<int>();
      private int[] m_ExplicitTagCountByIndex = Array.Empty<int>();
      private GameplayTagContainerIndices m_Indices = GameplayTagContainerIndices.Create();

      /// <inheritdoc />
      public GameplayTagEnumerator GetExplicitTags()
      {
         return new GameplayTagEnumerator(m_Indices.Explicit);
      }

      /// <inheritdoc />
      public GameplayTagEnumerator GetTags()
      {
         return new GameplayTagEnumerator(m_Indices.Implicit);
      }

      /// <inheritdoc />
      public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags)
      {
         GameplayTagContainerUtility.GetParentTags(m_Indices.Implicit, tag, parentTags);
      }

      /// <inheritdoc />
      public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags)
      {
         GameplayTagContainerUtility.GetChildTags(m_Indices.Implicit, tag, childTags);
      }

      /// <inheritdoc />
      public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags)
      {
         GameplayTagContainerUtility.GetParentTags(m_Indices.Explicit, tag, parentTags);
      }

      /// <inheritdoc />
      public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags)
      {
         GameplayTagContainerUtility.GetChildTags(m_Indices.Explicit, tag, childTags);
      }

      /// <inheritdoc />
      public int GetTagCount(GameplayTag tag)
      {
         int runtimeIndex = tag.RuntimeIndex;
         return runtimeIndex >= 0 && runtimeIndex < m_TagCountByIndex.Length ? m_TagCountByIndex[runtimeIndex] : 0;
      }

      /// <inheritdoc />
      public int GetExplicitTagCount(GameplayTag tag)
      {
         int runtimeIndex = tag.RuntimeIndex;
         return runtimeIndex >= 0 && runtimeIndex < m_ExplicitTagCountByIndex.Length ? m_ExplicitTagCountByIndex[runtimeIndex] : 0;
      }

      public bool ContainsRuntimeIndex(int runtimeIndex, bool explicitOnly)
      {
         if (runtimeIndex <= 0)
         {
            return false;
         }

         int[] counts = explicitOnly ? m_ExplicitTagCountByIndex : m_TagCountByIndex;
         return runtimeIndex < counts.Length && counts[runtimeIndex] > 0;
      }

      /// <inheritdoc />
      public void RegisterTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback)
      {
         m_TagDelegateInfoMap.TryGetValue(tag.RuntimeIndex, out GameplayTagDelegateInfo delegateInfo);
         GetEventDelegate(ref delegateInfo, eventType) += callback;
         m_TagDelegateInfoMap[tag.RuntimeIndex] = delegateInfo;
      }

      /// <inheritdoc />
      public void RemoveTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback)
      {
         if (m_TagDelegateInfoMap.TryGetValue(tag.RuntimeIndex, out GameplayTagDelegateInfo delegateInfo))
         {
            GetEventDelegate(ref delegateInfo, eventType) -= callback;
            m_TagDelegateInfoMap[tag.RuntimeIndex] = delegateInfo;
         }
      }

      /// <inheritdoc />
      public void RemoveAllTagEventCallbacks()
      {
         m_TagDelegateInfoMap.Clear();
      }

      private static ref OnTagCountChangedDelegate GetEventDelegate(ref GameplayTagDelegateInfo delegateInfo, GameplayTagEventType eventType)
      {
         switch (eventType)
         {
            case GameplayTagEventType.AnyCountChange:
               return ref delegateInfo.OnAnyChange;

            case GameplayTagEventType.NewOrRemoved:
               return ref delegateInfo.OnNewOrRemove;
         }

         throw new ArgumentException(nameof(eventType));
      }

      /// <inheritdoc />
      public void AddTag(GameplayTag tag)
      {
         if (!tag.IsValid)
            throw new ArgumentException("Cannot add an invalid gameplay tag.", nameof(tag));

         using (Pools.ListPool<PendingTagChange>.Get(out List<PendingTagChange> pendingChanges))
         using (Pools.DictionaryPool<int, int>.Get(out Dictionary<int, int> changeIndexMap))
         {
            AddTagInternal(tag, pendingChanges, changeIndexMap);
            FlushPendingChanges(pendingChanges);
         }
      }

      /// <inheritdoc />
      public void AddTags<T>(in T other) where T : IGameplayTagContainer
      {
         using (Pools.ListPool<PendingTagChange>.Get(out List<PendingTagChange> pendingChanges))
         using (Pools.DictionaryPool<int, int>.Get(out Dictionary<int, int> changeIndexMap))
         {
            bool explicitChanged = false;
            bool implicitChanged = false;
            foreach (GameplayTag gameplayTag in other.GetExplicitTags())
            {
               if (!gameplayTag.IsValid)
                  continue;

               AddTagInternal(gameplayTag, pendingChanges, changeIndexMap, ref explicitChanged, ref implicitChanged, batchMode: true);
            }

            if (explicitChanged && m_Indices.Explicit.Count > 1)
               m_Indices.Explicit.Sort();

            if (implicitChanged && m_Indices.Implicit.Count > 1)
               m_Indices.Implicit.Sort();

            FlushPendingChanges(pendingChanges);
         }
      }

      private void AddTagInternal(GameplayTag tag, List<PendingTagChange> pendingChanges, Dictionary<int, int> changeIndexMap)
      {
         bool explicitChanged = false;
         bool implicitChanged = false;
         AddTagInternal(tag, pendingChanges, changeIndexMap, ref explicitChanged, ref implicitChanged, batchMode: false);
      }

      private void AddTagInternal(GameplayTag tag, List<PendingTagChange> pendingChanges, Dictionary<int, int> changeIndexMap, ref bool explicitChanged, ref bool implicitChanged, bool batchMode)
      {
         EnsureCountCapacity(tag.RuntimeIndex);
         int previousExplictTagCount = m_ExplicitTagCountByIndex[tag.RuntimeIndex];
         m_ExplicitTagCountByIndex[tag.RuntimeIndex] = previousExplictTagCount + 1;

         if (previousExplictTagCount == 0)
         {
            if (batchMode)
            {
               m_Indices.Explicit.Add(tag.RuntimeIndex);
               explicitChanged = true;
            }
            else
            {
               int index = ~BinarySearchUtility.Search(m_Indices.Explicit, tag.RuntimeIndex);
               m_Indices.Explicit.Insert(index, tag.RuntimeIndex);
            }
         }

         ReadOnlySpan<int> hierarchyTagIndices = GameplayTagManager.GetHierarchyRuntimeIndicesSpan(tag.RuntimeIndex);
         for (int i = 0; i < hierarchyTagIndices.Length; i++)
         {
            int hierarchyRuntimeIndex = hierarchyTagIndices[i];
            EnsureCountCapacity(hierarchyRuntimeIndex);
            int previousTagCount = m_TagCountByIndex[hierarchyRuntimeIndex];
            m_TagCountByIndex[hierarchyRuntimeIndex] = previousTagCount + 1;

            if (previousTagCount == 0)
            {
               if (batchMode)
               {
                  m_Indices.Implicit.Add(hierarchyRuntimeIndex);
                  implicitChanged = true;
               }
               else
               {
                  int index = ~BinarySearchUtility.Search(m_Indices.Implicit, hierarchyRuntimeIndex);
                  m_Indices.Implicit.Insert(index, hierarchyRuntimeIndex);
               }
            }

            byte flags = PendingTagChange.NotifyAnyCount;
            if (previousTagCount == 0)
               flags |= PendingTagChange.NotifyNewOrRemoved;

            RecordPendingChange(pendingChanges, changeIndexMap, hierarchyRuntimeIndex, previousTagCount + 1, flags);
         }
      }

      /// <inheritdoc />
      public void RemoveTag(GameplayTag tag)
      {
         if (!tag.IsValid)
            throw new ArgumentException("Cannot remove an invalid gameplay tag.", nameof(tag));

         using (Pools.ListPool<PendingTagChange>.Get(out List<PendingTagChange> pendingChanges))
         using (Pools.DictionaryPool<int, int>.Get(out Dictionary<int, int> changeIndexMap))
         {
            RemoveTagInternal(tag, pendingChanges, changeIndexMap);
            FlushPendingChanges(pendingChanges);
         }
      }

      /// <inheritdoc />
      public void RemoveTags<T>(in T other) where T : IGameplayTagContainer
      {
         using (Pools.ListPool<PendingTagChange>.Get(out List<PendingTagChange> pendingChanges))
         using (Pools.DictionaryPool<int, int>.Get(out Dictionary<int, int> changeIndexMap))
         {
            bool explicitChanged = false;
            bool implicitChanged = false;
            foreach (GameplayTag gameplayTag in other.GetExplicitTags())
            {
               RemoveTagInternal(gameplayTag, pendingChanges, changeIndexMap, ref explicitChanged, ref implicitChanged, batchMode: true);
            }

            if (explicitChanged)
               CompactListFromCounts(m_Indices.Explicit, m_ExplicitTagCountByIndex);

            if (implicitChanged)
               CompactListFromCounts(m_Indices.Implicit, m_TagCountByIndex);

            FlushPendingChanges(pendingChanges);
         }
      }

      private void RemoveTagInternal(GameplayTag tag, List<PendingTagChange> pendingChanges, Dictionary<int, int> changeIndexMap)
      {
         bool explicitChanged = false;
         bool implicitChanged = false;
         RemoveTagInternal(tag, pendingChanges, changeIndexMap, ref explicitChanged, ref implicitChanged, batchMode: false);
      }

      private void RemoveTagInternal(GameplayTag tag, List<PendingTagChange> pendingChanges, Dictionary<int, int> changeIndexMap, ref bool explicitChanged, ref bool implicitChanged, bool batchMode)
      {
         int runtimeIndex = tag.RuntimeIndex;
         int explictTagCount = runtimeIndex >= 0 && runtimeIndex < m_ExplicitTagCountByIndex.Length ? m_ExplicitTagCountByIndex[runtimeIndex] : 0;
         if (explictTagCount == 0)
         {
            GameplayTagUtility.WarnNotExplictlyAddedTagRemoval(tag);
            return;
         }

         if (explictTagCount == 1)
         {
            if (batchMode)
            {
               explicitChanged = true;
            }
            else
            {
               int index = BinarySearchUtility.Search(m_Indices.Explicit, tag.RuntimeIndex);
               m_Indices.Explicit.RemoveAt(index);
            }
            m_ExplicitTagCountByIndex[runtimeIndex] = 0;
         }
         else
         {
            m_ExplicitTagCountByIndex[runtimeIndex] = explictTagCount - 1;
         }

         ReadOnlySpan<int> hierarchyTagIndices = GameplayTagManager.GetHierarchyRuntimeIndicesSpan(tag.RuntimeIndex);
         for (int i = 0; i < hierarchyTagIndices.Length; i++)
         {
            int hierarchyRuntimeIndex = hierarchyTagIndices[i];
            int tagCount = hierarchyRuntimeIndex >= 0 && hierarchyRuntimeIndex < m_TagCountByIndex.Length ? m_TagCountByIndex[hierarchyRuntimeIndex] : 0;
            if (tagCount == 0)
            {
               break;
            }

            if (tagCount == 1)
            {
               if (batchMode)
               {
                  implicitChanged = true;
               }
               else
               {
                  int index = BinarySearchUtility.Search(m_Indices.Implicit, hierarchyRuntimeIndex);
                  m_Indices.Implicit.RemoveAt(index);
               }
               m_TagCountByIndex[hierarchyRuntimeIndex] = 0;
               RecordPendingChange(pendingChanges, changeIndexMap, hierarchyRuntimeIndex, 0, PendingTagChange.NotifyAnyCount | PendingTagChange.NotifyNewOrRemoved);
               continue;
            }

            m_TagCountByIndex[hierarchyRuntimeIndex] = tagCount - 1;
            RecordPendingChange(pendingChanges, changeIndexMap, hierarchyRuntimeIndex, tagCount - 1, PendingTagChange.NotifyAnyCount);
         }
      }

      /// <inheritdoc />
      public void Clear()
      {
         using (Pools.ListPool<PendingTagChange>.Get(out List<PendingTagChange> pendingChanges))
         using (Pools.DictionaryPool<int, int>.Get(out Dictionary<int, int> changeIndexMap))
         {
            foreach (GameplayTag tag in GetTags())
            {
               RecordPendingChange(pendingChanges, changeIndexMap, tag.RuntimeIndex, 0, PendingTagChange.NotifyAnyCount | PendingTagChange.NotifyNewOrRemoved);
            }

            Array.Clear(m_ExplicitTagCountByIndex, 0, m_ExplicitTagCountByIndex.Length);
            Array.Clear(m_TagCountByIndex, 0, m_TagCountByIndex.Length);
            m_Indices.Clear();

            FlushPendingChanges(pendingChanges);
         }
      }

      public GameplayTagEnumerator GetEnumerator()
      {
         return new GameplayTagEnumerator(m_Indices.Implicit);
      }

      IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator()
      {
         return GetEnumerator();
      }

      IEnumerator IEnumerable.GetEnumerator()
      {
         return GetEnumerator();
      }

      private void EnsureCountCapacity(int runtimeIndex)
      {
         if (runtimeIndex < 0)
         {
            return;
         }

         if (runtimeIndex >= m_TagCountByIndex.Length)
         {
            int newLength = Math.Max(runtimeIndex + 1, Math.Max(16, m_TagCountByIndex.Length * 2));
            Array.Resize(ref m_TagCountByIndex, newLength);
         }

         if (runtimeIndex >= m_ExplicitTagCountByIndex.Length)
         {
            int newLength = Math.Max(runtimeIndex + 1, Math.Max(16, m_ExplicitTagCountByIndex.Length * 2));
            Array.Resize(ref m_ExplicitTagCountByIndex, newLength);
         }
      }

      private void FlushPendingChanges(List<PendingTagChange> pendingChanges)
      {
         for (int i = 0; i < pendingChanges.Count; i++)
         {
            PendingTagChange change = pendingChanges[i];
            GameplayTag tag = GameplayTagManager.GetTagFromRuntimeIndex(change.RuntimeIndex);
            m_TagDelegateInfoMap.TryGetValue(change.RuntimeIndex, out GameplayTagDelegateInfo delegateInfo);

            if ((change.Flags & PendingTagChange.NotifyNewOrRemoved) != 0)
            {
               delegateInfo.OnNewOrRemove?.Invoke(tag, change.NewCount);
               OnAnyTagNewOrRemove?.Invoke(tag, change.NewCount);
            }

            if ((change.Flags & PendingTagChange.NotifyAnyCount) != 0)
            {
               delegateInfo.OnAnyChange?.Invoke(tag, change.NewCount);
               OnAnyTagCountChange?.Invoke(tag, change.NewCount);
            }
         }
      }

      private static void RecordPendingChange(List<PendingTagChange> pendingChanges, Dictionary<int, int> changeIndexMap, int runtimeIndex, int newCount, byte flags)
      {
         if (changeIndexMap.TryGetValue(runtimeIndex, out int existingIndex))
         {
            PendingTagChange existing = pendingChanges[existingIndex];
            existing.NewCount = newCount;
            existing.Flags |= flags;
            pendingChanges[existingIndex] = existing;
            return;
         }

         changeIndexMap.Add(runtimeIndex, pendingChanges.Count);
         pendingChanges.Add(new PendingTagChange
         {
            RuntimeIndex = runtimeIndex,
            NewCount = newCount,
            Flags = flags
         });
      }

      private static void CompactListFromCounts(List<int> indices, int[] counts)
      {
         int writeIndex = 0;
         for (int readIndex = 0; readIndex < indices.Count; readIndex++)
         {
            int runtimeIndex = indices[readIndex];
            if (runtimeIndex < counts.Length && counts[runtimeIndex] > 0)
            {
               indices[writeIndex++] = runtimeIndex;
            }
         }

         if (writeIndex < indices.Count)
            indices.RemoveRange(writeIndex, indices.Count - writeIndex);
      }
   }
}
