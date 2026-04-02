using System;
using System.Runtime.CompilerServices;
using TMPro;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime.Integrations.Localization
{
    [Serializable]
    public struct TrackedElement
    {
        public RectTransform Target;
        public TMP_Text Text;
    }

    [Serializable]
    public struct ElementSnapshot
    {
        public float FontSize;
        public float LineSpacing;
        public float CharacterSpacing;
        public Vector2 AnchoredPosition;
        public Vector2 SizeDelta;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ElementSnapshot Capture(RectTransform rect, TMP_Text text)
        {
            var snap = new ElementSnapshot();
            if (rect != null)
            {
                snap.AnchoredPosition = rect.anchoredPosition;
                snap.SizeDelta = rect.sizeDelta;
            }
            if (text != null)
            {
                snap.FontSize = text.fontSize;
                snap.LineSpacing = text.lineSpacing;
                snap.CharacterSpacing = text.characterSpacing;
            }
            return snap;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ApplyTo(RectTransform rect, TMP_Text text)
        {
            if (rect != null)
            {
                rect.anchoredPosition = AnchoredPosition;
                rect.sizeDelta = SizeDelta;
            }
            if (text != null)
            {
                text.fontSize = FontSize;
                text.lineSpacing = LineSpacing;
                text.characterSpacing = CharacterSpacing;
            }
        }

        public bool ApproximatelyEquals(in ElementSnapshot other)
        {
            return Mathf.Approximately(FontSize, other.FontSize) &&
                   Mathf.Approximately(LineSpacing, other.LineSpacing) &&
                   Mathf.Approximately(CharacterSpacing, other.CharacterSpacing) &&
                   Mathf.Approximately(AnchoredPosition.x, other.AnchoredPosition.x) &&
                   Mathf.Approximately(AnchoredPosition.y, other.AnchoredPosition.y) &&
                   Mathf.Approximately(SizeDelta.x, other.SizeDelta.x) &&
                   Mathf.Approximately(SizeDelta.y, other.SizeDelta.y);
        }
    }

    [Serializable]
    public struct LocaleSnapshot
    {
        public string LocaleCode;
        public ElementSnapshot[] Elements;
    }
}
