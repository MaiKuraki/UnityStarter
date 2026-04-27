#if UNITY_5_3_OR_NEWER
using UnityEngine;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayTags.Unity.Runtime
{
   public class GameObjectGameplayTagContainer : MonoBehaviour
   {
      public GameplayTagCountContainer GameplayTagContainer => m_GameplayTagContainer;

      [SerializeField]
      private GameplayTagContainer m_PersistentTags;

      private GameplayTagCountContainer m_GameplayTagContainer;

      private void Awake()
      {
         m_GameplayTagContainer = new GameplayTagCountContainer();
         m_GameplayTagContainer.AddTags(m_PersistentTags);
      }

      public static implicit operator GameplayTagCountContainer(GameObjectGameplayTagContainer container)
      {
         return container.GameplayTagContainer;
      }
   }
}
#endif
