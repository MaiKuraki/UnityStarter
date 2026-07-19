using System;
using System.Collections.Generic;
#if UNITY_EDITOR
using System.Reflection;
#endif
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
   /// Process-wide gameplay tag registry. Writers build a complete candidate registry and publish one
   /// immutable snapshot; readers capture that snapshot and never take the writer lock.
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

      private sealed class TreeChangeBroadcastScope : IDisposable
      {
         private int m_IsDisposed;

         public void Dispose()
         {
            if (Interlocked.Exchange(ref m_IsDisposed, 1) == 0)
               PopDeferOnGameplayTagTreeChangedBroadcast();
         }
      }

      private static readonly object s_InitLock = new();
      private static readonly object s_BroadcastLock = new();
      private static List<PendingRegistration> s_PendingDynamicRegistrations = new();
      private static HashSet<string> s_PendingDynamicNames = new(StringComparer.Ordinal);
      private static TagDataSnapshot s_Snapshot;
      private static volatile bool s_IsInitialized;
      private static bool s_HasBeenReloaded;
      private static int s_DeferTreeChangeBroadcastCount;
      private static bool s_DeferredTreeChangeNeeded;
      private static int s_RuntimeIndexEpochSeed;

      /// <summary>Raised synchronously after a complete snapshot has been published.</summary>
      public static event Action OnGameplayTagTreeChanged;

      /// <summary>True after an explicit registry reload in the current process lifetime.</summary>
      public static bool HasBeenReloaded
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => Volatile.Read(ref s_HasBeenReloaded);
      }

      /// <summary>Generation of the currently published immutable registry snapshot.</summary>
      public static int CurrentGeneration
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get => Snapshot.Generation;
      }

      /// <summary>Epoch shared by snapshots whose existing runtime indices are compatible.</summary>
      public static int CurrentRuntimeIndexEpoch => Snapshot.RuntimeIndexEpoch;

      /// <summary>
      /// Current immutable registry snapshot. Capture once when an operation performs multiple lookups.
      /// </summary>
      public static TagDataSnapshot Snapshot
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get
         {
            TagDataSnapshot snapshot = Volatile.Read(ref s_Snapshot);
            if (snapshot != null)
               return snapshot;

            InitializeIfNeeded();
            return Volatile.Read(ref s_Snapshot);
         }
      }

      public static ulong CurrentManifestHash
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get
         {
            TagDataSnapshot snapshot = Snapshot;
            ulong redirectHash = GameplayTagRedirector.CurrentManifestHash;
            return redirectHash == 0
               ? snapshot.RegistryManifestHash
               : GameplayTagUtility.CombineStableHash(snapshot.RegistryManifestHash, redirectHash);
         }
      }

      public static ReadOnlySpan<GameplayTag> GetAllTags() => new(Snapshot.Tags);

      internal static int GetRegisteredTagCount() => Snapshot.TotalTagCount;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static ulong GetStableIdFromRuntimeIndex(int runtimeIndex)
      {
         TagDataSnapshot snapshot = Snapshot;
         return (uint)runtimeIndex < (uint)snapshot.TagStableIds.Length
            ? snapshot.TagStableIds[runtimeIndex]
            : 0UL;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static bool TryGetTagFromStableId(ulong stableId, out GameplayTag tag)
      {
         TagDataSnapshot snapshot = Snapshot;
         if (stableId != 0UL && snapshot.TryGetRuntimeIndex(stableId, out int runtimeIndex))
         {
            tag = GetTagFromRuntimeIndex(snapshot, runtimeIndex);
            return true;
         }

         tag = GameplayTag.None;
         return false;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static GameplayTagDefinition GetDefinitionFromRuntimeIndex(int runtimeIndex)
      {
         TagDataSnapshot snapshot = Snapshot;
         return (uint)runtimeIndex < (uint)snapshot.TotalTagCount
            ? snapshot.Definitions[runtimeIndex]
            : snapshot.Definitions[0];
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static GameplayTag GetTagFromRuntimeIndex(int runtimeIndex)
      {
         return GetTagFromRuntimeIndex(Snapshot, runtimeIndex);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static GameplayTag GetTagFromRuntimeIndex(TagDataSnapshot snapshot, int runtimeIndex)
      {
         if (runtimeIndex <= 0)
            return GameplayTag.None;

         int arrayIndex = runtimeIndex - 1;
         return (uint)arrayIndex < (uint)snapshot.Tags.Length
            ? snapshot.Tags[arrayIndex]
            : GameplayTag.None;
      }

      public static GameplayTag RequestTag(string name, bool logWarningIfNotFound = true)
      {
         if (string.IsNullOrEmpty(name))
            return GameplayTag.None;

         name = GameplayTagRedirector.Resolve(name);
         if (TryRequestTagWithoutRedirect(name, out GameplayTag tag))
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

         return TryRequestTagWithoutRedirect(GameplayTagRedirector.Resolve(name), out tag);
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      private static bool TryRequestTagWithoutRedirect(string name, out GameplayTag tag)
      {
         TagDataSnapshot snapshot = Snapshot;
         if (snapshot.TryGetRuntimeIndex(name, out int runtimeIndex))
         {
            tag = GetTagFromRuntimeIndex(snapshot, runtimeIndex);
            return true;
         }

         tag = GameplayTag.None;
         return false;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static string GetTagName(int runtimeIndex)
      {
         TagDataSnapshot snapshot = Snapshot;
         return (uint)runtimeIndex < (uint)snapshot.TagNames.Length ? snapshot.TagNames[runtimeIndex] : string.Empty;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static string GetTagDescription(int runtimeIndex)
      {
         TagDataSnapshot snapshot = Snapshot;
         return (uint)runtimeIndex < (uint)snapshot.TagDescriptions.Length ? snapshot.TagDescriptions[runtimeIndex] : string.Empty;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static string GetTagLabel(int runtimeIndex)
      {
         TagDataSnapshot snapshot = Snapshot;
         return (uint)runtimeIndex < (uint)snapshot.TagLabels.Length ? snapshot.TagLabels[runtimeIndex] : string.Empty;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static GameplayTagFlags GetTagFlags(int runtimeIndex)
      {
         TagDataSnapshot snapshot = Snapshot;
         return (uint)runtimeIndex < (uint)snapshot.TagFlags.Length ? snapshot.TagFlags[runtimeIndex] : GameplayTagFlags.None;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static int GetTagHierarchyLevel(int runtimeIndex)
      {
         TagDataSnapshot snapshot = Snapshot;
         return (uint)runtimeIndex < (uint)snapshot.TagHierarchyLevels.Length ? snapshot.TagHierarchyLevels[runtimeIndex] : 0;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static int GetParentRuntimeIndex(int runtimeIndex)
      {
         TagDataSnapshot snapshot = Snapshot;
         return (uint)runtimeIndex < (uint)snapshot.TagParentRuntimeIndices.Length ? snapshot.TagParentRuntimeIndices[runtimeIndex] : 0;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static bool IsLeafTag(int runtimeIndex)
      {
         TagDataSnapshot snapshot = Snapshot;
         return runtimeIndex > 0 && (uint)runtimeIndex < (uint)snapshot.ChildTagRanges.Length &&
            snapshot.ChildTagRanges[runtimeIndex].Count == 0;
      }

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static ReadOnlySpan<GameplayTag> GetParentTagsSpan(int runtimeIndex)
         => runtimeIndex > 0 ? Snapshot.GetParentTagsSpan(runtimeIndex) : ReadOnlySpan<GameplayTag>.Empty;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static ReadOnlySpan<GameplayTag> GetChildTagsSpan(int runtimeIndex)
         => runtimeIndex > 0 ? Snapshot.GetChildTagsSpan(runtimeIndex) : ReadOnlySpan<GameplayTag>.Empty;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static ReadOnlySpan<GameplayTag> GetHierarchyTagsSpan(int runtimeIndex)
         => runtimeIndex > 0 ? Snapshot.GetHierarchyTagsSpan(runtimeIndex) : ReadOnlySpan<GameplayTag>.Empty;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static ReadOnlySpan<int> GetParentRuntimeIndicesSpan(int runtimeIndex)
         => runtimeIndex > 0 ? Snapshot.GetParentRuntimeIndicesSpan(runtimeIndex) : ReadOnlySpan<int>.Empty;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static ReadOnlySpan<int> GetChildRuntimeIndicesSpan(int runtimeIndex)
         => runtimeIndex > 0 ? Snapshot.GetChildRuntimeIndicesSpan(runtimeIndex) : ReadOnlySpan<int>.Empty;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static ReadOnlySpan<int> GetHierarchyRuntimeIndicesSpan(int runtimeIndex)
         => runtimeIndex > 0 ? Snapshot.GetHierarchyRuntimeIndicesSpan(runtimeIndex) : ReadOnlySpan<int>.Empty;

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static bool IsChildOf(int runtimeIndex, int parentRuntimeIndex)
         => Snapshot.IsChildOf(runtimeIndex, parentRuntimeIndex);

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      internal static bool IsParentOf(int runtimeIndex, int childRuntimeIndex)
         => Snapshot.IsParentOf(runtimeIndex, childRuntimeIndex);

      [MethodImpl(MethodImplOptions.AggressiveInlining)]
      public static int MatchesTagDepth(in GameplayTag a, in GameplayTag b)
         => Snapshot.MatchesTagDepth(a.RuntimeIndex, b.RuntimeIndex);

      public static void InitializeIfNeeded()
      {
         if (s_IsInitialized)
            return;

         bool published = false;
         lock (s_InitLock)
         {
            if (s_IsInitialized)
               return;

            RegistryCandidate candidate = BuildProjectCandidateLocked(preserveCurrentIndices: false);
            PublishCandidateLocked(candidate);
            s_IsInitialized = true;
            published = true;
         }

         if (published)
            BroadcastTreeChanged();
      }

      public static void ReloadTags()
      {
         bool preserveCurrentIndices = GameplayTagRuntimePlatform.IsRuntimePlaying();
         lock (s_InitLock)
         {
            RegistryCandidate candidate = BuildProjectCandidateLocked(preserveCurrentIndices);
            PublishCandidateLocked(candidate);
            Volatile.Write(ref s_HasBeenReloaded, true);
            s_IsInitialized = true;
         }

         if (preserveCurrentIndices)
         {
            GameplayTagLogger.LogWarning(
               "Gameplay tags were reloaded during play. Existing runtime indices were preserved; " +
               "tags removed from authoring sources remain registered until the next runtime reset.");
         }

         BroadcastTreeChanged();
      }

      public static void RegisterDynamicTags(IEnumerable<string> tags)
      {
         if (tags == null)
            return;

         const int maxAttempts = GameplayTagRegistrationContext.DefaultMaxRegistrationAttemptCount;
         if (tags is ICollection<string> collection && collection.Count > maxAttempts)
         {
            throw new InvalidOperationException(
               $"Dynamic gameplay tag input cannot exceed {maxAttempts} registration attempts.");
         }
         if (tags is IReadOnlyCollection<string> readOnlyCollection && readOnlyCollection.Count > maxAttempts)
         {
            throw new InvalidOperationException(
               $"Dynamic gameplay tag input cannot exceed {maxAttempts} registration attempts.");
         }

         List<PendingRegistration> registrations = null;
         int attemptCount = 0;
         foreach (string tag in tags)
         {
            if (attemptCount >= maxAttempts)
            {
               throw new InvalidOperationException(
                  $"Dynamic gameplay tag input cannot exceed {maxAttempts} registration attempts.");
            }
            attemptCount++;

            if (string.IsNullOrEmpty(tag))
               continue;

            registrations ??= new List<PendingRegistration>();
            AddRegistrationToBatch(
               registrations,
               new PendingRegistration(tag, string.Empty, GameplayTagFlags.None));
         }

         RegisterDynamicRegistrations(registrations);
      }

      public static void RegisterDynamicTag(string name, string description = null, GameplayTagFlags flags = GameplayTagFlags.None)
      {
         if (string.IsNullOrEmpty(name))
            return;

         RegisterDynamicRegistrations(new List<PendingRegistration>
         {
            new(name, description, flags)
         });
      }

      private static void AddRegistrationToBatch(
         List<PendingRegistration> registrations,
         PendingRegistration registration)
      {
         if (registrations.Count >= GameplayTagRegistrationContext.DefaultMaxRegistrationAttemptCount)
         {
            throw new InvalidOperationException(
               $"Dynamic gameplay tag input cannot exceed " +
               $"{GameplayTagRegistrationContext.DefaultMaxRegistrationAttemptCount} registration attempts.");
         }

         registrations.Add(registration);
      }

      private static void RegisterDynamicRegistrations(List<PendingRegistration> registrations)
      {
         if (registrations == null || registrations.Count == 0)
            return;

         bool published = false;
         lock (s_InitLock)
         {
            if (!s_IsInitialized)
            {
               AddPendingRegistrationsLocked(registrations);
               return;
            }

            TagDataSnapshot current = Volatile.Read(ref s_Snapshot);
            GameplayTagRegistrationContext context = CreateContextFromSnapshot(current);
            for (int i = 0; i < registrations.Count && !context.IsRegistrationTerminated; i++)
            {
               PendingRegistration registration = registrations[i];
               context.RegisterTag(registration.Name, registration.Description, registration.Flags);
            }

            Dictionary<string, int> preferredIndices = CopyRuntimeIndices(current);
            RegistryCandidate candidate = BuildCandidate(
               context,
               preferredIndices,
               NextGeneration(current),
               current.RuntimeIndexEpoch);
            if (candidate.Snapshot.TotalTagCount == current.TotalTagCount)
               return;

            AddPendingRegistrationsLocked(registrations);
            PublishCandidateLocked(candidate);
            published = true;
         }

         if (published)
            BroadcastTreeChanged();
      }

      private static RegistryCandidate BuildProjectCandidateLocked(bool preserveCurrentIndices)
      {
         TagDataSnapshot current = Volatile.Read(ref s_Snapshot);
         GameplayTagRegistrationContext context = new();

#if UNITY_EDITOR
         RegisterEditorAssemblySources(context);
         IEnumerable<IGameplayTagSource> projectSources = GameplayTagRuntimePlatform.EnumerateProjectTagSources != null
            ? GameplayTagRuntimePlatform.EnumerateProjectTagSources()
            : Array.Empty<IGameplayTagSource>();
         foreach (IGameplayTagSource source in projectSources)
         {
            if (context.IsRegistrationTerminated)
               break;
            source?.RegisterTags(context);
         }
#else
         new BuildGameplayTagSource().RegisterTags(context);
#endif

         if (!context.IsRegistrationTerminated)
            GameplayTagRuntimePlatform.RegisterAdditionalProjectTagSources(context);

         for (int i = 0;
              i < s_PendingDynamicRegistrations.Count && !context.IsRegistrationTerminated;
              i++)
         {
            PendingRegistration registration = s_PendingDynamicRegistrations[i];
            context.RegisterTag(registration.Name, registration.Description, registration.Flags);
         }

         Dictionary<string, int> preferredIndices = null;
         if (preserveCurrentIndices && current != null)
         {
            preferredIndices = CopyRuntimeIndices(current);
            for (int i = 1; i < current.TotalTagCount && !context.IsRegistrationTerminated; i++)
            {
               context.RegisterTag(
                  current.TagNames[i],
                  current.TagDescriptions[i],
                  current.TagFlags[i]);
            }
         }

         int runtimeIndexEpoch = current == null
            ? NextRuntimeIndexEpoch()
            : preserveCurrentIndices
               ? current.RuntimeIndexEpoch
               : NextRuntimeIndexEpoch();
         return BuildCandidate(context, preferredIndices, NextGeneration(current), runtimeIndexEpoch);
      }

#if UNITY_EDITOR
      private static void RegisterEditorAssemblySources(GameplayTagRegistrationContext context)
      {
         Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
         for (int assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
         {
            if (context.IsRegistrationTerminated)
               return;

            Assembly assembly = assemblies[assemblyIndex];
            if (assembly.IsDynamic)
               continue;
            new AssemblyGameplayTagSource(assembly).RegisterTags(context);
            if (context.IsRegistrationTerminated)
               return;

            foreach (RegisterGameplayTagsFromAttribute attribute in assembly.GetCustomAttributes<RegisterGameplayTagsFromAttribute>())
            {
               if (context.IsRegistrationTerminated)
                  return;

               Type targetType = attribute.TargetType;
               if (targetType == null)
                  continue;

               FieldInfo[] fields = targetType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
               for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
               {
                  if (context.IsRegistrationTerminated)
                     return;

                  FieldInfo field = fields[fieldIndex];
                  if (!field.IsLiteral || field.IsInitOnly || field.FieldType != typeof(string))
                     continue;

                  string tagName = (string)field.GetValue(null);
                  if (!string.IsNullOrEmpty(tagName))
                     context.RegisterTag(tagName, tagName, GameplayTagFlags.None);
               }
            }
         }
      }
#endif

      private static GameplayTagRegistrationContext CreateContextFromSnapshot(TagDataSnapshot snapshot)
      {
         GameplayTagRegistrationContext context = new();
         for (int i = 1; i < snapshot.TotalTagCount; i++)
            context.RegisterTag(snapshot.TagNames[i], snapshot.TagDescriptions[i], snapshot.TagFlags[i]);
         return context;
      }

      private static RegistryCandidate BuildCandidate(
         GameplayTagRegistrationContext context,
         IReadOnlyDictionary<string, int> preferredIndices,
         int generation,
         int runtimeIndexEpoch)
      {
         ThrowIfRegistrationErrors(context);

         List<GameplayTagDefinition> definitions = context.GenerateDefinitions(true, preferredIndices);
         ThrowIfRegistrationErrors(context);
         if (definitions == null)
            throw new InvalidOperationException("Gameplay tag registry generation failed before publication.");

         TagDataSnapshot snapshot = new(definitions, generation, runtimeIndexEpoch);
         return new RegistryCandidate(snapshot);
      }

      private static void ThrowIfRegistrationErrors(GameplayTagRegistrationContext context)
      {
         if (!context.HasRegistrationErrors)
            return;

         foreach (GameplayTagRegistrationError error in context.GetRegistrationErrors())
         {
            GameplayTagLogger.LogError(
               $"Failed to register gameplay tag '{error.TagName}': {error.Message} " +
               $"(source: {error.Source?.Name ?? "unknown"})");
         }

         if (context.SuppressedRegistrationErrorCount > 0)
         {
            GameplayTagLogger.LogError(
               $"Suppressed {context.SuppressedRegistrationErrorCount} additional gameplay tag registration diagnostic(s). " +
               "The registry candidate was not published.");
         }

         throw new InvalidOperationException(
            $"Gameplay tag registry candidate contains {context.RegistrationErrorCount} registration error(s). " +
            "The current snapshot was not changed.");
      }

      private static Dictionary<string, int> CopyRuntimeIndices(TagDataSnapshot snapshot)
      {
         Dictionary<string, int> indices = new(snapshot.TagCount, StringComparer.Ordinal);
         for (int i = 1; i < snapshot.TotalTagCount; i++)
            indices.Add(snapshot.TagNames[i], i);
         return indices;
      }

      private static int NextGeneration(TagDataSnapshot current)
      {
         int generation = current == null ? 1 : unchecked(current.Generation + 1);
         return generation > 0 ? generation : 1;
      }

      private static int NextRuntimeIndexEpoch()
      {
         int epoch = unchecked(++s_RuntimeIndexEpochSeed);
         if (epoch > 0)
            return epoch;
         s_RuntimeIndexEpochSeed = 1;
         return 1;
      }

      private static void PublishCandidateLocked(RegistryCandidate candidate)
      {
         Volatile.Write(ref s_Snapshot, candidate.Snapshot);
      }

      private static void AddPendingRegistrationsLocked(List<PendingRegistration> registrations)
      {
         List<PendingRegistration> additions = null;
         HashSet<string> batchNames = null;
         for (int i = 0; i < registrations.Count; i++)
         {
            PendingRegistration registration = registrations[i];
            if (string.IsNullOrEmpty(registration.Name) || s_PendingDynamicNames.Contains(registration.Name))
               continue;

            batchNames ??= new HashSet<string>(StringComparer.Ordinal);
            if (!batchNames.Add(registration.Name))
               continue;

            if (s_PendingDynamicNames.Count + batchNames.Count > GameplayTagUtility.MaxRegisteredTagCount)
            {
               throw new InvalidOperationException(
                  $"Pending dynamic gameplay tag count cannot exceed {GameplayTagUtility.MaxRegisteredTagCount}.");
            }

            additions ??= new List<PendingRegistration>();
            additions.Add(registration);
         }

         if (additions == null)
            return;

         for (int i = 0; i < additions.Count; i++)
         {
            PendingRegistration registration = additions[i];
            s_PendingDynamicNames.Add(registration.Name);
            s_PendingDynamicRegistrations.Add(registration);
         }
      }

      private readonly struct RegistryCandidate
      {
         public readonly TagDataSnapshot Snapshot;

         public RegistryCandidate(TagDataSnapshot snapshot)
         {
            Snapshot = snapshot;
         }
      }

      /// <summary>Defers tree-change notifications until the returned scope is disposed.</summary>
      public static IDisposable DeferTreeChangeBroadcast()
      {
         PushDeferOnGameplayTagTreeChangedBroadcast();
         return new TreeChangeBroadcastScope();
      }

      /// <summary>Begins a manually managed tree-change notification deferral.</summary>
      public static void PushDeferOnGameplayTagTreeChangedBroadcast()
      {
         lock (s_BroadcastLock)
            s_DeferTreeChangeBroadcastCount++;
      }

      /// <summary>Ends a manually managed tree-change notification deferral.</summary>
      public static void PopDeferOnGameplayTagTreeChangedBroadcast()
      {
         Action handlers = null;
         lock (s_BroadcastLock)
         {
            if (s_DeferTreeChangeBroadcastCount == 0)
               throw new InvalidOperationException("Gameplay tag tree-change deferral scope underflow.");

            s_DeferTreeChangeBroadcastCount--;
            if (s_DeferTreeChangeBroadcastCount == 0 && s_DeferredTreeChangeNeeded)
            {
               s_DeferredTreeChangeNeeded = false;
               handlers = OnGameplayTagTreeChanged;
            }
         }

         InvokeTreeChangedHandlers(handlers);
      }

      private static void BroadcastTreeChanged()
      {
         Action handlers;
         lock (s_BroadcastLock)
         {
            if (s_DeferTreeChangeBroadcastCount > 0)
            {
               s_DeferredTreeChangeNeeded = true;
               return;
            }

            handlers = OnGameplayTagTreeChanged;
         }

         InvokeTreeChangedHandlers(handlers);
      }

      private static void InvokeTreeChangedHandlers(Action handlers)
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
            catch (Exception exception)
            {
               GameplayTagLogger.LogError($"Gameplay tag tree-change subscriber failed: {exception}");
            }
         }
      }

      internal static void ResetRuntimeState()
      {
         lock (s_InitLock)
         {
            s_PendingDynamicRegistrations = new List<PendingRegistration>();
            s_PendingDynamicNames = new HashSet<string>(StringComparer.Ordinal);
            Volatile.Write(ref s_Snapshot, null);
            s_IsInitialized = false;
            Volatile.Write(ref s_HasBeenReloaded, false);
         }

         lock (s_BroadcastLock)
         {
            s_DeferTreeChangeBroadcastCount = 0;
            s_DeferredTreeChangeNeeded = false;
            OnGameplayTagTreeChanged = null;
         }
      }

#if UNITY_INCLUDE_TESTS
      internal static void ResetForTests() => ResetRuntimeState();
#endif
   }
}
