using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CycloneGames.UIFramework.DynamicAtlas;

namespace CycloneGames.UIFramework.Editor.DynamicAtlas
{
    public class DynamicAtlasDebuggerWindow : EditorWindow
    {
        private Vector2 _sidebarScrollPosition;
        private Vector2 _mainScrollPosition;
        private Vector2 _itemsScrollPosition;
        private Vector2 _imageScrollPosition;
        private float _zoom = 1.0f;
        
        private DynamicAtlasService _activeService;
        private DynamicAtlasPage _selectedPage;
        
        private readonly List<DynamicAtlasService.EditorAtlasItem> _reusableItemList = new List<DynamicAtlasService.EditorAtlasItem>();
        
        // Colors for debug drawing
        private static readonly Color BgColor = new Color(0.15f, 0.15f, 0.15f, 1f);
        private static readonly Color GridColor = new Color(0.25f, 0.25f, 0.25f, 1f);
        private static readonly Color OutlineColor = new Color(0f, 1f, 0f, 0.5f);
        private static readonly Color FillColor = new Color(0f, 0.5f, 0f, 0.2f);
        private static readonly Color PaddingColor = new Color(1f, 0.5f, 0f, 0.3f);
        private static readonly Color TextColor = Color.white;

        [MenuItem("Tools/CycloneGames/Dynamic Atlas/Dynamic Atlas Debugger")]
        public static void ShowWindow()
        {
            var window = GetWindow<DynamicAtlasDebuggerWindow>("Dynamic Atlas Debugger");
            window.minSize = new Vector2(800, 600);
            window.Show();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            // Auto-refresh the window continuously when playing
            if (EditorApplication.isPlaying && _activeService != null)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Dynamic Atlas Debugger is only active during Play Mode.", MessageType.Info);
                return;
            }

            FindActiveService();

            if (_activeService == null)
            {
                EditorGUILayout.HelpBox("No active DynamicAtlasService found. Make sure DynamicAtlasManager is initialized.", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            
            DrawSidebar();

            DrawVerticalLine();

            DrawMainArea();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Force Refresh", EditorStyles.toolbarButton))
            {
                Repaint();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void FindActiveService()
        {
            if (DynamicAtlasManager.Instance != null && DynamicAtlasManager.Instance.EditorAtlasService is DynamicAtlasService service)
            {
                _activeService = service;
            }
            else
            {
                _activeService = null;
                _selectedPage = null;
            }
        }

        private void DrawSidebar()
        {
            var pages = _activeService.EditorGetPages();
            
            EditorGUILayout.BeginVertical(GUILayout.Width(250));
            _sidebarScrollPosition = EditorGUILayout.BeginScrollView(_sidebarScrollPosition);

            EditorGUILayout.LabelField("Active Pages", EditorStyles.boldLabel);
            
            // Total stats
            long totalBytes = 0;
            foreach (var p in pages)
            {
                // Simple VRAM estimation
                int bpp = (p.Format == TextureFormat.RGBA32 || p.Format == TextureFormat.ARGB32) ? 4 : 1; 
                totalBytes += p.Width * p.Height * bpp;
            }
            EditorGUILayout.LabelField($"Total VRAM: {(totalBytes / 1024f / 1024f):F2} MB");
            EditorGUILayout.LabelField($"Total Pages: {pages.Count}");
            
            EditorGUILayout.Space();

            if (pages.Count == 0)
            {
                EditorGUILayout.HelpBox("No pages allocated.", MessageType.None);
                _selectedPage = null;
            }
            else
            {
                // Validate selection
                if (_selectedPage != null)
                {
                    bool pageExists = false;
                    foreach (var p in pages)
                    {
                        if (p == _selectedPage)
                        {
                            pageExists = true;
                            break;
                        }
                    }

                    if (!pageExists)
                    {
                        _selectedPage = null;
                    }
                }

                foreach (var page in pages)
                {
                    DrawPageButton(page);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawPageButton(DynamicAtlasPage page)
        {
            bool isSelected = _selectedPage == page;
            
            // Custom styling for selected state
            GUIStyle style = new GUIStyle(EditorStyles.helpBox);
            if (isSelected)
            {
                style.normal.background = EditorGUIUtility.whiteTexture;
            }

            GUI.color = isSelected ? new Color(0.3f, 0.5f, 0.8f, 1f) : Color.white;

            EditorGUILayout.BeginVertical(style);
            GUI.color = Color.white; // Reset text color

            EditorGUILayout.LabelField($"Page ID: {page.PageId}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Size: {page.Width}x{page.Height}");
            EditorGUILayout.LabelField($"Format: {page.Format}");
            EditorGUILayout.LabelField($"Active Sprites: {page.ActiveSpriteCount}");
            
            // Draw a mini progress bar for Y usage
            // Usage is how far down we've gone (CurrentY) plus the height of the current row we are working on (MaxYInRow)
            float usageY = (float)(page.CurrentY + page.MaxYInRow) / page.Height;
            if (page.IsFull) usageY = 1.0f;
            else if (usageY > 1.0f) usageY = 1.0f;
            
            EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, 15), usageY, $"Usage: {(usageY*100):F1}%");

            EditorGUILayout.EndVertical();

            // Handle Clicks
            Rect rect = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                _selectedPage = page;
                Event.current.Use();
            }
        }

        private void DrawMainArea()
        {
            EditorGUILayout.BeginVertical();

            if (_selectedPage == null)
            {
                EditorGUILayout.HelpBox("Select a page from the sidebar to inspect.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            float atlasPreviewHeight = position.height * 0.6f;
            
            // Visual Atlas Preview
            EditorGUILayout.LabelField($"Atlas Visualizer - Page {_selectedPage.PageId}", EditorStyles.boldLabel);
            _mainScrollPosition = EditorGUILayout.BeginScrollView(_mainScrollPosition, GUILayout.Height(atlasPreviewHeight));
            DrawAtlasPreview(_selectedPage);
            EditorGUILayout.EndScrollView();
            
            DrawHorizontalLine();

            // Cached Items List for this page
            EditorGUILayout.LabelField("Sprites Residing on this Page", EditorStyles.boldLabel);
            
            var allItems = _activeService.EditorGetCachedItems();
            _reusableItemList.Clear();
            foreach (var item in allItems)
            {
                if (item.Page == _selectedPage)
                {
                    _reusableItemList.Add(item);
                }
            }

            DrawItemsList(_reusableItemList);

            EditorGUILayout.EndVertical();
        }

        private void DrawAtlasPreview(DynamicAtlasPage page)
        {
            if (page.Texture == null) return;

            // Hint box
            EditorGUILayout.HelpBox("Controls: Scroll Wheel to Zoom In/Out. Click & Drag Scrollbars to Pan.", MessageType.Info);

            // Container for the scroll view
            Rect viewportRect = GUILayoutUtility.GetRect(10, 10000, 10, 10000);

            // Handle Zoom
            Event e = Event.current;
            if (viewportRect.Contains(e.mousePosition) && e.type == EventType.ScrollWheel)
            {
                float zoomDelta = -e.delta.y * 0.05f;
                float oldZoom = _zoom;
                _zoom = Mathf.Clamp(_zoom + zoomDelta, 0.1f, 10.0f);
                
                // Adjust scroll position to keep mouse position relatively stable
                Vector2 relativeMousePos = (e.mousePosition - viewportRect.position + _imageScrollPosition) / oldZoom;
                _imageScrollPosition = relativeMousePos * _zoom - (e.mousePosition - viewportRect.position);
                
                e.Use();
            }

            // Calculate Base Size to fit window perfectly at zoom 1.0
            float aspect = (float)page.Width / page.Height;
            float targetHeight = viewportRect.height - 20; // 20px padding for scrollbars
            float targetWidth = targetHeight * aspect;
            
            if (targetWidth > viewportRect.width - 20)
            {
                targetWidth = viewportRect.width - 20;
                targetHeight = targetWidth / aspect;
            }

            float scaledWidth = targetWidth * _zoom;
            float scaledHeight = targetHeight * _zoom;

            Rect contentRect = new Rect(0, 0, scaledWidth, scaledHeight);
            
            _imageScrollPosition = GUI.BeginScrollView(viewportRect, _imageScrollPosition, contentRect);
            
            Rect baseRect = new Rect(0, 0, scaledWidth, scaledHeight);

            DrawCheckerboard(baseRect);

            GUI.DrawTexture(baseRect, page.Texture, ScaleMode.StretchToFill, true);

            var allItems = _activeService.EditorGetCachedItems();
            _reusableItemList.Clear();
            foreach (var item in allItems)
            {
                if (item.Page == page)
                {
                    _reusableItemList.Add(item);
                }
            }

            foreach (var item in _reusableItemList)
            {
                if (item.Sprite == null) continue;

                Rect spriteRect = item.Sprite.rect;
                
                float scaleX = baseRect.width / page.Width;
                float scaleY = baseRect.height / page.Height;

                float x = baseRect.x + spriteRect.x * scaleX;
                // GUI Y goes down. Texture Y goes up.
                float y = baseRect.y + baseRect.height - ((spriteRect.y + spriteRect.height) * scaleY);
                float w = spriteRect.width * scaleX;
                float h = spriteRect.height * scaleY;

                Rect guiRect = new Rect(x, y, w, h);

                EditorGUI.DrawRect(guiRect, FillColor);
                
                Handles.color = OutlineColor;
                Handles.DrawWireCube(new Vector3(guiRect.center.x, guiRect.center.y, 0), new Vector3(guiRect.width, guiRect.height, 0));

                if (_zoom > 0.5f) // Hide text if zoomed out too far
                {
                    GUIStyle labelStyle = new GUIStyle(EditorStyles.miniLabel);
                    labelStyle.normal.textColor = TextColor;
                    
                    string shortName = item.Path;
                    if (shortName.Length > 15) shortName = "..." + shortName.Substring(shortName.Length - 15);
                    GUI.Label(guiRect, shortName, labelStyle);
                }
            }

            Handles.color = Color.white;
            GUI.EndScrollView();
        }

        private void DrawItemsList(List<DynamicAtlasService.EditorAtlasItem> items)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Sprite Name / Path", EditorStyles.boldLabel, GUILayout.Width(300));
            EditorGUILayout.LabelField("Size (Pixels)", EditorStyles.boldLabel, GUILayout.Width(100));
            EditorGUILayout.LabelField("RefCount", EditorStyles.boldLabel, GUILayout.Width(80));
            EditorGUILayout.EndHorizontal();

            _itemsScrollPosition = EditorGUILayout.BeginScrollView(_itemsScrollPosition);

            if (items.Count == 0)
            {
                EditorGUILayout.HelpBox("No active sprites tracked here. (They might have been released)", MessageType.Info);
            }
            else
            {
                foreach (var item in items)
                {
                    EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                    
                    // Path
                    EditorGUILayout.SelectableLabel(item.Path, GUILayout.Width(300), GUILayout.Height(18));
                    
                    // Size
                    string sizeInfo = "N/A";
                    if (item.Sprite != null)
                    {
                        sizeInfo = $"{item.Sprite.rect.width}x{item.Sprite.rect.height}";
                    }
                    EditorGUILayout.LabelField(sizeInfo, GUILayout.Width(100));

                    // RefCount (make it green if alive, red if dying)
                    GUIStyle refStyle = new GUIStyle(EditorStyles.boldLabel);
                    refStyle.normal.textColor = item.RefCount > 0 ? new Color(0.2f, 0.8f, 0.2f) : Color.red;
                    EditorGUILayout.LabelField(item.RefCount.ToString(), refStyle, GUILayout.Width(80));
                    
                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawCheckerboard(Rect rect)
        {
            EditorGUI.DrawRect(rect, BgColor);
            
            float cellSize = 16f;
            int cols = Mathf.CeilToInt(rect.width / cellSize);
            int rows = Mathf.CeilToInt(rect.height / cellSize);

            Handles.color = GridColor;
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    if ((r + c) % 2 == 0) continue;
                    
                    float x = rect.x + c * cellSize;
                    float y = rect.y + r * cellSize;
                    float w = Mathf.Min(cellSize, rect.width - (x - rect.x));
                    float h = Mathf.Min(cellSize, rect.height - (y - rect.y));
                    
                    EditorGUI.DrawRect(new Rect(x, y, w, h), GridColor);
                }
            }
            Handles.color = Color.white;
        }

        private void DrawVerticalLine()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, GUILayout.Width(2));
            rect.height = position.height;
            EditorGUI.DrawRect(rect, Color.gray);
        }

        private void DrawHorizontalLine()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 2);
            EditorGUI.DrawRect(rect, new Color(0.3f, 0.3f, 0.3f, 1f));
        }
    }
}