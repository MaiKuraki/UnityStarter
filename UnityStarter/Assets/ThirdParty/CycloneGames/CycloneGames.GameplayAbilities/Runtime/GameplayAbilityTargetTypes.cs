using System.Collections.Concurrent;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public enum TargetDataValidationResult
    {
        Valid,
        MissingData,
        PredictionKeyMismatch,
        AbilitySpecMismatch,
        SourceMismatch,
        TooOld,
        InvalidTarget,
        TargetOutOfRange
    }

    public enum TargetDataNetworkType
    {
        None,
        ActorArray,
        SingleTargetHit
    }

    public readonly struct TargetDataNetworkData
    {
        public readonly TargetDataNetworkType Type;
        public readonly GASPredictionKey PredictionKey;
        public readonly int AbilitySpecHandle;
        public readonly int SourceAscNetId;
        public readonly int CreatedFrame;
        public readonly int[] TargetObjectIds;
        public readonly int TargetObjectCount;
        public readonly Vector3 HitPoint;
        public readonly Vector3 HitNormal;
        public readonly float HitDistance;

        public TargetDataNetworkData(
            TargetDataNetworkType type,
            GASPredictionKey predictionKey,
            int abilitySpecHandle,
            int sourceAscNetId,
            int createdFrame,
            int[] targetObjectIds,
            Vector3 hitPoint,
            Vector3 hitNormal,
            float hitDistance)
            : this(type, predictionKey, abilitySpecHandle, sourceAscNetId, createdFrame, targetObjectIds, targetObjectIds?.Length ?? 0, hitPoint, hitNormal, hitDistance)
        {
        }

        public TargetDataNetworkData(
            TargetDataNetworkType type,
            GASPredictionKey predictionKey,
            int abilitySpecHandle,
            int sourceAscNetId,
            int createdFrame,
            int[] targetObjectIds,
            int targetObjectCount,
            Vector3 hitPoint,
            Vector3 hitNormal,
            float hitDistance)
        {
            Type = type;
            PredictionKey = predictionKey;
            AbilitySpecHandle = abilitySpecHandle;
            SourceAscNetId = sourceAscNetId;
            CreatedFrame = createdFrame;
            TargetObjectIds = targetObjectIds;
            TargetObjectCount = targetObjectCount < 0 ? 0 : targetObjectCount;
            HitPoint = hitPoint;
            HitNormal = hitNormal;
            HitDistance = hitDistance;
        }
    }

    /// <summary>
    /// Optional extension for network bridges that support predicted target-data RPCs.
    /// Existing IGASNetworkBridge implementations do not need to implement this until they need multiplayer target data.
    /// </summary>
    public interface IGASTargetDataNetworkBridge
    {
        void ClientSendTargetData(AbilitySystemComponent sourceAsc, int specHandle, GASPredictionKey predictionKey, in TargetDataNetworkData snapshot);
        void ServerConfirmTargetData(AbilitySystemComponent targetAsc, int specHandle, GASPredictionKey predictionKey);
        void ServerRejectTargetData(AbilitySystemComponent targetAsc, int specHandle, GASPredictionKey predictionKey, TargetDataValidationResult reason);
    }

    public static class TargetDataNetworkCodec
    {
        public static int GetRequiredTargetIdCapacity(TargetData data)
        {
            if (data is GameplayAbilityTargetData_ActorArray actorArray)
            {
                return actorArray.Actors.Count;
            }

            return 0;
        }

        public static TargetDataNetworkData CaptureNonAlloc(TargetData data, System.Func<GameObject, int> targetIdResolver, int[] targetIdBuffer, System.Func<AbilitySystemComponent, int> sourceIdResolver = null)
        {
            if (data == null)
            {
                return default;
            }

            int sourceId = sourceIdResolver != null && data.Source != null ? sourceIdResolver(data.Source) : 0;
            int[] targetIds = System.Array.Empty<int>();
            int targetCount = 0;
            var type = TargetDataNetworkType.None;
            var hitPoint = default(Vector3);
            var hitNormal = default(Vector3);
            float hitDistance = 0f;

            if (data is GameplayAbilityTargetData_ActorArray actorArray)
            {
                type = TargetDataNetworkType.ActorArray;
                int count = actorArray.Actors.Count;
                if (count > 0)
                {
                    if (targetIdBuffer != null && targetIdBuffer.Length >= count)
                    {
                        targetIds = targetIdBuffer;
                    }
                    else
                    {
                        return default;
                    }

                    targetCount = count;
                    for (int i = 0; i < count; i++)
                    {
                        var target = actorArray.Actors[i];
                        targetIds[i] = target != null && targetIdResolver != null ? targetIdResolver(target) : 0;
                    }
                }
            }

            if (data is GameplayAbilityTargetData_SingleTargetHit hitData)
            {
                type = TargetDataNetworkType.SingleTargetHit;
                hitPoint = hitData.HitPoint;
                hitNormal = hitData.HitNormal;
                hitDistance = hitData.HitDistance;
            }

            return new TargetDataNetworkData(
                type,
                data.PredictionKey,
                data.AbilitySpecHandle,
                sourceId,
                data.CreatedFrame,
                targetIds,
                targetCount,
                hitPoint,
                hitNormal,
                hitDistance);
        }

        public static TargetData Create(in TargetDataNetworkData snapshot, System.Func<int, GameObject> targetResolver)
        {
            switch (snapshot.Type)
            {
                case TargetDataNetworkType.ActorArray:
                    return CreateActorArray(in snapshot, targetResolver);
                case TargetDataNetworkType.SingleTargetHit:
                    return CreateSingleTargetHit(in snapshot, targetResolver);
                default:
                    return null;
            }
        }

        private static GameplayAbilityTargetData_MultiTarget CreateActorArray(in TargetDataNetworkData snapshot, System.Func<int, GameObject> targetResolver)
        {
            var data = GameplayAbilityTargetData_MultiTarget.Get();
            data.Clear();
            AddResolvedTargets(data, snapshot.TargetObjectIds, snapshot.TargetObjectCount, targetResolver);
            data.ApplyNetworkStamp(snapshot.PredictionKey, snapshot.AbilitySpecHandle, snapshot.CreatedFrame);
            return data;
        }

        private static GameplayAbilityTargetData_SingleTargetHit CreateSingleTargetHit(in TargetDataNetworkData snapshot, System.Func<int, GameObject> targetResolver)
        {
            var data = GameplayAbilityTargetData_SingleTargetHit.Get();
            data.InitNetworkHit(snapshot.HitPoint, snapshot.HitNormal, snapshot.HitDistance);
            AddResolvedTargets(data, snapshot.TargetObjectIds, snapshot.TargetObjectCount, targetResolver);
            data.ApplyNetworkStamp(snapshot.PredictionKey, snapshot.AbilitySpecHandle, snapshot.CreatedFrame);
            return data;
        }

        private static void AddResolvedTargets(GameplayAbilityTargetData_ActorArray data, int[] targetObjectIds, int targetObjectCount, System.Func<int, GameObject> targetResolver)
        {
            if (targetObjectIds == null || targetResolver == null)
            {
                return;
            }

            int count = targetObjectCount <= targetObjectIds.Length ? targetObjectCount : targetObjectIds.Length;
            for (int i = 0; i < count; i++)
            {
                int targetId = targetObjectIds[i];
                if (targetId == 0)
                {
                    continue;
                }

                data.AddTarget(targetResolver(targetId));
            }
        }
    }

    /// <summary>
    /// Abstract base class for all targeting data structures passed from TargetActors to GameplayAbilities.
    /// </summary>
    public abstract class TargetData
    {
        public GASPredictionKey PredictionKey { get; private set; }
        public int AbilitySpecHandle { get; private set; }
        public AbilitySystemComponent Source { get; private set; }
        public int CreatedFrame { get; private set; }

        public void StampPrediction(GameplayAbility ability, GASPredictionKey predictionKey)
        {
            PredictionKey = predictionKey;
            AbilitySpecHandle = ability?.Spec?.Handle ?? 0;
            Source = ability?.AbilitySystemComponent;
            CreatedFrame = Time.frameCount;
        }

        internal void ApplyNetworkStamp(GASPredictionKey predictionKey, int abilitySpecHandle, int createdFrame)
        {
            PredictionKey = predictionKey;
            AbilitySpecHandle = abilitySpecHandle;
            Source = null;
            CreatedFrame = createdFrame;
        }

        protected void ClearPredictionStamp()
        {
            PredictionKey = default;
            AbilitySpecHandle = 0;
            Source = null;
            CreatedFrame = 0;
        }

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
        private static readonly ConcurrentStack<GameplayAbilityTargetData_SingleTargetHit> pool = new();

        /// <summary>
        /// The specific, engine-dependent physics hit result.
        /// An ability can still access this if it needs detailed collision info (e.g., impact normal for ricochets).
        /// </summary>
        public RaycastHit HitResult { get; private set; }

        public static GameplayAbilityTargetData_SingleTargetHit Get() => pool.TryPop(out var item) ? item : new GameplayAbilityTargetData_SingleTargetHit();

        public void Init(RaycastHit hit)
        {
            HitResult = hit;

            Clear();
            if (hit.collider != null)
            {
                AddTarget(hit.collider.gameObject);
            }
        }

        internal void InitNetworkHit(Vector3 point, Vector3 normal, float distance)
        {
            HitResult = default;
            networkHitPoint = point;
            networkHitNormal = normal;
            networkHitDistance = distance;
            Clear();
        }

        private Vector3 networkHitPoint;
        private Vector3 networkHitNormal;
        private float networkHitDistance;

        public Vector3 HitPoint => HitResult.collider != null ? HitResult.point : networkHitPoint;
        public Vector3 HitNormal => HitResult.collider != null ? HitResult.normal : networkHitNormal;
        public float HitDistance => HitResult.collider != null ? HitResult.distance : networkHitDistance;

        public override void ReturnToPool()
        {
            Clear();
            HitResult = default;
            networkHitPoint = default;
            networkHitNormal = default;
            networkHitDistance = 0f;
            ClearPredictionStamp();
            pool.Push(this);
        }
    }

    /// <summary>
    /// This class defined as a data container for multiple actors
    /// found via non-physics or bulk-physics checks (like sphere overlap).
    /// </summary>
    public class GameplayAbilityTargetData_MultiTarget : GameplayAbilityTargetData_ActorArray
    {
        private static readonly ConcurrentStack<GameplayAbilityTargetData_MultiTarget> pool = new();

        public static GameplayAbilityTargetData_MultiTarget Get() => pool.TryPop(out var item) ? item : new GameplayAbilityTargetData_MultiTarget();

        public void Init(List<GameObject> targets)
        {
            Clear();
            AddTargets(targets);
        }

        public override void ReturnToPool()
        {
            Clear();
            ClearPredictionStamp();
            pool.Push(this);
        }
    }
}