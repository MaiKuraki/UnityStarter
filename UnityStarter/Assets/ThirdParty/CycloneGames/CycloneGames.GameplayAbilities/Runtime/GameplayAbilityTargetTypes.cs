using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    // Base class for all data passing structures
    public abstract class TargetData
    {
        public virtual void ReturnToPool() { }
    }

    // A concrete implementation for a single hit result
    public class GameplayAbilityTargetData_SingleTargetHit : TargetData
    {
        private static readonly Stack<GameplayAbilityTargetData_SingleTargetHit> pool = new Stack<GameplayAbilityTargetData_SingleTargetHit>();

        public RaycastHit HitResult { get; private set; }

        public static GameplayAbilityTargetData_SingleTargetHit Get() => pool.Count > 0 ? pool.Pop() : new GameplayAbilityTargetData_SingleTargetHit();

        public void Init(RaycastHit hit) => HitResult = hit;

        public override void ReturnToPool()
        {
            HitResult = default;
            pool.Push(this);
        }
    }
}