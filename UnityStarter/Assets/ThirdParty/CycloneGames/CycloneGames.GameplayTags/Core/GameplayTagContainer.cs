using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// The durable form of a tag container: the names of its explicitly held tags.
   /// </summary>
   /// <remarks>
   /// <para>
   /// A <see cref="GameplayTagContainer"/> stores runtime indices, which are only meaningful for one
   /// registry epoch. Names are the durable identity, so names are what crosses a persistence boundary.
   /// </para>
   /// <para>
   /// Core deliberately does not own this payload. It is a plain string array with no engine attribute
   /// attached, which keeps Core free of any host reference: a Unity host maps it onto a
   /// <c>[SerializeField] string[]</c> field, a Godot host onto an <c>[Export] string[]</c> property, and
   /// a headless host onto whatever its save format wants. See <c>IGameplayTagHostPlatform</c>.
   /// </para>
   /// </remarks>
   public readonly struct GameplayTagContainerPersisted
   {
      /// <summary>The names of the explicitly held tags, in ascending runtime-index order.</summary>
      public readonly string[] ExplicitNames;

      public GameplayTagContainerPersisted(string[] explicitNames)
      {
         ExplicitNames = explicitNames;
      }
   }

   /// <summary>Read access shared by every tag container.</summary>
   /// <remarks>
   /// <para>
   /// Implementations store sorted runtime-index arrays. <see cref="ContainsRuntimeIndex"/> with
   /// <c>explicitOnly</c> tests the explicitly held set; with <c>false</c> it tests the expanded set that
   /// also contains every ancestor of every explicitly held tag, which is what <c>HasTag</c> means.
   /// </para>
   /// <para>
   /// An index is only meaningful against the registry epoch it was produced by. Reads perform no epoch
   /// check - see <see cref="IGameplayTagContainer"/> for where that line is drawn.
   /// </para>
   /// </remarks>
   public interface IReadOnlyGameplayTagContainer : IEnumerable<GameplayTag>
   {
      /// <summary>True when the container holds no explicit tag.</summary>
      bool IsEmpty { get; }

      /// <summary>Explicitly held tags, excluding ancestors.</summary>
      int ExplicitTagCount { get; }

      /// <summary>Expanded tag count: explicit tags plus every ancestor of every explicit tag.</summary>
      int TagCount { get; }

      /// <summary>The expanded tag at <paramref name="index"/>, in ascending index order.</summary>
      GameplayTag GetTag(int index);

      /// <summary>The explicit tag at <paramref name="index"/>, in ascending index order.</summary>
      GameplayTag GetExplicitTag(int index);

      /// <summary>Enumerates the expanded tag set.</summary>
      GameplayTagEnumerator GetTags();

      /// <summary>Enumerates the explicitly held tags.</summary>
      GameplayTagEnumerator GetExplicitTags();

      /// <summary>
      /// Appends the ancestors of <paramref name="tag"/> that this container holds, farthest first.
      /// The caller owns the buffer, so this never allocates.
      /// </summary>
      void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags);

      /// <summary>
      /// Appends the descendants of <paramref name="tag"/> that this container holds, nearest first.
      /// The caller owns the buffer, so this never allocates.
      /// </summary>
      void GetChildTags(GameplayTag tag, List<GameplayTag> childTags);

      /// <summary><see cref="GetParentTags"/> restricted to ancestors of explicitly held tags.</summary>
      void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags);

      /// <summary><see cref="GetChildTags"/> restricted to descendants of explicitly held tags.</summary>
      void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags);

      /// <summary>True when the container holds <paramref name="runtimeIndex"/>.</summary>
      bool ContainsRuntimeIndex(int runtimeIndex, bool explicitOnly);
   }

   /// <summary>Write access to a tag container.</summary>
   /// <remarks>
   /// <para>
   /// <b>Epoch contract.</b> A container's indices are valid for exactly one registry epoch. Reads are
   /// unchecked, because an epoch change is a rare authoring-time event and a per-read check would tax
   /// every query for a condition that never happens in a running game. Mutations are checked: writing
   /// into a stale container would mix indices from two incompatible registries, so it throws instead.
   /// When a container can outlive a registry rebuild, the owner resolves it again from
   /// <see cref="GameplayTagContainerPersisted"/> - in a Unity host that is the
   /// <c>[SerializeField] string[]</c> the adapter holds, which is why Core never needs to keep a
   /// recoverable copy of its own.
   /// </para>
   /// </remarks>
   public interface IGameplayTagContainer : IReadOnlyGameplayTagContainer
   {
      void AddTag(GameplayTag gameplayTag);
      void RemoveTag(GameplayTag gameplayTag);
      void AddTags<T>(in T other) where T : IReadOnlyGameplayTagContainer;
      void RemoveTags<T>(in T other) where T : IReadOnlyGameplayTagContainer;
      void Clear();
   }

   /// <summary>
   /// The mutable, owner-owned gameplay tag container.
   /// </summary>
   /// <remarks>
   /// <para>
   /// Storage is two sorted <see cref="int"/> arrays - the explicitly held tags and the expanded set -
   /// plus a lazily built bitset with two bits per runtime index: one for "held explicitly", one for
   /// "held at all". That is the whole object. There is no per-tag managed object, no name payload, and
   /// no dictionary, so a container costs roughly <c>8 bytes per distinct tag</c> plus the bitset.
   /// </para>
   /// <para>
   /// <see cref="GameplayTag.HasTag"/> resolves against the expanded set, matching Unreal's
   /// <c>FGameplayTagContainer::HasTag</c> semantics: a container holding "A.B.C" answers yes for "A.B.C",
   /// "A.B", and "A". <see cref="GameplayTag"/>-level helpers live in
   /// <see cref="GameplayTagContainerExtensionMethods"/>.
   /// </para>
   /// <para>
   /// <b>Threading.</b> A container is owner-thread state. It has no internal synchronization and its
   /// reads do not write, so a container may be read concurrently with itself only while no thread is
   /// mutating it. Hand a <see cref="ReadOnlyGameplayTagContainer"/> across threads instead.
   /// </para>
   /// </remarks>
   [DebuggerTypeProxy(typeof(GameplayTagContainerDebugView))]
   [DebuggerDisplay("{DebuggerDisplay,nq}")]
   public class GameplayTagContainer : IGameplayTagContainer, IEnumerable<GameplayTag>
   {
      /// <summary>Bits of bitset consumed by one runtime index: one for explicit, one for implicit.</summary>
      internal const int BitsPerIndex = 2;

      /// <summary>Runtime indices covered by one <see cref="int"/> bitset word.</summary>
      internal const int IndicesPerWord = 32 / BitsPerIndex;

      internal const int BitsetActivationTagCount = 64;
      internal const int MaxBitsetWordsPerTag = 2;

      private int[] m_Explicit = Array.Empty<int>();
      private int[] m_Implicit = Array.Empty<int>();
      private int[] m_Scratch = Array.Empty<int>();
      private int[] m_Bitset = Array.Empty<int>();
      private int m_ExplicitCount;
      private int m_ImplicitCount;
      private int m_RuntimeIndexEpoch;

      /// <summary>
      /// The registry this container resolves against, or null for the ambient registry. Set for DI use;
      /// a tag resolved by one registry must never be mixed into a container bound to another.
      /// </summary>
      private readonly GameplayTagRegistry m_Registry;


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
               $"{nameof(GameplayTagContainer)} was first mutated on managed thread {owner} but is being mutated on thread " +
               $"{current}. A container is owner-thread state; hand a {nameof(ReadOnlyGameplayTagContainer)} " +
               "across threads instead.");
         }
      }

      public GameplayTagContainer() { }

      /// <summary>Creates a container that resolves hierarchy and names against a specific registry.</summary>
      public GameplayTagContainer(GameplayTagRegistry registry)
      {
         m_Registry = registry ?? throw new ArgumentNullException(nameof(registry));
      }

      public GameplayTagContainer(IReadOnlyGameplayTagContainer other)
      {
         Copy(this, other);
      }

      public bool IsEmpty => m_ExplicitCount == 0;

      public int ExplicitTagCount => m_ExplicitCount;

      public int TagCount => m_ImplicitCount;

      /// <summary>The registry epoch these indices belong to, or 0 before the first mutation.</summary>
      public int RuntimeIndexEpoch => m_RuntimeIndexEpoch;

      /// <summary>
      /// True while membership tests are answered from the bitset rather than a binary search. Diagnostics
      /// and tests only - it exposes which storage the queries are using, not new capability.
      /// </summary>
      internal bool UsesBitset => m_Bitset.Length > 0;

      /// <summary>
      /// True when the registry has been rebuilt with reassigned indices since this container was last
      /// written. A stale container's indices are meaningless and must be re-resolved from
      /// <see cref="GameplayTagContainerPersisted"/>; reads do not detect this for you.
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

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private TagDataSnapshot GetSnapshot()
         => m_Registry != null ? m_Registry.Snapshot : GameplayTagManager.Snapshot;

      /// <summary>Like <see cref="GetSnapshot"/> but rejects a container whose epoch has moved on.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private TagDataSnapshot GetSnapshotForMutation()
      {
         TagDataSnapshot snapshot = GetSnapshot();
         if (m_RuntimeIndexEpoch != 0 && m_RuntimeIndexEpoch != snapshot.RuntimeIndexEpoch)
         {
            throw new InvalidOperationException(
               $"This gameplay tag container holds indices from registry epoch {m_RuntimeIndexEpoch} but the " +
               $"registry is now at epoch {snapshot.RuntimeIndexEpoch}. Re-resolve the container from " +
               $"{nameof(GameplayTagContainerPersisted)} before writing to it; writing would mix indices from " +
               "two incompatible registries.");
         }

         return snapshot;
      }

      public GameplayTag GetTag(int index)
      {
         if ((uint)index >= (uint)m_ImplicitCount)
            throw new ArgumentOutOfRangeException(nameof(index));

         return new GameplayTag(m_Implicit[index]);
      }

      public GameplayTag GetExplicitTag(int index)
      {
         if ((uint)index >= (uint)m_ExplicitCount)
            throw new ArgumentOutOfRangeException(nameof(index));

         return new GameplayTag(m_Explicit[index]);
      }

      public GameplayTagEnumerator GetTags() => new(m_Implicit, m_ImplicitCount);

      public GameplayTagEnumerator GetExplicitTags() => new(m_Explicit, m_ExplicitCount);

      public void GetParentTags(GameplayTag tag, List<GameplayTag> parentTags)
         => FillAncestors(m_Implicit, m_ImplicitCount, tag, parentTags);

      public void GetChildTags(GameplayTag tag, List<GameplayTag> childTags)
         => FillDescendants(m_Implicit, m_ImplicitCount, tag, childTags);

      public void GetExplicitParentTags(GameplayTag tag, List<GameplayTag> parentTags)
         => FillAncestors(m_Explicit, m_ExplicitCount, tag, parentTags);

      public void GetExplicitChildTags(GameplayTag tag, List<GameplayTag> childTags)
         => FillDescendants(m_Explicit, m_ExplicitCount, tag, childTags);


      public bool ContainsRuntimeIndex(int runtimeIndex, bool explicitOnly)
      {
         if (runtimeIndex <= 0)
            return false;

         if (m_Bitset.Length > 0)
            return HasBit(m_Bitset, runtimeIndex, explicitOnly);

         return explicitOnly
            ? BinarySearchUtility.Contains(m_Explicit, m_ExplicitCount, runtimeIndex)
            : BinarySearchUtility.Contains(m_Implicit, m_ImplicitCount, runtimeIndex);
      }

      /// <summary>Copies the explicit and expanded sets of another container into this one.</summary>
      public static void Copy<T>(GameplayTagContainer destination, in T source)
         where T : IReadOnlyGameplayTagContainer
      {
         if (destination == null)
            throw new ArgumentNullException(nameof(destination));
         if (source is GameplayTagContainer same && ReferenceEquals(destination, same))
            return;

         destination.Clear();

         int count = source.ExplicitTagCount;
         if (count == 0)
            return;

         // Drained by index, not by GetExplicitTags: the enumerator is a struct and reading it through
         // this interface would box it once per copy.
         if (destination.m_Explicit.Length < count)
            destination.m_Explicit = new int[count];

         for (int i = 0; i < count; i++)
            destination.m_Explicit[i] = source.GetExplicitTag(i).RuntimeIndex;

         destination.m_ExplicitCount = count;
         destination.m_RuntimeIndexEpoch = destination.GetSnapshotForMutation().RuntimeIndexEpoch;
         destination.RebuildImplicit();
         destination.RebuildBitset();
      }

      public GameplayTagContainer Clone()
      {
         GameplayTagContainer clone = new(m_Registry);
         Copy(clone, this);
         return clone;
      }

      public void Clear()
      {
         m_ExplicitCount = 0;
         m_ImplicitCount = 0;
         m_Explicit = Array.Empty<int>();
         m_Implicit = Array.Empty<int>();
         m_Scratch = Array.Empty<int>();
         m_Bitset = Array.Empty<int>();
         m_RuntimeIndexEpoch = 0;
      }

      public void AddTag(GameplayTag tag)
      {
         if (tag.IsNone || !tag.IsValid)
            throw new ArgumentException("Cannot add an invalid gameplay tag.", nameof(tag));

         AssertMutationThreadAffinity();
         TagDataSnapshot snapshot = GetSnapshotForMutation();
         if (m_RuntimeIndexEpoch == 0)
            m_RuntimeIndexEpoch = snapshot.RuntimeIndexEpoch;

         if (InsertSortedUnique(ref m_Explicit, ref m_ExplicitCount, tag.RuntimeIndex))
         {
            AppendChain(snapshot, tag.RuntimeIndex);
            RebuildBitset();
         }
      }

      public void AddTags<T>(in T container) where T : IReadOnlyGameplayTagContainer
      {
         if (container == null || container.IsEmpty)
            return;

         AssertMutationThreadAffinity();
         TagDataSnapshot snapshot = GetSnapshotForMutation();
         if (m_RuntimeIndexEpoch == 0)
            m_RuntimeIndexEpoch = snapshot.RuntimeIndexEpoch;

         bool changed = false;
         int sourceCount = container.ExplicitTagCount;
         for (int i = 0; i < sourceCount; i++)
         {
            int runtimeIndex = container.GetExplicitTag(i).RuntimeIndex;
            if (runtimeIndex <= 0)
               continue;

            changed |= InsertSortedUnique(ref m_Explicit, ref m_ExplicitCount, runtimeIndex);
         }

         if (changed)
         {
            RebuildImplicit();
            RebuildBitset();
         }
      }

      public void RemoveTag(GameplayTag tag)
      {
         if (tag.IsNone || !tag.IsValid)
            throw new ArgumentException("Cannot remove an invalid gameplay tag.", nameof(tag));

         AssertMutationThreadAffinity();
         GetSnapshotForMutation();
         if (m_ExplicitCount == 0)
            return;

         if (!RemoveSorted(m_Explicit, ref m_ExplicitCount, tag.RuntimeIndex))
         {
            GameplayTagUtility.WarnNotExplicitlyAddedTagRemoval(tag);
            return;
         }

         RebuildImplicit();
         RebuildBitset();
      }

      public void RemoveTags<T>(in T container) where T : IReadOnlyGameplayTagContainer
      {
         AssertMutationThreadAffinity();
         GetSnapshotForMutation();
         if (m_ExplicitCount == 0 || container == null || container.IsEmpty)
            return;

         bool changed = false;
         int sourceCount = container.ExplicitTagCount;
         for (int i = 0; i < sourceCount; i++)
         {
            GameplayTag tag = container.GetExplicitTag(i);
            int runtimeIndex = tag.RuntimeIndex;
            if (runtimeIndex <= 0)
            {
               GameplayTagUtility.WarnNotExplicitlyAddedTagRemoval(tag);
               continue;
            }

            changed |= RemoveSorted(m_Explicit, ref m_ExplicitCount, runtimeIndex);
         }

         if (changed)
         {
            RebuildImplicit();
            RebuildBitset();
         }
      }

      /// <summary>
      /// Replaces this container's explicit set with the union of two containers' explicit sets.
      /// </summary>
      /// <remarks>
      /// Both sides are drained by index, so nothing boxes regardless of the concrete container types.
      /// The merge is a two-pointer walk over two sorted arrays, which produces a sorted deduplicated
      /// result directly.
      /// </remarks>
      public void AddUnion<T, U>(in T first, in U second)
         where T : IReadOnlyGameplayTagContainer
         where U : IReadOnlyGameplayTagContainer
      {
         Clear();
         TagDataSnapshot snapshot = GetSnapshotForMutation();
         if (m_RuntimeIndexEpoch == 0)
            m_RuntimeIndexEpoch = snapshot.RuntimeIndexEpoch;

         int firstCount = first?.ExplicitTagCount ?? 0;
         int secondCount = second?.ExplicitTagCount ?? 0;
         if (firstCount + secondCount == 0)
            return;

         if (m_Explicit.Length < firstCount + secondCount)
            m_Explicit = new int[Math.Max(4, firstCount + secondCount)];

         int i = 0;
         int j = 0;
         while (i < firstCount || j < secondCount)
         {
            int next;
            if (i >= firstCount)
            {
               next = second.GetExplicitTag(j++).RuntimeIndex;
            }
            else if (j >= secondCount)
            {
               next = first.GetExplicitTag(i++).RuntimeIndex;
            }
            else
            {
               int a = first.GetExplicitTag(i).RuntimeIndex;
               int b = second.GetExplicitTag(j).RuntimeIndex;
               if (a == b)
               {
                  i++;
                  j++;
               }
               else if (a < b)
               {
                  i++;
               }
               else
               {
                  j++;
               }

               next = Math.Min(a, b);
            }

            if (next <= 0)
               continue;

            m_Explicit[m_ExplicitCount++] = next;
         }

         RebuildImplicit();
         RebuildBitset();
      }

      /// <summary>
      /// Replaces this container's explicit set with the tags both containers hold explicitly.
      /// </summary>
      public void AddIntersection<T, U>(in T first, in U second)
         where T : IReadOnlyGameplayTagContainer
         where U : IReadOnlyGameplayTagContainer
      {
         Clear();
         TagDataSnapshot snapshot = GetSnapshotForMutation();
         if (m_RuntimeIndexEpoch == 0)
            m_RuntimeIndexEpoch = snapshot.RuntimeIndexEpoch;

         int firstCount = first?.ExplicitTagCount ?? 0;
         int secondCount = second?.ExplicitTagCount ?? 0;
         if (firstCount > 0 && secondCount > 0)
         {
            // Clear leaves the explicit array empty; the intersection can never exceed the smaller side.
            m_Explicit = new int[Math.Min(firstCount, secondCount)];
         }

         int i = 0;
         int j = 0;
         while (i < firstCount && j < secondCount)
         {
            int a = first.GetExplicitTag(i).RuntimeIndex;
            int b = second.GetExplicitTag(j).RuntimeIndex;
            if (a == b)
            {
               if (a > 0)
                  m_Explicit[m_ExplicitCount++] = a;

               i++;
               j++;
            }
            else if (a < b)
            {
               i++;
            }
            else
            {
               j++;
            }
         }

         RebuildImplicit();
         RebuildBitset();
      }

      /// <summary>A new container holding every tag either side holds explicitly.</summary>
      public static GameplayTagContainer Union<T, U>(in T first, in U second)
         where T : IReadOnlyGameplayTagContainer
         where U : IReadOnlyGameplayTagContainer
      {
         GameplayTagContainer union = new(ResolveBinding(first));
         union.AddUnion(first, second);
         return union;
      }

      /// <summary>A new container holding every tag both sides hold explicitly.</summary>
      public static GameplayTagContainer Intersection<T, U>(in T first, in U second)
         where T : IReadOnlyGameplayTagContainer
         where U : IReadOnlyGameplayTagContainer
      {
         GameplayTagContainer intersection = new(ResolveBinding(first));
         intersection.AddIntersection(first, second);
         return intersection;
      }

      /// <summary>
      /// Set-operation results bind to the registry their left operand is bound to, so a DI caller does
      /// not silently get an ambient-bound container back.
      /// </summary>
      private static GameplayTagRegistry ResolveBinding<T>(in T container)
         where T : IReadOnlyGameplayTagContainer
         => container is GameplayTagContainer concrete ? concrete.m_Registry : null;

      /// <summary>
      /// Writes the names of the explicitly held tags for a persistence boundary.
      /// </summary>
      /// <returns>
      /// False when this container is stale, in which case <paramref name="persisted"/> is null and the
      /// caller must fall back to the durable copy it already holds. Returning wrong names would corrupt
      /// the save, so this refuses rather than guessing.
      /// </returns>
      public bool TryToPersisted(out string[] explicitNames)
      {
         explicitNames = null;
         if (m_ExplicitCount == 0)
         {
            explicitNames = Array.Empty<string>();
            return true;
         }

         TagDataSnapshot snapshot;
         try
         {
            snapshot = GetSnapshotForMutation();
         }
         catch (InvalidOperationException)
         {
            return false;
         }

         string[] names = new string[m_ExplicitCount];
         for (int i = 0; i < m_ExplicitCount; i++)
            names[i] = snapshot.Names[m_Explicit[i]];

         explicitNames = names;
         return true;
      }

      /// <summary>
      /// Resolves an explicit tag list produced by <see cref="TryToPersisted"/> against the owning
      /// registry, replacing this container's contents.
      /// </summary>
      /// <remarks>
      /// Names the registry does not know are skipped with a diagnostic rather than failing the load: a
      /// hot-updated assembly legitimately removes tags, and a save must still load.
      /// </remarks>
      public void LoadPersisted(string[] explicitNames)
      {
         Clear();

         if (explicitNames == null || explicitNames.Length == 0)
            return;

         TagDataSnapshot snapshot = GetSnapshot();
         m_RuntimeIndexEpoch = snapshot.RuntimeIndexEpoch;

         for (int i = 0; i < explicitNames.Length; i++)
         {
            string name = explicitNames[i];
            if (string.IsNullOrEmpty(name))
               continue;

            if (!snapshot.TryGetIndex(name, out int runtimeIndex))
            {
               if (GameplayTagsCoreDiagnostics.TryGetEnabled(
                  GameplayTagsDiagnosticLevel.Warning,
                  GameplayTagsDiagnosticCategories.Root,
                  out IGameplayTagsDiagnostics diagnostics))
               {
                  GameplayTagsCoreDiagnostics.TryWrite(
                     diagnostics,
                     GameplayTagsDiagnosticLevel.Warning,
                     GameplayTagsDiagnosticCategories.Root,
                     $"Persisted gameplay tag \"{name}\" is not registered; it was skipped while loading a container.");
               }

               continue;
            }

            if (InsertSortedUnique(ref m_Explicit, ref m_ExplicitCount, runtimeIndex))
               AppendChain(snapshot, runtimeIndex);
         }

         RebuildBitset();
      }

      /// <summary>Appends the whole ancestor chain of <paramref name="runtimeIndex"/> to the implicit set.</summary>
      private void AppendChain(TagDataSnapshot snapshot, int runtimeIndex)
      {
         int start = snapshot.HierarchyOffsets[runtimeIndex];
         int end = snapshot.HierarchyOffsets[runtimeIndex + 1];
         for (int i = start; i < end; i++)
            InsertSortedUnique(ref m_Implicit, ref m_ImplicitCount, snapshot.HierarchyPool[i]);
      }

      /// <summary>
      /// Recomputes the expanded set as the union of the ancestor chains of every explicit tag.
      /// </summary>
      /// <remarks>
      /// Chains are read out of the snapshot's compressed pool into a retained scratch array, then sorted
      /// and deduplicated into the implicit array. Zero allocations once warm: the scratch array is kept
      /// and only grows.
      /// </remarks>
      private void RebuildImplicit()
      {
         if (m_ExplicitCount == 0)
         {
            m_ImplicitCount = 0;
            return;
         }

         TagDataSnapshot snapshot = GetSnapshot();

         int total = 0;
         for (int i = 0; i < m_ExplicitCount; i++)
         {
            int index = m_Explicit[i];
            total += snapshot.HierarchyOffsets[index + 1] - snapshot.HierarchyOffsets[index];
         }

         if (total == 0)
         {
            m_ImplicitCount = 0;
            return;
         }

         if (m_Scratch.Length < total)
            m_Scratch = new int[Math.Max(total, m_Scratch.Length * 2)];

         int count = 0;
         for (int i = 0; i < m_ExplicitCount; i++)
         {
            int index = m_Explicit[i];
            int start = snapshot.HierarchyOffsets[index];
            int end = snapshot.HierarchyOffsets[index + 1];
            for (int k = start; k < end; k++)
               m_Scratch[count++] = snapshot.HierarchyPool[k];
         }

         if (count > 1)
            Array.Sort(m_Scratch, 0, count);

         if (m_Implicit.Length < count)
            m_Implicit = new int[Math.Max(count, m_Implicit.Length * 2)];

         int implicitCount = 0;
         for (int i = 0; i < count; i++)
         {
            int value = m_Scratch[i];
            if (implicitCount > 0 && m_Implicit[implicitCount - 1] == value)
               continue;

            m_Implicit[implicitCount++] = value;
         }

         m_ImplicitCount = implicitCount;
      }

      private void RebuildBitset()
      {
         if (m_ImplicitCount == 0)
         {
            m_Bitset = Array.Empty<int>();
            return;
         }

         int maxIndex = m_Implicit[m_ImplicitCount - 1];
         int wordCount = (maxIndex / IndicesPerWord) + 1;
         if (m_ImplicitCount < BitsetActivationTagCount || wordCount > m_ImplicitCount * MaxBitsetWordsPerTag)
         {
            m_Bitset = Array.Empty<int>();
            return;
         }

         if (m_Bitset.Length != wordCount)
            m_Bitset = new int[wordCount];
         else
            Array.Clear(m_Bitset, 0, wordCount);

         for (int i = 0; i < m_ExplicitCount; i++)
            SetBit(m_Bitset, m_Explicit[i], explicitOnly: true);

         for (int i = 0; i < m_ImplicitCount; i++)
            SetBit(m_Bitset, m_Implicit[i], explicitOnly: false);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static bool HasBit(int[] bitset, int runtimeIndex, bool explicitOnly)
      {
         int word = runtimeIndex / IndicesPerWord;
         if ((uint)word >= (uint)bitset.Length)
            return false;

         int bit = 1 << (((runtimeIndex % IndicesPerWord) * BitsPerIndex) + (explicitOnly ? 0 : 1));
         return (bitset[word] & bit) != 0;
      }

      private static void SetBit(int[] bitset, int runtimeIndex, bool explicitOnly)
      {
         int word = runtimeIndex / IndicesPerWord;
         if ((uint)word >= (uint)bitset.Length)
            return;

         bitset[word] |= 1 << (((runtimeIndex % IndicesPerWord) * BitsPerIndex) + (explicitOnly ? 0 : 1));
      }

      private static bool InsertSortedUnique(ref int[] array, ref int count, int value)
      {
         int position = BinarySearchUtility.Search(array, count, value);
         if (position >= 0)
            return false;

         position = ~position;
         if (count == array.Length)
         {
            int newSize = Math.Max(4, count * 2);
            int[] grown = new int[newSize];
            Array.Copy(array, grown, count);
            array = grown;
         }

         Array.Copy(array, position, array, position + 1, count - position);
         array[position] = value;
         count++;
         return true;
      }

      private static bool RemoveSorted(int[] array, ref int count, int value)
      {
         int position = BinarySearchUtility.Search(array, count, value);
         if (position < 0)
            return false;

         Array.Copy(array, position + 1, array, position, count - position - 1);
         array[--count] = 0;
         return true;
      }

      private void FillAncestors(int[] indices, int count, GameplayTag tag, List<GameplayTag> destination)
      {
         if (count == 0 || destination == null)
            return;

         TagDataSnapshot snapshot = GetSnapshot();
         if ((uint)tag.RuntimeIndex >= (uint)snapshot.TotalTagCount)
            return;

         // The pool slice is root-first, so walking it backwards yields nearest-ancestor-first.
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

      [EditorBrowsable(EditorBrowsableState.Never)]
      public void Add(GameplayTag tag) => AddTag(tag);

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public GameplayTagEnumerator GetEnumerator() => new(m_Implicit, m_ImplicitCount);

      IEnumerator<GameplayTag> IEnumerable<GameplayTag>.GetEnumerator() => GetEnumerator();

      IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
   }
}
