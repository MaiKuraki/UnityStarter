using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Runtime
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas), typeof(GraphicRaycaster))]
    public sealed class UILayer : MonoBehaviour
    {
        private const int InitialWindowCapacity = 8;

        [SerializeField] private string layerName;

        private readonly List<UIWindow> _windows =
            new List<UIWindow>(InitialWindowCapacity);
        private readonly Dictionary<string, UIWindow> _windowById =
            new Dictionary<string, UIWindow>(InitialWindowCapacity, StringComparer.Ordinal);
        private Canvas _canvas;
        private GraphicRaycaster _raycaster;
        private bool _initialized;

        public string LayerName => layerName ?? string.Empty;
        public Canvas UICanvas => _canvas;
        public GraphicRaycaster WindowGraphicRaycaster => _raycaster;
        public int WindowCount => _windows.Count;
        public bool IsInitialized => _initialized;

        private void Awake() => EnsureInitialized();

        internal void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _canvas = GetComponent<Canvas>();
            _raycaster = GetComponent<GraphicRaycaster>();
            _initialized = true;
        }

        public bool TryGetWindow(string windowId, out UIWindow window)
        {
            if (!string.IsNullOrEmpty(windowId) &&
                _windowById.TryGetValue(windowId, out UIWindow candidate) &&
                candidate != null)
            {
                window = candidate;
                return true;
            }

            window = null;
            return false;
        }

        public void CopyWindows(List<UIWindow> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            destination.Clear();
            destination.AddRange(_windows);
        }

        internal void Attach(UIWindow window)
        {
            if (window == null)
            {
                throw new ArgumentNullException(nameof(window));
            }

            EnsureInitialized();
            if (TryGetWindow(window.WindowId, out UIWindow existing) && !ReferenceEquals(existing, window))
            {
                throw new InvalidOperationException(
                    $"Layer '{LayerName}' already contains window '{window.WindowId}'.");
            }

            if (_windows.Contains(window))
            {
                return;
            }

            window.transform.SetParent(transform, false);
            window.SetLayer(this);
            if (!string.IsNullOrEmpty(window.WindowId))
            {
                _windowById[window.WindowId] = window;
            }

            InsertSorted(window);
        }

        internal bool Detach(UIWindow window)
        {
            if (ReferenceEquals(window, null))
            {
                return false;
            }

            int index = _windows.IndexOf(window);
            if (index < 0)
            {
                return false;
            }

            _windows.RemoveAt(index);
            if (!string.IsNullOrEmpty(window.WindowId) &&
                _windowById.TryGetValue(window.WindowId, out UIWindow tracked) &&
                ReferenceEquals(tracked, window))
            {
                _windowById.Remove(window.WindowId);
            }

            window.SetLayer(null);
            return true;
        }

        private void InsertSorted(UIWindow window)
        {
            int index = _windows.Count;
            while (index > 0 && _windows[index - 1] != null && _windows[index - 1].Priority > window.Priority)
            {
                index--;
            }

            _windows.Insert(index, window);

            // Invariant: this layer maintains the child transform order of its windows so it
            // matches _windows. Unity shifts the siblings that follow the target index, so a
            // single move reproduces the priority-sorted order instead of rewriting every
            // sibling below the insertion point.
            if (window.transform.parent == transform)
            {
                window.transform.SetSiblingIndex(index);
            }
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _windows.Count; i++)
            {
                UIWindow window = _windows[i];
                if (window != null)
                {
                    window.SetLayer(null);
                }
            }

            _windows.Clear();
            _windowById.Clear();
            _initialized = false;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            layerName = layerName?.Trim();
        }
#endif
    }
}
