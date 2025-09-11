namespace CycloneGames.GameplayTags.Runtime
{
   internal interface IDeleteTagHandler
   {
      public void DeleteTag(string tagName);
   }

   internal interface IGameplayTagSource
   {
      public string Name { get; }

      public void RegisterTags(GameplayTagRegistrationContext context);
   }
}
