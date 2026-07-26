namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// Allocation-free aggregate view of the immutable registry and its existing admission limits.
   /// This is a diagnostics value and does not expose mutable registry storage.
   /// </summary>
   public readonly struct GameplayTagMemorySnapshot
   {
      internal GameplayTagMemorySnapshot(
         int tagCount,
         int generation,
         int runtimeIndexEpoch,
         ulong manifestHash,
         int redirectCount)
      {
         TagCount = tagCount;
         Generation = generation;
         RuntimeIndexEpoch = runtimeIndexEpoch;
         ManifestHash = manifestHash;
         RedirectCount = redirectCount;
      }

      public int TagCount { get; }
      public int MaximumTagCount => GameplayTagUtility.MaxRegisteredTagCount;
      public int Generation { get; }
      public int RuntimeIndexEpoch { get; }
      public ulong ManifestHash { get; }
      public int RedirectCount { get; }
      public int MaximumRedirectCount => GameplayTagRedirector.MaxRedirectCount;
      public int MaximumQueryDepth => GameplayTagQuery.MaxExpressionDepth;
      public int MaximumQueryNodeCount => GameplayTagQuery.MaxExpressionNodes;
      public int MaximumQueryReferencedTagCount => GameplayTagQuery.MaxReferencedTags;
   }

   public static partial class GameplayTagManager
   {
      /// <summary>
      /// Captures the current registry reference once and returns O(1) memory diagnostics.
      /// </summary>
      public static GameplayTagMemorySnapshot GetMemorySnapshot()
      {
         TagDataSnapshot snapshot = Snapshot;
         return new GameplayTagMemorySnapshot(
            snapshot.TagCount,
            snapshot.Generation,
            snapshot.RuntimeIndexEpoch,
            CurrentManifestHash,
            GameplayTagRedirector.CurrentCount);
      }
   }
}
