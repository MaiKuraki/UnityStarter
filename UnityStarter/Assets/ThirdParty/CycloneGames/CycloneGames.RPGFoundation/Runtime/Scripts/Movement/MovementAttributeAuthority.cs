using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Simple movement authority that provides attribute modifiers and base value overrides.
    /// Does not require GAS. Use for runtime attribute modification without Gameplay Ability System.
    /// </summary>
    public class MovementAttributeAuthority : MonoBehaviour, IMovementAuthority
    {
        [Header("Base Value Overrides")]
        [SerializeField] private float? walkSpeedOverride;
        [SerializeField] private float? runSpeedOverride;
        [SerializeField] private float? sprintSpeedOverride;
        [SerializeField] private float? crouchSpeedOverride;
        [SerializeField] private float? jumpForceOverride;
        [SerializeField] private float? gravityOverride;
        [SerializeField] private float? airControlMultiplierOverride;
        [SerializeField] private float? rotationSpeedOverride;

        [Header("Multipliers")]
        [SerializeField] private float walkSpeedMultiplier = 1f;
        [SerializeField] private float runSpeedMultiplier = 1f;
        [SerializeField] private float sprintSpeedMultiplier = 1f;
        [SerializeField] private float crouchSpeedMultiplier = 1f;
        [SerializeField] private float jumpForceMultiplier = 1f;
        [SerializeField] private float gravityMultiplier = 1f;
        [SerializeField] private float airControlMultiplier = 1f;
        [SerializeField] private float rotationSpeedMultiplier = 1f;

        private Dictionary<MovementAttribute, float> _multipliers = new Dictionary<MovementAttribute, float>();

        void Awake()
        {
            InitializeMultipliers();
        }

        private void InitializeMultipliers()
        {
            _multipliers[MovementAttribute.WalkSpeed] = walkSpeedMultiplier;
            _multipliers[MovementAttribute.RunSpeed] = runSpeedMultiplier;
            _multipliers[MovementAttribute.SprintSpeed] = sprintSpeedMultiplier;
            _multipliers[MovementAttribute.CrouchSpeed] = crouchSpeedMultiplier;
            _multipliers[MovementAttribute.JumpForce] = jumpForceMultiplier;
            _multipliers[MovementAttribute.Gravity] = gravityMultiplier;
            _multipliers[MovementAttribute.AirControlMultiplier] = airControlMultiplier;
            _multipliers[MovementAttribute.RotationSpeed] = rotationSpeedMultiplier;
        }

        public bool CanEnterState(MovementStateType stateType, object context = null)
        {
            return true;
        }

        public void OnStateEntered(MovementStateType stateType)
        {
        }

        public void OnStateExited(MovementStateType stateType)
        {
        }

        public MovementAttributeModifier GetAttributeModifier(MovementAttribute attribute)
        {
            return new MovementAttributeModifier(GetBaseValue(attribute), GetMultiplier(attribute));
        }

        public float? GetBaseValue(MovementAttribute attribute)
        {
            return attribute switch
            {
                MovementAttribute.WalkSpeed => walkSpeedOverride,
                MovementAttribute.RunSpeed => runSpeedOverride,
                MovementAttribute.SprintSpeed => sprintSpeedOverride,
                MovementAttribute.CrouchSpeed => crouchSpeedOverride,
                MovementAttribute.JumpForce => jumpForceOverride,
                MovementAttribute.Gravity => gravityOverride,
                MovementAttribute.AirControlMultiplier => airControlMultiplierOverride,
                MovementAttribute.RotationSpeed => rotationSpeedOverride,
                _ => null
            };
        }

        public float GetMultiplier(MovementAttribute attribute)
        {
            return _multipliers.TryGetValue(attribute, out float multiplier) ? multiplier : 1f;
        }

        public float GetFinalValue(MovementAttribute attribute, float configValue)
        {
            float? baseOverride = GetBaseValue(attribute);
            float finalBase = baseOverride ?? configValue;
            return finalBase * GetMultiplier(attribute);
        }

        /// <summary>
        /// Sets base value override for an attribute. Set to null to use config value.
        /// </summary>
        public void SetBaseValueOverride(MovementAttribute attribute, float? value)
        {
            switch (attribute)
            {
                case MovementAttribute.WalkSpeed:
                    walkSpeedOverride = value;
                    break;
                case MovementAttribute.RunSpeed:
                    runSpeedOverride = value;
                    break;
                case MovementAttribute.SprintSpeed:
                    sprintSpeedOverride = value;
                    break;
                case MovementAttribute.CrouchSpeed:
                    crouchSpeedOverride = value;
                    break;
                case MovementAttribute.JumpForce:
                    jumpForceOverride = value;
                    break;
                case MovementAttribute.Gravity:
                    gravityOverride = value;
                    break;
                case MovementAttribute.AirControlMultiplier:
                    airControlMultiplierOverride = value;
                    break;
                case MovementAttribute.RotationSpeed:
                    rotationSpeedOverride = value;
                    break;
            }
        }

        /// <summary>
        /// Sets multiplier for an attribute.
        /// </summary>
        public void SetMultiplier(MovementAttribute attribute, float multiplier)
        {
            _multipliers[attribute] = multiplier;

            switch (attribute)
            {
                case MovementAttribute.WalkSpeed:
                    walkSpeedMultiplier = multiplier;
                    break;
                case MovementAttribute.RunSpeed:
                    runSpeedMultiplier = multiplier;
                    break;
                case MovementAttribute.SprintSpeed:
                    sprintSpeedMultiplier = multiplier;
                    break;
                case MovementAttribute.CrouchSpeed:
                    crouchSpeedMultiplier = multiplier;
                    break;
                case MovementAttribute.JumpForce:
                    jumpForceMultiplier = multiplier;
                    break;
                case MovementAttribute.Gravity:
                    gravityMultiplier = multiplier;
                    break;
                case MovementAttribute.AirControlMultiplier:
                    airControlMultiplier = multiplier;
                    break;
                case MovementAttribute.RotationSpeed:
                    rotationSpeedMultiplier = multiplier;
                    break;
            }
        }
    }
}