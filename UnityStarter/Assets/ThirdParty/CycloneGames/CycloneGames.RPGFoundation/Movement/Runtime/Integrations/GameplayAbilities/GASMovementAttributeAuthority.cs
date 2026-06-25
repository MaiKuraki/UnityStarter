#if CYCLONE_RPGFOUNDATION_HAS_GAMEPLAY_ABILITIES
using System;
using System.Collections.Generic;
using UnityEngine;

using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using CycloneGames.RPGFoundation.Movement.Core;
using CycloneGames.RPGFoundation.Movement.Runtime;

namespace CycloneGames.RPGFoundation.Movement.Integrations.GameplayAbilities
{
    public enum GASMovementAbilityPolicy
    {
        DoNotActivateAbility,
        ActivateAbilityIfGranted,
        RequireAbilityAndEnterState,
        RequireAbilityAndBlockDirectState
    }

    public class GASMovementAttributeAuthority : MovementAttributeAuthorityBase
    {
        [Serializable]
        public sealed class AttributeMapping
        {
            public MovementAttribute Attribute;
            public string GASAttributeName;
            public bool UseGASAttribute = true;
            public float BaseValueForMultiplier = 100f;
        }

        [Serializable]
        public sealed class StateRule
        {
            public MovementStateType StateType;
            public GASMovementAbilityPolicy AbilityPolicy = GASMovementAbilityPolicy.ActivateAbilityIfGranted;
            public string AbilityTagName;
            public List<string> RequiredTagNames = new List<string>();
            public List<string> BlockedTagNames = new List<string>();
            public string MinimumAttributeName;
            public float MinimumAttributeValue;
        }

        [Header("GAS Attribute Mappings")]
        [SerializeField] private List<AttributeMapping> AttributeMappings = new List<AttributeMapping>();

        [Header("State Rules")]
        [Tooltip("Optional GAS gates for Movement states. Empty rules fall back to direct Movement behavior.")]
        [SerializeField] private List<StateRule> StateRules = new List<StateRule>();

        [Header("Default State Rule Values")]
        [Tooltip("Minimum stamina required to enter Sprint when the default Sprint rule is generated.")]
        [SerializeField] private float MinStaminaForSprint = 10f;

        private readonly Dictionary<MovementAttribute, float> _baseValues = new Dictionary<MovementAttribute, float>(16);

        private AbilitySystemComponent _asc;

        protected override void Awake()
        {
            base.Awake();
            _asc = GetComponent<AbilitySystemComponent>();
            InitializeMappings();
            InitializeStateRules();
        }

        private void Update()
        {
            if (_asc != null)
            {
                UpdateMultipliers();
            }
        }

        public override bool CanEnterState(MovementStateType stateType, object context = null)
        {
            if (_asc == null)
            {
                return true;
            }

            StateRule rule = GetStateRule(stateType);
            if (rule == null)
            {
                return true;
            }

            if (!HasAllTags(rule.RequiredTagNames))
            {
                return false;
            }

            if (HasAnyTag(rule.BlockedTagNames))
            {
                return false;
            }

            if (!MeetsMinimumAttribute(rule))
            {
                return false;
            }

            if (IsAbilityDrivenContext(context))
            {
                return true;
            }

            return EvaluateAbilityPolicy(rule);
        }

        public override void SetMultiplier(MovementAttribute attribute, float multiplier)
        {
            _multipliers[attribute] = multiplier;
            SyncSerializedMultiplier(attribute, multiplier);
        }

        public void AddAttributeMapping(MovementAttribute attribute, string gasAttributeName, float baseValue = 100f)
        {
            if (AttributeMappings == null)
            {
                AttributeMappings = new List<AttributeMapping>();
            }

            for (int i = 0; i < AttributeMappings.Count; i++)
            {
                AttributeMapping mapping = AttributeMappings[i];
                if (mapping.Attribute != attribute)
                {
                    continue;
                }

                mapping.GASAttributeName = gasAttributeName;
                mapping.BaseValueForMultiplier = baseValue;
                mapping.UseGASAttribute = true;
                _baseValues[attribute] = baseValue;
                _multipliers[attribute] = 1f;
                return;
            }

            AttributeMappings.Add(new AttributeMapping
            {
                Attribute = attribute,
                GASAttributeName = gasAttributeName,
                BaseValueForMultiplier = baseValue,
                UseGASAttribute = true
            });

            _baseValues[attribute] = baseValue;
            _multipliers[attribute] = 1f;
        }

        public void AddStateRule(StateRule rule)
        {
            if (rule == null)
            {
                return;
            }

            if (StateRules == null)
            {
                StateRules = new List<StateRule>();
            }

            for (int i = 0; i < StateRules.Count; i++)
            {
                if (StateRules[i].StateType == rule.StateType)
                {
                    StateRules[i] = rule;
                    return;
                }
            }

            StateRules.Add(rule);
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

        private void InitializeMappings()
        {
            if (AttributeMappings == null || AttributeMappings.Count == 0)
            {
                CreateDefaultMappings();
            }

            for (int i = 0; i < AttributeMappings.Count; i++)
            {
                AttributeMapping mapping = AttributeMappings[i];
                if (mapping == null)
                {
                    continue;
                }

                _baseValues[mapping.Attribute] = ResolveBaseValue(mapping);
                _multipliers[mapping.Attribute] = 1f;
            }
        }

        private void InitializeStateRules()
        {
            if (StateRules == null || StateRules.Count == 0)
            {
                CreateDefaultStateRules();
            }
        }

        private float ResolveBaseValue(AttributeMapping mapping)
        {
            if (!mapping.UseGASAttribute || _asc == null || string.IsNullOrWhiteSpace(mapping.GASAttributeName))
            {
                return mapping.BaseValueForMultiplier;
            }

            GameplayAttribute gasAttribute = _asc.GetAttribute(mapping.GASAttributeName);
            if (gasAttribute == null)
            {
                return mapping.BaseValueForMultiplier;
            }

            return gasAttribute.BaseValue > 0f ? gasAttribute.BaseValue : mapping.BaseValueForMultiplier;
        }

        private void CreateDefaultMappings()
        {
            AttributeMappings = new List<AttributeMapping>
            {
                CreateAttributeMapping(MovementAttribute.WalkSpeed, "Attribute.Secondary.Speed", 100f),
                CreateAttributeMapping(MovementAttribute.RunSpeed, "Attribute.Secondary.Speed", 100f),
                CreateAttributeMapping(MovementAttribute.SprintSpeed, "Attribute.Secondary.Speed", 100f),
                CreateAttributeMapping(MovementAttribute.CrouchSpeed, "Attribute.Secondary.Speed", 100f),
                CreateAttributeMapping(MovementAttribute.JumpForce, "Attribute.Secondary.JumpPower", 100f),
                CreateAttributeMapping(MovementAttribute.MaxJumpCount, "Attribute.Secondary.JumpCount", 1f),
                CreateAttributeMapping(MovementAttribute.Gravity, "Attribute.Secondary.Gravity", 100f),
                CreateAttributeMapping(MovementAttribute.AirControlMultiplier, "Attribute.Secondary.AirControl", 100f),
                CreateAttributeMapping(MovementAttribute.RotationSpeed, "Attribute.Secondary.RotationSpeed", 100f),
                CreateAttributeMapping(MovementAttribute.ClimbSpeed, "Attribute.Secondary.ClimbSpeed", 100f),
                CreateAttributeMapping(MovementAttribute.SwimSpeed, "Attribute.Secondary.SwimSpeed", 100f),
                CreateAttributeMapping(MovementAttribute.FlySpeed, "Attribute.Secondary.FlySpeed", 100f)
            };
        }

        private void CreateDefaultStateRules()
        {
            StateRules = new List<StateRule>
            {
                CreateStateRule(
                    MovementStateType.Sprint,
                    abilityTagName: null,
                    abilityPolicy: GASMovementAbilityPolicy.DoNotActivateAbility,
                    minimumAttributeName: "Attribute.Primary.Stamina",
                    minimumAttributeValue: MinStaminaForSprint,
                    blockedTags: new[] { "State.Debuff.CantSprint" }),
                CreateStateRule(
                    MovementStateType.Jump,
                    abilityTagName: "Ability.Movement.Jump",
                    abilityPolicy: GASMovementAbilityPolicy.ActivateAbilityIfGranted,
                    minimumAttributeName: null,
                    minimumAttributeValue: 0f,
                    blockedTags: new[] { "State.Debuff.CantJump" }),
                CreateStateRule(
                    MovementStateType.Roll,
                    abilityTagName: "Ability.Movement.Roll",
                    abilityPolicy: GASMovementAbilityPolicy.ActivateAbilityIfGranted,
                    minimumAttributeName: null,
                    minimumAttributeValue: 0f,
                    blockedTags: new[] { "State.Debuff.CantRoll" }),
                CreateStateRule(
                    MovementStateType.Climb,
                    abilityTagName: "Ability.Movement.Climb",
                    abilityPolicy: GASMovementAbilityPolicy.ActivateAbilityIfGranted,
                    minimumAttributeName: null,
                    minimumAttributeValue: 0f,
                    blockedTags: new[] { "State.Debuff.CantClimb" }),
                CreateStateRule(
                    MovementStateType.Crouch,
                    abilityTagName: "Ability.Movement.Crouch",
                    abilityPolicy: GASMovementAbilityPolicy.ActivateAbilityIfGranted,
                    minimumAttributeName: null,
                    minimumAttributeValue: 0f,
                    blockedTags: new[] { "State.Debuff.CantCrouch" })
            };
        }

        private void UpdateMultipliers()
        {
            for (int i = 0; i < AttributeMappings.Count; i++)
            {
                AttributeMapping mapping = AttributeMappings[i];
                if (mapping == null || !mapping.UseGASAttribute || string.IsNullOrWhiteSpace(mapping.GASAttributeName))
                {
                    continue;
                }

                GameplayAttribute gasAttribute = _asc.GetAttribute(mapping.GASAttributeName);
                if (gasAttribute != null && _baseValues.TryGetValue(mapping.Attribute, out float baseValue) && baseValue > 0f)
                {
                    _multipliers[mapping.Attribute] = gasAttribute.CurrentValue / baseValue;
                }
                else
                {
                    _multipliers[mapping.Attribute] = 1f;
                }
            }
        }

        private StateRule GetStateRule(MovementStateType stateType)
        {
            if (StateRules == null)
            {
                return null;
            }

            for (int i = 0; i < StateRules.Count; i++)
            {
                StateRule rule = StateRules[i];
                if (rule != null && rule.StateType == stateType)
                {
                    return rule;
                }
            }

            return null;
        }

        private bool EvaluateAbilityPolicy(StateRule rule)
        {
            switch (rule.AbilityPolicy)
            {
                case GASMovementAbilityPolicy.DoNotActivateAbility:
                    return true;
                case GASMovementAbilityPolicy.ActivateAbilityIfGranted:
                    return TryFindAbilitySpec(rule.AbilityTagName, out GameplayAbilitySpec optionalSpec)
                        ? _asc.TryActivateAbility(optionalSpec)
                        : true;
                case GASMovementAbilityPolicy.RequireAbilityAndEnterState:
                    return TryFindAbilitySpec(rule.AbilityTagName, out GameplayAbilitySpec requiredSpec)
                        && _asc.TryActivateAbility(requiredSpec);
                case GASMovementAbilityPolicy.RequireAbilityAndBlockDirectState:
                    if (!TryFindAbilitySpec(rule.AbilityTagName, out GameplayAbilitySpec blockingSpec))
                    {
                        return false;
                    }

                    _asc.TryActivateAbility(blockingSpec);
                    return false;
                default:
                    return true;
            }
        }

        private bool TryFindAbilitySpec(string abilityTagName, out GameplayAbilitySpec spec)
        {
            spec = null;

            if (_asc == null || string.IsNullOrWhiteSpace(abilityTagName))
            {
                return false;
            }

            if (!GameplayTagManager.TryRequestTag(abilityTagName, out GameplayTag abilityTag))
            {
                return false;
            }

            IReadOnlyList<GameplayAbilitySpec> specs = _asc.GetActivatableAbilities();
            for (int i = 0; i < specs.Count; i++)
            {
                GameplayAbilitySpec candidate = specs[i];
                if (candidate == null)
                {
                    continue;
                }

                GameplayAbility ability = candidate.GetPrimaryInstance() ?? candidate.Ability;
                if (ability?.AbilityTags != null && ability.AbilityTags.HasTag(abilityTag))
                {
                    spec = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool HasAllTags(List<string> tagNames)
        {
            if (tagNames == null || tagNames.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < tagNames.Count; i++)
            {
                string tagName = tagNames[i];
                if (string.IsNullOrWhiteSpace(tagName))
                {
                    continue;
                }

                if (!GameplayTagManager.TryRequestTag(tagName, out GameplayTag tag) || !_asc.HasMatchingGameplayTag(tag))
                {
                    return false;
                }
            }

            return true;
        }

        private bool HasAnyTag(List<string> tagNames)
        {
            if (tagNames == null || tagNames.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < tagNames.Count; i++)
            {
                string tagName = tagNames[i];
                if (string.IsNullOrWhiteSpace(tagName))
                {
                    continue;
                }

                if (GameplayTagManager.TryRequestTag(tagName, out GameplayTag tag) && _asc.HasMatchingGameplayTag(tag))
                {
                    return true;
                }
            }

            return false;
        }

        private bool MeetsMinimumAttribute(StateRule rule)
        {
            if (string.IsNullOrWhiteSpace(rule.MinimumAttributeName))
            {
                return true;
            }

            GameplayAttribute attribute = _asc.GetAttribute(rule.MinimumAttributeName);
            return attribute != null && attribute.CurrentValue >= rule.MinimumAttributeValue;
        }

        private static bool IsAbilityDrivenContext(object context)
        {
            if (context is MovementStateRequestContext requestContext)
            {
                return requestContext.IsAbilityDriven;
            }

            return context is GameplayAbility || context is GameplayAbilitySpec;
        }

        private static AttributeMapping CreateAttributeMapping(MovementAttribute attribute, string gasAttributeName, float baseValue)
        {
            return new AttributeMapping
            {
                Attribute = attribute,
                GASAttributeName = gasAttributeName,
                BaseValueForMultiplier = baseValue,
                UseGASAttribute = true
            };
        }

        private static StateRule CreateStateRule(
            MovementStateType stateType,
            string abilityTagName,
            GASMovementAbilityPolicy abilityPolicy,
            string minimumAttributeName,
            float minimumAttributeValue,
            IReadOnlyList<string> blockedTags)
        {
            StateRule rule = new StateRule
            {
                StateType = stateType,
                AbilityTagName = abilityTagName,
                AbilityPolicy = abilityPolicy,
                MinimumAttributeName = minimumAttributeName,
                MinimumAttributeValue = minimumAttributeValue
            };

            if (blockedTags != null)
            {
                for (int i = 0; i < blockedTags.Count; i++)
                {
                    rule.BlockedTagNames.Add(blockedTags[i]);
                }
            }

            return rule;
        }
    }
}
#endif
