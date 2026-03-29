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
        public struct TagColorRule
        {
            [Tooltip("Substring to match against tag names (case-sensitive). E.g., 'Debuff', 'Cooldown'.")]
            public string TagSubstring;

            [Tooltip("Color used to display tags matching this substring.")]
            public Color Color;
        }

        [Header("Tag Color Rules")]
        [Tooltip("Ordered list of tag-color rules. First matching rule wins. If no rule matches, DefaultTagColor is used.")]
        public List<TagColorRule> TagColorRules = new List<TagColorRule>();

        [Tooltip("Color for tags that don't match any rule.")]
        public Color DefaultTagColor = new Color(0.8f, 0.8f, 0.8f, 1f);

        [Header("Effect Color Rules")]
        [Tooltip("Substring to identify debuff effects via their GrantedTags. Leave empty to disable debuff coloring.")]
        public List<string> DebuffTagSubstrings = new List<string>();

        [Tooltip("Color for debuff effects.")]
        public Color DebuffEffectColor = new Color(1f, 0.4f, 0.4f, 1f);

        [Tooltip("Color for inhibited effects.")]
        public Color InhibitedEffectColor = new Color(0.8f, 0.8f, 0.27f, 1f);

        [Tooltip("Color for normal (buff) effects.")]
        public Color NormalEffectColor = new Color(0.6f, 0.87f, 0.67f, 1f);

        [Header("Panel Settings")]
        [Tooltip("Attribute name substrings to prefer for collapsed panel summary (checked in order). If none match, the first attribute is used.")]
        public List<string> PrimaryAttributeSubstrings = new List<string> { "Health", "HP", "Shield", "Mana", "MP", "Stamina", "SP", "Energy" };

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
        /// Get the display color for a tag name by matching against configured rules.
        /// </summary>
        public Color GetTagColor(string tagName)
        {
            for (int i = 0; i < TagColorRules.Count; i++)
            {
                var rule = TagColorRules[i];
                if (!string.IsNullOrEmpty(rule.TagSubstring) && tagName.Contains(rule.TagSubstring))
                    return rule.Color;
            }
            return DefaultTagColor;
        }

        /// <summary>
        /// Check if an effect's granted tags indicate it's a debuff.
        /// </summary>
        public bool IsDebuffEffect(GameplayTags.Runtime.GameplayTagContainer grantedTags)
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

        /// <summary>Convert Color to hex string for rich text.</summary>
        public static string ColorToHex(Color c)
        {
            return $"#{ColorUtility.ToHtmlStringRGB(c)}";
        }
    }
}
