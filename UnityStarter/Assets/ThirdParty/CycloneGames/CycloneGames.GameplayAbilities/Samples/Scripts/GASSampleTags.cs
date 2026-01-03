[assembly: CycloneGames.GameplayTags.Runtime.RegisterGameplayTagsFrom(typeof(CycloneGames.GameplayAbilities.Sample.GASSampleTags))]

namespace CycloneGames.GameplayAbilities.Sample
{
    public static class GASSampleTags
    {
        // Attributes
        public const string Attribute_Primary_Level = "Attribute.Primary.Level";
        public const string Attribute_Primary_Attack = "Attribute.Primary.Attack";
        public const string Attribute_Primary_Defense = "Attribute.Primary.Defense";
        public const string Attribute_Secondary_Health = "Attribute.Secondary.Health";
        public const string Attribute_Secondary_MaxHealth = "Attribute.Secondary.MaxHealth";
        public const string Attribute_Secondary_Mana = "Attribute.Secondary.Mana";
        public const string Attribute_Secondary_MaxMana = "Attribute.Secondary.MaxMana";
        public const string Attribute_Secondary_Speed = "Attribute.Secondary.Speed";
        public const string Attribute_Meta_Experience = "Attribute.Meta.Experience";
        public const string Attribute_Meta_Damage = "Attribute.Meta.Damage";

        // States
        public const string State_Dead = "State.Dead";
        public const string State_Stunned = "State.Stunned";
        public const string State_Burning = "State.Burning";
        public const string State_Poisoned = "State.Poisoned";
        public const string State_Berserk = "State.Berserk";
        public const string State_Shielded = "State.Shielded";

        // Buffs
        public const string Buff_ArmorStack = "Buff.ArmorStack";
        public const string Buff_Berserk = "Buff.Berserk";
        public const string Buff_ShieldOfLight = "Buff.ShieldOfLight";

        // Debuffs
        public const string Debuff_Burn = "Debuff.Burn";
        public const string Debuff_Poison = "Debuff.Poison";
        public const string Debuff_Any = "Debuff";

        // Cooldowns
        public const string Cooldown_Fireball = "Cooldown.Skill.Fireball";
        public const string Cooldown_PoisonBlade = "Cooldown.Skill.PoisonBlade";
        public const string Cooldown_Purify = "Cooldown.Skill.Purify";
        public const string Cooldown_ChainLightning = "Cooldown.Skill.ChainLightning";
        public const string Cooldown_SlamAttack = "Cooldown.Skill.SlamAttack";
        public const string Cooldown_ArmorStack = "Cooldown.Skill.ArmorStack";
        public const string Cooldown_Berserk = "Cooldown.Skill.Berserk";
        public const string Cooldown_ShieldOfLight = "Cooldown.Skill.ShieldOfLight";

        // Abilities
        public const string Ability_Fireball = "Ability.Fireball";
        public const string Ability_PoisonBlade = "Ability.PoisonBlade";
        public const string Ability_Purify = "Ability.Purify";
        public const string Ability_ArmorStack = "Ability.ArmorStack";
        public const string Ability_Berserk = "Ability.Berserk";
        public const string Ability_Execute = "Ability.Execute";
        public const string Ability_ShieldOfLight = "Ability.ShieldOfLight";

        // Events
        public const string Event_Character_Death = "Event.Character.Death";
        public const string Event_Character_LeveledUp = "Event.Character.LeveledUp";
        public const string Event_Experience_Gain = "Event.Experience.Gain";

        // Datas
        public const string Data_DamageMultiplier = "Data.DamageMultiplier";

        // GameplayCues
        public const string GameplayCue_Fireball_Impact = "GameplayCue.Fireball.Impact";
        public const string GameplayCue_Burn_Loop = "GameplayCue.Burn.Loop";
        public const string GameplayCue_PoisonBlade_Impact = "GameplayCue.PoisonBlade.Impact";
        public const string GameplayCue_Poison_Loop = "GameplayCue.Poison.Loop";
        public const string GameplayCue_Purify_Effect = "GameplayCue.Purify.Effect";
        public const string GameplayCue_Lightning_Impact = "GameplayCue.Lightning.Impact";
        public const string GameplayCue_Slam_Impact = "GameplayCue.Slam.Impact";
        public const string GameplayCue_ArmorStack = "GameplayCue.ArmorStack";
        public const string GameplayCue_Berserk_Activate = "GameplayCue.Berserk.Activate";
        public const string GameplayCue_ShieldOfLight = "GameplayCue.ShieldOfLight";

        // Factions
        public const string Faction_Player = "Faction.Player";
        public const string Faction_NPC_Enemy = "Faction.NPC.Enemy";
    }
}