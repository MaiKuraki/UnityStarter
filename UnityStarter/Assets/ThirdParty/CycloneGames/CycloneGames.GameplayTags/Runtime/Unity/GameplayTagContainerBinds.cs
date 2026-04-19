using System;
using System.Collections.Generic;
#if UNITY_5_3_OR_NEWER
using UnityEngine;
#endif

namespace CycloneGames.GameplayTags.Runtime
{
    public class GameplayTagContainerBinds
    {
        private struct BindData
        {
            public Action<bool> OriginalAction;
            public OnTagCountChangedDelegate MappedAction;
            public GameplayTag Tag;
        }

        private readonly GameplayTagCountContainer m_Container;
        private List<BindData> m_Binds;

        private readonly Dictionary<Action<bool>, OnTagCountChangedDelegate> actionMap =
            new Dictionary<Action<bool>, OnTagCountChangedDelegate>();

        public GameplayTagContainerBinds(GameplayTagCountContainer container)
        {
            m_Container = container;
        }

#if UNITY_5_3_OR_NEWER
        public GameplayTagContainerBinds(GameObject gameObject)
        {
            GameObjectGameplayTagContainer component = gameObject.GetComponent<GameObjectGameplayTagContainer>();
            m_Container = component.GameplayTagContainer;
        }
#endif

        public void Bind(GameplayTag tag, Action<bool> onTagAddedOrRemoved)
        {
            m_Binds ??= new List<BindData>();

            if (!actionMap.TryGetValue(onTagAddedOrRemoved, out OnTagCountChangedDelegate mappedAction))
            {
                mappedAction = (_, newCount) => onTagAddedOrRemoved(newCount > 0);
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
