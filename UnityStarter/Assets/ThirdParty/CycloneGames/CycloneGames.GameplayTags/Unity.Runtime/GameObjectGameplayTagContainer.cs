#if UNITY_5_3_OR_NEWER
using UnityEngine;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayTags.Unity.Runtime
{
   public class GameObjectGameplayTagContainer : MonoBehaviour
   {
      public GameplayTagCountContainer GameplayTagContainer
      {
         get
         {
            EnsureRuntimeContainerInitialized();
            return m_GameplayTagContainer;
         }
      }

      [SerializeField]
      private GameplayTagContainer m_PersistentTags;

      private GameplayTagCountContainer m_GameplayTagContainer;

      private void Awake()
      {
         EnsureRuntimeContainerInitialized();
      }

      private void EnsureRuntimeContainerInitialized()
      {
         if (m_GameplayTagContainer != null)
         {
            return;
         }

         m_GameplayTagContainer = new GameplayTagCountContainer();
         if (m_PersistentTags != null && !m_PersistentTags.IsEmpty)
         {
            m_GameplayTagContainer.AddTags(m_PersistentTags);
         }
      }

      public static implicit operator GameplayTagCountContainer(GameObjectGameplayTagContainer container)
      {
         if (container == null)
         {
            throw new System.ArgumentNullException(nameof(container));
         }

         return container.GameplayTagContainer;
      }
   }
}
#endif
