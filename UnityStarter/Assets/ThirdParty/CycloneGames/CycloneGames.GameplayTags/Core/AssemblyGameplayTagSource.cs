#if UNITY_EDITOR
using System.Reflection;

namespace CycloneGames.GameplayTags.Core
{
   internal sealed class AssemblyGameplayTagSource : IGameplayTagSource
   {
      public string Name => m_Assembly.GetName().Name;

      private readonly Assembly m_Assembly;

      public AssemblyGameplayTagSource(Assembly assembly)
      {
         m_Assembly = assembly;
      }

      public void RegisterTags(GameplayTagRegistrationContext context)
      {
         foreach (GameplayTagAttribute attribute in m_Assembly.GetCustomAttributes<GameplayTagAttribute>())
            context.RegisterTag(attribute.TagName, attribute.Description, attribute.Flags, this);
      }
   }
}
#endif
