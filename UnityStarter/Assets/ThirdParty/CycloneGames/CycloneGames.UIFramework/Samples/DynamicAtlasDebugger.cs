using System.Collections.Generic;
using System.Reflection;
using CycloneGames.UIFramework.DynamicAtlas;
using UnityEngine;

namespace CycloneGames.UIFramework.Samples
{
    /// <summary>
    /// A debug tool to visualize the internal state of the Dynamic Atlas.
    /// Uses Reflection to access private pages for visualization.
    /// </summary>
    public class DynamicAtlasDebugger : MonoBehaviour
    {
        [Header("Debug Settings")]
        public bool showDebugger = true;
        public float scale = 0.5f;
        public Vector2 position = new Vector2(10, 10);

        private FieldInfo _managerServiceField;
        private FieldInfo _servicePagesField;
        private List<DynamicAtlasPage> _pagesRef;
        
        private void Start()
        {
            var managerType = typeof(DynamicAtlasManager);
            _managerServiceField = managerType.GetField("_atlasService", BindingFlags.NonPublic | BindingFlags.Instance);

            var serviceType = typeof(DynamicAtlasService);
            _servicePagesField = serviceType.GetField("_pages", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private void OnGUI()
        {
            if (!showDebugger) return;
            if (DynamicAtlasManager.Instance == null) return;

            if (_pagesRef == null)
            {
                object serviceInstance = null;
                
                if (_managerServiceField != null)
                {
                    serviceInstance = _managerServiceField.GetValue(DynamicAtlasManager.Instance);
                }
                
                if (serviceInstance == null)
                {
                    serviceInstance = DynamicAtlasManager.Instance.Service;
                }

                if (serviceInstance != null && _servicePagesField != null)
                {
                    _pagesRef = _servicePagesField.GetValue(serviceInstance) as List<DynamicAtlasPage>;
                }
            }

            if (_pagesRef == null)
            {
                GUILayout.Label("Waiting for DynamicAtlasService initialization...");
                return;
            }

            DrawPages();
        }

        private void DrawPages()
        {
            float currentY = position.y;
            float currentX = position.x;

            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(currentX, currentY, 1000, 1000));
            
            GUILayout.Label($"<b>Dynamic Atlas Debugger</b> (Pages: {_pagesRef.Count})");

            for (int i = 0; i < _pagesRef.Count; i++)
            {
                var page = _pagesRef[i];
                if (page == null || page.Texture == null) continue;

                float displaySize = 512 * scale;
                
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"Page {i} [{page.Width}x{page.Height}] - Items: {page.ActiveSpriteCount}");
                
                Rect texRect = GUILayoutUtility.GetRect(displaySize, displaySize);
                GUI.DrawTexture(texRect, page.Texture, ScaleMode.ScaleToFit, false);
                
                GUILayout.EndVertical();
                GUILayout.Space(10);
            }

            GUILayout.EndArea();
        }
    }
}