using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayTags
{
   public struct GameplayTagContainerBinds
   {
      private struct BindData
      {
         public OnTagCountChangedDelegate OnTagAddedOrRemved;
         public GameplayTag Tag;
      }

      private GameplayTagCountContainer m_Container;
      private List<BindData> m_Binds;

      public GameplayTagContainerBinds(GameplayTagCountContainer container)
      {
         m_Container = container;
         m_Binds = null;
      }

      public GameplayTagContainerBinds(GameObject gameObject)
      {
         GameObjectGameplayTagContainer component = gameObject.GetComponent<GameObjectGameplayTagContainer>();
         m_Container = component.GameplayTagContainer;
         m_Binds = null;
      }

      public void Bind(GameplayTag tag, Action<bool> onTagAddedOrRemoved)
      {
         m_Binds ??= new List<BindData>();

         void OnTagAddedOrRemoved(GameplayTag gameplayTag, int newCount)
         {
            onTagAddedOrRemoved(newCount > 0);
         }

         m_Binds.Add(new BindData { Tag = tag, OnTagAddedOrRemved = OnTagAddedOrRemoved });
         m_Container.RegisterTagEventCallback(tag, GameplayTagEventType.NewOrRemoved, OnTagAddedOrRemoved);

         int count = m_Container.GetTagCount(tag);
         onTagAddedOrRemoved(count > 0);
      }

      public void UnbindAll()
      {
         if (m_Binds == null)
         {
            return;
         }

         foreach (BindData bind in m_Binds)
         {
            m_Container.RemoveTagEventCallback(bind.Tag, GameplayTagEventType.NewOrRemoved, bind.OnTagAddedOrRemved);
         }

         m_Binds.Clear();
      }
   }
}