using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// A gameplay tag declared once in code and reused as a constant.
   /// </summary>
   /// <remarks>
   /// <para>
   /// This is the equivalent of Unreal's <c>UE_DEFINE_GAMEPLAY_TAG</c>: the tag is named once, registered
   /// through a catalog, and afterwards game code reads <see cref="Tag"/> without ever touching a string.
   /// The alternative - calling <see cref="GameplayTagManager.Request"/> with a literal everywhere - pays a
   /// dictionary lookup per call and puts the tag's identity in a string that a typo breaks silently.
   /// </para>
   /// <para>
   /// Declare handles as static fields so their identity survives registry rebuilds, and register them
   /// through <see cref="GameplayTagCatalogBuilder.Add(NativeGameplayTag)"/>. A handle created inside
   /// <c>Collect</c> would be recreated on every rebuild and lose its cached index.
   /// </para>
   /// <code>
   /// public static class GameTags
   /// {
   ///    public static readonly NativeGameplayTag Stunned = new("State.CrowdControl.Stunned", "Cannot act");
   /// }
   ///
   /// internal sealed class GameCatalog : IGameplayTagCatalog
   /// {
   ///    public void Collect(GameplayTagCatalogBuilder builder) => builder.Add(GameTags.Stunned);
   /// }
   /// </code>
   /// <para>
   /// <see cref="Tag"/> caches the resolved index against the registry epoch it was resolved under and
   /// re-resolves automatically after a rebuild that reassigned indices. The cache is a single aligned
   /// <see cref="long"/> packing epoch and index, so a reader never sees an index paired with the wrong
   /// epoch even on ARM.
   /// </para>
   /// </remarks>
   public sealed class NativeGameplayTag
   {
      private const long Uncached = 0;

      private readonly string m_Name;
      private readonly string m_Description;
      private readonly GameplayTagFlags m_Flags;

      // High 32 bits: the registry epoch the index was resolved under. Low 32: the index. Written as one
      // aligned 64-bit store, which is atomic on every platform this module targets.
      private long m_Cache;
      private int m_WarnedUnregistered;

      /// <summary>Creates a handle. Declaring one does not register it; a catalog does that.</summary>
      public NativeGameplayTag(string name, string description = null, GameplayTagFlags flags = GameplayTagFlags.None)
      {
         if (string.IsNullOrEmpty(name))
            throw new ArgumentException("A native gameplay tag needs a name.", nameof(name));

         m_Name = name;
         m_Description = description;
         m_Flags = flags;
      }

      /// <summary>The full dotted name. This is the durable identity; the index is not.</summary>
      public string Name => m_Name;

      /// <summary>The description registered with the tag, for authoring tools.</summary>
      public string Description => m_Description;

      /// <summary>The flags registered with the tag.</summary>
      public GameplayTagFlags Flags => m_Flags;

      /// <summary>
      /// The tag as resolved against the ambient registry. Resolves once per registry epoch; a rebuild
      /// that preserves indices does not even cost that.
      /// </summary>
      /// <remarks>
      /// Resolves against <see cref="GameplayTagManager"/>. In DI code call
      /// <see cref="GameplayTagRegistry.GetTag"/> on the owning registry instead.
      /// </remarks>
      public GameplayTag Tag
      {
         [MethodImpl(MethodImplOptions.AggressiveInlining)]
         get
         {
            long cache = Volatile.Read(ref m_Cache);
            int index = unchecked((int)(cache & 0xFFFFFFFFL));
            int epoch = unchecked((int)(cache >> 32));
            int currentEpoch = GameplayTagManager.RuntimeIndexEpoch;

            if (epoch == currentEpoch && index > 0)
               return new GameplayTag(index);

            return Resolve(currentEpoch);
         }
      }

      /// <summary>
      /// True when the cached index is still valid for <paramref name="epoch"/>. Pass the runtime index
      /// epoch of the registry the handle is being read through.
      /// </summary>
      /// <remarks>
      /// The cache holds one slot, so a handle belongs to one registry. Reading the same handle through
      /// two registries is not wrong - it just re-resolves on every access, because each read invalidates
      /// the other's epoch.
      /// </remarks>
      internal bool HasCurrentCache(int epoch)
      {
         long cache = Volatile.Read(ref m_Cache);
         int cachedEpoch = unchecked((int)(cache >> 32));
         return cachedEpoch == epoch && (cache & 0xFFFFFFFFL) != 0;
      }

      /// <summary>Reads the handle's cached index if it is still valid for <paramref name="epoch"/>.</summary>
      internal bool TryGetCached(int epoch, out GameplayTag tag)
      {
         long cache = Volatile.Read(ref m_Cache);
         int cachedIndex = unchecked((int)(cache & 0xFFFFFFFFL));
         int cachedEpoch = unchecked((int)(cache >> 32));
         if (cachedEpoch == epoch && cachedIndex > 0)
         {
            tag = new GameplayTag(cachedIndex);
            return true;
         }

         tag = GameplayTag.None;
         return false;
      }

      /// <summary>Stores a freshly resolved index together with the epoch it is valid for.</summary>
      internal void Cache(int epoch, int runtimeIndex)
      {
         Volatile.Write(ref m_Cache, ((long)epoch << 32) | (uint)runtimeIndex);
      }

      /// <summary>Reports the handle as unresolved against the registry it was asked about.</summary>
      internal void MarkUnregistered()
      {
         if (Interlocked.Exchange(ref m_WarnedUnregistered, 1) == 0 &&
             GameplayTagsCoreDiagnostics.TryGetEnabled(
                GameplayTagsDiagnosticLevel.Error,
                GameplayTagsDiagnosticCategories.Root,
                out IGameplayTagsDiagnostics diagnostics))
         {
            GameplayTagsCoreDiagnostics.TryWrite(
               diagnostics,
               GameplayTagsDiagnosticLevel.Error,
               GameplayTagsDiagnosticCategories.Root,
               $"Native gameplay tag \"{m_Name}\" is not registered. Check that the catalog declaring it " +
               "was added to the registry being read.");
         }
      }

      [MethodImpl(MethodImplOptions.NoInlining)]
      private GameplayTag Resolve(int currentEpoch)
      {
         if (GameplayTagManager.TryRequest(m_Name, out GameplayTag tag))
         {
            Volatile.Write(ref m_Cache, ((long)currentEpoch << 32) | (uint)tag.RuntimeIndex);
            return tag;
         }

         // A native tag that is not registered is always a wiring bug: the catalog that owns it was never
         // added, or it was added to a different registry than the one being read. Warn once per handle
         // rather than per access, and return None so a shipping build degrades instead of crashing.
         if (Interlocked.Exchange(ref m_WarnedUnregistered, 1) == 0 &&
             GameplayTagsCoreDiagnostics.TryGetEnabled(
                GameplayTagsDiagnosticLevel.Error,
                GameplayTagsDiagnosticCategories.Root,
                out IGameplayTagsDiagnostics diagnostics))
         {
            GameplayTagsCoreDiagnostics.TryWrite(
               diagnostics,
               GameplayTagsDiagnosticLevel.Error,
               GameplayTagsDiagnosticCategories.Root,
               $"Native gameplay tag \"{m_Name}\" is not registered. Check that the catalog declaring it " +
               "was added to the registry being read.");
         }

         return GameplayTag.None;
      }

      /// <summary>Registers this handle's declaration into a context. Called by the catalog builder.</summary>
      internal void Register(GameplayTagRegistrationContext context, IGameplayTagSource source)
      {
         context.RegisterTag(m_Name, m_Description, m_Flags, source);
      }

      public override string ToString() => m_Name;
   }
}
