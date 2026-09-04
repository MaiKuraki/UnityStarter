using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// An owned, thread-safe gameplay tag registry.
   /// </summary>
   /// <remarks>
   /// <para>
   /// This is the DI-friendly form of the registry. Construct one with
   /// <see cref="GameplayTagRegistryBuilder"/>, hand it to whoever needs tags, and every lookup and
   /// membership test runs against that instance without touching any ambient state. Tests, headless
   /// simulation servers, and multi-world hosts each get their own instance.
   /// </para>
   /// <para>
   /// <b>Threading.</b> A registry is the only mutable object in this module, and it is fully thread
   /// safe. Reads are lock free: they load one reference and then touch only the immutable arrays of a
   /// <see cref="TagDataSnapshot"/>. Writes rebuild a complete candidate and publish it with a single
   /// <see cref="Volatile.Write"/>, so a reader never observes a partially built registry. A rebuild is
   /// serialized against other rebuilds; it never blocks readers.
   /// </para>
   /// <para>
   /// <b>Reentrancy.</b> Sources and catalogs are enumerated while the rebuild lock is held, because a
   /// rebuild must be atomic with respect to the snapshot it extends. A source that re-enters the
   /// registry - by resolving, requesting, or registering a tag - fails immediately with a clear
   /// exception instead of deadlocking or silently publishing a registry that lost declarations.
   /// </para>
   /// </remarks>
   public sealed class GameplayTagRegistry
   {
      private readonly struct PendingRegistration
      {
         internal readonly string Name;
         internal readonly string Description;
         internal readonly GameplayTagFlags Flags;

         internal PendingRegistration(string name, string description, GameplayTagFlags flags)
         {
            Name = name;
            Description = description;
            Flags = flags;
         }
      }

      /// <summary>
      /// Process-wide epoch source. Epochs are shared across registries so an index cached from one
      /// registry can never be mistaken for a valid index in another.
      /// </summary>
      private static int s_EpochSeed;

      private static readonly TagDataSnapshot s_EmptySnapshot = TagDataSnapshot.CreateEmpty(0, 0);

      private readonly object m_Gate = new();

      private static int s_InstanceIdCounter;

      /// <summary>
      /// Unique per registry instance. Cache keys that pair this with the epoch are collision-proof:
      /// RepublishDefaultRegistry creates a fresh instance whose epoch restarts at 1, which a bare epoch
      /// comparison would misread as unchanged.
      /// </summary>
      internal int InstanceId { get; } = ++s_InstanceIdCounter;
      private readonly object m_BroadcastGate = new();
      private readonly int m_MaxRegisteredTagCount;

      private IGameplayTagSource[] m_Sources;
      private IGameplayTagCatalog[] m_Catalogs;
      private TagDataSnapshot m_Snapshot;
      private List<PendingRegistration> m_PendingDynamic = new();
      private HashSet<string> m_PendingDynamicNames = new(StringComparer.Ordinal);
      private volatile bool m_IsRebuilding;
      private Dictionary<string, List<IGameplayTagSource>> m_TagSourceMap;
      private int m_DeferTreeChangeBroadcastCount;
      private bool m_IsDeferredTreeChangePending;

      internal GameplayTagRegistry(GameplayTagRegistryBuilder builder)
      {
         m_Sources = builder.Sources.ToArray();
         m_Catalogs = builder.Catalogs.ToArray();
         m_MaxRegisteredTagCount = builder.MaxRegisteredTagCount;
      }

      /// <summary>Raised after a complete registry snapshot has been published.</summary>
      public event Action TreeChanged;

      /// <summary>True once at least one snapshot has been published.</summary>
      public bool IsInitialized => Volatile.Read(ref m_Snapshot) != null;

      /// <summary>
      /// The published snapshot, or an empty snapshot before the first publication.
      /// </summary>
      /// <remarks>
      /// <para>
      /// Capturing this once and reusing it across several operations avoids repeated reference loads and
      /// guarantees every operation in the batch sees the same registry state.
      /// </para>
      /// <para>
      /// Reading this while a build is running throws. At that moment the only code with access to the
      /// registry is the sources and catalogs being enumerated, and a lookup they perform can only be a
      /// mistake: the tags they are about to declare do not exist yet. Failing loudly here is what
      /// replaces the previous behaviour, where such a lookup recursed into a second build and silently
      /// dropped declarations.
      /// </para>
      /// </remarks>
      public TagDataSnapshot Snapshot
      {
         get
         {
            TagDataSnapshot snapshot = Volatile.Read(ref m_Snapshot);
            if (snapshot != null)
               return snapshot;

            if (m_IsRebuilding)
               throw new InvalidOperationException(
                  "A gameplay tag source or catalog read the tag registry while it was rebuilding. " +
                  "Sources may only declare tags through the registration context they are handed; they " +
                  "must not resolve, request, or register gameplay tags.");

            EnsureInitialized();
            return Volatile.Read(ref m_Snapshot) ?? s_EmptySnapshot;
         }
      }

      /// <summary>
      /// The published snapshot, or null before the first publication.
      /// </summary>
      /// <remarks>
      /// Unlike <see cref="Snapshot"/> this never triggers a build, so it is safe to read during wiring
      /// and teardown.
      /// </remarks>
      internal TagDataSnapshot CurrentSnapshotOrNull => Volatile.Read(ref m_Snapshot);

      /// <summary>Incremented on every publication.</summary>
      public int Generation => Snapshot.Generation;

      /// <summary>
      /// Incremented only when existing runtime indices may have been reassigned or removed. Cached
      /// indices stay valid while this value is unchanged.
      /// </summary>
      public int RuntimeIndexEpoch => Snapshot.RuntimeIndexEpoch;

      /// <summary>Registered tags, excluding <see cref="GameplayTag.None"/>.</summary>
      public int TagCount => Snapshot.TagCount;

      /// <summary>
      /// An order-independent hash of the tag manifest. Two peers that agree about the registry compute
      /// the same value, which makes it suitable for a replication handshake.
      /// </summary>
      public ulong ManifestHash => Snapshot.RegistryManifestHash;

      /// <summary>
      /// Resolves a tag by its full dotted name.
      /// </summary>
      /// <param name="name">The name to resolve.</param>
      /// <param name="logWarningIfNotFound">
      /// When true, a miss is reported through the diagnostics sink before <see cref="GameplayTag.None"/>
      /// is returned. Pass false when a miss is an expected outcome.
      /// </param>
      /// <returns>The resolved tag, or <see cref="GameplayTag.None"/> when no such tag is registered.</returns>
      public GameplayTag Request(string name, bool logWarningIfNotFound = true)
      {
         if (string.IsNullOrEmpty(name))
            return GameplayTag.None;

         if (TryRequest(name, out GameplayTag tag))
            return tag;

         if (logWarningIfNotFound)
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
                  $"No gameplay tag registered with name \"{name}\".");
            }
         }

         return GameplayTag.None;
      }

      /// <summary>Resolves a tag by name without reporting a miss.</summary>
      public bool TryRequest(string name, out GameplayTag tag)
      {
         if (string.IsNullOrEmpty(name))
         {
            tag = GameplayTag.None;
            return false;
         }

         TagDataSnapshot snapshot = Snapshot;
         if (snapshot.TryGetIndex(name, out int runtimeIndex))
         {
            tag = new GameplayTag(runtimeIndex);
            return true;
         }

         tag = GameplayTag.None;
         return false;
      }

      /// <summary>Resolves a tag by its platform-stable identifier.</summary>
      public bool TryGetByStableId(ulong stableId, out GameplayTag tag)
      {
         TagDataSnapshot snapshot = Snapshot;
         if (snapshot.TryGetIndex(stableId, out int runtimeIndex))
         {
            tag = new GameplayTag(runtimeIndex);
            return true;
         }

         tag = GameplayTag.None;
         return false;
      }

      /// <summary>
      /// Resolves a native tag against this registry, reusing the handle's cache while the epoch holds.
      /// </summary>
      public GameplayTag GetTag(NativeGameplayTag nativeTag)
      {
         if (nativeTag == null)
            throw new ArgumentNullException(nameof(nativeTag));

         int epoch = Snapshot.RuntimeIndexEpoch;
         if (nativeTag.TryGetCached(epoch, out GameplayTag tag))
            return tag;

         if (TryRequest(nativeTag.Name, out tag))
         {
            nativeTag.Cache(epoch, tag.RuntimeIndex);
            return tag;
         }

         nativeTag.MarkUnregistered();
         return GameplayTag.None;
      }

      /// <summary>Rebuilds a tag from a runtime index captured from this registry.</summary>
      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public GameplayTag FromRuntimeIndex(int runtimeIndex)
         => runtimeIndex > 0 && runtimeIndex < Snapshot.TotalTagCount
            ? new GameplayTag(runtimeIndex)
            : GameplayTag.None;

      /// <summary>The full dotted name of <paramref name="tag"/>.</summary>
      public string GetName(in GameplayTag tag) => Snapshot.GetName(tag.RuntimeIndex);

      /// <summary>The authoring description of <paramref name="tag"/>. Empty when it has none.</summary>
      public string GetDescription(in GameplayTag tag) => Snapshot.GetDescription(tag.RuntimeIndex);

      /// <summary>The platform-stable identifier of <paramref name="tag"/>.</summary>
      public ulong GetStableId(in GameplayTag tag) => Snapshot.GetStableId(tag.RuntimeIndex);

      /// <summary>The authoring flags of <paramref name="tag"/>.</summary>
      public GameplayTagFlags GetFlags(in GameplayTag tag) => Snapshot.GetFlags(tag.RuntimeIndex);

      /// <summary>The number of dotted segments in <paramref name="tag"/>'s name.</summary>
      public int GetHierarchyLevel(in GameplayTag tag) => Snapshot.GetHierarchyLevel(tag.RuntimeIndex);

      /// <summary>The immediate parent of <paramref name="tag"/>, or <see cref="GameplayTag.None"/>.</summary>
      public GameplayTag GetParentTag(in GameplayTag tag) => new(Snapshot.GetParentIndex(tag.RuntimeIndex));

      /// <summary>True when <paramref name="tag"/> has no direct children.</summary>
      public bool IsLeaf(in GameplayTag tag) => Snapshot.IsLeaf(tag.RuntimeIndex);

      /// <summary>True when <paramref name="descendant"/> descends from <paramref name="ancestor"/>.</summary>
      public bool IsDescendantOf(in GameplayTag descendant, in GameplayTag ancestor)
         => Snapshot.IsAncestorOf(ancestor.RuntimeIndex, descendant.RuntimeIndex);

      /// <summary>The number of leading hierarchy segments two tags share.</summary>
      public int MatchesTagDepth(in GameplayTag a, in GameplayTag b)
         => Snapshot.MatchesTagDepth(a.RuntimeIndex, b.RuntimeIndex);

      /// <summary>
      /// Appends the strict ancestors of <paramref name="tag"/> to <paramref name="destination"/>, root
      /// first. The caller owns the buffer, so this never allocates.
      /// </summary>
      public void AppendAncestors(in GameplayTag tag, List<GameplayTag> destination)
         => Snapshot.AppendAncestors(tag.RuntimeIndex, destination);

      /// <summary>
      /// Appends the direct children of <paramref name="tag"/> to <paramref name="destination"/> in
      /// ascending index order. The caller owns the buffer, so this never allocates.
      /// </summary>
      public void AppendChildren(in GameplayTag tag, List<GameplayTag> destination)
         => Snapshot.AppendChildren(tag.RuntimeIndex, destination);

      /// <summary>
      /// The sources that declared <paramref name="tag"/> during the current build, in registration order.
      /// </summary>
      /// <remarks>
      /// Authoring tooling only - the Editor uses this to show which file or catalog owns a tag and whether
      /// it can be deleted. The map is retained only when at least one source registered itself, so a
      /// Player build built purely from baked data carries none of it.
      /// </remarks>
      public IReadOnlyList<IGameplayTagSource> GetTagSources(in GameplayTag tag)
      {
         Dictionary<string, List<IGameplayTagSource>> map = m_TagSourceMap;
         if (map == null)
            return EmptySources;

         string name = Snapshot.GetName(tag.RuntimeIndex);
         return map.TryGetValue(name, out List<IGameplayTagSource> sources) ? sources : (IReadOnlyList<IGameplayTagSource>)EmptySources;
      }

      private static readonly IGameplayTagSource[] EmptySources = Array.Empty<IGameplayTagSource>();

      /// <summary>
      /// Copies every registered tag into <paramref name="destination"/> in ascending index order.
      /// </summary>
      /// <returns>The number of tags written, which is <see cref="TagCount"/>.</returns>
      public int CopyAllTags(GameplayTag[] destination)
      {
         if (destination == null)
            throw new ArgumentNullException(nameof(destination));

         TagDataSnapshot snapshot = Snapshot;
         int count = Math.Min(snapshot.TagCount, destination.Length);
         for (int i = 0; i < count; i++)
            destination[i] = new GameplayTag(i + 1);

         return count;
      }

      /// <summary>Allocates an array holding every registered tag, in ascending index order.</summary>
      public GameplayTag[] CreateAllTagsArray()
      {
         GameplayTag[] tags = new GameplayTag[Snapshot.TagCount];
         CopyAllTags(tags);
         return tags;
      }

      /// <summary>
      /// Adds a tag to the registry and republishes it.
      /// </summary>
      /// <remarks>
      /// Every call rebuilds and republishes the whole registry, because a published registry is
      /// immutable. Batch additions with <see cref="RegisterDynamicTags"/> instead of looping here.
      /// </remarks>
      public void RegisterDynamicTag(
         string name,
         string description = null,
         GameplayTagFlags flags = GameplayTagFlags.None)
      {
         if (string.IsNullOrEmpty(name))
            return;

         RegisterDynamicTags(new[] { name }, description, flags);
      }

      /// <summary>Adds a batch of tags to the registry and republishes it once.</summary>
      public void RegisterDynamicTags(
         IEnumerable<string> names,
         string description = null,
         GameplayTagFlags flags = GameplayTagFlags.None)
      {
         if (names == null)
            return;

         List<PendingRegistration> additions = null;
         HashSet<string> seen = null;
         foreach (string name in names)
         {
            if (string.IsNullOrEmpty(name))
               continue;

            seen ??= new HashSet<string>(StringComparer.Ordinal);
            if (!seen.Add(name))
               continue;

            additions ??= new List<PendingRegistration>();
            additions.Add(new PendingRegistration(name, description, flags));
         }

         ApplyDynamicRegistrations(additions);
      }

      /// <summary>
      /// Adds a source to this registry and republishes it, keeping every index the current snapshot
      /// already assigned.
      /// </summary>
      /// <remarks>
      /// Sources are stored copy-on-write, so a concurrent reader keeps seeing the previous set until the
      /// new snapshot is published. Safe to call at any time, including from a hot-updated assembly.
      /// </remarks>
      public void AddSource(IGameplayTagSource source)
      {
         if (source == null)
            throw new ArgumentNullException(nameof(source));

         TagDataSnapshot previous;
         lock (m_Gate)
         {
            if (Array.IndexOf(m_Sources, source) >= 0)
               return;

            IGameplayTagSource[] updated = new IGameplayTagSource[m_Sources.Length + 1];
            Array.Copy(m_Sources, updated, m_Sources.Length);
            updated[m_Sources.Length] = source;
            m_Sources = updated;

            previous = Volatile.Read(ref m_Snapshot);
         }

         if (previous == null)
            return;

         lock (m_Gate)
            RebuildLocked(previous, null, false);

         BroadcastTreeChanged();
      }

      /// <summary>
      /// Adds a catalog to this registry and republishes it, keeping every index the current snapshot
      /// already assigned.
      /// </summary>
      /// <remarks>
      /// This is the path a hot-updated assembly uses to contribute tags under HybridCLR: the assembly
      /// ships a generated <see cref="IGameplayTagCatalog"/>, calls this during its own bootstrap, and
      /// every index already cached by live gameplay survives the addition.
      /// </remarks>
      public void AddCatalog(IGameplayTagCatalog catalog)
      {
         if (catalog == null)
            throw new ArgumentNullException(nameof(catalog));

         TagDataSnapshot previous;
         lock (m_Gate)
         {
            if (Array.IndexOf(m_Catalogs, catalog) >= 0)
               return;

            IGameplayTagCatalog[] updated = new IGameplayTagCatalog[m_Catalogs.Length + 1];
            Array.Copy(m_Catalogs, updated, m_Catalogs.Length);
            updated[m_Catalogs.Length] = catalog;
            m_Catalogs = updated;

            previous = Volatile.Read(ref m_Snapshot);
         }

         if (previous == null)
            return;

         lock (m_Gate)
            RebuildLocked(previous, null, false);

         BroadcastTreeChanged();
      }

      /// <summary>
      /// Rebuilds the registry from its configured sources and catalogs.
      /// </summary>
      /// <param name="preserveRuntimeIndices">
      /// When true, every tag in the current registry keeps its runtime index even when no source
      /// declares it any more, so live index caches stay valid across an authoring refresh. When false,
      /// indices are reassigned and the runtime index epoch advances.
      /// </param>
      public void Reload(bool preserveRuntimeIndices)
      {
         lock (m_Gate)
            RebuildLocked(preserveRuntimeIndices ? Volatile.Read(ref m_Snapshot) : null, null, !preserveRuntimeIndices);

         BroadcastTreeChanged();
      }

      /// <summary>
      /// Rebuilds this registry while keeping every index <paramref name="previous"/> assigned.
      /// </summary>
      /// <remarks>
      /// Used when the ambient registry is replaced by a new instance - a late catalog registration, or a
      /// host swapping in a differently-wired registry - and live index caches must survive the swap.
      /// </remarks>
      internal void ReloadPreservingIndicesFrom(TagDataSnapshot previous)
      {
         if (previous == null)
            return;

         lock (m_Gate)
            RebuildLocked(previous, null, false);

         BroadcastTreeChanged();
      }

      /// <summary>Defers <see cref="TreeChanged"/> notifications until the returned scope is disposed.</summary>
      public IDisposable DeferTreeChangeBroadcast()
      {
         PushDeferTreeChangeBroadcast();
         return new TreeChangeBroadcastScope(this);
      }

      /// <summary>Begins a manually managed notification deferral.</summary>
      public void PushDeferTreeChangeBroadcast()
      {
         lock (m_BroadcastGate)
            m_DeferTreeChangeBroadcastCount++;
      }

      /// <summary>Ends a manually managed notification deferral.</summary>
      public void PopDeferTreeChangeBroadcast()
      {
         Action handlers = null;
         lock (m_BroadcastGate)
         {
            if (m_DeferTreeChangeBroadcastCount == 0)
               throw new InvalidOperationException("Gameplay tag tree-change deferral scope underflow.");

            m_DeferTreeChangeBroadcastCount--;
            if (m_DeferTreeChangeBroadcastCount == 0 && m_IsDeferredTreeChangePending)
            {
               m_IsDeferredTreeChangePending = false;
               handlers = TreeChanged;
            }
         }

         InvokeTreeChangedHandlers(handlers);
      }

      /// <summary>
      /// Discards every published snapshot and pending registration, returning the registry to its
      /// uninitialized state. Sources, catalogs, and event subscribers are retained.
      /// </summary>
      internal void Reset()
      {
         lock (m_Gate)
         {
            m_PendingDynamic = new List<PendingRegistration>();
            m_PendingDynamicNames = new HashSet<string>(StringComparer.Ordinal);
            Volatile.Write(ref m_Snapshot, null);
         }

         lock (m_BroadcastGate)
         {
            m_DeferTreeChangeBroadcastCount = 0;
            m_IsDeferredTreeChangePending = false;
         }
      }

      private void EnsureInitialized()
      {
         if (Volatile.Read(ref m_Snapshot) != null)
            return;

         lock (m_Gate)
         {
            if (Volatile.Read(ref m_Snapshot) != null)
               return;

            RebuildLocked(null, null, true);
         }

         BroadcastTreeChanged();
      }

      private void ApplyDynamicRegistrations(List<PendingRegistration> additions)
      {
         if (additions == null || additions.Count == 0)
            return;

         lock (m_Gate)
         {
            TagDataSnapshot current = Volatile.Read(ref m_Snapshot);
            if (current == null)
            {
               QueuePendingLocked(additions);
               return;
            }

            // Appending never reassigns an existing index, so the epoch is preserved and every cached
            // index anywhere in the process stays valid.
            if (!RebuildLocked(current, additions, false))
               return;

            QueuePendingLocked(additions);
         }

         BroadcastTreeChanged();
      }

      private void QueuePendingLocked(List<PendingRegistration> additions)
      {
         for (int i = 0; i < additions.Count; i++)
         {
            PendingRegistration addition = additions[i];
            if (string.IsNullOrEmpty(addition.Name) || !m_PendingDynamicNames.Add(addition.Name))
               continue;

            if (m_PendingDynamic.Count >= m_MaxRegisteredTagCount)
            {
               throw new InvalidOperationException(
                  $"Pending dynamic gameplay tag count cannot exceed {m_MaxRegisteredTagCount}.");
            }

            m_PendingDynamic.Add(addition);
         }
      }

      /// <summary>
      /// Builds and publishes one snapshot. Must be called with <see cref="m_Gate"/> held.
      /// </summary>
      /// <param name="baseSnapshot">
      /// When non-null, its tags are adopted and its index assignment is used as the preferred ordering,
      /// so every tag it contains keeps its index regardless of what the sources declare.
      /// </param>
      /// <returns>True when a new snapshot was published.</returns>
      private bool RebuildLocked(
         TagDataSnapshot baseSnapshot,
         List<PendingRegistration> additions,
         bool advanceEpoch)
      {
         if (m_IsRebuilding)
         {
            throw new InvalidOperationException(
               "A gameplay tag source or catalog re-entered the tag registry while it was rebuilding. " +
               "Sources may only declare tags through the registration context they are handed; they " +
               "must not resolve, request, or register gameplay tags.");
         }

         m_IsRebuilding = true;
         try
         {
            TagDataSnapshot current = Volatile.Read(ref m_Snapshot);
            GameplayTagRegistrationContext context = Collect(baseSnapshot, additions);
            ThrowIfRegistrationErrors(context);

            Dictionary<string, int> preferred = baseSnapshot != null
               ? BuildPreferredIndices(baseSnapshot)
               : null;

            TagDataSnapshot candidate = Publish(context, preferred, current, advanceEpoch);
            m_TagSourceMap = context.CaptureSourceMap();

            // An append that added nothing is a no-op, not a new generation. Batching callers probe for
            // existence one tag at a time, and republishing - with a tree-changed broadcast - for every
            // already-present tag would make that pattern quadratic.
            bool isAppend = additions != null && ReferenceEquals(baseSnapshot, current);
            if (isAppend && candidate.TotalTagCount == current.TotalTagCount)
               return false;

            Volatile.Write(ref m_Snapshot, candidate);
            return true;
         }
         finally
         {
            m_IsRebuilding = false;
         }
      }

      private GameplayTagRegistrationContext Collect(
         TagDataSnapshot baseSnapshot,
         List<PendingRegistration> additions)
      {
         GameplayTagRegistrationContext context = new(m_MaxRegisteredTagCount);

         IGameplayTagSource[] sources = m_Sources;
         for (int i = 0; i < sources.Length && !context.IsRegistrationTerminated; i++)
            sources[i]?.RegisterTags(context);

         IGameplayTagCatalog[] catalogs = m_Catalogs;
         for (int i = 0; i < catalogs.Length && !context.IsRegistrationTerminated; i++)
            catalogs[i]?.Collect(new GameplayTagCatalogBuilder(context, null));

         if (additions != null)
         {
            for (int i = 0; i < additions.Count && !context.IsRegistrationTerminated; i++)
            {
               PendingRegistration addition = additions[i];
               context.RegisterTag(addition.Name, addition.Description, addition.Flags);
            }
         }

         List<PendingRegistration> pending = m_PendingDynamic;
         for (int i = 0; i < pending.Count && !context.IsRegistrationTerminated; i++)
         {
            PendingRegistration queued = pending[i];
            context.RegisterTag(queued.Name, queued.Description, queued.Flags);
         }

         if (baseSnapshot != null)
         {
            // Names that came from a published snapshot have already been validated, so adopting them
            // skips re-validation. This is the dominant cost of an authoring refresh or a dynamic add.
            for (int i = 1; i < baseSnapshot.TotalTagCount && !context.IsRegistrationTerminated; i++)
               context.Adopt(baseSnapshot.Names[i], baseSnapshot.Descriptions[i], baseSnapshot.Flags[i]);
         }

         return context;
      }

      private TagDataSnapshot Publish(
         GameplayTagRegistrationContext context,
         Dictionary<string, int> preferred,
         TagDataSnapshot current,
         bool advanceEpoch)
      {
         GameplayTagBuildResult result = context.Build(preferred);
         ThrowIfRegistrationErrors(context);
         if (result == null)
            throw new InvalidOperationException("Gameplay tag registry generation failed before publication.");

         int generation = current == null ? 1 : unchecked(current.Generation + 1);
         if (generation <= 0)
            generation = 1;

         int epoch = advanceEpoch || current == null ? NextRuntimeIndexEpoch() : current.RuntimeIndexEpoch;

         return new TagDataSnapshot(
            result.Names,
            result.Descriptions,
            result.Flags,
            result.ParentIndices,
            generation,
            epoch);
      }

      private static Dictionary<string, int> BuildPreferredIndices(TagDataSnapshot snapshot)
      {
         Dictionary<string, int> preferred = new(snapshot.TagCount, StringComparer.Ordinal);
         for (int i = 1; i < snapshot.TotalTagCount; i++)
            preferred[snapshot.Names[i]] = i;

         return preferred;
      }

      private static int NextRuntimeIndexEpoch()
      {
         int epoch = unchecked(Interlocked.Increment(ref s_EpochSeed));
         if (epoch > 0)
            return epoch;

         Interlocked.Exchange(ref s_EpochSeed, 1);
         return 1;
      }

      private static void ThrowIfRegistrationErrors(GameplayTagRegistrationContext context)
      {
         if (!context.HasRegistrationErrors)
            return;

         if (GameplayTagsCoreDiagnostics.TryGetEnabled(
            GameplayTagsDiagnosticLevel.Error,
            GameplayTagsDiagnosticCategories.Root,
            out IGameplayTagsDiagnostics diagnostics))
         {
            foreach (GameplayTagRegistrationError error in context.GetRegistrationErrors())
            {
               GameplayTagsCoreDiagnostics.TryWrite(
                  diagnostics,
                  GameplayTagsDiagnosticLevel.Error,
                  GameplayTagsDiagnosticCategories.Root,
                  $"Failed to register gameplay tag '{error.TagName}': {error.Message} " +
                  $"(source: {error.Source?.Name ?? "unknown"})");
            }

            if (context.SuppressedRegistrationErrorCount > 0)
            {
               GameplayTagsCoreDiagnostics.TryWrite(
                  diagnostics,
                  GameplayTagsDiagnosticLevel.Error,
                  GameplayTagsDiagnosticCategories.Root,
                  $"Suppressed {context.SuppressedRegistrationErrorCount} additional gameplay tag " +
                  "registration diagnostic(s). The registry candidate was not published.");
            }
         }

         throw new InvalidOperationException(
            $"Gameplay tag registry candidate contains {context.RegistrationErrorCount} registration " +
            "error(s). The current snapshot was not changed.");
      }

      private void BroadcastTreeChanged()
      {
         Action handlers;
         lock (m_BroadcastGate)
         {
            if (m_DeferTreeChangeBroadcastCount > 0)
            {
               m_IsDeferredTreeChangePending = true;
               return;
            }

            handlers = TreeChanged;
         }

         InvokeTreeChangedHandlers(handlers);
      }

      internal static void InvokeTreeChangedHandlers(Action handlers)
      {
         if (handlers == null)
            return;

         Delegate[] subscribers = handlers.GetInvocationList();
         for (int i = 0; i < subscribers.Length; i++)
         {
            try
            {
               ((Action)subscribers[i]).Invoke();
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
               if (GameplayTagsCoreDiagnostics.TryGetEnabled(
                  GameplayTagsDiagnosticLevel.Error,
                  GameplayTagsDiagnosticCategories.Root,
                  out IGameplayTagsDiagnostics diagnostics))
               {
                  GameplayTagsCoreDiagnostics.TryWriteException(
                     diagnostics,
                     GameplayTagsDiagnosticLevel.Error,
                     GameplayTagsDiagnosticCategories.Root,
                     exception,
                     "Gameplay tag tree-change subscriber failed.");
               }
            }
         }
      }

      private sealed class TreeChangeBroadcastScope : IDisposable
      {
         private readonly GameplayTagRegistry m_Owner;
         private int m_IsDisposed;

         internal TreeChangeBroadcastScope(GameplayTagRegistry owner)
         {
            m_Owner = owner;
         }

         public void Dispose()
         {
            if (Interlocked.Exchange(ref m_IsDisposed, 1) == 0)
               m_Owner.PopDeferTreeChangeBroadcast();
         }
      }
   }
}
