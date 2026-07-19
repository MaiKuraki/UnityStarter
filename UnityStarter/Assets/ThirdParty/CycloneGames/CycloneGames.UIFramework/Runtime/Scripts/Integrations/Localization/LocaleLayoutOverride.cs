using System;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Runtime.Integrations.Localization
{
    [Serializable]
    public struct TrackedElement
    {
        public RectTransform Target;
        public TMP_Text Text;
        public LayoutGroup LayoutGroup;
    }

    [Serializable]
    public struct ElementSnapshot
    {
        // Schema 0 fields. Their names and types are retained for serialized compatibility.
        public float FontSize;
        public float LineSpacing;
        public float CharacterSpacing;
        public Vector2 AnchoredPosition;
        public Vector2 SizeDelta;

        // Schema 2 fields.
        public Vector2 AnchorMin;
        public Vector2 AnchorMax;
        public Vector2 Pivot;
        public Vector3 LocalScale;
        public TextAlignmentOptions TextAlignment;
        public bool IsRightToLeftText;
        public TextAnchor ChildAlignment;
        public bool HasValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ElementSnapshot Capture(RectTransform rect, TMP_Text text)
        {
            return Capture(rect, text, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ElementSnapshot Capture(
            RectTransform rect,
            TMP_Text text,
            LayoutGroup layoutGroup)
        {
            ElementSnapshot snapshot = default;
            snapshot.HasValue = true;
            if (rect != null)
            {
                snapshot.AnchoredPosition = rect.anchoredPosition;
                snapshot.SizeDelta = rect.sizeDelta;
                snapshot.AnchorMin = rect.anchorMin;
                snapshot.AnchorMax = rect.anchorMax;
                snapshot.Pivot = rect.pivot;
                snapshot.LocalScale = rect.localScale;
            }

            if (text != null)
            {
                snapshot.FontSize = text.fontSize;
                snapshot.LineSpacing = text.lineSpacing;
                snapshot.CharacterSpacing = text.characterSpacing;
                snapshot.TextAlignment = text.alignment;
                snapshot.IsRightToLeftText = text.isRightToLeftText;
            }

            if (layoutGroup != null)
            {
                snapshot.ChildAlignment = layoutGroup.childAlignment;
            }

            return snapshot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ElementSnapshot Capture(in TrackedElement element)
        {
            return Capture(element.Target, element.Text, element.LayoutGroup);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ApplyTo(RectTransform rect, TMP_Text text)
        {
            ApplyTo(rect, text, null);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ApplyTo(
            RectTransform rect,
            TMP_Text text,
            LayoutGroup layoutGroup)
        {
            if (rect != null)
            {
                rect.anchorMin = AnchorMin;
                rect.anchorMax = AnchorMax;
                rect.pivot = Pivot;
                rect.anchoredPosition = AnchoredPosition;
                rect.sizeDelta = SizeDelta;
                rect.localScale = LocalScale;
            }

            if (text != null)
            {
                text.fontSize = FontSize;
                text.lineSpacing = LineSpacing;
                text.characterSpacing = CharacterSpacing;
                text.alignment = TextAlignment;
                text.isRightToLeftText = IsRightToLeftText;
            }

            if (layoutGroup != null)
            {
                layoutGroup.childAlignment = ChildAlignment;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ApplyTo(in TrackedElement element)
        {
            ApplyTo(element.Target, element.Text, element.LayoutGroup);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ApplyLegacyTo(in TrackedElement element)
        {
            if (element.Target != null)
            {
                element.Target.anchoredPosition = AnchoredPosition;
                element.Target.sizeDelta = SizeDelta;
            }

            if (element.Text != null)
            {
                element.Text.fontSize = FontSize;
                element.Text.lineSpacing = LineSpacing;
                element.Text.characterSpacing = CharacterSpacing;
            }
        }

        public bool ApproximatelyEquals(in ElementSnapshot other)
        {
            return ApproximatelyEquals(other, false);
        }

        public bool ApproximatelyEquals(in ElementSnapshot other, bool legacyOnly)
        {
            bool legacyEqual = Mathf.Approximately(FontSize, other.FontSize) &&
                               Mathf.Approximately(LineSpacing, other.LineSpacing) &&
                               Mathf.Approximately(CharacterSpacing, other.CharacterSpacing) &&
                               Approximately(AnchoredPosition, other.AnchoredPosition) &&
                               Approximately(SizeDelta, other.SizeDelta);

            if (!legacyEqual || legacyOnly)
            {
                return legacyEqual;
            }

            return Approximately(AnchorMin, other.AnchorMin) &&
                   Approximately(AnchorMax, other.AnchorMax) &&
                   Approximately(Pivot, other.Pivot) &&
                   Approximately(LocalScale, other.LocalScale) &&
                   TextAlignment == other.TextAlignment &&
                   IsRightToLeftText == other.IsRightToLeftText &&
                   ChildAlignment == other.ChildAlignment &&
                   HasValue == other.HasValue;
        }

        internal bool IsRuntimeValid(in TrackedElement element, bool legacyOnly)
        {
            if (element.Target != null &&
                (!IsFinite(AnchoredPosition) || !IsFinite(SizeDelta)))
            {
                return false;
            }

            if (element.Text != null &&
                (!IsFinite(FontSize) ||
                 !IsFinite(LineSpacing) ||
                 !IsFinite(CharacterSpacing)))
            {
                return false;
            }

            if (legacyOnly)
            {
                return true;
            }

            if (element.Target != null &&
                (!IsFinite(AnchorMin) ||
                 !IsFinite(AnchorMax) ||
                 !IsFinite(Pivot) ||
                 !IsFinite(LocalScale)))
            {
                return false;
            }

            if (element.Text != null && !IsValidTextAlignment(TextAlignment))
            {
                return false;
            }

            return element.LayoutGroup == null ||
                   (ChildAlignment >= TextAnchor.UpperLeft &&
                    ChildAlignment <= TextAnchor.LowerRight);
        }

        private static bool IsValidTextAlignment(TextAlignmentOptions alignment)
        {
            int value = (int)alignment;
            if (value == (int)TextAlignmentOptions.Converted)
            {
                return true;
            }

            int horizontal = value & 0xFF;
            int vertical = value & 0xFF00;
            bool validHorizontal = horizontal == 0x1 ||
                                   horizontal == 0x2 ||
                                   horizontal == 0x4 ||
                                   horizontal == 0x8 ||
                                   horizontal == 0x10 ||
                                   horizontal == 0x20;
            bool validVertical = vertical == 0x100 ||
                                 vertical == 0x200 ||
                                 vertical == 0x400 ||
                                 vertical == 0x800 ||
                                 vertical == 0x1000 ||
                                 vertical == 0x2000;
            return validHorizontal && validVertical && (value & ~0xFFFF) == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Approximately(Vector2 left, Vector2 right)
        {
            return Mathf.Approximately(left.x, right.x) &&
                   Mathf.Approximately(left.y, right.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return Mathf.Approximately(left.x, right.x) &&
                   Mathf.Approximately(left.y, right.y) &&
                   Mathf.Approximately(left.z, right.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFinite(Vector2 value)
        {
            return IsFinite(value.x) && IsFinite(value.y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }
    }

    [Serializable]
    public struct LocaleSnapshot
    {
        public const int CurrentSchemaVersion = 2;

        public string LocaleCode;
        public ElementSnapshot[] Elements;
        public int SchemaVersion;

        public bool UsesLegacySchema => SchemaVersion < CurrentSchemaVersion;
    }
}
