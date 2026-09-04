using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// Assembles the inputs of a <see cref="GameplayTagRegistry"/>.
   /// </summary>
   /// <remarks>
   /// <para>
   /// Non-DI code never touches this type; it uses the ambient <see cref="GameplayTagManager"/>. DI code,
   /// tests, and host bootstraps build an explicit registry here and hand the instance to their consumers.
   /// </para>
   /// <para>
   /// Inputs are snapshotted at <see cref="Build"/> time. A builder is a single-use wiring object and is
   /// not thread safe.
   /// </para>
   /// </remarks>
   public sealed class GameplayTagRegistryBuilder
   {
      internal readonly List<IGameplayTagSource> Sources = new();
      internal readonly List<IGameplayTagCatalog> Catalogs = new();

      internal int MaxRegisteredTagCount { get; private set; } = GameplayTagUtility.MaxRegisteredTagCount;

      /// <summary>Adds a source that declares tags during every rebuild.</summary>
      public GameplayTagRegistryBuilder AddSource(IGameplayTagSource source)
      {
         if (source == null)
            throw new ArgumentNullException(nameof(source));
         if (!Sources.Contains(source))
            Sources.Add(source);

         return this;
      }

      /// <summary>Adds several sources that declare tags during every rebuild.</summary>
      public GameplayTagRegistryBuilder AddSources(IEnumerable<IGameplayTagSource> sources)
      {
         if (sources == null)
            return this;

         foreach (IGameplayTagSource source in sources)
            AddSource(source);

         return this;
      }

      /// <summary>
      /// Adds a generated catalog. Catalogs are the reflection-free replacement for assembly attribute
      /// sweeping: the tag list is compiled into the assembly as ordinary code.
      /// </summary>
      public GameplayTagRegistryBuilder AddCatalog(IGameplayTagCatalog catalog)
      {
         if (catalog == null)
            throw new ArgumentNullException(nameof(catalog));
         if (!Catalogs.Contains(catalog))
            Catalogs.Add(catalog);

         return this;
      }

      /// <summary>Adds several generated catalogs.</summary>
      public GameplayTagRegistryBuilder AddCatalogs(IEnumerable<IGameplayTagCatalog> catalogs)
      {
         if (catalogs == null)
            return this;

         foreach (IGameplayTagCatalog catalog in catalogs)
            AddCatalog(catalog);

         return this;
      }

      /// <summary>
      /// Caps how many tags this registry accepts, including implicit parents. Defaults to
      /// <see cref="GameplayTagUtility.MaxRegisteredTagCount"/>.
      /// </summary>
      public GameplayTagRegistryBuilder SetMaxRegisteredTagCount(int count)
      {
         if (count <= 0 || count > GameplayTagUtility.MaxRegisteredTagCount)
         {
            throw new ArgumentOutOfRangeException(
               nameof(count),
               $"Tag count must be between 1 and {GameplayTagUtility.MaxRegisteredTagCount}.");
         }

         MaxRegisteredTagCount = count;
         return this;
      }

      public GameplayTagRegistry Build() => new(this);
   }
}
