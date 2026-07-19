using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace CycloneGames.GameplayTags.Core
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
      public OnTagCountChangedDelegate[] OnAnyChange;
      public OnTagCountChangedDelegate[] OnNewOrRemove;
   }

   public interface IGameplayTagCountContainer : IGameplayTagContainer
   {
      event OnTagCountChangedDelegate OnAnyTagCountChange;
      event OnTagCountChangedDelegate OnAnyTagNewOrRemove;

      int GetExplicitTagCount(GameplayTag tag);
      int GetTagCount(GameplayTag tag);
      void RegisterTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback);
      void RemoveTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback);
      void RemoveAllTagEventCallbacks();
   }

   [DebuggerDisplay("{DebuggerDisplay,nq}")]
   [DebuggerTypeProxy(typeof(GameplayTagContainerDebugView))]
   public class GameplayTagCountContainer : IGameplayTagCountContainer, IGameplayTagRuntimeIndexView
   {
      internal const int MaxRetainedMutationScratchEntries = 256;

      private sealed class BatchMutationScratch
      {
         internal readonly Dictionary<int, int> ExplicitDeltas = new();
         internal readonly Dictionary<int, int> TotalDeltas = new();
         internal readonly List<int> SortedRuntimeIndices = new();

         internal int MaximumEntryCount => Math.Max(
            Math.Max(ExplicitDeltas.Count, TotalDeltas.Count),
            SortedRuntimeIndices.Count);

         internal void Clear()
         {
            ExplicitDeltas.Clear();
            TotalDeltas.Clear();
            SortedRuntimeIndices.Clear();
         }
      }

      public bool IsEmpty { get { EnsureCompatible(); return m_Indices.IsEmpty; } }
      public int ExplicitTagCount { get { EnsureCompatible(); return m_Indices.ExplicitTagCount; } }
      public int TagCount { get { EnsureCompatible(); return m_Indices.TagCount; } }
      public GameplayTagContainerIndices Indices { get { EnsureCompatible(); return m_Indices; } }

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "It's used for debugging")]
      private string DebuggerDisplay => $"Count (Explicit, Total) = ({ExplicitTagCount}, {TagCount})";

      public event OnTagCountChangedDelegate OnAnyTagNewOrRemove
      {
         add => m_OnAnyTagNewOrRemove = AddSubscriber(m_OnAnyTagNewOrRemove, value);
         remove => m_OnAnyTagNewOrRemove = RemoveSubscriber(m_OnAnyTagNewOrRemove, value);
      }

      public event OnTagCountChangedDelegate OnAnyTagCountChange
      {
         add => m_OnAnyTagCountChange = AddSubscriber(m_OnAnyTagCountChange, value);
         remove => m_OnAnyTagCountChange = RemoveSubscriber(m_OnAnyTagCountChange, value);
      }

      private Dictionary<int, GameplayTagDelegateInfo> m_TagDelegateInfoMap;
      private Dictionary<int, int> m_TagCounts;
      private Dictionary<int, int> m_ExplicitTagCounts;
      private GameplayTagContainerIndices m_Indices;
      private BatchMutationScratch m_BatchMutationScratch;
      private OnTagCountChangedDelegate[] m_OnAnyTagNewOrRemove = Array.Empty<OnTagCountChangedDelegate>();
      private OnTagCountChangedDelegate[] m_OnAnyTagCountChange = Array.Empty<OnTagCountChangedDelegate>();
      private int m_RuntimeIndexEpoch;
      private int m_PeakStateEntryCount;
      private bool m_IsMutatingOrNotifying;

      internal bool HasRetainedMutationScratch => m_BatchMutationScratch != null;

      int IGameplayTagRuntimeIndexView.RuntimeIndexEpoch
      {
         get
         {
            EnsureCompatible();
            return m_RuntimeIndexEpoch;
         }
      }

      public GameplayTagEnumerator GetExplicitTags()
      {
         EnsureCompatible();
         return new GameplayTagEnumerator(m_Indices.Explicit);
      }

      public GameplayTagEnumerator GetTags()
      {
         EnsureCompatible();
         return new GameplayTagEnumerator(m_Indices.Implicit);
      }

      public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags)
      {
         EnsureCompatible();
         GameplayTagContainerUtility.GetParentTags(m_Indices.Implicit, tag, parentTags);
      }

      public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags)
      {
         EnsureCompatible();
         GameplayTagContainerUtility.GetChildTags(m_Indices.Implicit, tag, childTags);
      }

      public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags)
      {
         EnsureCompatible();
         GameplayTagContainerUtility.GetParentTags(m_Indices.Explicit, tag, parentTags);
      }

      public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags)
      {
         EnsureCompatible();
         GameplayTagContainerUtility.GetChildTags(m_Indices.Explicit, tag, childTags);
      }

      public int GetTagCount(GameplayTag tag)
      {
         EnsureCompatible();
         return tag.RuntimeIndex > 0 &&
                m_TagCounts != null &&
                m_TagCounts.TryGetValue(tag.RuntimeIndex, out int count)
            ? count
            : 0;
      }

      public int GetExplicitTagCount(GameplayTag tag)
      {
         EnsureCompatible();
         return tag.RuntimeIndex > 0 &&
                m_ExplicitTagCounts != null &&
                m_ExplicitTagCounts.TryGetValue(tag.RuntimeIndex, out int count)
            ? count
            : 0;
      }

      public bool ContainsRuntimeIndex(int runtimeIndex, bool explicitOnly)
      {
         EnsureCompatible();
         if (runtimeIndex <= 0)
            return false;

         Dictionary<int, int> counts = explicitOnly ? m_ExplicitTagCounts : m_TagCounts;
         return counts != null && counts.ContainsKey(runtimeIndex);
      }

      public void RegisterTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback)
      {
         EnsureCompatible();
         ValidateTagAndCallback(tag, callback);

         int runtimeIndex = tag.RuntimeIndex;
         m_TagDelegateInfoMap ??= new Dictionary<int, GameplayTagDelegateInfo>();
         m_TagDelegateInfoMap.TryGetValue(runtimeIndex, out GameplayTagDelegateInfo delegateInfo);
         switch (eventType)
         {
            case GameplayTagEventType.AnyCountChange:
               delegateInfo.OnAnyChange = AddSubscriber(delegateInfo.OnAnyChange, callback);
               break;
            case GameplayTagEventType.NewOrRemoved:
               delegateInfo.OnNewOrRemove = AddSubscriber(delegateInfo.OnNewOrRemove, callback);
               break;
            default:
               throw new ArgumentOutOfRangeException(nameof(eventType));
         }

         m_TagDelegateInfoMap[runtimeIndex] = delegateInfo;
      }

      public void RemoveTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback)
      {
         EnsureCompatible();
         if (callback == null)
            throw new ArgumentNullException(nameof(callback));
         if (m_TagDelegateInfoMap == null ||
             !m_TagDelegateInfoMap.TryGetValue(tag.RuntimeIndex, out GameplayTagDelegateInfo delegateInfo))
            return;

         switch (eventType)
         {
            case GameplayTagEventType.AnyCountChange:
               delegateInfo.OnAnyChange = RemoveSubscriber(delegateInfo.OnAnyChange, callback);
               break;
            case GameplayTagEventType.NewOrRemoved:
               delegateInfo.OnNewOrRemove = RemoveSubscriber(delegateInfo.OnNewOrRemove, callback);
               break;
            default:
               throw new ArgumentOutOfRangeException(nameof(eventType));
         }

         if (HasNoSubscribers(delegateInfo.OnAnyChange) && HasNoSubscribers(delegateInfo.OnNewOrRemove))
         {
            m_TagDelegateInfoMap.Remove(tag.RuntimeIndex);
            if (m_TagDelegateInfoMap.Count == 0)
               m_TagDelegateInfoMap = null;
         }
         else
            m_TagDelegateInfoMap[tag.RuntimeIndex] = delegateInfo;
      }

      public void RemoveAllTagEventCallbacks()
      {
         m_TagDelegateInfoMap = null;
         m_OnAnyTagNewOrRemove = Array.Empty<OnTagCountChangedDelegate>();
         m_OnAnyTagCountChange = Array.Empty<OnTagCountChangedDelegate>();
      }

      public void AddTag(GameplayTag tag)
      {
         MutateSingleTag(tag, 1);
      }

      public void AddTags<T>(in T other) where T : IReadOnlyGameplayTagContainer
      {
         MutateTags(other, 1);
      }

      public void RemoveTag(GameplayTag tag)
      {
         MutateSingleTag(tag, -1);
      }

      public void RemoveTags<T>(in T other) where T : IReadOnlyGameplayTagContainer
      {
         MutateTags(other, -1);
      }

      public void Clear()
      {
         BeginMutation();
         BatchMutationScratch scratch = null;
         List<Exception> callbackFailures = null;
         try
         {
            TagDataSnapshot operationSnapshot = GameplayTagManager.Snapshot;
            if (m_RuntimeIndexEpoch != 0 && m_RuntimeIndexEpoch != operationSnapshot.RuntimeIndexEpoch)
            {
               ReleaseAllOwnedStorage();
               m_TagDelegateInfoMap = null;
               m_RuntimeIndexEpoch = operationSnapshot.RuntimeIndexEpoch;
               return;
            }

            EnsureCompatible(operationSnapshot.RuntimeIndexEpoch);
            if (m_Indices.IsEmpty)
            {
               m_BatchMutationScratch = null;
               return;
            }

            if (HasAnySubscribers())
            {
               scratch = AcquireBatchMutationScratch();
               scratch.SortedRuntimeIndices.AddRange(m_Indices.Implicit);
            }

            bool releaseStateStorage = m_PeakStateEntryCount > MaxRetainedMutationScratchEntries;
            ClearStateStorage(releaseStateStorage);

            if (scratch != null)
            {
               for (int i = 0; i < scratch.SortedRuntimeIndices.Count; i++)
               {
                   FlushPendingChange(new PendingTagChange
                   {
                      RuntimeIndex = scratch.SortedRuntimeIndices[i],
                      NewCount = 0,
                      Flags = PendingTagChange.NotifyAnyCount | PendingTagChange.NotifyNewOrRemoved
                   }, operationSnapshot, ref callbackFailures);
                }
            }

            ThrowIfCallbackFailures(callbackFailures);
         }
         finally
         {
            if (scratch != null)
               ReleaseBatchMutationScratch(scratch, retain: false);
            else
               m_BatchMutationScratch = null;
            EndMutation();
         }
      }

      public GameplayTagEnumerator GetEnumerator()
      {
         EnsureCompatible();
         return new GameplayTagEnumerator(m_Indices.Implicit);
      }

      IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => GetEnumerator();
      IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

      private void MutateSingleTag(GameplayTag tag, int delta)
      {
         TagDataSnapshot operationSnapshot = GameplayTagManager.Snapshot;
         EnsureCompatible(operationSnapshot.RuntimeIndexEpoch);
         int runtimeIndex = ResolveRuntimeIndex(tag, operationSnapshot, nameof(tag));
         MutateSingleRuntimeIndex(runtimeIndex, delta, operationSnapshot);
      }

      private void MutateSingleRuntimeIndex(
         int runtimeIndex,
         int delta,
         TagDataSnapshot operationSnapshot)
      {
         ValidateRuntimeIndex(runtimeIndex, operationSnapshot, nameof(runtimeIndex));
         BeginMutation();
         List<Exception> callbackFailures = null;
         try
         {
            ReadOnlySpan<int> hierarchyRuntimeIndices = operationSnapshot.GetHierarchyRuntimeIndicesSpan(runtimeIndex);
            if (hierarchyRuntimeIndices.Length == 0 ||
                hierarchyRuntimeIndices.Length > GameplayTagUtility.MaxHierarchyDepth)
            {
               throw new InvalidOperationException(
                  $"Gameplay tag runtime index {runtimeIndex} has an invalid hierarchy depth.");
            }

            ValidateDelta(m_ExplicitTagCounts, runtimeIndex, delta, "explicit");
            for (int i = 0; i < hierarchyRuntimeIndices.Length; i++)
               ValidateDelta(m_TagCounts, hierarchyRuntimeIndices[i], delta, "hierarchical");

            EnsureStateStorageCreated();
            ApplyDelta(m_ExplicitTagCounts, m_Indices.Explicit, runtimeIndex, delta);

            Span<PendingTagChange> pendingChanges =
               stackalloc PendingTagChange[hierarchyRuntimeIndices.Length];
            for (int i = 0; i < hierarchyRuntimeIndices.Length; i++)
            {
               int hierarchyRuntimeIndex = hierarchyRuntimeIndices[i];
               int newCount = ApplyDelta(m_TagCounts, m_Indices.Implicit, hierarchyRuntimeIndex, delta);
               int previousCount = newCount - delta;
               byte flags = PendingTagChange.NotifyAnyCount;
               if ((previousCount == 0) != (newCount == 0))
                  flags |= PendingTagChange.NotifyNewOrRemoved;

               pendingChanges[i] = new PendingTagChange
               {
                  RuntimeIndex = hierarchyRuntimeIndex,
                  NewCount = newCount,
                  Flags = flags
               };
            }

            SortPendingChangesByRuntimeIndex(pendingChanges.Slice(0, hierarchyRuntimeIndices.Length));
            UpdatePeakStateEntryCount();
            for (int i = 0; i < hierarchyRuntimeIndices.Length; i++)
               FlushPendingChange(pendingChanges[i], operationSnapshot, ref callbackFailures);

            ReleaseOversizedEmptyStateStorage();
            ThrowIfCallbackFailures(callbackFailures);
         }
         finally
         {
            EndMutation();
         }
      }

      private void MutateTags<T>(in T other, int delta) where T : IReadOnlyGameplayTagContainer
      {
         TagDataSnapshot operationSnapshot = GameplayTagManager.Snapshot;
         EnsureCompatible(operationSnapshot.RuntimeIndexEpoch);
         if (other is null || other.IsEmpty)
            return;

         EnsureContainerCompatible(other, operationSnapshot.RuntimeIndexEpoch);
         GameplayTagContainerIndices otherIndices = other.Indices;
         List<int> explicitRuntimeIndices = otherIndices.Explicit;
         if (explicitRuntimeIndices == null || explicitRuntimeIndices.Count != other.ExplicitTagCount)
            throw new InvalidOperationException("A gameplay tag container reported inconsistent explicit runtime-index storage.");

         if (explicitRuntimeIndices.Count == 1)
         {
            MutateSingleRuntimeIndex(explicitRuntimeIndices[0], delta, operationSnapshot);
            return;
         }

         BeginMutation();
         BatchMutationScratch scratch = null;
         bool committed = false;
         List<Exception> callbackFailures = null;
         try
         {
            scratch = AcquireBatchMutationScratch();
            for (int i = 0; i < explicitRuntimeIndices.Count; i++)
            {
               int runtimeIndex = explicitRuntimeIndices[i];
               ValidateRuntimeIndex(runtimeIndex, operationSnapshot, nameof(other));
               AccumulateTagDeltas(
                  runtimeIndex,
                  delta,
                  operationSnapshot,
                  scratch.ExplicitDeltas,
                  scratch.TotalDeltas);
            }

            ValidateDeltas(m_ExplicitTagCounts, scratch.ExplicitDeltas, "explicit");
            ValidateDeltas(m_TagCounts, scratch.TotalDeltas, "hierarchical");
            EnsureStateStorageCreated();
            ApplyDeltas(
               m_ExplicitTagCounts,
               m_Indices.Explicit,
               scratch.ExplicitDeltas,
               scratch.SortedRuntimeIndices);
            ApplyDeltas(
               m_TagCounts,
               m_Indices.Implicit,
               scratch.TotalDeltas,
               scratch.SortedRuntimeIndices);
            UpdatePeakStateEntryCount();
            committed = true;
            FlushCommittedBatchChanges(
               scratch.SortedRuntimeIndices,
               scratch.TotalDeltas,
               operationSnapshot,
               ref callbackFailures);
            ReleaseOversizedEmptyStateStorage();
            ThrowIfCallbackFailures(callbackFailures);
         }
         finally
         {
            if (scratch != null)
               ReleaseBatchMutationScratch(scratch, retain: committed);
            EndMutation();
         }
      }

      private static void AccumulateTagDeltas(
         int runtimeIndex,
         int delta,
         TagDataSnapshot operationSnapshot,
         Dictionary<int, int> explicitDeltas,
         Dictionary<int, int> totalDeltas)
      {
         AccumulateDelta(explicitDeltas, runtimeIndex, delta);

         ReadOnlySpan<int> hierarchyRuntimeIndices = operationSnapshot.GetHierarchyRuntimeIndicesSpan(runtimeIndex);
         for (int i = 0; i < hierarchyRuntimeIndices.Length; i++)
            AccumulateDelta(totalDeltas, hierarchyRuntimeIndices[i], delta);
      }

      private static void AccumulateDelta(Dictionary<int, int> deltas, int runtimeIndex, int delta)
      {
         deltas.TryGetValue(runtimeIndex, out int existingDelta);
         deltas[runtimeIndex] = checked(existingDelta + delta);
      }

      private static void ValidateDeltas(Dictionary<int, int> counts, Dictionary<int, int> deltas, string countKind)
      {
         foreach (KeyValuePair<int, int> pair in deltas)
         {
            int currentCount = 0;
            counts?.TryGetValue(pair.Key, out currentCount);
            long nextCount = (long)currentCount + pair.Value;
            if (nextCount < 0)
               throw new InvalidOperationException($"Cannot decrement {countKind} gameplay tag count below zero for runtime index {pair.Key}.");
            if (nextCount > int.MaxValue)
               throw new OverflowException($"Cannot increment {countKind} gameplay tag count above Int32.MaxValue for runtime index {pair.Key}.");
         }
      }

      private static void ApplyDeltas(
         Dictionary<int, int> counts,
         List<int> activeIndices,
         Dictionary<int, int> deltas,
         List<int> sortedRuntimeIndices)
      {
         sortedRuntimeIndices.Clear();
         foreach (int runtimeIndex in deltas.Keys)
            sortedRuntimeIndices.Add(runtimeIndex);
         sortedRuntimeIndices.Sort();

         for (int i = 0; i < sortedRuntimeIndices.Count; i++)
         {
            int runtimeIndex = sortedRuntimeIndices[i];
            ApplyDelta(counts, activeIndices, runtimeIndex, deltas[runtimeIndex]);
         }
      }

      private static void ValidateDelta(
         Dictionary<int, int> counts,
         int runtimeIndex,
         int delta,
         string countKind)
      {
         int currentCount = 0;
         counts?.TryGetValue(runtimeIndex, out currentCount);
         long nextCount = (long)currentCount + delta;
         if (nextCount < 0)
            throw new InvalidOperationException(
               $"Cannot decrement {countKind} gameplay tag count below zero for runtime index {runtimeIndex}.");
         if (nextCount > int.MaxValue)
            throw new OverflowException(
               $"Cannot increment {countKind} gameplay tag count above Int32.MaxValue for runtime index {runtimeIndex}.");
      }

      private static int ApplyDelta(
         Dictionary<int, int> counts,
         List<int> activeIndices,
         int runtimeIndex,
         int delta)
      {
         counts.TryGetValue(runtimeIndex, out int previousCount);
         int newCount = previousCount + delta;
         if (newCount == 0)
         {
            counts.Remove(runtimeIndex);
            int activeIndex = BinarySearchUtility.Search(activeIndices, runtimeIndex);
            if (activeIndex >= 0)
               activeIndices.RemoveAt(activeIndex);
         }
         else
         {
            counts[runtimeIndex] = newCount;
            if (previousCount == 0)
            {
               int activeIndex = BinarySearchUtility.Search(activeIndices, runtimeIndex);
               if (activeIndex < 0)
                  activeIndices.Insert(~activeIndex, runtimeIndex);
            }
         }

         return newCount;
      }

      private static void SortPendingChangesByRuntimeIndex(Span<PendingTagChange> changes)
      {
         for (int i = 1; i < changes.Length; i++)
         {
            PendingTagChange value = changes[i];
            int insertionIndex = i - 1;
            while (insertionIndex >= 0 && changes[insertionIndex].RuntimeIndex > value.RuntimeIndex)
            {
               changes[insertionIndex + 1] = changes[insertionIndex];
               insertionIndex--;
            }

            changes[insertionIndex + 1] = value;
         }
      }

      private void FlushCommittedBatchChanges(
         List<int> sortedRuntimeIndices,
         Dictionary<int, int> totalDeltas,
         TagDataSnapshot operationSnapshot,
         ref List<Exception> callbackFailures)
      {
         for (int i = 0; i < sortedRuntimeIndices.Count; i++)
         {
            int runtimeIndex = sortedRuntimeIndices[i];
            int newCount = 0;
            m_TagCounts?.TryGetValue(runtimeIndex, out newCount);
            int previousCount = newCount - totalDeltas[runtimeIndex];
            byte flags = PendingTagChange.NotifyAnyCount;
            if ((previousCount == 0) != (newCount == 0))
               flags |= PendingTagChange.NotifyNewOrRemoved;

            FlushPendingChange(new PendingTagChange
            {
               RuntimeIndex = runtimeIndex,
               NewCount = newCount,
               Flags = flags
            }, operationSnapshot, ref callbackFailures);
         }
      }

      private void FlushPendingChange(
         PendingTagChange change,
         TagDataSnapshot operationSnapshot,
         ref List<Exception> callbackFailures)
      {
         GameplayTag tag = operationSnapshot.GetTagFromRuntimeIndex(change.RuntimeIndex);
         GameplayTagDelegateInfo delegateInfo = default;
         if (m_TagDelegateInfoMap != null)
            m_TagDelegateInfoMap.TryGetValue(change.RuntimeIndex, out delegateInfo);

         if ((change.Flags & PendingTagChange.NotifyNewOrRemoved) != 0)
         {
            InvokeSubscribers(delegateInfo.OnNewOrRemove, tag, change.NewCount, ref callbackFailures);
            InvokeSubscribers(m_OnAnyTagNewOrRemove, tag, change.NewCount, ref callbackFailures);
         }

         if ((change.Flags & PendingTagChange.NotifyAnyCount) != 0)
         {
            InvokeSubscribers(delegateInfo.OnAnyChange, tag, change.NewCount, ref callbackFailures);
            InvokeSubscribers(m_OnAnyTagCountChange, tag, change.NewCount, ref callbackFailures);
         }
      }

      private BatchMutationScratch AcquireBatchMutationScratch()
      {
         m_BatchMutationScratch ??= new BatchMutationScratch();
         return m_BatchMutationScratch;
      }

      private void ReleaseBatchMutationScratch(BatchMutationScratch scratch, bool retain)
      {
         int maximumEntryCount = scratch.MaximumEntryCount;
         scratch.Clear();
         if (!retain || maximumEntryCount > MaxRetainedMutationScratchEntries)
            m_BatchMutationScratch = null;
      }

      private void EnsureStateStorageCreated()
      {
         m_TagCounts ??= new Dictionary<int, int>();
         m_ExplicitTagCounts ??= new Dictionary<int, int>();
         GameplayTagContainerIndices.Create(ref m_Indices);
      }

      private void UpdatePeakStateEntryCount()
      {
         m_PeakStateEntryCount = Math.Max(m_PeakStateEntryCount, m_Indices.TagCount);
      }

      private void ReleaseOversizedEmptyStateStorage()
      {
         if (!m_Indices.IsEmpty || m_PeakStateEntryCount <= MaxRetainedMutationScratchEntries)
            return;

         ReleaseAllOwnedStorage();
      }

      private void ClearStateStorage(bool releaseStorage)
      {
         if (releaseStorage)
         {
            ReleaseAllOwnedStorage();
            return;
         }

         m_ExplicitTagCounts?.Clear();
         m_TagCounts?.Clear();
         m_Indices.Clear();
         m_PeakStateEntryCount = 0;
      }

      private void ReleaseAllOwnedStorage()
      {
         m_ExplicitTagCounts = null;
         m_TagCounts = null;
         m_Indices = default;
         m_BatchMutationScratch = null;
         m_PeakStateEntryCount = 0;
      }

      private bool HasAnySubscribers()
      {
         return (m_TagDelegateInfoMap != null && m_TagDelegateInfoMap.Count != 0) ||
                !HasNoSubscribers(m_OnAnyTagNewOrRemove) ||
                !HasNoSubscribers(m_OnAnyTagCountChange);
      }

      private static void InvokeSubscribers(
         OnTagCountChangedDelegate[] subscribers,
         GameplayTag tag,
         int newCount,
         ref List<Exception> callbackFailures)
      {
         if (subscribers == null)
            return;

         for (int i = 0; i < subscribers.Length; i++)
         {
            try
            {
               subscribers[i](tag, newCount);
            }
            catch (Exception exception)
            {
               callbackFailures ??= new List<Exception>(1);
               callbackFailures.Add(exception);
               try
               {
                  GameplayTagLogger.LogError(
                     $"Gameplay tag count callback failed for '{tag}' with committed count {newCount}: {exception}");
               }
               catch
               {
                  // A host logger must not prevent later committed-state subscribers from running.
               }
            }
         }
      }

      private static void ThrowIfCallbackFailures(List<Exception> callbackFailures)
      {
         if (callbackFailures == null)
            return;

         throw new AggregateException(
            "One or more gameplay tag count callbacks failed after the tag state was committed. " +
            "Do not retry the mutation without reconciling the committed state.",
            callbackFailures);
      }

      private void BeginMutation()
      {
         if (m_IsMutatingOrNotifying)
            throw new InvalidOperationException("GameplayTagCountContainer does not allow mutation reentry from a count callback.");
         m_IsMutatingOrNotifying = true;
      }

      private void EndMutation()
      {
         m_IsMutatingOrNotifying = false;
      }

      private void EnsureCompatible()
      {
         EnsureCompatible(GameplayTagManager.CurrentRuntimeIndexEpoch);
      }

      private void EnsureCompatible(int expectedRuntimeIndexEpoch)
      {
         if (m_RuntimeIndexEpoch == 0)
         {
            m_RuntimeIndexEpoch = expectedRuntimeIndexEpoch;
            return;
         }

         if (m_RuntimeIndexEpoch != expectedRuntimeIndexEpoch)
         {
            throw new InvalidOperationException(
               "GameplayTagCountContainer belongs to an incompatible gameplay tag runtime-index epoch.");
         }
      }

      private static void EnsureContainerCompatible<T>(in T container, int expectedRuntimeIndexEpoch)
         where T : IReadOnlyGameplayTagContainer
      {
         if (container is IGameplayTagRuntimeIndexView runtimeIndexView &&
             runtimeIndexView.RuntimeIndexEpoch != expectedRuntimeIndexEpoch)
         {
            throw new InvalidOperationException(
               "Gameplay tag containers from different runtime-index epochs cannot be combined.");
         }
      }

      private static int ResolveRuntimeIndex(
         GameplayTag tag,
         TagDataSnapshot operationSnapshot,
         string parameterName)
      {
         int runtimeIndex = tag.GetResolvedRuntimeIndex(operationSnapshot);
         ValidateRuntimeIndex(runtimeIndex, operationSnapshot, parameterName);
         return runtimeIndex;
      }

      private static void ValidateRuntimeIndex(
         int runtimeIndex,
         TagDataSnapshot operationSnapshot,
         string parameterName)
      {
         if (runtimeIndex <= 0 || (uint)runtimeIndex >= (uint)operationSnapshot.TotalTagCount)
            throw new ArgumentException("A valid gameplay tag is required.", parameterName);
      }

      private static void ValidateTag(GameplayTag tag, string parameterName)
      {
         if (tag.IsNone || !tag.IsValid)
            throw new ArgumentException("A valid gameplay tag is required.", parameterName);
      }

      private static void ValidateTagAndCallback(GameplayTag tag, OnTagCountChangedDelegate callback)
      {
         ValidateTag(tag, nameof(tag));
         if (callback == null)
            throw new ArgumentNullException(nameof(callback));
      }

      private static bool HasNoSubscribers(OnTagCountChangedDelegate[] subscribers)
      {
         return subscribers == null || subscribers.Length == 0;
      }

      private static OnTagCountChangedDelegate[] AddSubscriber(
         OnTagCountChangedDelegate[] subscribers,
         OnTagCountChangedDelegate subscriber)
      {
         if (subscriber == null)
            throw new ArgumentNullException(nameof(subscriber));

         int count = subscribers?.Length ?? 0;
         OnTagCountChangedDelegate[] next = new OnTagCountChangedDelegate[count + 1];
         if (count > 0)
            Array.Copy(subscribers, next, count);
         next[count] = subscriber;
         return next;
      }

      private static OnTagCountChangedDelegate[] RemoveSubscriber(
         OnTagCountChangedDelegate[] subscribers,
         OnTagCountChangedDelegate subscriber)
      {
         if (subscriber == null || subscribers == null || subscribers.Length == 0)
            return subscribers ?? Array.Empty<OnTagCountChangedDelegate>();

         int removeIndex = -1;
         for (int i = subscribers.Length - 1; i >= 0; i--)
         {
            if (subscribers[i] == subscriber)
            {
               removeIndex = i;
               break;
            }
         }

         if (removeIndex < 0)
            return subscribers;
         if (subscribers.Length == 1)
            return Array.Empty<OnTagCountChangedDelegate>();

         OnTagCountChangedDelegate[] next = new OnTagCountChangedDelegate[subscribers.Length - 1];
         if (removeIndex > 0)
            Array.Copy(subscribers, 0, next, 0, removeIndex);
         if (removeIndex < next.Length)
            Array.Copy(subscribers, removeIndex + 1, next, removeIndex, next.Length - removeIndex);
         return next;
      }
   }
}
