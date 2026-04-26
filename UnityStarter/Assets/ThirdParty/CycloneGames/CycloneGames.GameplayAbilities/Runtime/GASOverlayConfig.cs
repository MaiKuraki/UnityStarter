using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Configuration asset for the GAS Debug Overlay.
    /// Stores tag-color mapping rules and visual settings.
    /// Create via Assets menu: Create > CycloneGames > GAS Overlay Config.
    /// Place in a Resources folder to enable auto-loading at runtime.
    /// </summary>
    [CreateAssetMenu(fileName = "GASOverlayConfig", menuName = "CycloneGames/GameplayAbilitySystem/GAS Overlay Config", order = 200)]
    public class GASOverlayConfig : ScriptableObject
    {
        private const string ResourcePath = "GASOverlayConfig";

        [Serializable]
        public struct ResourceBarRule
        {
            [Tooltip("Substring used to match the current resource attribute name. E.g., 'Health', 'Mana'.")]
            public string CurrentAttributeSubstring;

            [Tooltip("Substring used to locate the corresponding max resource attribute. E.g., 'MaxHealth', 'MaxMana'.")]
            public string MaxAttributeSubstring;
        }

        [Serializable]
        public struct SemanticTagClassification
        {
            [Tooltip("Semantic category name (e.g., 'Positive Buff', 'Negative Debuff', 'Crowd Control'). Used for identification and organization.")]
            public string CategoryName;

            [Tooltip("Color applied to tags matching any keyword in this classification.")]
            public Color CategoryColor;

            [Tooltip("Keyword list: tags containing any of these substrings are classified under this category (case-sensitive). First matching keyword wins.")]
            public List<string> Keywords;

            /// <summary>Check if a tag name matches any keyword in this classification.</summary>
            public bool Matches(string tagName)
            {
                if (Keywords == null || Keywords.Count == 0 || string.IsNullOrEmpty(tagName))
                    return false;

                for (int i = 0; i < Keywords.Count; i++)
                {
                    if (!string.IsNullOrEmpty(Keywords[i]) && tagName.Contains(Keywords[i]))
                        return true;
                }
                return false;
            }
        }

        [Header("Tag Color Rules")]
        [Tooltip("Ordered list of semantic tag classifications. Tags matching ANY keyword in a category get that category's color. If a tag matches multiple categories, the first one wins. If no category matches, DefaultTagColor is used.")]
        public List<SemanticTagClassification> SemanticTagClassifications = new List<SemanticTagClassification>
        {
            new SemanticTagClassification 
            { 
                CategoryName = "Positive Buff",
                CategoryColor = new Color(0.2f, 1.0f, 0.4f, 1f),
                Keywords = new List<string> { "Buff", "Haste", "Blessing", "Strengthen", "Enhance", "Heal", "Barrier", "Shield", "Focus", "Power", "Guard", "Regen" }
            },
            new SemanticTagClassification 
            { 
                CategoryName = "Negative Debuff",
                CategoryColor = new Color(1.0f, 0.3f, 0.3f, 1f),
                Keywords = new List<string> { "Debuff", "Poison", "Curse", "Weak", "Bleed", "Burn", "Corruption", "Exhaustion", "Vulnerability", "Drain" }
            },
            new SemanticTagClassification 
            { 
                CategoryName = "Crowd Control",
                CategoryColor = new Color(0.8f, 0.4f, 1.0f, 1f),
                Keywords = new List<string> { "Stun", "Freeze", "Slow", "Root", "Silence", "Knockback", "Immobilize", "Sleep", "Blind", "Confuse" }
            },
            new SemanticTagClassification 
            { 
                CategoryName = "Status/Utility",
                CategoryColor = new Color(1.0f, 0.9f, 0.3f, 1f),
                Keywords = new List<string> { "Status", "Utility", "Passive", "Aura", "Stealth", "Invulnerable", "Reveal" }
            }
        };

        [Tooltip("Color for tags that don't match any semantic classification.")]
        public Color DefaultTagColor = new Color(0.70f, 1.0f, 0.78f, 1f);

        [Header("Effect Color Rules")]
        [Tooltip("Substring to identify debuff effects via their GrantedTags. Leave empty to disable debuff coloring.")]
        public List<string> DebuffTagSubstrings = new List<string>();

        [Tooltip("Color for debuff effects.")]
        public Color DebuffEffectColor = new Color(1.0f, 0.62f, 0.84f, 1f);

        [Tooltip("Color for inhibited effects.")]
        public Color InhibitedEffectColor = new Color(0.70f, 0.80f, 1.0f, 1f);

        [Tooltip("Color for normal (buff) effects.")]
        public Color NormalEffectColor = new Color(0.35f, 1.0f, 0.72f, 1f);

        [Header("Panel Settings")]
        [Tooltip("Attribute name substrings to prefer for collapsed panel summary (checked in order). If none match, the first attribute is used.")]
        public List<string> PrimaryAttributeSubstrings = new List<string> { "Health", "HP", "Shield", "Mana", "MP", "Stamina", "SP", "Energy" };

        [Header("Attribute Bar Rules")]
        [Tooltip("If enabled, attribute bars first try Current/MaxAttribute using ResourceBarRules, then fallback to Current/Base.")]
        public bool PreferMaxAttributeForBars = true;

        [Tooltip("Mapping rules from current attributes to max attributes. First matching rule wins.")]
        public List<ResourceBarRule> ResourceBarRules = new List<ResourceBarRule>
        {
            new ResourceBarRule { CurrentAttributeSubstring = "Health", MaxAttributeSubstring = "MaxHealth" },
            new ResourceBarRule { CurrentAttributeSubstring = "Mana", MaxAttributeSubstring = "MaxMana" },
            new ResourceBarRule { CurrentAttributeSubstring = "Stamina", MaxAttributeSubstring = "MaxStamina" },
            new ResourceBarRule { CurrentAttributeSubstring = "Energy", MaxAttributeSubstring = "MaxEnergy" },
            new ResourceBarRule { CurrentAttributeSubstring = "Shield", MaxAttributeSubstring = "MaxShield" }
        };

        [Tooltip("Background alpha of ASC panels (used as initial value; adjustable at runtime).")]
        [Range(0.15f, 1f)]
        public float PanelAlpha = 0.8f;

        [Tooltip("Maximum number of panels to display at once.")]
        [Range(1, 32)]
        public int MaxPanels = 8;

        [Tooltip("Panel width as fraction of screen width.")]
        [Range(0.1f, 0.5f)]
        public float PanelWidthRatio = 0.20f;

        [Tooltip("Minimum panel width in logical pixels (before UI scale).")]
        public float MinPanelWidth = 200f;

        [Tooltip("Maximum panel width in logical pixels (before UI scale).")]
        public float MaxPanelWidth = 360f;

        [Header("Layout")]
        [Tooltip("If enabled, panels are stacked from top-left instead of following world position.")]
        public bool PreferTopLeftStackLayout = true;

        [Tooltip("In top-left layout, keep panels in a single vertical column.")]
        public bool StackSingleColumn = true;

        [Tooltip("When using top-left stacked layout, draw connector lines from panel to target actor.")]
        public bool DrawConnectionLinesInStackedMode = true;

        [Tooltip("Connector line opacity in stacked layout.")]
        [Range(0.1f, 1f)]
        public float StackedConnectionLineAlpha = 0.82f;

        [Header("Connector Style")]
        [Tooltip("Use a high-contrast double outline (dark + light) so lines stay readable on both bright and dark backgrounds.")]
        public bool UseHighContrastConnectionLines = true;

        [Tooltip("Core connector line color.")]
        public Color ConnectionLineCoreColor = new Color(0.985f, 0.992f, 1f, 1f);

        [Tooltip("Dark outline color used by high-contrast connector lines.")]
        public Color ConnectionLineDarkOutlineColor = new Color(0f, 0f, 0f, 0.85f);

        [Tooltip("Light outline color used by high-contrast connector lines.")]
        public Color ConnectionLineLightOutlineColor = new Color(1f, 1f, 1f, 0.65f);

        [Tooltip("Base connector line thickness in pixels before UI scale.")]
        [Range(0.5f, 4f)]
        public float ConnectionLineThickness = 1.2f;

        [Header("World Tracking")]
        [Tooltip("If enabled, panels follow their owner's screen position instead of stacking in columns.")]
        public bool TrackWorldPosition = true;

        [Tooltip("Vertical offset in screen pixels above the object's head.")]
        public float WorldTrackingOffsetY = 30f;

        /// <summary>Loads the config from Resources. Returns null if not found.</summary>
        public static GASOverlayConfig Load()
        {
            return Resources.Load<GASOverlayConfig>(ResourcePath);
        }

        /// <summary>
        /// Get the display color for a tag name by checking semantic classifications.
        /// Checks in order: semantic categories → default tag color.
        /// </summary>
        public Color GetTagColor(string tagName)
        {
            if (string.IsNullOrEmpty(tagName))
                return DefaultTagColor;

            // Check semantic classifications
            if (SemanticTagClassifications != null && SemanticTagClassifications.Count > 0)
            {
                for (int i = 0; i < SemanticTagClassifications.Count; i++)
                {
                    if (SemanticTagClassifications[i].Matches(tagName))
                        return SemanticTagClassifications[i].CategoryColor;
                }
            }

            // Return default color
            return DefaultTagColor;
        }

        /// <summary>
        /// Check if an effect's granted tags indicate it's a debuff.
        /// </summary>
        public bool IsDebuffEffect(GameplayTags.Core.GameplayTagContainer grantedTags)
        {
            if (grantedTags == null || grantedTags.IsEmpty || DebuffTagSubstrings.Count == 0)
                return false;

            foreach (var tag in grantedTags.GetTags())
            {
                for (int i = 0; i < DebuffTagSubstrings.Count; i++)
                {
                    if (!string.IsNullOrEmpty(DebuffTagSubstrings[i]) && tag.Name.Contains(DebuffTagSubstrings[i]))
                        return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Tries to resolve a mapped max attribute name token from a current attribute name.
        /// First matching rule wins.
        /// </summary>
        public bool TryGetMappedMaxAttributeName(string currentAttributeName, out string mappedMaxAttributeName)
        {
            mappedMaxAttributeName = null;
            if (string.IsNullOrEmpty(currentAttributeName) || ResourceBarRules == null || ResourceBarRules.Count == 0)
                return false;

            for (int i = 0; i < ResourceBarRules.Count; i++)
            {
                var rule = ResourceBarRules[i];
                if (string.IsNullOrEmpty(rule.CurrentAttributeSubstring) || string.IsNullOrEmpty(rule.MaxAttributeSubstring))
                    continue;

                if (currentAttributeName.IndexOf(rule.CurrentAttributeSubstring, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    mappedMaxAttributeName = rule.MaxAttributeSubstring;
                    return true;
                }
            }

            return false;
        }

        /// <summary>Convert Color to hex string for rich text.</summary>
        public static string ColorToHex(Color c)
        {
            return $"#{ColorUtility.ToHtmlStringRGB(c)}";
        }
    }
}
