namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Supported pathfinding systems for AI navigation.
    /// Selection is controlled via MovementConfig in Inspector.
    /// </summary>
    public enum PathfindingSystem
    {
        /// <summary>No pathfinding - manual control only.</summary>
        None = 0,

        /// <summary>Unity's built-in NavMesh system. Requires com.unity.ai.navigation package.</summary>
        UnityNavMesh = 1,

        /// <summary>A* Pathfinding Project by Aron Granberg. Requires com.arongranberg.astar package.</summary>
        AStarPathfinding = 2,

        /// <summary>Agents Navigation (DOTS-based). Requires com.projectdawn.navigation package.</summary>
        AgentsNavigation = 3
    }
}
