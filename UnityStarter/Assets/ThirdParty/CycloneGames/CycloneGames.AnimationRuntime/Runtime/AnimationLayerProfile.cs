using System;
using UnityEngine;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>
    /// Authored layer stack for a character archetype. Layer count and per-layer capacity are fixed at graph
    /// initialization and cannot change at runtime.
    ///
    /// A layer weight of 0 means "unset" and projects to 1. Runtime <c>SetLayerWeight(0)</c> mutes a layer.
    /// </summary>
    [CreateAssetMenu(menuName = "CycloneGames/Animation/Layer Profile", fileName = "AnimationLayerProfile")]
    public sealed class AnimationLayerProfile : ScriptableObject
    {
        [Serializable]
        public struct Layer
        {
            /// <summary>Authoring label shown by the default Inspector. Never read at runtime.</summary>
            public string Name;

            public AvatarMask Mask;
            public bool Additive;

            [Range(0f, 1f)]
            public float Weight;

            [Range(1, 8)]
            public byte Capacity;

            public bool ApplyFootIK;
            public bool ApplyPlayableIK;
        }

        [SerializeField] private Layer[] _layers = Array.Empty<Layer>();

        public int LayerCount => _layers != null ? _layers.Length : 0;

        public bool TryGetLayer(int index, out Layer layer)
        {
            if (_layers == null || index < 0 || index >= _layers.Length)
            {
                layer = default;
                return false;
            }

            layer = _layers[index];
            return true;
        }

        /// <summary>Projects the asset into the engine-light descriptors the graph consumes. Allocates once, at init.</summary>
        public AnimationLayerDescriptor[] ToDescriptors()
        {
            if (_layers == null || _layers.Length == 0)
            {
                return new[] { AnimationLayerDescriptor.Default };
            }

            var descriptors = new AnimationLayerDescriptor[_layers.Length];
            for (int i = 0; i < _layers.Length; i++)
            {
                Layer layer = _layers[i];
                float weight = layer.Weight <= 0f ? 1f : layer.Weight;
                descriptors[i] = new AnimationLayerDescriptor(
                    layer.Mask,
                    layer.Additive,
                    weight,
                    layer.Capacity,
                    layer.ApplyFootIK,
                    layer.ApplyPlayableIK);
            }

            return descriptors;
        }

        /// <summary>Returns authoring problems for Editor tooling, or an empty string.</summary>
        public string GetValidationReport()
        {
            if (_layers == null || _layers.Length == 0)
            {
                return string.Empty;
            }

            var report = new System.Text.StringBuilder();
            if (_layers.Length > Core.AnimationRuntimeLimits.MaxLayers)
            {
                report.Append("Layer count ").Append(_layers.Length)
                    .Append(" exceeds the maximum of ").Append(Core.AnimationRuntimeLimits.MaxLayers)
                    .Append("; only the first layers will be built.\n");
            }

            for (int i = 0; i < _layers.Length; i++)
            {
                Layer layer = _layers[i];

                if (layer.Additive && layer.Mask == null)
                {
                    report.Append("Layer ").Append(i)
                        .Append(" is additive without an AvatarMask; a full-body additive pose needs an explicit reference pose.\n");
                }

                if (layer.Capacity < Core.AnimationRuntimeLimits.MinLayerCapacity
                    || layer.Capacity > Core.AnimationRuntimeLimits.MaxLayerCapacity)
                {
                    report.Append("Layer ").Append(i).Append(" capacity ").Append(layer.Capacity)
                        .Append(" is outside [").Append(Core.AnimationRuntimeLimits.MinLayerCapacity)
                        .Append(", ").Append(Core.AnimationRuntimeLimits.MaxLayerCapacity)
                        .Append("]; re-save the asset to clamp it.\n");
                }

                if (float.IsNaN(layer.Weight) || layer.Weight < 0f || layer.Weight > 1f)
                {
                    report.Append("Layer ").Append(i).Append(" weight ").Append(layer.Weight)
                        .Append(" is outside [0, 1]. A weight of 0 means unset and projects to 1.\n");
                }

                if (string.IsNullOrEmpty(layer.Name))
                {
                    continue;
                }

                for (int j = 0; j < i; j++)
                {
                    if (string.Equals(layer.Name, _layers[j].Name, StringComparison.Ordinal))
                    {
                        report.Append("Layer ").Append(i).Append(" reuses the name '").Append(layer.Name)
                            .Append("' from layer ").Append(j).Append(".\n");
                        break;
                    }
                }
            }

            return report.ToString();
        }

        private void OnValidate()
        {
            if (_layers == null)
            {
                return;
            }

            for (int i = 0; i < _layers.Length; i++)
            {
                if (_layers[i].Capacity < Core.AnimationRuntimeLimits.MinLayerCapacity)
                {
                    _layers[i].Capacity = (byte)Core.AnimationRuntimeLimits.MinLayerCapacity;
                }
                else if (_layers[i].Capacity > Core.AnimationRuntimeLimits.MaxLayerCapacity)
                {
                    _layers[i].Capacity = (byte)Core.AnimationRuntimeLimits.MaxLayerCapacity;
                }
            }
        }
    }
}
