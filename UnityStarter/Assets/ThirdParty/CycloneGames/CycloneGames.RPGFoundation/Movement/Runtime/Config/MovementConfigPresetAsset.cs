using System;
using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    [CreateAssetMenu(fileName = "MovementPreset", menuName = "CycloneGames/Movement Config Preset")]
    public sealed class MovementConfigPresetAsset : ScriptableObject
    {
        [Serializable]
        public struct NumericOverride
        {
            [Tooltip("Field defined by MovementConfigField; validated against documented bounds.")]
            public MovementConfigField Field;
            public float Value;
        }

        [Serializable]
        public struct RawOverride
        {
            [Tooltip("Serialized field name for bool / enum / vector values.")]
            public string SerializedField;
            public float Value;
        }

        [SerializeField] private string presetName = "Custom";
        [TextArea] [SerializeField] private string description = "";
        [SerializeField] private bool is3D = true;
        [SerializeField] private NumericOverride[] numericOverrides;
        [SerializeField] private RawOverride[] rawOverrides;

        public bool Is3D { get { return is3D; } }

        public MovementConfigPreset ToPreset()
        {
            int numericCount = numericOverrides == null ? 0 : numericOverrides.Length;
            var entries = new MovementConfigPresetEntry[numericCount];
            for (int i = 0; i < numericCount; i++)
                entries[i] = new MovementConfigPresetEntry(
                    numericOverrides[i].Field, numericOverrides[i].Value);

            int rawCount = rawOverrides == null ? 0 : rawOverrides.Length;
            var raw = new MovementConfigRawOverride[rawCount];
            for (int i = 0; i < rawCount; i++)
                raw[i] = new MovementConfigRawOverride(
                    rawOverrides[i].SerializedField, rawOverrides[i].Value);

            return new MovementConfigPreset(
                string.IsNullOrWhiteSpace(presetName) ? "Custom" : presetName,
                description ?? string.Empty,
                is3D,
                entries,
                raw);
        }
    }
}
