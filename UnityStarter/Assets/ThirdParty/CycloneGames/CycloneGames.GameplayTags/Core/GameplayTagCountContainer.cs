using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>Why a tag-event callback is being invoked.</summary>
   public enum GameplayTagEventType
   {
      /// <summary>The count of the tag changed, including through zero.</summary>
      AnyCountChange = 0,

      /// <summary>The tag count crossed zero in either direction.</summary>
      NewOrRemoved = 1,
   }

   /// <summary>Callback shape for tag count changes.</summary>
   public delegate void OnTagCountChangedDelegate(GameplayTag gameplayTag, int newCount);

   /// <summary>Write access to a tag container whose members carry reference counts.</summary>
   public interface IGameplayTagCountContainer : IGameplayTagContainer
   {
      /// <summary>Raised for every tag whose count changed, including through zero.</summary>
      event OnTagCountChangedDelegate OnAnyTagCountChange;

      /// <summary>Raised when a tag's count crosses zero in either direction.</summary>
      event OnTagCountChangedDelegate OnAnyTagNewOrRemove;

      /// <summary>The number of times <paramref name="tag"/> has been added, which is 0 when absent.</summary>
      int GetTagCount(GameplayTag tag);

      /// <summary>The number of times <paramref name="tag"/> was added explicitly.</summary>
      int GetExplicitTagCount(GameplayTag tag);

      void RegisterTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback);
      void RemoveTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback);
      void RemoveAllTagEventCallbacks();
   }

   /// <summary>
   /// A tag container whose members carry reference counts instead of presence flags.
   /// </summary>
   /// <remarks>
   /// <para>
   /// This is what a GameplayAbility System uses for granted tags: two effects may both grant "Status.Burning",
   /// and the tag must stay present until both are removed. Adding a tag increments its count and the count of
   /// every ancestor; removing decrements. A tag is contained while its count is above zero.
   /// </para>
   /// <para>
   /// Storage is two sorted parallel index/count array pairs - one for the explicitly granted set, one for
   /// the expanded set. There is no dictionary on any mutation path, so an add or remove touches a handful of
   /// contiguous array slots and allocates nothing once warm.
   /// </para>
   /// <para>
   /// <b>Notifications.</b> Subscribers are invoked only after a mutation has been fully applied, so a
   /// callback never observes a half-mutated container. A callback that throws does not stop the others; the
   /// failures are aggregated and rethrown once the batch has been flushed. Re-entrant mutation from inside a
   /// callback is rejected.
   /// </para>
   /// <para>
   /// <b>Threading and epochs.</b> Owner-thread state, like <see cref="GameplayTagContainer"/>. Mutations on a
   /// container whose registry epoch has moved on throw; reads do not check.
   /// </para>
   /// </remarks>
   [DebuggerTypeProxy(typeof(GameplayTagContainerDebugView))]
   [DebuggerDisplay("{DebuggerDisplay,nq}")]
   public class GameplayTagCountContainer : IGameplayTagCountContainer, IEnumerable<GameplayTag>
   {
      private int[] m_ExplicitIndices = Array.Empty<int>();
      private int[] m_ExplicitCounts = Array.Empty<int>();
      private int[] m_ImplicitIndices = Array.Empty<int>();
      private int[] m_ImplicitCounts = Array.Empty<int>();
      private int m_ExplicitCount;
      private int m_ImplicitCount;

      // Retained batch scratch. Sorted unique touched indices with their per-set deltas, so a batch
      // mutation allocates nothing after its first use at that size.
      private int[] m_BatchIndices = Array.Empty<int>();
      private int[] m_BatchExplicitDeltas = Array.Empty<int>();
      private int[] m_BatchTotalDeltas = Array.Empty<int>();
      private int m_BatchCount;

      private readonly GameplayTagRegistry m_Registry;
      private Dictionary<int, TagDelegateEntry> m_DelegateMap;
      private List<OnTagCountChangedDelegate> m_GlobalAnyChange;
      private List<OnTagCountChangedDelegate> m_GlobalNewOrRemove;
      private int m_RuntimeIndexEpoch;
      private int m_MutationDepth;
      private bool m_Poisoned;

      /// <summary>Capacity of the stack buffer used to stage a batch mutation of a small container.</summary>
      private const int StackBatchCapacity = 64;

      /// <summary>
      /// The largest batch staging buffer retained between mutations. A batch past this size releases its
      /// buffers when it completes instead of pinning the peak for the container's lifetime.
      /// </summary>
      internal const int MaxRetainedBatchEntryCount = 256;

      /// <summary>
      /// True while this container is holding batch staging buffers between mutations. Diagnostics and
      /// tests only - it exposes whether retained memory exists, not new capability.
      /// </summary>
      internal bool HasRetainedBatchBuffers => m_BatchIndices.Length > 0;


      private int m_OwningThreadId;

      /// <summary>
      /// Records or verifies the mutating thread. See
      /// <see cref="GameplayTagsDiagnostics.ThreadAffinityChecksEnabled"/>.
      /// </summary>
      private void AssertMutationThreadAffinity()
      {
         if (!GameplayTagsDiagnostics.ThreadAffinityChecksEnabled)
            return;

         int current = Environment.CurrentManagedThreadId;
         int owner = Volatile.Read(ref m_OwningThreadId);
         if (owner == 0)
         {
            Volatile.Write(ref m_OwningThreadId, current);
            return;
         }

         if (owner != current)
         {
            throw new InvalidOperationException(
               $"{nameof(GameplayTagCountContainer)} was first mutated on managed thread {owner} but is being mutated on thread " +
               $"{current}. A container is owner-thread state; hand a {nameof(ReadOnlyGameplayTagContainer)} " +
               "across threads instead.");
         }
      }

      public GameplayTagCountContainer() { }

      public GameplayTagCountContainer(GameplayTagRegistry registry)
      {
         m_Registry = registry ?? throw new ArgumentNullException(nameof(registry));
      }

      public bool IsEmpty
      {
         get
         {
            ThrowIfMutationInProgress();
            return m_ExplicitCount == 0;
         }
      }

      public int ExplicitTagCount => m_ExplicitCount;

      public int TagCount => m_ImplicitCount;

      /// <summary>The registry epoch these indices belong to, or 0 before the first mutation.</summary>
      public int RuntimeIndexEpoch => m_RuntimeIndexEpoch;

      /// <summary>
      /// True when the registry has been rebuilt with reassigned indices since this container was last
      /// written. Reads do not detect this; re-resolve before writing.
      /// </summary>
      public bool IsStale
      {
         get
         {
            int epoch = GetSnapshot().RuntimeIndexEpoch;
            return m_RuntimeIndexEpoch != 0 && m_RuntimeIndexEpoch != epoch;
         }
      }

      [DebuggerBrowsable(DebuggerBrowsableState.Never)]
      private string DebuggerDisplay => $"Count (Explicit, Total) = ({m_ExplicitCount}, {m_ImplicitCount})";

      /// <summary>Raised for every tag whose count changed, including through zero.</summary>
      public event OnTagCountChangedDelegate OnAnyTagCountChange
      {
         add
         {
            m_GlobalAnyChange ??= new List<OnTagCountChangedDelegate>();
            m_GlobalAnyChange.Add(value);
         }
         remove
         {
            m_GlobalAnyChange?.Remove(value);
         }
      }

      /// <summary>Raised when a tag's count crosses zero in either direction.</summary>
      public event OnTagCountChangedDelegate OnAnyTagNewOrRemove
      {
         add
         {
            m_GlobalNewOrRemove ??= new List<OnTagCountChangedDelegate>();
            m_GlobalNewOrRemove.Add(value);
         }
         remove
         {
            m_GlobalNewOrRemove?.Remove(value);
         }
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private TagDataSnapshot GetSnapshot()
         => m_Registry != null ? m_Registry.Snapshot : GameplayTagManager.Snapshot;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      /// <summary>
      /// Read members fail fast while a mutation is in flight. A notification callback may have put the
      /// container into a transient state (and may even have swapped the registry underneath it), so any
      /// read served mid-mutation would observe half-applied counts. Callbacks queue work and apply it
      /// after <see cref="Clear"/> completes; the same rule protects reads.
      /// </summary>
      private void ThrowIfMutationInProgress()
      {
         if (m_MutationDepth > 0)
         {
            m_Poisoned = true;
            throw new InvalidOperationException(
               "This gameplay tag count container is mid-mutation (a notification callback is running). " +
               "Queue the read and retry after the current operation completes.");
         }

         if (m_Poisoned)
         {
            throw new InvalidOperationException(
               "This gameplay tag count container is poisoned by a rejected reentrant operation. Its counts " +
               "are committed and unchanged, but callbacks already observed the container mid-mutation, so " +
               "reads stay locked until Clear() resets the container.");
         }
      }

      private TagDataSnapshot GetSnapshotForMutation()
      {
         TagDataSnapshot snapshot = GetSnapshot();
         if (m_RuntimeIndexEpoch != 0 && m_RuntimeIndexEpoch != snapshot.RuntimeIndexEpoch)
         {
            throw new InvalidOperationException(
               $"This gameplay tag count container holds indices from registry epoch {m_RuntimeIndexEpoch} but " +
               $"the registry is now at epoch {snapshot.RuntimeIndexEpoch}. Re-resolve the container before " +
               "writing to it; writing would mix indices from two incompatible registries.");
         }

         return snapshot;
      }

      public GameplayTag GetTag(int index)
      {
         if ((uint)index >= (uint)m_ImplicitCount)
            throw new ArgumentOutOfRangeException(nameof(index));

         return new GameplayTag(m_ImplicitIndices[index]);
      }

      public GameplayTag GetExplicitTag(int index)
      {
         if ((uint)index >= (uint)m_ExplicitCount)
            throw new ArgumentOutOfRangeException(nameof(index));

         return new GameplayTag(m_ExplicitIndices[index]);
      }

      public GameplayTagEnumerator GetTags() => new(m_ImplicitIndices, m_ImplicitCount);

      public GameplayTagEnumerator GetExplicitTags() => new(m_ExplicitIndices, m_ExplicitCount);

      public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags)
         => FillAncestors(m_ImplicitIndices, m_ImplicitCount, tag, parentTags);

      public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags)
         => FillDescendants(m_ImplicitIndices, m_ImplicitCount, tag, childTags);

      public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags)
         => FillAncestors(m_ExplicitIndices, m_ExplicitCount, tag, parentTags);

      public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags)
         => FillDescendants(m_ExplicitIndices, m_ExplicitCount, tag, childTags);

      public bool ContainsRuntimeIndex(int runtimeIndex, bool explicitOnly)
      {
         if (runtimeIndex <= 0)
            return false;

         return explicitOnly
            ? Find(m_ExplicitIndices, m_ExplicitCount, runtimeIndex) >= 0
            : Find(m_ImplicitIndices, m_ImplicitCount, runtimeIndex) >= 0;
      }

      /// <summary>The number of times <paramref name="tag"/> has been added, which is 0 when absent.</summary>
      public int GetTagCount(GameplayTag tag)
      {
         if (tag.IsNone || !tag.IsValid)
            return 0;

         int position = Find(m_ImplicitIndices, m_ImplicitCount, tag.RuntimeIndex);
         return position >= 0 ? m_ImplicitCounts[position] : 0;
      }

      /// <summary>The number of times <paramref name="tag"/> was added explicitly.</summary>
      public int GetExplicitTagCount(GameplayTag tag)
      {
         if (tag.IsNone || !tag.IsValid)
            return 0;

         int position = Find(m_ExplicitIndices, m_ExplicitCount, tag.RuntimeIndex);
         return position >= 0 ? m_ExplicitCounts[position] : 0;
      }

      /// <summary>
      /// Registers <paramref name="callback"/> for one tag and one event kind. The same callback may be
      /// registered more than once and will then be invoked that many times.
      /// </summary>
      public void RegisterTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback)
      {
         if (callback == null)
            throw new ArgumentNullException(nameof(callback));
         if (tag.IsNone || !tag.IsValid)
            throw new ArgumentException("Cannot register a callback for an invalid gameplay tag.", nameof(tag));

         m_DelegateMap ??= new Dictionary<int, TagDelegateEntry>();
         if (!m_DelegateMap.TryGetValue(tag.RuntimeIndex, out TagDelegateEntry entry))
         {
            entry = new TagDelegateEntry();
            m_DelegateMap.Add(tag.RuntimeIndex, entry);
         }

         if (eventType == GameplayTagEventType.AnyCountChange)
         {
            entry.OnAnyChange ??= new List<OnTagCountChangedDelegate>();
            entry.OnAnyChange.Add(callback);
         }
         else
         {
            entry.OnNewOrRemove ??= new List<OnTagCountChangedDelegate>();
            entry.OnNewOrRemove.Add(callback);
         }
      }

      /// <summary>Removes one registration made by <see cref="RegisterTagEventCallback"/>.</summary>
      public void RemoveTagEventCallback(GameplayTag tag, GameplayTagEventType eventType, OnTagCountChangedDelegate callback)
      {
         if (callback == null || tag.IsNone || !tag.IsValid)
            return;

         if (m_DelegateMap == null ||
             !m_DelegateMap.TryGetValue(tag.RuntimeIndex, out TagDelegateEntry entry))
         {
            return;
         }

         List<OnTagCountChangedDelegate> list = eventType == GameplayTagEventType.AnyCountChange
            ? entry.OnAnyChange
            : entry.OnNewOrRemove;
         list?.Remove(callback);
      }

      /// <summary>Removes every per-tag registration. Global event subscribers are untouched.</summary>
      public void RemoveAllTagEventCallbacks()
      {
         m_DelegateMap = null;
      }

      public void AddTag(GameplayTag tag) => AddTag(tag, 1);

      /// <summary>Adds <paramref name="count"/> stack of <paramref name="tag"/> in one mutation.</summary>
      public void AddTag(GameplayTag tag, int count)
      {
         if (tag.IsNone || !tag.IsValid)
            throw new ArgumentException("Cannot add an invalid gameplay tag.", nameof(tag));
         if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));

         AssertMutationThreadAffinity();
         TagDataSnapshot snapshot = GetSnapshotForMutation();
         if (m_RuntimeIndexEpoch == 0)
            m_RuntimeIndexEpoch = snapshot.RuntimeIndexEpoch;

         Mutate(snapshot, stackalloc int[] { tag.RuntimeIndex }, count);
      }

      public void RemoveTag(GameplayTag tag) => RemoveTag(tag, 1);

      /// <summary>Removes <paramref name="count"/> stack of <paramref name="tag"/> in one mutation.</summary>
      public void RemoveTag(GameplayTag tag, int count)
      {
         if (tag.IsNone || !tag.IsValid)
            throw new ArgumentException("Cannot remove an invalid gameplay tag.", nameof(tag));
         if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count));

         AssertMutationThreadAffinity();
         TagDataSnapshot snapshot = GetSnapshotForMutation();
         if (m_RuntimeIndexEpoch == 0)
            m_RuntimeIndexEpoch = snapshot.RuntimeIndexEpoch;

         Mutate(snapshot, stackalloc int[] { tag.RuntimeIndex }, -count);
      }

      public void AddTags<T>(in T container) where T : IReadOnlyGameplayTagContainer
         => MutateFromContainer(container, 1);

      public void RemoveTags<T>(in T container) where T : IReadOnlyGameplayTagContainer
         => MutateFromContainer(container, -1);

      public void Clear()
      {
         if (m_MutationDepth > 0)
         {
            // A rejected reentrant Clear poisons the container: callbacks already observed it mid-mutation,
            // so reads stay locked until a Clear issued outside the mutation resets it.
            m_Poisoned = true;
            throw new InvalidOperationException(
               "A gameplay tag count container cannot be cleared from inside its own mutation callback.");
         }

         m_Poisoned = false;
         m_ExplicitCount = 0;
         m_ImplicitCount = 0;
         m_BatchCount = 0;
         m_BatchIndices = Array.Empty<int>();
         m_BatchExplicitDeltas = Array.Empty<int>();
         m_BatchTotalDeltas = Array.Empty<int>();
         m_RuntimeIndexEpoch = 0;
      }

      /// <summary>
      /// Buffers are kept between mutations so a run of small batches allocates once. A batch that peaked
      /// past the retention budget releases them instead of pinning the peak.
      /// </summary>
      private void ReleaseOversizedBatchBuffers()
      {
         if (m_BatchIndices.Length <= MaxRetainedBatchEntryCount)
            return;

         m_BatchIndices = Array.Empty<int>();
         m_BatchExplicitDeltas = Array.Empty<int>();
         m_BatchTotalDeltas = Array.Empty<int>();
      }

      private void MutateFromContainer<T>(in T container, int delta) where T : IReadOnlyGameplayTagContainer
      {
         if (container == null || container.IsEmpty)
            return;

         AssertMutationThreadAffinity();
         TagDataSnapshot snapshot = GetSnapshotForMutation();
         if (m_RuntimeIndexEpoch == 0)
            m_RuntimeIndexEpoch = snapshot.RuntimeIndexEpoch;

         int explicitCount = container.ExplicitTagCount;
         Span<int> indices = stackalloc int[StackBatchCapacity];
         if (explicitCount > StackBatchCapacity)
            indices = new int[explicitCount];
         indices = indices.Slice(0, explicitCount);

         for (int i = 0; i < explicitCount; i++)
         {
            int runtimeIndex = container.GetExplicitTag(i).RuntimeIndex;
            if (runtimeIndex <= 0)
               throw new ArgumentException("Cannot mutate a container holding an invalid gameplay tag.", nameof(container));

            indices[i] = runtimeIndex;
         }

         Mutate(snapshot, indices, delta);
      }

      /// <summary>
      /// Applies <paramref name="delta"/> to every tag in <paramref name="explicitRuntimeIndices"/> and to
      /// all of their ancestors, then flushes notifications for every touched tag.
      /// </summary>
      private void Mutate(TagDataSnapshot snapshot, ReadOnlySpan<int> explicitRuntimeIndices, int delta)
      {
         if (m_MutationDepth > 0)
         {
            m_Poisoned = true;
            throw new InvalidOperationException(
               "A gameplay tag count container cannot be mutated from inside its own mutation callback. " +
               "Queue the change and apply it after the current one completes.");
         }

         m_MutationDepth++;
         try
         {
            // Pass 1: validate every delta before applying any of them, so a rejected mutation leaves the
            // container exactly as it was.
            for (int i = 0; i < explicitRuntimeIndices.Length; i++)
            {
               int runtimeIndex = explicitRuntimeIndices[i];
               if ((uint)runtimeIndex >= (uint)snapshot.TotalTagCount)
                  throw new ArgumentException($"Runtime index {runtimeIndex} is not registered.", nameof(explicitRuntimeIndices));

               int explicitPosition = Find(m_ExplicitIndices, m_ExplicitCount, runtimeIndex);
               if (explicitPosition < 0 && delta < 0)
                  WarnAndRejectNegative(snapshot, runtimeIndex, delta, explicitSet: true);

               int start = snapshot.HierarchyOffsets[runtimeIndex];
               int end = snapshot.HierarchyOffsets[runtimeIndex + 1];
               for (int k = start; k < end; k++)
               {
                  int ancestor = snapshot.HierarchyPool[k];
                  int position = Find(m_ImplicitIndices, m_ImplicitCount, ancestor);
                  if (position < 0 && delta < 0)
                     WarnAndRejectNegative(snapshot, ancestor, delta, explicitSet: false);
               }
            }

            // Pass 2: accumulate into the batch buffers.
            for (int i = 0; i < explicitRuntimeIndices.Length; i++)
            {
               int runtimeIndex = explicitRuntimeIndices[i];
               Accumulate(runtimeIndex, delta, explicitSet: true);
               int start = snapshot.HierarchyOffsets[runtimeIndex];
               int end = snapshot.HierarchyOffsets[runtimeIndex + 1];
               for (int k = start; k < end; k++)
                  Accumulate(snapshot.HierarchyPool[k], delta, explicitSet: false);
            }

            if (m_BatchCount == 0)
               return;

            if (m_BatchCount > 1)
               Array.Sort(m_BatchIndices, 0, m_BatchCount);

            // Pass 3: apply.
            for (int i = 0; i < m_BatchCount; i++)
            {
               int runtimeIndex = m_BatchIndices[i];
               int explicitDelta = m_BatchExplicitDeltas[i];
               int totalDelta = m_BatchTotalDeltas[i];

               if (explicitDelta != 0)
                  AdjustSet(m_ExplicitIndices, m_ExplicitCounts, ref m_ExplicitCount, runtimeIndex, explicitDelta, explicitSet: true);
               if (totalDelta != 0)
                  AdjustSet(m_ImplicitIndices, m_ImplicitCounts, ref m_ImplicitCount, runtimeIndex, totalDelta, explicitSet: false);
            }

            // Pass 4: notify, after the container is fully consistent.
            List<Exception> failures = null;
            for (int i = 0; i < m_BatchCount; i++)
            {
               int runtimeIndex = m_BatchIndices[i];
               int newTotal = CountOf(m_ImplicitIndices, m_ImplicitCounts, m_ImplicitCount, runtimeIndex);
               int oldTotal = newTotal - m_BatchTotalDeltas[i];
               Notify(runtimeIndex, newTotal, oldTotal, ref failures);
            }

            m_BatchCount = 0;

            if (failures != null)
               throw new AggregateException("One or more gameplay tag count callbacks failed.", failures);
         }
         finally
         {
            ReleaseOversizedBatchBuffers();
            m_BatchCount = 0;
            m_MutationDepth--;
         }
      }

      private void WarnAndRejectNegative(TagDataSnapshot snapshot, int runtimeIndex, int delta, bool explicitSet)
      {
         int current = explicitSet
            ? CountOf(m_ExplicitIndices, m_ExplicitCounts, m_ExplicitCount, runtimeIndex)
            : CountOf(m_ImplicitIndices, m_ImplicitCounts, m_ImplicitCount, runtimeIndex);

         if (current + delta >= 0)
            return;

         string name = snapshot.GetName(runtimeIndex);
         throw new InvalidOperationException(
            $"Removing gameplay tag \"{name}\" would drive its {(explicitSet ? "explicit" : "hierarchical")} " +
            $"count below zero (current {current}, delta {delta}). A tag cannot be removed more times than it was added.");
      }

      private void Accumulate(int runtimeIndex, int delta, bool explicitSet)
      {
         int position = Find(m_BatchIndices, m_BatchCount, runtimeIndex);
         if (position >= 0)
         {
            if (explicitSet)
               m_BatchExplicitDeltas[position] += delta;
            else
               m_BatchTotalDeltas[position] += delta;
            return;
         }

         position = ~position;
         if (m_BatchCount == m_BatchIndices.Length)
         {
            int newSize = Math.Max(8, m_BatchCount * 2);
            m_BatchIndices = Grow(m_BatchIndices, m_BatchCount, newSize);
            m_BatchExplicitDeltas = Grow(m_BatchExplicitDeltas, m_BatchCount, newSize);
            m_BatchTotalDeltas = Grow(m_BatchTotalDeltas, m_BatchCount, newSize);
         }

         Array.Copy(m_BatchIndices, position, m_BatchIndices, position + 1, m_BatchCount - position);
         Array.Copy(m_BatchExplicitDeltas, position, m_BatchExplicitDeltas, position + 1, m_BatchCount - position);
         Array.Copy(m_BatchTotalDeltas, position, m_BatchTotalDeltas, position + 1, m_BatchCount - position);

         m_BatchIndices[position] = runtimeIndex;
         m_BatchExplicitDeltas[position] = explicitSet ? delta : 0;
         m_BatchTotalDeltas[position] = explicitSet ? 0 : delta;
         m_BatchCount++;
      }

      private static int[] Grow(int[] source, int count, int newSize)
      {
         int[] grown = new int[newSize];
         Array.Copy(source, grown, count);
         return grown;
      }

      private void AdjustSet(
         int[] indices,
         int[] counts,
         ref int count,
         int runtimeIndex,
         int delta,
         bool explicitSet)
      {
         int position = Find(indices, count, runtimeIndex);
         if (position >= 0)
         {
            counts[position] += delta;
            if (counts[position] != 0)
               return;

            // The tag dropped out of this set entirely.
            Array.Copy(indices, position + 1, indices, position, count - position - 1);
            Array.Copy(counts, position + 1, counts, position, count - position - 1);
            count--;
            indices[count] = 0;
            counts[count] = 0;
            return;
         }

         if (delta <= 0)
            return;

         position = ~position;
         if (count == indices.Length)
         {
            int newSize = Math.Max(8, count * 2);
            int[] grownIndices = Grow(indices, count, newSize);
            int[] grownCounts = Grow(counts, count, newSize);
            if (explicitSet)
            {
               m_ExplicitIndices = grownIndices;
               m_ExplicitCounts = grownCounts;
            }
            else
            {
               m_ImplicitIndices = grownIndices;
               m_ImplicitCounts = grownCounts;
            }

            indices = grownIndices;
            counts = grownCounts;
         }

         Array.Copy(indices, position, indices, position + 1, count - position);
         Array.Copy(counts, position, counts, position + 1, count - position);
         indices[position] = runtimeIndex;
         counts[position] = delta;
         count++;
      }

      private static int CountOf(int[] indices, int[] counts, int count, int runtimeIndex)
      {
         int position = Find(indices, count, runtimeIndex);
         return position >= 0 ? counts[position] : 0;
      }

      private void Notify(int runtimeIndex, int newTotal, int oldTotal, ref List<Exception> failures)
      {
         bool crossedZero = (oldTotal == 0) != (newTotal == 0);

         if (m_GlobalAnyChange != null)
            Invoke(m_GlobalAnyChange, runtimeIndex, newTotal, ref failures);

         if (m_GlobalNewOrRemove != null && crossedZero)
            Invoke(m_GlobalNewOrRemove, runtimeIndex, newTotal, ref failures);

         if (m_DelegateMap != null && m_DelegateMap.TryGetValue(runtimeIndex, out TagDelegateEntry entry))
         {
            if (entry.OnAnyChange != null)
               Invoke(entry.OnAnyChange, runtimeIndex, newTotal, ref failures);

            if (entry.OnNewOrRemove != null && crossedZero)
               Invoke(entry.OnNewOrRemove, runtimeIndex, newTotal, ref failures);
         }
      }

      private static void Invoke(
         List<OnTagCountChangedDelegate> callbacks,
         int runtimeIndex,
         int newCount,
         ref List<Exception> failures)
      {
         GameplayTag tag = new(runtimeIndex);
         for (int i = 0; i < callbacks.Count; i++)
         {
            try
            {
               callbacks[i](tag, newCount);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
               failures ??= new List<Exception>();
               failures.Add(exception);
            }
         }
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static int Find(int[] indices, int count, int value)
         => BinarySearchUtility.Search(indices, count, value);

      private void FillAncestors(int[] indices, int count, GameplayTag tag, List<GameplayTag> destination)
      {
         if (count == 0 || destination == null)
            return;

         TagDataSnapshot snapshot = GetSnapshot();
         if ((uint)tag.RuntimeIndex >= (uint)snapshot.TotalTagCount)
            return;

         int start = snapshot.HierarchyOffsets[tag.RuntimeIndex];
         int end = snapshot.HierarchyOffsets[tag.RuntimeIndex + 1] - 1;
         for (int i = end - 1; i >= start; i--)
         {
            int ancestor = snapshot.HierarchyPool[i];
            if (BinarySearchUtility.Contains(indices, count, ancestor))
               destination.Add(new GameplayTag(ancestor));
         }
      }

      private void FillDescendants(int[] indices, int count, GameplayTag tag, List<GameplayTag> destination)
      {
         if (count == 0 || destination == null)
            return;

         TagDataSnapshot snapshot = GetSnapshot();
         if ((uint)tag.RuntimeIndex >= (uint)snapshot.TotalTagCount)
            return;

         int start = snapshot.ChildOffsets[tag.RuntimeIndex];
         int end = snapshot.ChildOffsets[tag.RuntimeIndex + 1];
         for (int i = start; i < end; i++)
         {
            int child = snapshot.ChildPool[i];
            if (BinarySearchUtility.Contains(indices, count, child))
               destination.Add(new GameplayTag(child));
         }
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public GameplayTagEnumerator GetEnumerator() => new(m_ImplicitIndices, m_ImplicitCount);

      IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => GetEnumerator();

      IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

      private sealed class TagDelegateEntry
      {
         internal List<OnTagCountChangedDelegate> OnAnyChange;
         internal List<OnTagCountChangedDelegate> OnNewOrRemove;
      }
   }
}
