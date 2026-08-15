using System;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Defines a form of damage. Implementations are treated as immutable data holders.
    ///
    /// Usage patterns:
    /// 1. Simple: Use the DamageType ScriptableObject from the CycloneGames/GameplayFramework/DamageType asset menu.
    /// 2. GameplayTags: Implement IDamageType with a tag-based adapter carrying GameplayTagContainer.
    /// 3. GameplayAbilities: Use an integration adapter that captures stable definition IDs and immutable damage metadata.
    /// 4. Custom: Implement IDamageType with any project-specific damage metadata.
    /// </summary>
    public interface IDamageType
    {
        /// <summary>
        /// Whether this damage is caused by the world (falling, lava, environmental hazards).
        /// </summary>
        bool CausedByWorld { get; }

        /// <summary>
        /// Whether to scale imparted momentum by the receiving pawn's mass.
        /// </summary>
        bool ScaleMomentumByMass { get; }

        /// <summary>
        /// Impulse magnitude to apply to damaged actors' rigidbodies.
        /// </summary>
        float DamageImpulse { get; }

        /// <summary>
        /// Damage falloff exponent for radial damage. 1.0 = linear, 2.0 = quadratic.
        /// </summary>
        float DamageFalloff { get; }
    }

    /// <summary>
    /// Describes the category of a damage event.
    /// </summary>
    public enum EDamageEventType : byte
    {
        /// <summary>Generic unspecified damage.</summary>
        Generic,
        /// <summary>Damage originating from a single point with hit info.</summary>
        Point,
        /// <summary>Damage originating from a radial explosion.</summary>
        Radial
    }

    /// <summary>
    /// Result of validating a <see cref="DamageEvent"/> value.
    /// </summary>
    public enum DamageEventValidationResult : byte
    {
        Valid = 0,
        Uninitialized = 1,
        UnknownEventType = 2,
        NonFinitePointGeometry = 3,
        NonFiniteRadialOrigin = 4,
        InvalidRadialRadii = 5,
    }

    /// <summary>
    /// Immutable, zero-allocation damage event value. Instances are created through the typed
    /// factories so event-specific geometry cannot be published in an invalid state.
    ///
    /// GameplayAbilities integrations must copy stable IDs or an immutable snapshot while a GAS
    /// callback is valid. Do not store a GameplayEffectSpec or GameplayEffectContext reference here.
    /// </summary>
    public readonly struct DamageEvent
    {
        private const byte InitializationMarker = 1;

        private readonly EDamageEventType eventType;
        private readonly IDamageType damageType;
        private readonly Vector3 hitLocation;
        private readonly Vector3 hitNormal;
        private readonly Vector3 shotDirection;
        private readonly Vector3 origin;
        private readonly float innerRadius;
        private readonly float outerRadius;
        private readonly byte initializationMarker;

        private DamageEvent(
            EDamageEventType eventType,
            IDamageType damageType,
            Vector3 hitLocation,
            Vector3 hitNormal,
            Vector3 shotDirection,
            Vector3 origin,
            float innerRadius,
            float outerRadius)
        {
            this.eventType = eventType;
            this.damageType = damageType;
            this.hitLocation = hitLocation;
            this.hitNormal = hitNormal;
            this.shotDirection = shotDirection;
            this.origin = origin;
            this.innerRadius = innerRadius;
            this.outerRadius = outerRadius;
            initializationMarker = InitializationMarker;
        }

        /// <summary>The type of damage event.</summary>
        public EDamageEventType EventType => eventType;

        /// <summary>The damage type definition. Can be null for typeless damage.</summary>
        public IDamageType DamageType => damageType;

        /// <summary>World-space location of the hit for point damage.</summary>
        public Vector3 HitLocation => hitLocation;

        /// <summary>Surface normal at the hit point for point damage.</summary>
        public Vector3 HitNormal => hitNormal;

        /// <summary>Direction of the shot or projectile for point damage.</summary>
        public Vector3 ShotDirection => shotDirection;

        /// <summary>Origin of radial damage.</summary>
        public Vector3 Origin => origin;

        /// <summary>Inner radius that receives full radial damage.</summary>
        public float InnerRadius => innerRadius;

        /// <summary>Outer radius where radial damage reaches its minimum.</summary>
        public float OuterRadius => outerRadius;

        /// <summary>Creates a generic damage event with an optional damage type.</summary>
        public static DamageEvent MakeGenericDamage(IDamageType damageType = null)
        {
            return new DamageEvent(
                EDamageEventType.Generic,
                damageType,
                default,
                default,
                default,
                default,
                0f,
                0f);
        }

        /// <summary>Creates a point damage event with hit information.</summary>
        public static DamageEvent MakePointDamage(Vector3 hitLocation, Vector3 hitNormal, Vector3 shotDirection, IDamageType damageType = null)
        {
            var damageEvent = new DamageEvent(
                EDamageEventType.Point,
                damageType,
                hitLocation,
                hitNormal,
                shotDirection,
                default,
                0f,
                0f);
            if (damageEvent.Validate() != DamageEventValidationResult.Valid)
            {
                throw new ArgumentException("Point damage vectors must contain finite values.");
            }

            return damageEvent;
        }

        /// <summary>Creates a radial damage event with explosion parameters.</summary>
        public static DamageEvent MakeRadialDamage(Vector3 origin, float innerRadius, float outerRadius, IDamageType damageType = null)
        {
            var damageEvent = new DamageEvent(
                EDamageEventType.Radial,
                damageType,
                default,
                default,
                default,
                origin,
                innerRadius,
                outerRadius);
            if (damageEvent.Validate() != DamageEventValidationResult.Valid)
            {
                throw new ArgumentException(
                    "Radial damage requires a finite origin and 0 <= innerRadius <= outerRadius.");
            }

            return damageEvent;
        }

        /// <summary>
        /// Validates this value without allocating. This ingress check also protects against
        /// default values and data populated by binary or network serializers outside the factories.
        /// </summary>
        public DamageEventValidationResult Validate()
        {
            if (initializationMarker != InitializationMarker)
            {
                return DamageEventValidationResult.Uninitialized;
            }

            switch (eventType)
            {
                case EDamageEventType.Generic:
                    return DamageEventValidationResult.Valid;
                case EDamageEventType.Point:
                    return IsFinite(hitLocation) &&
                           IsFinite(hitNormal) &&
                           IsFinite(shotDirection)
                        ? DamageEventValidationResult.Valid
                        : DamageEventValidationResult.NonFinitePointGeometry;
                case EDamageEventType.Radial:
                    if (!IsFinite(origin))
                    {
                        return DamageEventValidationResult.NonFiniteRadialOrigin;
                    }

                    return IsFinite(innerRadius) &&
                           IsFinite(outerRadius) &&
                           innerRadius >= 0f &&
                           outerRadius >= innerRadius
                        ? DamageEventValidationResult.Valid
                        : DamageEventValidationResult.InvalidRadialRadii;
                default:
                    return DamageEventValidationResult.UnknownEventType;
            }
        }

        private static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
                   !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
                   !float.IsNaN(value.z) && !float.IsInfinity(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    /// <summary>
    /// Default implementation of IDamageType as a ScriptableObject.
    /// For GameplayAbilities/GameplayTags integration, implement IDamageType in an adapter class
    /// that carries immutable tag data, stable definition IDs, or another independently owned snapshot.
    /// </summary>
    [CreateAssetMenu(
        fileName = "NewDamageType",
        menuName = "CycloneGames/GameplayFramework/DamageType")]
    public class DamageType : ScriptableObject, IDamageType
    {
        [Tooltip("Whether this damage is caused by the world (falling, lava, etc.)")]
        [SerializeField] private bool causedByWorld;

        [Tooltip("Whether to scale impulse by the receiving pawn's mass")]
        [SerializeField] private bool scaleMomentumByMass = true;

        [Tooltip("Impulse magnitude to apply to damaged actors")]
        [SerializeField] private float damageImpulse = 800f;

        [Tooltip("Radial damage falloff exponent. 1.0 = linear, 2.0 = quadratic")]
        [SerializeField, Range(0f, 10f)] private float damageFalloff = 1f;

        public bool CausedByWorld => causedByWorld;
        public bool ScaleMomentumByMass => scaleMomentumByMass;
        public float DamageImpulse => damageImpulse;
        public float DamageFalloff => damageFalloff;
    }
}
