using System;
using System.Reflection;
using UnityEngine;

namespace CycloneGames.GameplayTags.Runtime
{
   internal class AssemblyGameplayTagSource : IGameplayTagSource
   {
      public string Name => m_Assembly.GetName().Name;

      private Assembly m_Assembly;

      public AssemblyGameplayTagSource(Assembly assembly)
      {
         m_Assembly = assembly;
      }

      public void RegisterTags(GameplayTagRegistrationContext context)
      {
         try
         {
            foreach (GameplayTagAttribute attribute in m_Assembly.GetCustomAttributes<GameplayTagAttribute>())
               context.RegisterTag(attribute.TagName, attribute.Description, attribute.Flags, this);
         }
         catch (ReflectionTypeLoadException ex)
         {
            foreach (Exception loaderException in ex.LoaderExceptions)
               Debug.LogError($"Failed to load type from assembly '{m_Assembly.FullName}': {loaderException.Message}");
         }
         catch (Exception ex)
         {
            Debug.LogError($"Failed to fetch tags from assembly '{m_Assembly.FullName}': {ex.Message}");
         }
      }
   }
}
