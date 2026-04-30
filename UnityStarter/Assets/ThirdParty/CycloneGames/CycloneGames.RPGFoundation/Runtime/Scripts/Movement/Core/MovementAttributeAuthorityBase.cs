using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    public abstract class MovementAttributeAuthorityBase : MonoBehaviour, IMovementAuthority
    {
        [Header("Base Value Overrides")]
        [SerializeField] protected float? walkSpeedOverride;
        [SerializeField] protected float? runSpeedOverride;
        [SerializeField] protected float? sprintSpeedOverride;
        [SerializeField] protected float? crouchSpeedOverride;
        [SerializeField] protected float? jumpForceOverride;
        [SerializeField] protected float? maxJumpCountOverride;
        [SerializeField] protected float? gravityOverride;
        [SerializeField] protected float? airControlMultiplierOverride;
        [SerializeField] protected float? rotationSpeedOverride;
        [SerializeField] protected float? climbSpeedOverride;
        [SerializeField] protected float? swimSpeedOverride;
        [SerializeField] protected float? flySpeedOverride;

        [Header("Multipliers")]
        [SerializeField] protected float walkSpeedMultiplier = 1f;
        [SerializeField] protected float runSpeedMultiplier = 1f;
        [SerializeField] protected float sprintSpeedMultiplier = 1f;
        [SerializeField] protected float crouchSpeedMultiplier = 1f;
        [SerializeField] protected float jumpForceMultiplier = 1f;
        [SerializeField] protected float maxJumpCountMultiplier = 1f;
        [SerializeField] protected float gravityMultiplier = 1f;
        [SerializeField] protected float airControlMultiplier = 1f;
        [SerializeField] protected float rotationSpeedMultiplier = 1f;
        [SerializeField] protected float climbSpeedMultiplier = 1f;
        [SerializeField] protected float swimSpeedMultiplier = 1f;
        [SerializeField] protected float flySpeedMultiplier = 1f;

        protected Dictionary<MovementAttribute, float> _multipliers = new Dictionary<MovementAttribute, float>(16);

        protected virtual void Awake()
        {
            InitializeMultipliers();
        }

        protected virtual void InitializeMultipliers()
        {
            _multipliers[MovementAttribute.WalkSpeed] = walkSpeedMultiplier;
            _multipliers[MovementAttribute.RunSpeed] = runSpeedMultiplier;
            _multipliers[MovementAttribute.SprintSpeed] = sprintSpeedMultiplier;
            _multipliers[MovementAttribute.CrouchSpeed] = crouchSpeedMultiplier;
            _multipliers[MovementAttribute.JumpForce] = jumpForceMultiplier;
            _multipliers[MovementAttribute.MaxJumpCount] = maxJumpCountMultiplier;
            _multipliers[MovementAttribute.Gravity] = gravityMultiplier;
            _multipliers[MovementAttribute.AirControlMultiplier] = airControlMultiplier;
            _multipliers[MovementAttribute.RotationSpeed] = rotationSpeedMultiplier;
            _multipliers[MovementAttribute.ClimbSpeed] = climbSpeedMultiplier;
            _multipliers[MovementAttribute.SwimSpeed] = swimSpeedMultiplier;
            _multipliers[MovementAttribute.FlySpeed] = flySpeedMultiplier;
        }

        public virtual bool CanEnterState(MovementStateType stateType, object context = null)
        {
            return true;
        }

        public virtual void OnStateEntered(MovementStateType stateType) { }
        public virtual void OnStateExited(MovementStateType stateType) { }

        public virtual MovementAttributeModifier GetAttributeModifier(MovementAttribute attribute)
        {
            return new MovementAttributeModifier(GetBaseValue(attribute), GetMultiplier(attribute));
        }

        public virtual float? GetBaseValue(MovementAttribute attribute)
        {
            return attribute switch
            {
                MovementAttribute.WalkSpeed => walkSpeedOverride,
                MovementAttribute.RunSpeed => runSpeedOverride,
                MovementAttribute.SprintSpeed => sprintSpeedOverride,
                MovementAttribute.CrouchSpeed => crouchSpeedOverride,
                MovementAttribute.JumpForce => jumpForceOverride,
                MovementAttribute.MaxJumpCount => maxJumpCountOverride,
                MovementAttribute.Gravity => gravityOverride,
                MovementAttribute.AirControlMultiplier => airControlMultiplierOverride,
                MovementAttribute.RotationSpeed => rotationSpeedOverride,
                MovementAttribute.ClimbSpeed => climbSpeedOverride,
                MovementAttribute.SwimSpeed => swimSpeedOverride,
                MovementAttribute.FlySpeed => flySpeedOverride,
                _ => null
            };
        }

        public virtual float GetMultiplier(MovementAttribute attribute)
        {
            return _multipliers.TryGetValue(attribute, out float multiplier) ? multiplier : 1f;
        }

        public virtual float GetFinalValue(MovementAttribute attribute, float configValue)
        {
            float? baseOverride = GetBaseValue(attribute);
            float finalBase = baseOverride ?? configValue;
            return finalBase * GetMultiplier(attribute);
        }

        public virtual void SetBaseValueOverride(MovementAttribute attribute, float? value)
        {
            switch (attribute)
            {
                case MovementAttribute.WalkSpeed: walkSpeedOverride = value; break;
                case MovementAttribute.RunSpeed: runSpeedOverride = value; break;
                case MovementAttribute.SprintSpeed: sprintSpeedOverride = value; break;
                case MovementAttribute.CrouchSpeed: crouchSpeedOverride = value; break;
                case MovementAttribute.JumpForce: jumpForceOverride = value; break;
                case MovementAttribute.MaxJumpCount: maxJumpCountOverride = value; break;
                case MovementAttribute.Gravity: gravityOverride = value; break;
                case MovementAttribute.AirControlMultiplier: airControlMultiplierOverride = value; break;
                case MovementAttribute.RotationSpeed: rotationSpeedOverride = value; break;
                case MovementAttribute.ClimbSpeed: climbSpeedOverride = value; break;
                case MovementAttribute.SwimSpeed: swimSpeedOverride = value; break;
                case MovementAttribute.FlySpeed: flySpeedOverride = value; break;
            }
        }

        public virtual void SetMultiplier(MovementAttribute attribute, float multiplier)
        {
            _multipliers[attribute] = multiplier;
            SyncSerializedMultiplier(attribute, multiplier);
        }

        protected virtual void SyncSerializedMultiplier(MovementAttribute attribute, float multiplier)
        {
            switch (attribute)
            {
                case MovementAttribute.WalkSpeed: walkSpeedMultiplier = multiplier; break;
                case MovementAttribute.RunSpeed: runSpeedMultiplier = multiplier; break;
                case MovementAttribute.SprintSpeed: sprintSpeedMultiplier = multiplier; break;
                case MovementAttribute.CrouchSpeed: crouchSpeedMultiplier = multiplier; break;
                case MovementAttribute.JumpForce: jumpForceMultiplier = multiplier; break;
                case MovementAttribute.MaxJumpCount: maxJumpCountMultiplier = multiplier; break;
                case MovementAttribute.Gravity: gravityMultiplier = multiplier; break;
                case MovementAttribute.AirControlMultiplier: airControlMultiplier = multiplier; break;
                case MovementAttribute.RotationSpeed: rotationSpeedMultiplier = multiplier; break;
                case MovementAttribute.ClimbSpeed: climbSpeedMultiplier = multiplier; break;
                case MovementAttribute.SwimSpeed: swimSpeedMultiplier = multiplier; break;
                case MovementAttribute.FlySpeed: flySpeedMultiplier = multiplier; break;
            }
        }
    }
}
