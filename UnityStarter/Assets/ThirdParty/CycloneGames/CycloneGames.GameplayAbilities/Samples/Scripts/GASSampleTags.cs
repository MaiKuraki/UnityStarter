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

        /// <summary>
        /// Every tag declared above, registered as ordinary code.
        /// The catalog every registry (editor authoring and player runtime) registers to make these tags
        /// exist. Public so the module's editor bootstrap can register it too.
        /// </summary>
        public sealed class Catalog : CycloneGames.GameplayTags.Core.IGameplayTagCatalog
        {
            public string Name => "CycloneGames.GameplayAbilities.Samples";

            public void Collect(CycloneGames.GameplayTags.Core.GameplayTagCatalogBuilder builder)
            {
                builder.Add(Attribute_Primary_Level, Attribute_Primary_Level);
                builder.Add(Attribute_Primary_Attack, Attribute_Primary_Attack);
                builder.Add(Attribute_Primary_Defense, Attribute_Primary_Defense);
                builder.Add(Attribute_Secondary_Health, Attribute_Secondary_Health);
                builder.Add(Attribute_Secondary_MaxHealth, Attribute_Secondary_MaxHealth);
                builder.Add(Attribute_Secondary_Mana, Attribute_Secondary_Mana);
                builder.Add(Attribute_Secondary_MaxMana, Attribute_Secondary_MaxMana);
                builder.Add(Attribute_Secondary_Speed, Attribute_Secondary_Speed);
                builder.Add(Attribute_Meta_Experience, Attribute_Meta_Experience);
                builder.Add(Attribute_Meta_Damage, Attribute_Meta_Damage);
                builder.Add(State_Dead, State_Dead);
                builder.Add(State_Stunned, State_Stunned);
                builder.Add(State_Burning, State_Burning);
                builder.Add(State_Poisoned, State_Poisoned);
                builder.Add(State_Berserk, State_Berserk);
                builder.Add(State_Shielded, State_Shielded);
                builder.Add(Buff_ArmorStack, Buff_ArmorStack);
                builder.Add(Buff_Berserk, Buff_Berserk);
                builder.Add(Buff_ShieldOfLight, Buff_ShieldOfLight);
                builder.Add(Debuff_Burn, Debuff_Burn);
                builder.Add(Debuff_Poison, Debuff_Poison);
                builder.Add(Debuff_Any, Debuff_Any);
                builder.Add(Cooldown_Fireball, Cooldown_Fireball);
                builder.Add(Cooldown_PoisonBlade, Cooldown_PoisonBlade);
                builder.Add(Cooldown_Purify, Cooldown_Purify);
                builder.Add(Cooldown_ChainLightning, Cooldown_ChainLightning);
                builder.Add(Cooldown_SlamAttack, Cooldown_SlamAttack);
                builder.Add(Cooldown_ArmorStack, Cooldown_ArmorStack);
                builder.Add(Cooldown_Berserk, Cooldown_Berserk);
                builder.Add(Cooldown_ShieldOfLight, Cooldown_ShieldOfLight);
                builder.Add(Ability_Fireball, Ability_Fireball);
                builder.Add(Ability_PoisonBlade, Ability_PoisonBlade);
                builder.Add(Ability_Purify, Ability_Purify);
                builder.Add(Ability_ArmorStack, Ability_ArmorStack);
                builder.Add(Ability_Berserk, Ability_Berserk);
                builder.Add(Ability_Execute, Ability_Execute);
                builder.Add(Ability_ShieldOfLight, Ability_ShieldOfLight);
                builder.Add(Event_Character_Death, Event_Character_Death);
                builder.Add(Event_Character_LeveledUp, Event_Character_LeveledUp);
                builder.Add(Event_Experience_Gain, Event_Experience_Gain);
                builder.Add(Data_DamageMultiplier, Data_DamageMultiplier);
                builder.Add(GameplayCue_Fireball_Impact, GameplayCue_Fireball_Impact);
                builder.Add(GameplayCue_Burn_Loop, GameplayCue_Burn_Loop);
                builder.Add(GameplayCue_PoisonBlade_Impact, GameplayCue_PoisonBlade_Impact);
                builder.Add(GameplayCue_Poison_Loop, GameplayCue_Poison_Loop);
                builder.Add(GameplayCue_Purify_Effect, GameplayCue_Purify_Effect);
                builder.Add(GameplayCue_Lightning_Impact, GameplayCue_Lightning_Impact);
                builder.Add(GameplayCue_Slam_Impact, GameplayCue_Slam_Impact);
                builder.Add(GameplayCue_ArmorStack, GameplayCue_ArmorStack);
                builder.Add(GameplayCue_Berserk_Activate, GameplayCue_Berserk_Activate);
                builder.Add(GameplayCue_ShieldOfLight, GameplayCue_ShieldOfLight);
                builder.Add(Faction_Player, Faction_Player);
                builder.Add(Faction_NPC_Enemy, Faction_NPC_Enemy);
            }
        }
    }
}
