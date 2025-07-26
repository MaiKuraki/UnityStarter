using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Abstract base class for all targeting data structures passed from TargetActors to GameplayAbilities.
    /// </summary>
    public abstract class TargetData
    {
        public virtual void ReturnToPool() { }
    }

    /// <summary>
    /// A generic and reusable base class for TargetData that provides a list of actor targets.
    /// This is the core class for any targeting that results in one or more GameObjects.
    /// Abilities can safely cast to this type to get a list of actors without needing to know
    /// the specific targeting method used (e.g., raycast, sphere overlap).
    /// </summary>
    public class GameplayAbilityTargetData_ActorArray : TargetData
    {
        // This list is the central, unified way to access actor targets.
        public List<GameObject> Actors { get; } = new List<GameObject>();

        /// <summary>
        /// A convenience property to get the first actor, or null if the list is empty.
        /// Useful for single-target scenarios.
        /// </summary>
        public GameObject FirstActor => Actors.Count > 0 ? Actors[0] : null;

        public virtual void AddTarget(GameObject target)
        {
            if (target != null)
            {
                Actors.Add(target);
            }
        }

        public virtual void AddTargets(List<GameObject> targets)
        {
            if (targets != null)
            {
                Actors.AddRange(targets);
            }
        }

        public virtual void Clear()
        {
            Actors.Clear();
        }
    }

    /// <summary>
    /// This class remains a concrete implementation for a single physics-based hit result,
    /// but now also conforms to the generic actor provider pattern.
    /// </summary>
    public class GameplayAbilityTargetData_SingleTargetHit : GameplayAbilityTargetData_ActorArray
    {
        private static readonly Stack<GameplayAbilityTargetData_SingleTargetHit> pool = new Stack<GameplayAbilityTargetData_SingleTargetHit>();

        /// <summary>
        /// The specific, engine-dependent physics hit result.
        /// An ability can still access this if it needs detailed collision info (e.g., impact normal for ricochets).
        /// </summary>
        public RaycastHit HitResult { get; private set; }

        public static GameplayAbilityTargetData_SingleTargetHit Get() => pool.Count > 0 ? pool.Pop() : new GameplayAbilityTargetData_SingleTargetHit();

        public void Init(RaycastHit hit)
        {
            HitResult = hit;
            
            Clear();
            if (hit.collider != null)
            {
                AddTarget(hit.collider.gameObject);
            }
        }

        public override void ReturnToPool()
        {
            Clear();
            HitResult = default;
            pool.Push(this);
        }
    }

    /// <summary>
    /// This class defined as a data container for multiple actors
    /// found via non-physics or bulk-physics checks (like sphere overlap).
    /// </summary>
    public class GameplayAbilityTargetData_MultiTarget : GameplayAbilityTargetData_ActorArray
    {
        private static readonly Stack<GameplayAbilityTargetData_MultiTarget> pool = new Stack<GameplayAbilityTargetData_MultiTarget>();

        public static GameplayAbilityTargetData_MultiTarget Get() => pool.Count > 0 ? pool.Pop() : new GameplayAbilityTargetData_MultiTarget();

        public void Init(List<GameObject> targets)
        {
            Clear();
            AddTargets(targets);
        }

        public override void ReturnToPool()
        {
            Clear();
            pool.Push(this);
        }
    }
}
