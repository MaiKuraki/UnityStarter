using System;
using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public enum EGameplayCueEvent
    {
        OnActive,    // Called when a GameplayCue is activated (added).
        WhileActive, // Called when GameplayCue is active, even if it wasn't actually just applied (e.g. Join in progress).
        Removed,     // Called when a GameplayCue is removed.
        Executed     // Called when a GameplayCue is executed (for instant effects).
    }

    public interface IGameplayCueAgent
    {
        void HandleGameplayCue(GameplayTag cueTag, EGameplayCueEvent eventType, GameplayEffectSpec spec);
    }

    /// <summary>
    /// Manages the registration and execution of GameplayCues.
    /// </summary>
    public class GameplayCueManager
    {
        //  may integrate DI framework, not register here?
        private static readonly GameplayCueManager instance = new GameplayCueManager();
        public static GameplayCueManager Instance => instance;
        
        private readonly Dictionary<GameplayTag, List<Action<EGameplayCueEvent>>> tagToCueActions = new Dictionary<GameplayTag, List<Action<EGameplayCueEvent>>>();

        // This would be replaced with a system that discovers agents, e.g., via component queries.
        private readonly List<IGameplayCueAgent> registeredAgents = new List<IGameplayCueAgent>();

        public void RegisterAgent(IGameplayCueAgent agent)
        {
            if (agent != null && !registeredAgents.Contains(agent))
            {
                registeredAgents.Add(agent);
            }
        }

        public void UnregisterAgent(IGameplayCueAgent agent)
        {
            if (agent != null)
            {
                registeredAgents.Remove(agent);
            }
        }

        /// <summary>
        /// Triggers a gameplay cue event for all registered agents.
        /// </summary>
        public void TriggerCue(GameplayTag cueTag, EGameplayCueEvent eventType, GameplayEffectSpec spec)
        {
            // In a networked game, the server would RPC this call to clients.
            // For now, we broadcast to all local agents. 
            foreach (var agent in registeredAgents)
            {
                agent.HandleGameplayCue(cueTag, eventType, spec);
            }
        }
    }
}