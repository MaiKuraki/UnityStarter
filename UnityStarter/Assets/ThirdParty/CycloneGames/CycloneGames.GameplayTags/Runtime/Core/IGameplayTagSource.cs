namespace CycloneGames.GameplayTags.Runtime
{
   public interface IDeleteTagHandler
   {
      public void DeleteTag(string tagName);
   }

   public interface IGameplayTagSource
   {
      public string Name { get; }

      public void RegisterTags(GameplayTagRegistrationContext context);
   }
}
