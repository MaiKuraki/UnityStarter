using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using UnityEngine;

[CreateAssetMenu(fileName = "GE_", menuName = "Cyclone/GameplayEffect")]
public class GameplayEffectSO : ScriptableObject
{
    public string EffectName;
    public EDurationPolicy DurationPolicy;
    [Tooltip("Only used if DurationPolicy is HasDuration.")]
    public float Duration;
    public List<ModifierInfo> Modifiers;
    public GameplayEffectExecutionCalculation Execution;
    public GameplayEffectStacking Stacking;
    public List<GameplayAbilitySO> GrantedAbilities;
    public GameplayTagContainer AssetTags;
    public GameplayTagContainer GrantedTags;
    public CycloneGames.GameplayAbilities.Runtime.GameplayTagRequirements ApplicationTagRequirements;
    public CycloneGames.GameplayAbilities.Runtime.GameplayTagRequirements OngoingTagRequirements;
    public GameplayTagContainer RemoveGameplayEffectsWithTags;
    public List<GameplayTag> GameplayCues;

    public GameplayEffect CreateGameplayEffect()
    {
        var grantedAbilities = new List<GameplayAbility>();
        if (GrantedAbilities != null)
        {
            foreach (var abilitySO in GrantedAbilities)
            {
                grantedAbilities.Add(abilitySO.CreateAbility());
            }
        }

        return new GameplayEffect(
            EffectName,
            DurationPolicy,
            Duration,
            Modifiers,
            Execution,
            Stacking,
            grantedAbilities,
            AssetTags,
            GrantedTags,
            ApplicationTagRequirements,
            OngoingTagRequirements,
            RemoveGameplayEffectsWithTags,
            GameplayCues
        );
    }
}