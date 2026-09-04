using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

[assembly: InternalsVisibleTo("CycloneGames.GameplayTags.Unity.Runtime")]

#if UNITY_INCLUDE_TESTS
[assembly: InternalsVisibleTo("CycloneGames.GameplayTags.Tests.Editor")]
[assembly: InternalsVisibleTo("CycloneGames.GameplayTags.DataTable.Tests.Editor")]
[assembly: InternalsVisibleTo("CycloneGames.GameplayTags.Tests.Performance")]
#endif

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// The ambient gameplay tag registry: the non-DI entry point to this module.
   /// </summary>
   /// <remarks>
   /// <para>
   /// This is a thin static facade over a <see cref="GameplayTagRegistry"/> instance. Code that does not
   /// use dependency injection resolves tags through it; code that does use DI takes a
   /// <see cref="GameplayTagRegistry"/> and never touches this type. Both paths run the same code and
   /// produce the same values.
   /// </para>
   /// <para>
   /// <see cref="Use"/> replaces the ambient instance. Tests and host bootstraps call it during wiring;
   /// nothing else should. Swapping the instance does not invalidate tags already resolved against the
   /// previous registry when <paramref name="preserveExistingIndices"/> is true, because every index the
   /// old snapshot assigned is carried into the new one.
   /// </para>
   /// <para>
   /// Every member is thread safe. Reads are lock free.
   /// </para>
   /// </remarks>
   public static partial class GameplayTagManager
   {
      private static readonly object s_Gate = new();
      private static readonly List<IGameplayTagSource> s_DefaultSources = new();
      private static readonly List<IGameplayTagCatalog> s_DefaultCatalogs = new();

      private static GameplayTagRegistry s_Registry;
      private static bool s_IsCustomRegistryInstalled;
      private static Action s_TreeChanged;

      /// <summary>Raised after the ambient registry publishes a new snapshot.</summary>
      public static event Action TreeChanged
      {
         add
         {
            lock (s_Gate)
               s_TreeChanged += value;
         }
         remove
         {
            lock (s_Gate)
               s_TreeChanged -= value;
         }
      }

      /// <summary>The ambient registry, created lazily from the registered default inputs.</summary>
      public static GameplayTagRegistry Current
      {
         get
         {
            GameplayTagRegistry registry = Volatile.Read(ref s_Registry);
            return registry ?? EnsureDefaultRegistry();
         }
      }

      /// <summary>The published snapshot of the ambient registry.</summary>
      public static TagDataSnapshot Snapshot => Current.Snapshot;

      /// <summary>Incremented on every publication of the ambient registry.</summary>
      public static int Generation => Current.Generation;

      /// <summary>
      /// Incremented only when existing runtime indices may have been reassigned or removed.
      /// </summary>
      public static int RuntimeIndexEpoch => Current.RuntimeIndexEpoch;

      /// <summary>Registered tags, excluding <see cref="GameplayTag.None"/>.</summary>
      public static int TagCount => Current.TagCount;

      /// <summary>An order-independent hash of the ambient tag manifest.</summary>
      public static ulong ManifestHash => Current.ManifestHash;

      /// <summary>
      /// Installs the ambient registry.
      /// </summary>
      /// <param name="registry">The registry every ambient lookup will resolve against.</param>
      /// <param name="preserveExistingIndices">
      /// When true, every index the outgoing snapshot had assigned is carried into
      /// <paramref name="registry"/>, so tags and cached indices already in use stay valid. Use this when
      /// swapping a registry into a live system. Defaults to false, which gives a clean slate.
      /// </param>
      public static void Use(GameplayTagRegistry registry, bool preserveExistingIndices = false)
      {
         if (registry == null)
            throw new ArgumentNullException(nameof(registry));

         TagDataSnapshot previous;
         lock (s_Gate)
         {
            previous = preserveExistingIndices ? Volatile.Read(ref s_Registry)?.CurrentSnapshotOrNull : null;
            DetachLocked(Volatile.Read(ref s_Registry));
            s_Registry = registry;
            s_IsCustomRegistryInstalled = true;
            registry.TreeChanged += OnRegistryTreeChanged;
         }

         if (previous != null && previous.TagCount > 0)
            registry.ReloadPreservingIndicesFrom(previous);

         RaiseTreeChanged();
      }

      /// <summary>Adds a source to the ambient registry's default inputs.</summary>
      public static void RegisterSource(IGameplayTagSource source)
      {
         if (source == null)
            throw new ArgumentNullException(nameof(source));

         if (TryGetCustomRegistry(out GameplayTagRegistry installed))
         {
            // A host-installed registry owns its own wiring; late sources go straight to it.
            installed.AddSource(source);
            return;
         }

         lock (s_Gate)
         {
            if (!s_DefaultSources.Contains(source))
               s_DefaultSources.Add(source);
            else
               return;
         }

         RepublishDefaultRegistry();
      }

      /// <summary>
      /// Adds a generated catalog to the ambient registry.
      /// </summary>
      /// <remarks>
      /// Generated catalogs are the reflection-free replacement for assembly attribute sweeping, so this
      /// is the call a generated registration bootstrap makes. It is safe at any point in the process
      /// lifetime, including from a hot-updated assembly: indices already assigned are preserved.
      /// </remarks>
      public static void RegisterCatalog(IGameplayTagCatalog catalog)
      {
         if (catalog == null)
            throw new ArgumentNullException(nameof(catalog));

         if (TryGetCustomRegistry(out GameplayTagRegistry installed))
         {
            installed.AddCatalog(catalog);
            return;
         }

         lock (s_Gate)
         {
            if (!s_DefaultCatalogs.Contains(catalog))
               s_DefaultCatalogs.Add(catalog);
            else
               return;
         }

         RepublishDefaultRegistry();
      }

      /// <summary>
      /// Resolves a native tag against the ambient registry. This is the constant-tag read path: after the
      /// first access it costs one epoch compare, not a dictionary lookup.
      /// </summary>
      public static GameplayTag Request(NativeGameplayTag nativeTag) => Current.GetTag(nativeTag);

      /// <summary>Resolves a tag by its full dotted name against the ambient registry.</summary>
      public static GameplayTag Request(string name, bool logWarningIfNotFound = true)
         => Current.Request(name, logWarningIfNotFound);

      /// <summary>Resolves a tag by name against the ambient registry without reporting a miss.</summary>
      public static bool TryRequest(string name, out GameplayTag tag)
         => Current.TryRequest(name, out tag);

      /// <summary>Resolves a tag by its platform-stable identifier against the ambient registry.</summary>
      public static bool TryGetByStableId(ulong stableId, out GameplayTag tag)
         => Current.TryGetByStableId(stableId, out tag);

      /// <summary>Rebuilds a tag from a runtime index captured from the ambient registry.</summary>
      public static GameplayTag FromRuntimeIndex(int runtimeIndex)
         => Current.FromRuntimeIndex(runtimeIndex);

      /// <summary>
      /// Adds a tag to the ambient registry and republishes it. Batch additions with
      /// <see cref="RegisterDynamicTags"/>; each call rebuilds the registry.
      /// </summary>
      public static void RegisterDynamicTag(
         string name,
         string description = null,
         GameplayTagFlags flags = GameplayTagFlags.None)
         => Current.RegisterDynamicTag(name, description, flags);

      /// <summary>Adds a batch of tags to the ambient registry and republishes it once.</summary>
      public static void RegisterDynamicTags(
         IEnumerable<string> names,
         string description = null,
         GameplayTagFlags flags = GameplayTagFlags.None)
         => Current.RegisterDynamicTags(names, description, flags);

      /// <summary>
      /// Forces the ambient registry to build now instead of on first use.
      /// </summary>
      public static void InitializeIfNeeded() => _ = Current.Snapshot;

      /// <summary>
      /// Rebuilds the ambient registry, preserving indices while the host reports the game as playing.
      /// </summary>
      public static void Reload() => Reload(GameplayTagHost.Current.IsRuntimePlaying);

      /// <summary>
      /// A unique id for the ambient registry instance. Caches keyed on this plus the epoch survive
      /// registry replacement (new instance, epoch restarts) without false hits.
      /// </summary>
      public static int RegistryInstanceId => Current.InstanceId;

      /// <summary>The sources that declared <paramref name="tag"/> during the current build.</summary>
      public static IReadOnlyList<IGameplayTagSource> GetTagSources(in GameplayTag tag)
         => Current.GetTagSources(tag);

      /// <summary>
      /// Rebuilds the ambient registry from its sources and catalogs.
      /// </summary>
      /// <param name="preserveRuntimeIndices">
      /// When true, existing indices are kept even for tags no source declares any more. When false,
      /// indices are reassigned and the runtime index epoch advances.
      /// </param>
      public static void Reload(bool preserveRuntimeIndices) => Current.Reload(preserveRuntimeIndices);

      /// <summary>Defers <see cref="TreeChanged"/> until the returned scope is disposed.</summary>
      public static IDisposable DeferTreeChangeBroadcast() => Current.DeferTreeChangeBroadcast();

      /// <summary>
      /// Discards the ambient registry's published snapshot and every pending dynamic registration.
      /// </summary>
      /// <remarks>
      /// Sources, catalogs, and event subscribers are retained. Host bootstraps call this on a domain
      /// reload or a play-mode transition; tests call it between cases.
      /// </remarks>
      public static void Reset()
      {
         GameplayTagRegistry registry;
         lock (s_Gate)
            registry = Volatile.Read(ref s_Registry);

         registry?.Reset();
      }

      /// <summary>
      /// Discards the ambient registry entirely, including its sources and catalogs, and restores the
      /// default empty instance.
      /// </summary>
      internal static void ResetForTests()
      {
         lock (s_Gate)
         {
            DetachLocked(Volatile.Read(ref s_Registry));
            s_Registry = null;
            s_IsCustomRegistryInstalled = false;
            s_DefaultSources.Clear();
            s_DefaultCatalogs.Clear();
         }
      }

      // ---- Per-index accessors used by GameplayTag. All resolve against the ambient registry. ----

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static string GetName(int runtimeIndex) => Current.Snapshot.GetName(runtimeIndex);

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static string GetDescription(int runtimeIndex) => Current.Snapshot.GetDescription(runtimeIndex);

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static ulong GetStableId(int runtimeIndex) => Current.Snapshot.GetStableId(runtimeIndex);

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static GameplayTagFlags GetFlags(int runtimeIndex) => Current.Snapshot.GetFlags(runtimeIndex);

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static int GetHierarchyLevel(int runtimeIndex) => Current.Snapshot.GetHierarchyLevel(runtimeIndex);

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static bool IsLeaf(int runtimeIndex) => Current.Snapshot.IsLeaf(runtimeIndex);

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static bool IsChildOf(int descendantIndex, int ancestorIndex)
         => Current.Snapshot.IsAncestorOf(ancestorIndex, descendantIndex);

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static int MatchesTagDepth(int indexA, int indexB)
         => Current.Snapshot.MatchesTagDepth(indexA, indexB);

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static GameplayTag GetParentTag(int runtimeIndex) => new(Current.Snapshot.GetParentIndex(runtimeIndex));

      internal static void AppendAncestors(int runtimeIndex, List<GameplayTag> destination)
         => Current.Snapshot.AppendAncestors(runtimeIndex, destination);

      internal static void AppendChildren(int runtimeIndex, List<GameplayTag> destination)
         => Current.Snapshot.AppendChildren(runtimeIndex, destination);

      /// <summary>
      /// The last dotted segment of a tag's name.
      /// </summary>
      /// <remarks>
      /// Allocates, because the label is not stored. It is a display concern; never call it on a hot path.
      /// </remarks>
      public static string GetLabel(int runtimeIndex)
      {
         string name = GetName(runtimeIndex);
         if (name.Length == 0)
            return string.Empty;

         int lastDot = name.LastIndexOf('.');
         return lastDot < 0 ? name : name.Substring(lastDot + 1);
      }

      /// <summary>
      /// The default registry's view of the host's project sources. DI registries never see this; they
      /// are wired explicitly through the builder. The adapter reads the platform at build time, so
      /// replacing the platform takes effect on the next rebuild.
      /// </summary>
      private sealed class HostProjectSourceAdapter : IGameplayTagSource
      {
         public string Name => "HostProject";

         public void RegisterTags(GameplayTagRegistrationContext context)
         {
            // The baked manifest comes first: it is the build's baseline, and authoring sources register
            // on top of it. A platform without a manifest - the editor, which reads its authored files
            // below instead - simply contributes nothing here.
            if (GameplayTagHost.Current.TryLoadBuildTagData(out byte[] buildData) &&
                buildData != null && buildData.Length > 0)
            {
               new BuildGameplayTagSource(buildData).RegisterTags(context);
            }

            List<IGameplayTagSource> scratch = HostSourceScratch;
            scratch.Clear();
            GameplayTagHost.Current.CollectProjectTagSources(scratch);
            for (int i = 0; i < scratch.Count && !context.IsRegistrationTerminated; i++)
               scratch[i].RegisterTags(context);
         }
      }

      [ThreadStatic]
      private static List<IGameplayTagSource> s_HostSourceScratch;

      private static List<IGameplayTagSource> HostSourceScratch
      {
         get
         {
            s_HostSourceScratch ??= new List<IGameplayTagSource>(4);
            return s_HostSourceScratch;
         }
      }

      private static readonly HostProjectSourceAdapter s_HostProjectSourceAdapter = new();

      private static bool TryGetCustomRegistry(out GameplayTagRegistry installed)
      {
         lock (s_Gate)
         {
            installed = Volatile.Read(ref s_Registry);
            return s_IsCustomRegistryInstalled && installed != null;
         }
      }

      /// <summary>
      /// Rebuilds the default ambient registry from the current default inputs, carrying over every index
      /// the outgoing snapshot had assigned.
      /// </summary>
      private static void RepublishDefaultRegistry()
      {
         TagDataSnapshot previous;
         GameplayTagRegistry registry;
         lock (s_Gate)
         {
            previous = Volatile.Read(ref s_Registry)?.CurrentSnapshotOrNull;
            registry = new GameplayTagRegistryBuilder()
               .AddSource(s_HostProjectSourceAdapter)
               .AddSources(s_DefaultSources)
               .AddCatalogs(s_DefaultCatalogs)
               .Build();

            DetachLocked(Volatile.Read(ref s_Registry));
            s_Registry = registry;
            registry.TreeChanged += OnRegistryTreeChanged;
         }

         if (previous != null && previous.TagCount > 0)
            registry.ReloadPreservingIndicesFrom(previous);

         RaiseTreeChanged();
      }

      private static GameplayTagRegistry EnsureDefaultRegistry()
      {
         lock (s_Gate)
            return EnsureDefaultRegistryLocked();
      }

      private static GameplayTagRegistry EnsureDefaultRegistryLocked()
      {
         GameplayTagRegistry registry = Volatile.Read(ref s_Registry);
         if (registry != null)
            return registry;

         registry = new GameplayTagRegistryBuilder()
            .AddSource(s_HostProjectSourceAdapter)
            .AddSources(s_DefaultSources)
            .AddCatalogs(s_DefaultCatalogs)
            .Build();

         s_Registry = registry;
         registry.TreeChanged += OnRegistryTreeChanged;
         return registry;
      }

      private static void DetachLocked(GameplayTagRegistry registry)
      {
         if (registry != null)
            registry.TreeChanged -= OnRegistryTreeChanged;
      }

      private static void OnRegistryTreeChanged() => RaiseTreeChanged();

      private static void RaiseTreeChanged()
         => GameplayTagRegistry.InvokeTreeChangedHandlers(Volatile.Read(ref s_TreeChanged));
   }
}
