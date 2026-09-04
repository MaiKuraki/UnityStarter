using System;

namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// The write surface handed to <see cref="IGameplayTagCatalog.Collect"/>.
   /// </summary>
   /// <remarks>
   /// Generated code calls <see cref="Add"/> once per declared tag. The builder is a transient scratch
   /// object owned by the registry build; it is not thread safe and must not be retained.
   /// </remarks>
   public sealed class GameplayTagCatalogBuilder
   {
      private readonly GameplayTagRegistrationContext m_OwningContext;
      private readonly IGameplayTagSource m_Source;

      internal GameplayTagCatalogBuilder(GameplayTagRegistrationContext owningContext, IGameplayTagSource source)
      {
         m_OwningContext = owningContext;
         m_Source = source;
      }

      /// <summary>Declares a tag with no description and no flags.</summary>
      public GameplayTagCatalogBuilder Add(string name)
         => Add(name, null, GameplayTagFlags.None);

      /// <summary>Declares a tag with a description and no flags.</summary>
      public GameplayTagCatalogBuilder Add(string name, string description)
         => Add(name, description, GameplayTagFlags.None);

      /// <summary>Declares a tag with a description and flags.</summary>
      public GameplayTagCatalogBuilder Add(string name, string description, GameplayTagFlags flags)
      {
         m_OwningContext.RegisterTag(name, description, flags, m_Source);
         return this;
      }

      /// <summary>
      /// Declares a tag whose identity game code holds as a <see cref="NativeGameplayTag"/> constant.
      /// </summary>
      /// <remarks>
      /// Passing the handle rather than its name is what makes the constant pattern work: the handle
      /// object stays alive across rebuilds, so its cached index survives a build that preserved indices
      /// and is invalidated on one that did not. Creating a handle inside <c>Collect</c> would rebuild it
      /// every time and throw the cache away.
      /// </remarks>
      public GameplayTagCatalogBuilder Add(NativeGameplayTag nativeTag)
      {
         if (nativeTag == null)
            throw new ArgumentNullException(nameof(nativeTag));

         nativeTag.Register(m_OwningContext, m_Source);
         return this;
      }
   }
}
