#if GAMEPLAY_ABILITIES_PRESENT
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// GAS-integrated movement authority that provides attribute modifiers and base value overrides.
    /// Supports initialization from GAS attributes and runtime modification through GameplayEffects.
    /// Requires GAMEPLAY_ABILITIES_PRESENT define symbol.
    /// </summary>
    public class GASMovementAttributeAuthority : MonoBehaviour, IMovementAuthority
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
        
        [Header("Base Value Overrides")]
        [SerializeField] private float? walkSpeedOverride;
        [SerializeField] private float? runSpeedOverride;
        [SerializeField] private float? sprintSpeedOverride;
        [SerializeField] private float? crouchSpeedOverride;
        [SerializeField] private float? jumpForceOverride;
        [SerializeField] private float? gravityOverride;
        [SerializeField] private float? airControlMultiplierOverride;
        [SerializeField] private float? rotationSpeedOverride;
        
        private AbilitySystemComponent _asc;
        private Dictionary<MovementAttribute, float> _multipliers = new Dictionary<MovementAttribute, float>();
        private Dictionary<MovementAttribute, float> _baseValues = new Dictionary<MovementAttribute, float>();
        
        void Awake()
        {
            _asc = GetComponent<AbilitySystemComponent>();
            InitializeMappings();
        }
        
        void Update()
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
            
            foreach (var mapping in attributeMappings)
            {
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
                new AttributeMapping { attribute = MovementAttribute.RunSpeed, gasAttributeTag = "Attribute.Secondary.Speed", baseValueForMultiplier = 100f },
                new AttributeMapping { attribute = MovementAttribute.JumpForce, gasAttributeTag = "Attribute.Secondary.JumpPower", baseValueForMultiplier = 100f }
            };
        }
        
        private void UpdateMultipliers()
        {
            foreach (var mapping in attributeMappings)
            {
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
            float? baseOverride = GetBaseValue(attribute);
            float multiplier = GetMultiplier(attribute);
            return new MovementAttributeModifier(baseOverride, multiplier);
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
            float multiplier = GetMultiplier(attribute);
            return finalBase * multiplier;
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
        /// Sets multiplier for an attribute directly. Overrides GAS-based multiplier.
        /// </summary>
        public void SetMultiplier(MovementAttribute attribute, float multiplier)
        {
            _multipliers[attribute] = multiplier;
        }
        
        /// <summary>
        /// Adds an attribute mapping for GAS integration.
        /// </summary>
        public void AddAttributeMapping(MovementAttribute attribute, string gasTag, float baseValue = 100f)
        {
            if (attributeMappings == null)
            {
                attributeMappings = new List<AttributeMapping>();
            }
            
            var existing = attributeMappings.Find(m => m.attribute == attribute);
            if (existing != null)
            {
                existing.gasAttributeTag = gasTag;
                existing.baseValueForMultiplier = baseValue;
                existing.useGASAttribute = true;
            }
            else
            {
                attributeMappings.Add(new AttributeMapping
                {
                    attribute = attribute,
                    gasAttributeTag = gasTag,
                    baseValueForMultiplier = baseValue,
                    useGASAttribute = true
                });
            }
            
            _baseValues[attribute] = baseValue;
            _multipliers[attribute] = 1f;
        }
        
        /// <summary>
        /// Gets current base value used for multiplier calculation.
        /// </summary>
        public float GetBaseValueForMultiplier(MovementAttribute attribute)
        {
            return _baseValues.TryGetValue(attribute, out float value) ? value : 100f;
        }
        
        /// <summary>
        /// Sets base value for multiplier calculation.
        /// </summary>
        public void SetBaseValueForMultiplier(MovementAttribute attribute, float baseValue)
        {
            _baseValues[attribute] = baseValue;
            UpdateMultipliers();
        }
    }
}
#endif