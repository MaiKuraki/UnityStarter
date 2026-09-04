namespace CycloneGames.GameplayTags.Core
{
   /// <summary>
   /// A source that authoring tooling can edit: remove a tag it declares, and add a new one. The rename
   /// flow needs both, so they live on one interface rather than letting a panel compose two unrelated
   /// capability checks.
   /// </summary>
   public interface IDeleteTagHandler
   {
      public void DeleteTag(string tagName);

      /// <summary>Adds a tag to this source. Throws if the name is already registered in it.</summary>
      public void AddTag(string tagName, string description);
   }

   public interface IGameplayTagSource
   {
      public string Name { get; }

      public void RegisterTags(GameplayTagRegistrationContext context);
   }
}
