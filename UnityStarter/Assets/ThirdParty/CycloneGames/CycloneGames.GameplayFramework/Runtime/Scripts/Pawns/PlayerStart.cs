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

        public static IReadOnlyList<PlayerStart> GetAllPlayerStarts() => _registry;
        public static bool IsRegistryDirty => _registryDirty;
        public static void ClearRegistryDirtyFlag() => _registryDirty = false;

        [SerializeField] private Transform Arrow;

        protected override void Awake()
        {
            base.Awake();
            if (Arrow != null && Arrow.gameObject.activeInHierarchy) Arrow.gameObject.SetActive(false);
        }

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

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, 0.5f);

            // Draw forward direction arrow
            Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.8f);
            Vector3 forward = transform.forward * 1.5f;
            Gizmos.DrawRay(transform.position, forward);
            Vector3 arrowHead = transform.position + forward;
            Vector3 right = transform.right * 0.25f;
            Gizmos.DrawRay(arrowHead, -forward.normalized * 0.4f + right);
            Gizmos.DrawRay(arrowHead, -forward.normalized * 0.4f - right);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 1f, 0.2f, 0.3f);
            Gizmos.DrawSphere(transform.position, 0.5f);

            UnityEditor.Handles.Label(transform.position + Vector3.up * 1.2f, GetName() ?? "PlayerStart");
        }
#endif
    }
}