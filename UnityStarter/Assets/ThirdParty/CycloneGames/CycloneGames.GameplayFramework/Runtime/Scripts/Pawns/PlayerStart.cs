using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Spawn point for players. Uses static registry pattern for zero-GC lookup.
    /// PlayerStart instances auto-register/unregister on enable/disable.
    /// </summary>
    public class PlayerStart : Actor
    {
        private static readonly List<PlayerStart> _registry = new List<PlayerStart>(16);
        private static bool _registryDirty;

#if UNITY_EDITOR
        public enum EGizmoMode
        {
            Auto = 0,
            ThreeDimensional = 1,
            SideScroller2D = 2,
            TopDown2D = 3
        }

        public enum EFacingAxis
        {
            Auto = 0,
            TransformForward = 1,
            TransformRight = 2,
            TransformUp = 3,
            NegativeTransformForward = 4,
            NegativeTransformRight = 5,
            NegativeTransformUp = 6
        }

        [SerializeField, HideInInspector]
        private EGizmoMode GizmoMode;

        [SerializeField, HideInInspector]
        private EFacingAxis GizmoFacingAxis;

        [SerializeField, HideInInspector]
        private bool GizmoDrawShape = true;

        [SerializeField, HideInInspector]
        private bool GizmoDrawSpawnAnchor = true;

        [SerializeField, HideInInspector]
        private bool GizmoDrawFacingArrow = true;

        [SerializeField, HideInInspector]
        private bool GizmoDrawLabel = true;

        [SerializeField, HideInInspector, Min(0f)]
        private float GizmoArrowLengthOverride;

        [SerializeField, HideInInspector, Min(0.1f)]
        private float GizmoArrowLengthScale = 1f;
#endif

        public static IReadOnlyList<PlayerStart> GetAllPlayerStarts() => _registry;
        public static bool IsRegistryDirty => _registryDirty;
        public static void ClearRegistryDirtyFlag() => _registryDirty = false;

#if UNITY_EDITOR
        public EGizmoMode GetGizmoMode() => GizmoMode;
        public EFacingAxis GetGizmoFacingAxis() => GizmoFacingAxis;
        public bool ShouldDrawGizmoShape() => GizmoDrawShape;
        public bool ShouldDrawGizmoSpawnAnchor() => GizmoDrawSpawnAnchor;
        public bool ShouldDrawGizmoFacingArrow() => GizmoDrawFacingArrow;
        public bool ShouldDrawGizmoLabel() => GizmoDrawLabel;
        public float GetGizmoArrowLengthOverride() => Mathf.Max(0f, GizmoArrowLengthOverride);
        public float GetGizmoArrowLengthScale() => Mathf.Max(0.1f, GizmoArrowLengthScale);
#endif

        protected virtual void OnEnable()
        {
            if (!_registry.Contains(this))
            {
                _registry.Add(this);
                _registryDirty = true;
            }
        }

        protected virtual void OnDisable()
        {
            if (_registry.Remove(this))
            {
                _registryDirty = true;
            }
        }
    }
}
