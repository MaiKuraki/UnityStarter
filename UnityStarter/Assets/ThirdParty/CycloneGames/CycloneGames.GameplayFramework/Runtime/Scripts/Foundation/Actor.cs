using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Burst;
using Unity.Mathematics;

namespace CycloneGames.GameplayFramework.Runtime
{
    public class Actor : MonoBehaviour
    {
        [SerializeField] private float initialLifeSpanSec;
        [SerializeField] private bool bCanBeDamaged = true;
        [SerializeField] private bool bHidden;
        [SerializeField, ActorTag] private List<string> tags;

        public event Action<Actor> OnDestroyed;
        public event Action OwnerChanged;
        public event Action<float, DamageEvent, Controller, Actor> OnTakePointDamage;
        public event Action<float, DamageEvent, Controller, Actor> OnTakeRadialDamage;

        private Actor owner;
        private Actor instigator;
        private string actorName;
        private bool hasBegunPlay;

        // Pre-allocated list for SetActorHiddenInGame to avoid GC
        private static readonly List<Renderer> s_rendererBuffer = new List<Renderer>(16);

        #region Owner
        public Actor GetOwner() => owner;
        public T GetOwner<T>() where T : Actor => owner as T;
        public void SetOwner(Actor NewOwner)
        {
            if (ReferenceEquals(owner, NewOwner)) return;
            owner = NewOwner;
            OwnerChanged?.Invoke();
        }
        #endregion

        #region Instigator
        public Actor GetInstigator() => instigator;
        public T GetInstigator<T>() where T : Actor => instigator as T;
        public void SetInstigator(Actor NewInstigator) => instigator = NewInstigator;
        #endregion

        #region Name
        public string GetName() => actorName;
        #endregion

        #region Transform
        public Vector3 GetActorLocation() => transform.position;
        public Quaternion GetActorRotation() => transform.rotation;
        public Vector3 GetActorScale() => transform.localScale;
        public float GetYaw() => transform.eulerAngles.y;
        public Vector3 GetActorForwardVector() => transform.forward;
        public Vector3 GetActorRightVector() => transform.right;
        public Vector3 GetActorUpVector() => transform.up;

        public void SetActorLocation(Vector3 NewLocation) => transform.position = NewLocation;
        public void SetActorRotation(Quaternion NewRotation) => transform.rotation = NewRotation;
        public void SetActorScale(Vector3 NewScale) => transform.localScale = NewScale;
        public void SetActorLocationAndRotation(Vector3 NewLocation, Quaternion NewRotation)
        {
            transform.SetPositionAndRotation(NewLocation, NewRotation);
        }
        #endregion

        #region Camera
        public virtual void GetActorEyesViewPoint(out Vector3 outLocation, out Quaternion outRotation)
        {
            outLocation = GetActorLocation();
            outRotation = GetActorRotation();
        }

        public virtual void CalcCamera(float deltaTime, out CameraPose outResult, float fallbackFov)
        {
            GetActorEyesViewPoint(out Vector3 location, out Quaternion rotation);
            outResult = new CameraPose(location, rotation, fallbackFov);
        }
        #endregion

        #region Visibility
        public bool IsHidden() => bHidden;
        public virtual void SetActorHiddenInGame(bool bNewHidden)
        {
            ApplyActorHiddenInGame(bNewHidden, false);
        }

        private void ApplyActorHiddenInGame(bool bNewHidden, bool forceRendererSync)
        {
            if (bHidden == bNewHidden && !forceRendererSync) return;
            bHidden = bNewHidden;
            s_rendererBuffer.Clear();
            GetComponentsInChildren(true, s_rendererBuffer);
            for (int i = 0; i < s_rendererBuffer.Count; i++)
            {
                s_rendererBuffer[i].enabled = !bHidden;
            }
            s_rendererBuffer.Clear();
        }
        #endregion

        #region Tags
        public bool ActorHasTag(string tag)
        {
            if (tags == null) return false;
            for (int i = 0; i < tags.Count; i++)
            {
                if (string.Equals(tags[i], tag, StringComparison.Ordinal)) return true;
            }
            return false;
        }
        public void AddTag(string tag)
        {
            tags ??= new List<string>(4);
            if (!ActorHasTag(tag)) tags.Add(tag);
        }
        public void RemoveTag(string tag) => tags?.Remove(tag);
        public IReadOnlyList<string> GetTags() => tags;
        #endregion

        #region Damage
        public bool CanBeDamaged() => bCanBeDamaged;
        public void SetCanBeDamaged(bool bValue) => bCanBeDamaged = bValue;

        /// <summary>
        /// Simple damage entry point for typeless damage. Delegates to the DamageEvent overload.
        /// </summary>
        public virtual float TakeDamage(float DamageAmount, Controller EventInstigator = null, Actor DamageCauser = null)
        {
            return TakeDamage(DamageAmount, DamageEvent.MakeGenericDamage(), EventInstigator, DamageCauser);
        }

        /// <summary>
        /// Extended damage entry point with full DamageEvent context.
        /// Routes to ReceivePointDamage/ReceiveRadialDamage based on event type,
        /// then always calls ReceiveAnyDamage.
        /// </summary>
        public virtual float TakeDamage(float DamageAmount, DamageEvent damageEvent, Controller EventInstigator = null, Actor DamageCauser = null)
        {
            if (!bCanBeDamaged) return 0f;
            float ActualDamage = InternalTakeDamage(DamageAmount, EventInstigator, DamageCauser);

            switch (damageEvent.EventType)
            {
                case EDamageEventType.Point:
                    ReceivePointDamage(ActualDamage, damageEvent, EventInstigator, DamageCauser);
                    OnTakePointDamage?.Invoke(ActualDamage, damageEvent, EventInstigator, DamageCauser);
                    break;
                case EDamageEventType.Radial:
                    ReceiveRadialDamage(ActualDamage, damageEvent, EventInstigator, DamageCauser);
                    OnTakeRadialDamage?.Invoke(ActualDamage, damageEvent, EventInstigator, DamageCauser);
                    break;
            }

            ReceiveAnyDamage(ActualDamage, EventInstigator, DamageCauser);
            return ActualDamage;
        }

        protected virtual float InternalTakeDamage(float DamageAmount, Controller EventInstigator, Actor DamageCauser)
        {
            return DamageAmount;
        }

        protected virtual void ReceiveAnyDamage(float Damage, Controller EventInstigator, Actor DamageCauser) { }
        protected virtual void ReceivePointDamage(float Damage, DamageEvent damageEvent, Controller EventInstigator, Actor DamageCauser) { }
        protected virtual void ReceiveRadialDamage(float Damage, DamageEvent damageEvent, Controller EventInstigator, Actor DamageCauser) { }
        #endregion

        #region Orientation
        public Vector3 GetOrientation()
        {
            float3 result = QuaternionToEulerXYZBurst(new quaternion(
                transform.rotation.x, transform.rotation.y, transform.rotation.z, transform.rotation.w));
            return new Vector3(result.x, result.y, result.z);
        }

        // Backward-compatible wrapper for managed callers
        public static Vector3 QuaternionToEulerXYZ(Quaternion rotation)
        {
            float3 result = QuaternionToEulerXYZBurst(new quaternion(rotation.x, rotation.y, rotation.z, rotation.w));
            return new Vector3(result.x, result.y, result.z);
        }

        // Burst direct-call optimized path using pure Mathematics types
        [BurstCompile]
        public static float3 QuaternionToEulerXYZBurst(in quaternion q)
        {
            float pitch = math.degrees(math.atan2(
                2f * q.value.x * q.value.w - 2f * q.value.y * q.value.z,
                1f - 2f * q.value.x * q.value.x - 2f * q.value.z * q.value.z));
            float yaw = math.degrees(math.atan2(
                2f * q.value.y * q.value.w - 2f * q.value.x * q.value.z,
                1f - 2f * q.value.y * q.value.y - 2f * q.value.z * q.value.z));
            float roll = math.degrees(math.asin(math.clamp(
                2f * q.value.x * q.value.y + 2f * q.value.z * q.value.w, -1f, 1f)));
            return new float3(pitch, yaw, roll);
        }
        #endregion

        #region Lifespan
        public float GetLifeSpan() => initialLifeSpanSec;
        public void SetLifeSpan(float newLifeSpan)
        {
            initialLifeSpanSec = newLifeSpan;
            if (initialLifeSpanSec > 0.001f)
            {
                Destroy(gameObject, initialLifeSpanSec);
            }
        }
        #endregion

        #region World Bounds
        public virtual void FellOutOfWorld() => Destroy(gameObject);
        public virtual void OutsideWorldBounds() { }
        #endregion

        #region Lifecycle
        protected virtual void Awake()
        {
            actorName = gameObject.name;
        }

        protected virtual void Start()
        {
            if (initialLifeSpanSec > 0.001f)
            {
                SetLifeSpan(initialLifeSpanSec);
            }
            if (!hasBegunPlay)
            {
                hasBegunPlay = true;
                BeginPlay();
            }
        }

        /// <summary>
        /// Called once after Start when this actor first enters play.
        /// </summary>
        protected virtual void BeginPlay() { }

        protected virtual void Update() { }
        protected virtual void LateUpdate() { }
        protected virtual void FixedUpdate() { }

        protected virtual void OnDestroy()
        {
            if (hasBegunPlay)
            {
                EndPlay();
                hasBegunPlay = false;
            }
            OnDestroyed?.Invoke(this);
            OnDestroyed = null;
            OnTakePointDamage = null;
            OnTakeRadialDamage = null;
            OwnerChanged = null;
            owner = null;
            instigator = null;
        }

        /// <summary>
        /// Called when this actor is being destroyed or removed from play.
        /// </summary>
        protected virtual void EndPlay() { }
        #endregion

        #region Network Extensibility
        /// <summary>
        /// Override in network layer to return true only on the authoritative instance.
        /// Default returns true (standalone/single-player).
        /// </summary>
        public virtual bool HasAuthority() => true;
        #endregion

        #region Migration
        /// <summary>
        /// Returns the prefab asset path used to re-instantiate this actor on a
        /// target process during migration. Override for dynamic or pooled actors.
        /// </summary>
        /// <remarks>
        /// The default returns <c>gameObject.name</c>. Override to return a stable
        /// asset path (e.g., "Prefabs/Characters/PlayerPawn") for production use.
        /// </remarks>
        public virtual string GetMigrationPrefabAssetPath() => gameObject.name;

        /// <summary>
        /// Captures all mutable state needed to reconstruct this actor on another process.
        /// Reference-typed fields (owner, instigator) are stored as integer identifiers
        /// because UnityEngine.Object references are not valid across processes.
        /// </summary>
        /// <param name="ownerConnectionId">
        /// The network connection ID to use for the owner field in the migration state.
        /// Callers must resolve the owner Actor reference to a connection ID before calling.
        /// </param>
        /// <param name="instigatorActorId">
        /// The globally unique actor ID to use for the instigator field, or 0 if none.
        /// </param>
        public virtual ActorMigrationState CaptureMigrationState(int ownerConnectionId, int instigatorActorId)
        {
            string[] tagCopy = tags != null && tags.Count > 0
                ? tags.ToArray()
                : System.Array.Empty<string>();

            return new ActorMigrationState(
                position: transform.position,
                rotation: transform.rotation,
                scale: transform.localScale,
                prefabAssetPath: GetMigrationPrefabAssetPath(),
                remainingLifeSpan: initialLifeSpanSec,
                canBeDamaged: bCanBeDamaged,
                hidden: bHidden,
                tags: tagCopy,
                ownerConnectionId: ownerConnectionId,
                instigatorActorId: instigatorActorId,
                actorName: actorName,
                hasBegunPlay: hasBegunPlay
            );
        }

        /// <summary>
        /// Restores actor state from a migration snapshot received from another process.
        /// Override to restore subclass-specific state, always calling the base implementation.
        /// </summary>
        public virtual void RestoreMigrationState(in ActorMigrationState state)
        {
            transform.SetPositionAndRotation(state.Position, state.Rotation);
            transform.localScale = state.Scale;
            initialLifeSpanSec = state.RemainingLifeSpan;
            bCanBeDamaged = state.CanBeDamaged;
            actorName = state.ActorName;

            tags?.Clear();
            if (state.Tags != null && state.Tags.Length > 0)
            {
                tags ??= new System.Collections.Generic.List<string>(state.Tags.Length);
                for (int i = 0; i < state.Tags.Length; i++)
                {
                    tags.Add(state.Tags[i]);
                }
            }

            ApplyActorHiddenInGame(state.Hidden, true);

            if (state.RemainingLifeSpan > 0.001f && hasBegunPlay)
            {
                Destroy(gameObject, state.RemainingLifeSpan);
            }
        }

        /// <summary>
        /// Called on the source process immediately before migration transfer.
        /// Override to detach references, stop behavior trees, or serialize custom state.
        /// Default implementation is a no-op.
        /// </summary>
        public virtual void PreMigration() { }

        /// <summary>
        /// Called on the target process after <see cref="RestoreMigrationState"/>
        /// completes. Override to reinitialize subsystems, restart behavior trees,
        /// or re-establish runtime references. Always call base.
        /// </summary>
        public virtual void PostMigration()
        {
            if (!hasBegunPlay)
            {
                hasBegunPlay = true;
                BeginPlay();
            }
        }
        #endregion
    }
}
