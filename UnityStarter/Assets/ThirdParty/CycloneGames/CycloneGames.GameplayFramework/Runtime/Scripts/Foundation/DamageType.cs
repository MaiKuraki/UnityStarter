using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Defines a form of damage. Implementations are treated as immutable data holders.
    ///
    /// Usage patterns:
    /// 1. Simple: Use the DamageType ScriptableObject directly (Create → CycloneGames → GameplayFramework → DamageType).
    /// 2. GameplayTags: Implement IDamageType with a tag-based adapter carrying GameplayTagContainer.
    /// 3. GameplayAbilities: Wrap GameplayEffectSpec context in an IDamageType adapter to bridge GAS damage.
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
    /// Lightweight damage event data. Zero-allocation value type carrying all damage context.
    /// Combines generic, point, and radial damage info in a single struct.
    ///
    /// For GameplayAbilities integration, set EffectContext to the GameplayEffectSpec or
    /// IGameplayEffectContext instance so downstream handlers can access GAS instigator data.
    /// </summary>
    public struct DamageEvent
    {
        /// <summary>The type of damage event.</summary>
        public EDamageEventType EventType;

        /// <summary>The damage type definition. Can be null for typeless damage.</summary>
        public IDamageType DamageType;

        /// <summary>
        /// Optional opaque context for external systems (GameplayAbilities, custom damage systems).
        /// When used with GAS, this should be the GameplayEffectSpec or IGameplayEffectContext
        /// so receivers can access the source AbilitySystemComponent instigator.
        /// </summary>
        public object EffectContext;

        // --- Point Damage Fields ---

        /// <summary>World-space location of the hit (Point damage).</summary>
        public Vector3 HitLocation;
        /// <summary>Surface normal at the hit point (Point damage).</summary>
        public Vector3 HitNormal;
        /// <summary>Direction of the shot/projectile (Point damage).</summary>
        public Vector3 ShotDirection;

        // --- Radial Damage Fields ---

        /// <summary>Origin of the radial damage (Radial damage).</summary>
        public Vector3 Origin;
        /// <summary>Inner radius for full damage (Radial damage).</summary>
        public float InnerRadius;
        /// <summary>Outer radius where damage falls to minimum (Radial damage).</summary>
        public float OuterRadius;

        /// <summary>Creates a generic damage event with an optional damage type.</summary>
        public static DamageEvent MakeGenericDamage(IDamageType damageType = null)
        {
            return new DamageEvent { EventType = EDamageEventType.Generic, DamageType = damageType };
        }

        /// <summary>Creates a point damage event with hit information.</summary>
        public static DamageEvent MakePointDamage(Vector3 hitLocation, Vector3 hitNormal, Vector3 shotDirection, IDamageType damageType = null)
        {
            return new DamageEvent
            {
                EventType = EDamageEventType.Point,
                DamageType = damageType,
                HitLocation = hitLocation,
                HitNormal = hitNormal,
                ShotDirection = shotDirection
            };
        }

        /// <summary>Creates a radial damage event with explosion parameters.</summary>
        public static DamageEvent MakeRadialDamage(Vector3 origin, float innerRadius, float outerRadius, IDamageType damageType = null)
        {
            return new DamageEvent
            {
                EventType = EDamageEventType.Radial,
                DamageType = damageType,
                Origin = origin,
                InnerRadius = innerRadius,
                OuterRadius = outerRadius
            };
        }
    }

    /// <summary>
    /// Default implementation of IDamageType as a ScriptableObject.
    /// For GameplayAbilities/GameplayTags integration, implement IDamageType in an adapter class
    /// that carries GameplayTagContainer or GameplayEffectSpec context.
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
