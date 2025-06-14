using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayTags.Runtime
{
    public class GameplayTagContainerBinds
    {
        private struct BindData
        {
            // Store the original user action to allow for proper unbinding.
            public Action<bool> OriginalAction;
            public OnTagCountChangedDelegate MappedAction;
            public GameplayTag Tag;
        }

        private readonly GameplayTagCountContainer m_Container;
        private List<BindData> m_Binds;

        // Cache for the mapping delegate to avoid GC allocation on every bind.
        private readonly Dictionary<Action<bool>, OnTagCountChangedDelegate> actionMap =
            new Dictionary<Action<bool>, OnTagCountChangedDelegate>();

        public GameplayTagContainerBinds(GameplayTagCountContainer container)
        {
            m_Container = container;
        }

        public GameplayTagContainerBinds(GameObject gameObject)
        {
            GameObjectGameplayTagContainer component = gameObject.GetComponent<GameObjectGameplayTagContainer>();
            m_Container = component.GameplayTagContainer;
        }

        public void Bind(GameplayTag tag, Action<bool> onTagAddedOrRemoved)
        {
            m_Binds ??= new List<BindData>();

            // Check if we've already created a mapped delegate for this action.
            if (!actionMap.TryGetValue(onTagAddedOrRemoved, out var mappedAction))
            {
                // If not, create it once and cache it. This prevents new delegate allocations on subsequent binds.
                mappedAction = (gameplayTag, newCount) => { onTagAddedOrRemoved(newCount > 0); };
                actionMap[onTagAddedOrRemoved] = mappedAction;
            }

            m_Binds.Add(new BindData { Tag = tag, OriginalAction = onTagAddedOrRemoved, MappedAction = mappedAction });
            m_Container.RegisterTagEventCallback(tag, GameplayTagEventType.NewOrRemoved, mappedAction);

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
                m_Container.RemoveTagEventCallback(bind.Tag, GameplayTagEventType.NewOrRemoved, bind.MappedAction);
            }

            m_Binds.Clear();
            actionMap.Clear();
        }
    }
}