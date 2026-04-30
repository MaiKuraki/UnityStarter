#if GAMEPLAY_ABILITIES_PRESENT
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    public class GASMovementAttributeAuthority : MovementAttributeAuthorityBase
    {
        [System.Serializable]
        public class AttributeMapping
        {
            public MovementAttribute attribute;
            public string gasAttributeTag;
            public bool useGASAttribute = true;
            public float baseValueForMultiplier = 100f;
        }

        [Header("GAS Attribute Mappings")]
        [SerializeField] private List<AttributeMapping> attributeMappings = new List<AttributeMapping>();

        [Header("State Gating - Stamina")]
        [Tooltip("Minimum stamina required to enter Sprint state.")]
        [SerializeField] private float minStaminaForSprint = 10f;

        private AbilitySystemComponent _asc;
        private Dictionary<MovementAttribute, float> _baseValues = new Dictionary<MovementAttribute, float>(16);

        protected override void Awake()
        {
            base.Awake();
            _asc = GetComponent<AbilitySystemComponent>();
            InitializeMappings();
        }

        private void Update()
        {
            if (_asc != null)
            {
                UpdateMultipliers();
            }
        }

        private void InitializeMappings()
        {
            if (attributeMappings == null || attributeMappings.Count == 0)
            {
                CreateDefaultMappings();
            }

            for (int i = 0; i < attributeMappings.Count; i++)
            {
                var mapping = attributeMappings[i];
                if (mapping.useGASAttribute && _asc != null)
                {
                    var gasAttr = _asc.GetAttribute(mapping.gasAttributeTag);
                    if (gasAttr != null)
                    {
                        _baseValues[mapping.attribute] = gasAttr.BaseValue > 0 ? gasAttr.BaseValue : mapping.baseValueForMultiplier;
                    }
                    else
                    {
                        _baseValues[mapping.attribute] = mapping.baseValueForMultiplier;
                    }
                }
                else
                {
                    _baseValues[mapping.attribute] = mapping.baseValueForMultiplier;
                }
                _multipliers[mapping.attribute] = 1f;
            }
        }

        private void CreateDefaultMappings()
        {
            attributeMappings = new List<AttributeMapping>
            {
                new AttributeMapping { attribute = MovementAttribute.WalkSpeed, gasAttributeTag = "Attribute.Secondary.Speed", baseValueForMultiplier = 100f },
                new AttributeMapping { attribute = MovementAttribute.RunSpeed, gasAttributeTag = "Attribute.Secondary.Speed", baseValueForMultiplier = 100f },
                new AttributeMapping { attribute = MovementAttribute.SprintSpeed, gasAttributeTag = "Attribute.Secondary.Speed", baseValueForMultiplier = 100f },
                new AttributeMapping { attribute = MovementAttribute.CrouchSpeed, gasAttributeTag = "Attribute.Secondary.Speed", baseValueForMultiplier = 100f },
                new AttributeMapping { attribute = MovementAttribute.JumpForce, gasAttributeTag = "Attribute.Secondary.JumpPower", baseValueForMultiplier = 100f },
                new AttributeMapping { attribute = MovementAttribute.MaxJumpCount, gasAttributeTag = "Attribute.Secondary.JumpCount", baseValueForMultiplier = 1f },
                new AttributeMapping { attribute = MovementAttribute.Gravity, gasAttributeTag = "Attribute.Secondary.Gravity", baseValueForMultiplier = 100f },
                new AttributeMapping { attribute = MovementAttribute.AirControlMultiplier, gasAttributeTag = "Attribute.Secondary.AirControl", baseValueForMultiplier = 100f },
                new AttributeMapping { attribute = MovementAttribute.RotationSpeed, gasAttributeTag = "Attribute.Secondary.RotationSpeed", baseValueForMultiplier = 100f },
                new AttributeMapping { attribute = MovementAttribute.ClimbSpeed, gasAttributeTag = "Attribute.Secondary.ClimbSpeed", baseValueForMultiplier = 100f },
                new AttributeMapping { attribute = MovementAttribute.SwimSpeed, gasAttributeTag = "Attribute.Secondary.SwimSpeed", baseValueForMultiplier = 100f },
                new AttributeMapping { attribute = MovementAttribute.FlySpeed, gasAttributeTag = "Attribute.Secondary.FlySpeed", baseValueForMultiplier = 100f }
            };
        }

        private void UpdateMultipliers()
        {
            for (int i = 0; i < attributeMappings.Count; i++)
            {
                var mapping = attributeMappings[i];
                if (!mapping.useGASAttribute) continue;

                var gasAttr = _asc.GetAttribute(mapping.gasAttributeTag);
                if (gasAttr != null && _baseValues.TryGetValue(mapping.attribute, out float baseValue) && baseValue > 0)
                {
                    _multipliers[mapping.attribute] = gasAttr.CurrentValue / baseValue;
                }
                else
                {
                    _multipliers[mapping.attribute] = 1f;
                }
            }
        }

        public override bool CanEnterState(MovementStateType stateType, object context = null)
        {
            if (_asc == null) return true;

            switch (stateType)
            {
                case MovementStateType.Sprint:
                    var staminaAttr = _asc.GetAttribute("Attribute.Primary.Stamina");
                    if (staminaAttr != null && staminaAttr.CurrentValue < minStaminaForSprint)
                        return false;
                    return !_asc.HasMatchingTag(GameplayTag.FromString("State.Debuff.CantSprint"));

                case MovementStateType.Jump:
                    return !_asc.HasMatchingTag(GameplayTag.FromString("State.Cooldown.Jump"));

                case MovementStateType.Roll:
                    return !_asc.HasMatchingTag(GameplayTag.FromString("State.Cooldown.Roll"));

                case MovementStateType.Climb:
                    return !_asc.HasMatchingTag(GameplayTag.FromString("State.Debuff.CantClimb"));

                case MovementStateType.Crouch:
                    return _asc.HasMatchingTag(GameplayTag.FromString("State.Buff.CanCrouch"));

                default:
                    return true;
            }
        }

        public override void SetMultiplier(MovementAttribute attribute, float multiplier)
        {
            _multipliers[attribute] = multiplier;
        }

        public void AddAttributeMapping(MovementAttribute attribute, string gasTag, float baseValue = 100f)
        {
            if (attributeMappings == null)
            {
                attributeMappings = new List<AttributeMapping>();
            }

            for (int i = 0; i < attributeMappings.Count; i++)
            {
                if (attributeMappings[i].attribute == attribute)
                {
                    attributeMappings[i].gasAttributeTag = gasTag;
                    attributeMappings[i].baseValueForMultiplier = baseValue;
                    attributeMappings[i].useGASAttribute = true;
                    _baseValues[attribute] = baseValue;
                    _multipliers[attribute] = 1f;
                    return;
                }
            }

            attributeMappings.Add(new AttributeMapping
            {
                attribute = attribute,
                gasAttributeTag = gasTag,
                baseValueForMultiplier = baseValue,
                useGASAttribute = true
            });

            _baseValues[attribute] = baseValue;
            _multipliers[attribute] = 1f;
        }

        public float GetBaseValueForMultiplier(MovementAttribute attribute)
        {
            return _baseValues.TryGetValue(attribute, out float value) ? value : 100f;
        }

        public void SetBaseValueForMultiplier(MovementAttribute attribute, float baseValue)
        {
            _baseValues[attribute] = baseValue;
            UpdateMultipliers();
        }
    }
}
#endif
