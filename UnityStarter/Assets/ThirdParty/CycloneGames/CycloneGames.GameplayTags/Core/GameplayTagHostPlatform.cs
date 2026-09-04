using System;
using System.Collections.Generic;
using System.Threading;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// The host-supplied facts the tag registry cannot know on its own.
   /// </summary>
   /// <remarks>
   /// <para>
   /// Core is engine-free, so it cannot open an asset, read a project directory, or know whether the game
   /// is playing. A host implements this interface and installs it once during bootstrap with
   /// <see cref="GameplayTagHost.Use"/>. Before that, and in any host that has no need for build data, the
   /// null implementation answers "nothing available", which makes the registry build from catalogs only.
   /// </para>
   /// <para>
   /// This replaces the previous four static mutable <c>Func</c> properties. Those were read without a
   /// memory barrier, so a platform installed on one thread could be observed half-initialized on another -
   /// a real hazard on ARM, where iOS and Android both live. <see cref="GameplayTagHost.Current"/> reads
   /// through <see cref="Volatile.Read"/> and writes publish through <see cref="Volatile.Write"/>.
   /// </para>
   /// <para>
   /// <b>Threading.</b> Implementations are read from any thread that resolves a tag, so they must be
   /// immutable after construction or internally synchronized. The registry calls them only during a
   /// rebuild, but it does not guarantee which thread that is.
   /// </para>
   /// </remarks>
   public interface IGameplayTagHostPlatform
   {
      /// <summary>A stable name for diagnostics.</summary>
      string Name { get; }

      /// <summary>
      /// True while the host considers the game to be playing. Editor hosts use this to decide whether an
      /// authoring refresh must preserve runtime indices.
      /// </summary>
      bool IsRuntimePlaying { get; }

      /// <summary>
      /// Supplies the pre-baked tag manifest a Player build ships with.
      /// </summary>
      /// <returns>False when the host has no baked manifest, in which case the registry falls back to its catalogs.</returns>
      bool TryLoadBuildTagData(out byte[] data);

      /// <summary>
      /// The directory an authoring host stores its tag files in. Editor-only; a Player host returns null.
      /// </summary>
      string GetProjectTagSettingsDirectory();

      /// <summary>
      /// Additional sources the host contributes to every rebuild, such as a DataTable-backed catalog.
      /// Returned in the order they should be applied.
      /// </summary>
      void CollectProjectTagSources(List<IGameplayTagSource> destinations);
   }

   /// <summary>
   /// The ambient host platform: the non-DI way a host plugs itself into the tag registry.
   /// </summary>
   /// <remarks>
   /// All members are thread safe. Reads are lock free and publish through
   /// <see cref="Volatile"/>, so a platform installed during bootstrap is fully visible to every later
   /// reader on every architecture.
   /// </remarks>
   public static class GameplayTagHost
   {
      private static readonly NullPlatform s_NullPlatform = new();
      private static IGameplayTagHostPlatform s_Platform = s_NullPlatform;

      /// <summary>
      /// The installed platform, or a null platform that provides nothing. Never null.
      /// </summary>
      public static IGameplayTagHostPlatform Current => Volatile.Read(ref s_Platform);

      /// <summary>
      /// Installs the platform every later registry build reads from.
      /// </summary>
      /// <remarks>
      /// Call once during bootstrap, before the first tag resolution. Installing a different platform
      /// later does not rebuild the registry by itself; call <see cref="GameplayTagManager.Reload"/> to
      /// apply new sources.
      /// </summary>
      public static void Use(IGameplayTagHostPlatform platform)
      {
         Volatile.Write(ref s_Platform, platform ?? s_NullPlatform);
      }

      /// <summary>
      /// Registers an additional project source with the currently installed platform, replacing any
      /// source registered under the same name.
      /// </summary>
      /// <remarks>
      /// <see cref="IGameplayTagHostPlatform.CollectProjectTagSources"/> is the contract a platform has to
      /// satisfy for this to work; a platform that ignores <paramref name="source"/> is a platform bug, so
      /// the null platform rejects it loudly.
      /// </remarks>
      public static void RegisterProjectTagSource(IGameplayTagSource source)
      {
         if (source == null)
            throw new ArgumentNullException(nameof(source));
         if (string.IsNullOrWhiteSpace(source.Name))
            throw new ArgumentException("Gameplay tag source name cannot be empty.", nameof(source));

         IGameplayTagHostPlatform platform = Current;
         if (platform is ISupportsProjectTagSources supports)
         {
            supports.RegisterProjectTagSource(source);
            return;
         }

         throw new NotSupportedException(
            $"The installed gameplay tag host platform '{platform.Name}' does not accept project tag sources. " +
            "Use a platform that implements ISupportsProjectTagSources, or contribute the source through " +
            "IGameplayTagHostPlatform.CollectProjectTagSources.");
      }

      /// <summary>
      /// Removes a source previously registered with <see cref="RegisterProjectTagSource"/>.
      /// </summary>
      public static bool UnregisterProjectTagSource(string sourceName)
      {
         if (string.IsNullOrWhiteSpace(sourceName))
            return false;

         return Current is ISupportsProjectTagSources supports && supports.UnregisterProjectTagSource(sourceName);
      }

      /// <summary>Removes every source registered with <see cref="RegisterProjectTagSource"/>.</summary>
      public static void ClearRegisteredProjectTagSources()
      {
         if (Current is ISupportsProjectTagSources supports)
            supports.ClearRegisteredProjectTagSources();
      }

      /// <summary>
      /// A platform that provides nothing. Used before a host installs one, so a headless or catalog-only
      /// host never has to.
      /// </summary>
      private sealed class NullPlatform : IGameplayTagHostPlatform
      {
         public string Name => "Null";

         public bool IsRuntimePlaying => false;

         public bool TryLoadBuildTagData(out byte[] data)
         {
            data = null;
            return false;
         }

         public string GetProjectTagSettingsDirectory() => null;

         public void CollectProjectTagSources(List<IGameplayTagSource> destinations)
         {
         }
      }
   }

   /// <summary>
   /// Implemented by host platforms that can hold a mutable set of project tag sources. Core never depends
   /// on it; it is how <see cref="GameplayTagHost.RegisterProjectTagSource"/> finds a willing platform.
   /// </summary>
   public interface ISupportsProjectTagSources
   {
      void RegisterProjectTagSource(IGameplayTagSource source);
      bool UnregisterProjectTagSource(string sourceName);
      void ClearRegisteredProjectTagSources();
   }

   /// <summary>
   /// A ready-to-use platform for hosts that want the project-source registry without writing their own.
   /// </summary>
   /// <remarks>
   /// Sources are stored copy-on-write and published with <see cref="Volatile.Write"/>, so a reader never
   /// observes a half-updated set. Subclasses override the virtual members to supply host facts; they keep
   /// the source registry for free.
   /// </remarks>
   public abstract class GameplayTagHostPlatformBase : IGameplayTagHostPlatform, ISupportsProjectTagSources
   {
      private readonly object m_Gate = new();
      private Dictionary<string, IGameplayTagSource> m_Sources = new(StringComparer.Ordinal);

      public abstract string Name { get; }

      public virtual bool IsRuntimePlaying => false;

      public virtual bool TryLoadBuildTagData(out byte[] data)
      {
         data = null;
         return false;
      }

      public virtual string GetProjectTagSettingsDirectory() => null;

      public virtual void CollectProjectTagSources(List<IGameplayTagSource> destinations)
      {
         if (destinations == null)
            throw new ArgumentNullException(nameof(destinations));

         Dictionary<string, IGameplayTagSource> sources = Volatile.Read(ref m_Sources);
         if (sources.Count == 0)
            return;

         // A stable order makes a rebuild reproducible, which is what keeps runtime indices identical
         // across two peers that registered their sources in different orders.
         string[] names = new string[sources.Count];
         sources.Keys.CopyTo(names, 0);
         Array.Sort(names, StringComparer.Ordinal);
         for (int i = 0; i < names.Length; i++)
            destinations.Add(sources[names[i]]);
      }

      public void RegisterProjectTagSource(IGameplayTagSource source)
      {
         if (source == null)
            throw new ArgumentNullException(nameof(source));

         lock (m_Gate)
         {
            Dictionary<string, IGameplayTagSource> next = new(m_Sources, StringComparer.Ordinal)
            {
               [source.Name] = source
            };
            Volatile.Write(ref m_Sources, next);
         }
      }

      public bool UnregisterProjectTagSource(string sourceName)
      {
         if (string.IsNullOrWhiteSpace(sourceName))
            return false;

         lock (m_Gate)
         {
            if (!m_Sources.ContainsKey(sourceName))
               return false;

            Dictionary<string, IGameplayTagSource> next = new(m_Sources, StringComparer.Ordinal);
            bool removed = next.Remove(sourceName);
            Volatile.Write(ref m_Sources, next);
            return removed;
         }
      }

      public void ClearRegisteredProjectTagSources()
      {
         lock (m_Gate)
            Volatile.Write(ref m_Sources, new Dictionary<string, IGameplayTagSource>(StringComparer.Ordinal));
      }
   }
}
