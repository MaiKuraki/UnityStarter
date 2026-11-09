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
