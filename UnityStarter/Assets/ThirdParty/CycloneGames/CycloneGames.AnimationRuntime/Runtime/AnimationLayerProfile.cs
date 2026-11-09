using System;
using UnityEngine;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>
    /// Authored layer stack for a character archetype. The schema is part of the contract: layer count and
    /// per-layer capacity are fixed at graph initialization and cannot change at runtime.
    /// </summary>
    [CreateAssetMenu(menuName = "CycloneGames/Animation/Layer Profile", fileName = "AnimationLayerProfile")]
    public sealed class AnimationLayerProfile : ScriptableObject
    {
        /// <summary>Schema version. Bump when a field default changes in a way that needs migration.</summary>
        public const int CurrentVersion = 1;

        [Serializable]
        public struct Layer
        {
            public string Name;
            public AvatarMask Mask;
            public bool Additive;

            [Range(0f, 1f)]
            public float Weight;

            [Range(1, 8)]
            public byte Capacity;

            public bool ApplyFootIK;
            public bool ApplyPlayableIK;
            public RootMotionAxis RootMotion;
            public float RootMotionScale;
        }

        [SerializeField] private int _version = CurrentVersion;
        [SerializeField] private Layer[] _layers = Array.Empty<Layer>();

        public int Version => _version;

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
                    layer.ApplyPlayableIK,
                    layer.RootMotion,
                    layer.RootMotionScale);
            }

            return descriptors;
        }

        private void OnValidate()
        {
            if (_version <= 0)
            {
                _version = CurrentVersion;
            }

            if (_layers == null)
            {
                return;
            }

            for (int i = 0; i < _layers.Length; i++)
            {
                if (_layers[i].Capacity < Core.AnimationRuntimeLimits.MinLayerCapacity)
                {
                    _layers[i].Capacity = (byte)Core.AnimationRuntimeLimits.DefaultLayerCapacity;
                }

                if (_layers[i].RootMotionScale <= 0f)
                {
                    _layers[i].RootMotionScale = 1f;
                }
            }
        }
    }
}
