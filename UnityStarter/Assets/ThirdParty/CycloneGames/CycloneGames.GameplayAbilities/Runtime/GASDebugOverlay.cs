using System.Collections.Generic;
using System.Text;
using CycloneGames.GameplayTags.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Runtime IMGUI debug overlay for all AbilitySystemComponents in the scene.
    /// Config: place a <see cref="GASOverlayConfig"/> asset in a Resources folder.
    /// Toggle: <see cref="Toggle"/> or menu Tools/CycloneGames/GameplayAbilities/GAS Overlay.
    /// </summary>
    public class GASDebugOverlay : MonoBehaviour
    {
        #region Default Colors (shared with GASOverlayConfig)

        // These defaults must match GASOverlayConfig initializer values
        private static readonly Color DefaultTagColorValue = new Color(0.70f, 1.0f, 0.78f, 1f);
        private static readonly Color DefaultDebuffEffectColor = new Color(1.0f, 0.62f, 0.84f, 1f);
        private static readonly Color DefaultInhibitedEffectColor = new Color(0.70f, 0.80f, 1.0f, 1f);
        private static readonly Color DefaultNormalEffectColor = new Color(0.35f, 1.0f, 0.72f, 1f);
        private static readonly Color DefaultConnectionLineCoreColor = new Color(0.985f, 0.992f, 1f, 1f);
        private static readonly Color DefaultConnectionLineDarkOutlineColor = new Color(0f, 0f, 0f, 0.85f);
        private static readonly Color DefaultConnectionLineLightOutlineColor = new Color(1f, 1f, 1f, 0.65f);

        #endregion

        #region Singleton

        private static GASDebugOverlay s_Instance;
    private static bool s_Initialized;

    public static bool IsActive => s_Instance != null && s_Instance.enabled;

    public static bool IsInitialized => s_Initialized;

    /// <summary>
    /// Initialize the overlay singleton. Call once at startup (0GC after first init).
    /// For production builds, wrap with conditional compilation or config checks.
    /// </summary>
    public static void Initialize(bool enableAtStart = false, bool dontDestroyOnLoad = false)
    {
        if (s_Initialized) return; // Already initialized
        s_Initialized = true;

        if (s_Instance != null) return; // Already exists

        var go = new GameObject("[GAS Debug Overlay]");
        s_Instance = go.AddComponent<GASDebugOverlay>();
        s_Instance.enabled = enableAtStart;

        if (dontDestroyOnLoad)
            DontDestroyOnLoad(go);
    }

    /// <summary>
    /// Toggle the overlay on/off. 
    /// If not initialized, automatically initializes first (for editor convenience).
    /// Returns the final enabled state (0GC after first init).
    /// </summary>
    public static bool Toggle()
    {
        // Auto-initialize on first call from editor (friendly UX)
        if (!s_Initialized)
        {
            Initialize(enableAtStart: true);
            return s_Instance != null && s_Instance.enabled;  // Return enabled state after init
        }

        if (s_Instance == null)
        {
            Debug.LogWarning("[GAS Debug Overlay] Failed to toggle. Instance is null.");
            return false;
        }

        // Toggle the state on subsequent calls
        s_Instance.enabled = !s_Instance.enabled;
        return s_Instance.enabled;
    }

    /// <summary>
    /// Set the overlay enabled state. 0GC operation.
    /// </summary>
    public static void SetEnabled(bool enabled)
    {
        if (s_Instance != null)
            s_Instance.enabled = enabled;
    }

    /// <summary>
    /// <summary>
    /// Destroy the overlay and free resources. Safe to call multiple times.
    /// </summary>
    public static void Cleanup()
    {
        if (s_Instance != null)
        {
            Destroy(s_Instance.gameObject);
            s_Instance = null;
        }
        s_Initialized = false;
    }

    #endregion

    #region Configuration

    [System.NonSerialized] public int MinPriority;

        private static readonly Dictionary<AbilitySystemComponent, int> s_PriorityMap = new Dictionary<AbilitySystemComponent, int>();

        public static void SetPriority(AbilitySystemComponent asc, int priority)
        {
            if (asc == null) return;
            s_PriorityMap[asc] = priority;
        }

        public static void ClearPriority(AbilitySystemComponent asc)
        {
            if (asc != null) s_PriorityMap.Remove(asc);
        }

        #endregion

        #region State

        private readonly List<DiscoveredASC> discoveredASCs = new List<DiscoveredASC>();
        private float lastScanTime;
        private const float ScanInterval = 1f;
        private const float CameraLookupInterval = 0.5f;
        private float lastCameraLookupTime = -100f;
        private Camera cachedMainCamera;

        private bool showConfig;

        private bool showAttributes = true;
        private bool showEffects = true;
        private bool showTags = true;
        private bool showAbilities = true;
        private float runtimeAlpha = 0.8f;
        private float runtimeScale = 1f;
        private readonly HashSet<int> collapsedPanels = new HashSet<int>();
        private GASOverlayConfig config;

        // Dragging system (0GC design - reusable storage, pre-allocated bounds)
        private int currentlyDraggingPanelKey = -1;
        private Vector2 cachedMousePos = Vector2.zero;
        private Vector2 dragStartMousePos = Vector2.zero;  // Position where drag started
        private Vector2 dragStartPanelOffset = Vector2.zero;
        private readonly Dictionary<int, Vector2> panelDragOffsets = new Dictionary<int, Vector2>(8);
        // Stable layout (auto-layout only on first appearance)
        private readonly Dictionary<int, Vector2> panelAnchorPositions = new Dictionary<int, Vector2>(8);
        private bool autoLayoutCursorInitialized;
        private float nextAutoLayoutX;
        private float nextAutoLayoutY;
        private float lastAppliedLayoutScale = -1f;
        // Reusable buffers for 0GC-ish periodic cleanup (Scan interval).
        private readonly HashSet<int> activePanelKeys = new HashSet<int>();
        private readonly List<int> stalePanelKeyBuffer = new List<int>(16);

        private readonly StringBuilder sb = new StringBuilder(512);
        private readonly Dictionary<string, GameplayAttribute> attributeLookupBuffer = new Dictionary<string, GameplayAttribute>(32);
        private GUIStyle headerStyle;
        private GUIStyle compactHeaderStyle;
        private GUIStyle subHeaderStyle;
        private GUIStyle labelStyle;
        private GUIStyle smallLabelStyle;
        private GUIStyle valueStyle;
        private GUIStyle smallValueStyle;
        private GUIStyle panelStyle;
        private GUIStyle configBtnStyle;
        private GUIStyle tooltipStyle;
        private GUIStyle cfgHeaderStyle;
        private Texture2D barBgTex;
        private Texture2D barFillTex;
        private Texture2D panelBgTex;
        private bool stylesInitialized;
        private float lastAlphaForTex;
        private readonly Dictionary<int, string> hexColorCache = new Dictionary<int, string>(32);
        private readonly Dictionary<string, string> shortNameCache = new Dictionary<string, string>(32);

        private struct DiscoveredASC
        {
            public AbilitySystemComponent ASC;
            public string DisplayName;
            public int Priority;
            public GameObject OwnerGO;
            public Transform TrackTarget;
        }

        #endregion

        #region Lifecycle

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            s_Instance = this;
            config = GASOverlayConfig.Load();
            if (config != null) runtimeAlpha = config.PanelAlpha;
        }

        private void OnDestroy()
        {
            if (s_Instance == this) s_Instance = null;
            if (barBgTex != null) Destroy(barBgTex);
            if (barFillTex != null) Destroy(barFillTex);
            if (panelBgTex != null) Destroy(panelBgTex);
        }

        private void Update()
        {
            if (Time.unscaledTime - lastScanTime > ScanInterval)
            {
                lastScanTime = Time.unscaledTime;
                ScanASCs();
            }
        }

        #endregion

        #region ASC Discovery

        // Reflection cache: avoids re-allocating PropertyInfo[]/FieldInfo[] arrays every scan
        private struct CachedTypeData
        {
            public System.Reflection.PropertyInfo[] Props;
            public System.Reflection.FieldInfo[] Fields;
        }
        private static readonly Dictionary<System.Type, CachedTypeData> s_TypeCache
            = new Dictionary<System.Type, CachedTypeData>();

        private static CachedTypeData GetCachedTypeData(System.Type type)
        {
            if (s_TypeCache.TryGetValue(type, out var data)) return data;

            const System.Reflection.BindingFlags flags
                = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance;

            List<System.Reflection.PropertyInfo> props = null;
            foreach (var p in type.GetProperties(flags))
            {
                if (p.PropertyType == typeof(AbilitySystemComponent) && p.CanRead)
                    (props ??= new List<System.Reflection.PropertyInfo>(1)).Add(p);
            }

            List<System.Reflection.FieldInfo> fields = null;
            foreach (var f in type.GetFields(flags))
            {
                if (f.FieldType == typeof(AbilitySystemComponent))
                    (fields ??= new List<System.Reflection.FieldInfo>(1)).Add(f);
            }

            data = new CachedTypeData
            {
                Props = props?.ToArray(),
                Fields = fields?.ToArray()
            };
            s_TypeCache[type] = data;
            return data;
        }

        private void ScanASCs()
        {
            discoveredASCs.Clear();

            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb == null) continue;
                var data = GetCachedTypeData(mb.GetType());

                if (data.Props != null)
                {
                    for (int i = 0; i < data.Props.Length; i++)
                    {
                        try
                        {
                            var asc = data.Props[i].GetValue(mb) as AbilitySystemComponent;
                            if (asc != null && !ContainsASC(asc))
                                AddDiscoveredASC(asc, mb);
                        }
                        catch { }
                    }
                }

                if (data.Fields != null)
                {
                    for (int i = 0; i < data.Fields.Length; i++)
                    {
                        try
                        {
                            var asc = data.Fields[i].GetValue(mb) as AbilitySystemComponent;
                            if (asc != null && !ContainsASC(asc))
                                AddDiscoveredASC(asc, mb);
                        }
                        catch { }
                    }
                }
            }

            discoveredASCs.Sort(ComparePriority);
            CleanupTransientPanelState();
        }

        private void CleanupTransientPanelState()
        {
            activePanelKeys.Clear();
            for (int i = 0; i < discoveredASCs.Count; i++)
            {
                int key = GetPanelKey(discoveredASCs[i]);
                activePanelKeys.Add(key);
            }

            stalePanelKeyBuffer.Clear();
            foreach (var kv in panelDragOffsets)
            {
                if (!activePanelKeys.Contains(kv.Key)) stalePanelKeyBuffer.Add(kv.Key);
            }
            for (int i = 0; i < stalePanelKeyBuffer.Count; i++) panelDragOffsets.Remove(stalePanelKeyBuffer[i]);

            stalePanelKeyBuffer.Clear();
            foreach (var kv in panelAnchorPositions)
            {
                if (!activePanelKeys.Contains(kv.Key)) stalePanelKeyBuffer.Add(kv.Key);
            }
            for (int i = 0; i < stalePanelKeyBuffer.Count; i++) panelAnchorPositions.Remove(stalePanelKeyBuffer[i]);

            stalePanelKeyBuffer.Clear();
            foreach (var key in collapsedPanels)
            {
                if (!activePanelKeys.Contains(key)) stalePanelKeyBuffer.Add(key);
            }
            for (int i = 0; i < stalePanelKeyBuffer.Count; i++) collapsedPanels.Remove(stalePanelKeyBuffer[i]);

            if (currentlyDraggingPanelKey >= 0 && !activePanelKeys.Contains(currentlyDraggingPanelKey))
                currentlyDraggingPanelKey = -1;

            if (discoveredASCs.Count == 0)
                autoLayoutCursorInitialized = false;
        }

        private void EnsureAutoLayoutCursorInitialized(float margin)
        {
            if (autoLayoutCursorInitialized) return;
            nextAutoLayoutX = margin;
            nextAutoLayoutY = margin;
            autoLayoutCursorInitialized = true;
        }

        private Vector2 AllocateStackedAnchor(float panelWidth, float panelHeight, float margin, float panelSpacing, float layoutRightBound, bool stackedSingleColumn)
        {
            EnsureAutoLayoutCursorInitialized(margin);

            float bottomBound = Screen.height - margin;

            if (nextAutoLayoutY + panelHeight > bottomBound)
            {
                float nextColumnX = nextAutoLayoutX + panelWidth + panelSpacing;
                bool canMoveNextColumn = nextColumnX + panelWidth <= layoutRightBound;

                if (canMoveNextColumn)
                {
                    nextAutoLayoutX = nextColumnX;
                    nextAutoLayoutY = margin;
                }
                else if (stackedSingleColumn)
                {
                    // Strict single column: pin to bottom instead of moving all panels.
                    nextAutoLayoutY = Mathf.Clamp(nextAutoLayoutY, margin, bottomBound - panelHeight);
                }
                else
                {
                    // Last column full: pin into visible area (no disappearing, no global relayout).
                    nextAutoLayoutY = Mathf.Clamp(nextAutoLayoutY, margin, bottomBound - panelHeight);
                }
            }

            Vector2 anchor = new Vector2(nextAutoLayoutX, nextAutoLayoutY);
            nextAutoLayoutY += panelHeight + panelSpacing;
            return anchor;
        }

        private static int GetPanelKey(DiscoveredASC entry)
        {
            if (entry.OwnerGO != null)
                return entry.OwnerGO.GetInstanceID();

            // Fallback when owner is unavailable.
            return entry.ASC != null
                ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(entry.ASC)
                : 0;
        }

        private void SyncLayoutWithScale(float currentScale)
        {
            if (currentScale <= 0f)
                return;

            if (lastAppliedLayoutScale <= 0f)
            {
                lastAppliedLayoutScale = currentScale;
                return;
            }

            if (Mathf.Abs(currentScale - lastAppliedLayoutScale) < 0.001f)
                return;

            float ratio = currentScale / lastAppliedLayoutScale;
            if (!float.IsFinite(ratio) || ratio <= 0f)
            {
                lastAppliedLayoutScale = currentScale;
                return;
            }

            stalePanelKeyBuffer.Clear();
            foreach (var kv in panelAnchorPositions) stalePanelKeyBuffer.Add(kv.Key);
            for (int i = 0; i < stalePanelKeyBuffer.Count; i++)
            {
                int key = stalePanelKeyBuffer[i];
                panelAnchorPositions[key] = panelAnchorPositions[key] * ratio;
            }

            stalePanelKeyBuffer.Clear();
            foreach (var kv in panelDragOffsets) stalePanelKeyBuffer.Add(kv.Key);
            for (int i = 0; i < stalePanelKeyBuffer.Count; i++)
            {
                int key = stalePanelKeyBuffer[i];
                panelDragOffsets[key] = panelDragOffsets[key] * ratio;
            }

            dragStartPanelOffset *= ratio;
            nextAutoLayoutX *= ratio;
            nextAutoLayoutY *= ratio;

            lastAppliedLayoutScale = currentScale;
        }

        private bool ContainsASC(AbilitySystemComponent asc)
        {
            for (int i = 0; i < discoveredASCs.Count; i++)
            {
                if (discoveredASCs[i].ASC == asc) return true;
            }
            return false;
        }

        private void AddDiscoveredASC(AbilitySystemComponent asc, MonoBehaviour holder)
        {
            s_PriorityMap.TryGetValue(asc, out int priority);

            var go = holder.gameObject;
            string goName = go.name;
            if (goName.EndsWith("(Clone)"))
                goName = goName.Substring(0, goName.Length - 7);

            // Abbreviate verbose holder type names: strip known suffixes, then hard-cap at 16 chars
            string holderType = holder.GetType().Name;
            if (holderType.Length > 16)
            {
                if (holderType.EndsWith("ComponentHolder"))
                    holderType = holderType.Substring(0, holderType.Length - 15);
                else if (holderType.EndsWith("Component"))
                    holderType = holderType.Substring(0, holderType.Length - 9);
                else if (holderType.EndsWith("Holder"))
                    holderType = holderType.Substring(0, holderType.Length - 6);
                if (holderType.Length > 16)
                    holderType = string.Concat(holderType.Substring(0, 14), "..");
            }
            string displayName = string.Concat(goName, " [", holderType, "]");

            discoveredASCs.Add(new DiscoveredASC
            {
                ASC = asc,
                DisplayName = displayName,
                Priority = priority,
                OwnerGO = go,
                TrackTarget = go.transform
            });
        }

        #endregion

        #region Styles

        private void EnsureStyles()
        {
            if (stylesInitialized) return;
            stylesInitialized = true;

            float baseScale = GetUIScale();
            float scale = baseScale * runtimeScale;
            int baseFontSize = Mathf.RoundToInt(11 * scale);
            int headerFontSize = Mathf.RoundToInt(13 * scale);
            int smallFontSize = Mathf.RoundToInt(10 * scale);

            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = headerFontSize,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.97f, 1f, 0.99f) },
                richText = true,
                wordWrap = false,
                clipping = TextClipping.Overflow
            };

            compactHeaderStyle = new GUIStyle(headerStyle)
            {
                fontSize = Mathf.RoundToInt(12 * scale)
            };

            subHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = smallFontSize,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 1f, 1f) },
                richText = true
            };

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = baseFontSize,
                normal = { textColor = new Color(0.86f, 1f, 0.84f) },
                richText = true,
                clipping = TextClipping.Clip
            };

            smallLabelStyle = new GUIStyle(labelStyle)
            {
                fontSize = smallFontSize
            };

            valueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = baseFontSize,
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.90f, 1.0f, 0.92f) },
                richText = true
            };

            smallValueStyle = new GUIStyle(valueStyle)
            {
                fontSize = smallFontSize
            };

            configBtnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.RoundToInt(10 * baseScale),
                padding = new RectOffset(4, 4, 2, 2)
            };

            tooltipStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(9 * baseScale),
                fontStyle = FontStyle.Italic,
                normal = { textColor = new Color(0.72f, 0.86f, 0.74f) },
                alignment = TextAnchor.UpperLeft
            };

            cfgHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(11 * baseScale),
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 1f, 1f) },
                richText = true
            };

            if (barBgTex == null)
                barBgTex = MakeTex(1, 1, new Color(0.10f, 0.12f, 0.16f, 0.92f));
            if (barFillTex == null)
                barFillTex = MakeTex(1, 1, Color.white);
            if (panelBgTex == null)
            {
                lastAlphaForTex = runtimeAlpha;
                panelBgTex = MakeTex(1, 1, new Color(0.05f, 0.08f, 0.13f, runtimeAlpha));
            }

            panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = panelBgTex },
                padding = new RectOffset(4, 4, 3, 3)
            };
        }

        private void RefreshPanelAlpha()
        {
            if (Mathf.Abs(lastAlphaForTex - runtimeAlpha) < 0.01f) return;
            lastAlphaForTex = runtimeAlpha;
            if (panelBgTex != null) Destroy(panelBgTex);
            panelBgTex = MakeTex(1, 1, new Color(0.05f, 0.08f, 0.13f, runtimeAlpha));
            if (panelStyle != null) panelStyle.normal.background = panelBgTex;
        }

        private void InvalidateStyles()
        {
            stylesInitialized = false;
        }

        private static float GetUIScale()
        {
            float dpiScale = Screen.dpi > 0 ? Screen.dpi / 96f : 1f;
            float resScale = Screen.height / 1080f;
            return Mathf.Clamp(Mathf.Max(dpiScale, resScale), 0.75f, 2.5f);
        }

        private static Texture2D MakeTex(int w, int h, Color col)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            var pixels = new Color[w * h];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = col;
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }

        #endregion

        #region OnGUI

        private void OnGUI()
        {
            EnsureStyles();
            RefreshPanelAlpha();

            float baseScale = GetUIScale();
            float scale = baseScale * runtimeScale;
            SyncLayoutWithScale(scale);
            float widthRatio = config != null ? config.PanelWidthRatio : 0.20f;
            float minW = (config != null ? config.MinPanelWidth : 200f) * scale;
            float maxW = (config != null ? config.MaxPanelWidth : 360f) * scale;
            float panelWidth = Mathf.Clamp(Screen.width * widthRatio, minW, maxW);
            float margin = 6 * baseScale;
            float panelSpacing = 4 * scale;
            float lineH = 16 * scale;
            float barH = 8 * scale;
            int maxPanels = config != null ? config.MaxPanels : 8;
            bool preferTopLeftStack = config != null ? config.PreferTopLeftStackLayout : true;
            bool trackWorld = !preferTopLeftStack && (config == null || (config != null && config.TrackWorldPosition));
            bool stackedSingleColumn = config != null ? config.StackSingleColumn : true;
            bool drawStackedLinks = config != null ? config.DrawConnectionLinesInStackedMode : true;
            float stackedLinkAlpha = config != null ? config.StackedConnectionLineAlpha : 0.82f;

            // Config button uses baseScale only --stays fixed regardless of runtimeScale
            float configW = 44 * baseScale;
            float configH = 18 * baseScale;
            Rect configRect = new Rect(Screen.width - configW - margin, margin, configW, configH);
            if (GUI.Button(configRect, showConfig ? "X" : "GAS", configBtnStyle))
                showConfig = !showConfig;

            if (showConfig)
                DrawConfigPanel(baseScale, configRect, configH, margin, lineH);

            Camera cam = GetOverlayCamera();
            
            // Process dragging input (0GC per-frame caching)
            ProcessPanelDragging();

            float layoutRightBound = showConfig ? (configRect.xMin - margin) : (Screen.width - margin);
            if (layoutRightBound <= margin + panelWidth)
                layoutRightBound = Screen.width - margin;

            float maxAvailableHeight = Screen.height - margin * 2f;
            int drawn = 0;

            for (int i = 0; i < discoveredASCs.Count && drawn < maxPanels; i++)
            {
                var entry = discoveredASCs[i];
                if (entry.ASC == null) continue;
                if (entry.Priority < MinPriority) continue;

                int panelKey = GetPanelKey(entry);
                bool collapsed = collapsedPanels.Contains(panelKey);

                float panelHeight = collapsed
                    ? CalculateCollapsedHeight(entry.ASC, scale, lineH, barH)
                    : CalculatePanelHeight(entry.ASC, scale, lineH, barH);
                if (panelHeight <= 0) continue;

                // Clamp panel height to prevent it from exceeding screen bounds dramatically
                float maxPanelHeight = maxAvailableHeight;
                if (panelHeight > maxPanelHeight)
                    panelHeight = maxPanelHeight;

                Rect panelRect;
                bool shouldDrawLink = false;
                float linkTargetX = 0f;
                float linkTargetY = 0f;
                Color linkColor = Color.white;
                bool linkFromBottomCenter = false;

                if (trackWorld && cam != null && entry.TrackTarget != null)
                {
                    Vector3 worldPos = entry.TrackTarget.position;
                    Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                    if (screenPos.z < 0) continue;

                    float screenY = Screen.height - screenPos.y;
                    float offsetY = config != null ? config.WorldTrackingOffsetY * scale : 30f * scale;

                    float px = screenPos.x - panelWidth * 0.5f;
                    float py = screenY - panelHeight - offsetY;
                    px = Mathf.Clamp(px, margin, Screen.width - panelWidth - margin);
                    py = Mathf.Clamp(py, margin, Screen.height - panelHeight - margin);

                    panelRect = new Rect(px, py, panelWidth, panelHeight);

                    Color worldLineColor = config != null ? config.ConnectionLineCoreColor : new Color(0.985f, 0.992f, 1f, 1f);
                    worldLineColor.a = Mathf.Max(0.55f, stackedLinkAlpha) * runtimeAlpha;
                    shouldDrawLink = true;
                    linkTargetX = screenPos.x;
                    linkTargetY = screenY;
                    linkColor = worldLineColor;
                    linkFromBottomCenter = true;
                }
                else
                {
                    if (!panelAnchorPositions.TryGetValue(panelKey, out Vector2 anchor))
                    {
                        // Auto-layout only once when panel first appears.
                        anchor = AllocateStackedAnchor(panelWidth, panelHeight, margin, panelSpacing, layoutRightBound, stackedSingleColumn);
                        panelAnchorPositions[panelKey] = anchor;
                    }

                    panelRect = new Rect(anchor.x, anchor.y, panelWidth, panelHeight);

                    if (drawStackedLinks && cam != null && entry.TrackTarget != null)
                    {
                        Vector3 worldPos = entry.TrackTarget.position;
                        Vector3 screenPos = cam.WorldToScreenPoint(worldPos);
                        if (screenPos.z > 0f)
                        {
                            float screenY = Screen.height - screenPos.y;
                            Color stackedLineColor = config != null ? config.ConnectionLineCoreColor : new Color(0.985f, 0.992f, 1f, 1f);
                            stackedLineColor.a = stackedLinkAlpha * runtimeAlpha;
                            shouldDrawLink = true;
                            linkTargetX = screenPos.x;
                            linkTargetY = screenY;
                            linkColor = stackedLineColor;
                            linkFromBottomCenter = false;
                        }
                    }
                }

                // Apply drag offset if this panel is being dragged (0GC: just add offset)
                if (panelDragOffsets.TryGetValue(panelKey, out Vector2 dragOffset))
                {
                    panelRect.x += dragOffset.x;
                    panelRect.y += dragOffset.y;
                }

                // Final hard clamp (applies to both auto-layout and dragged panels)
                float maxX = Mathf.Max(margin, layoutRightBound - panelRect.width);
                float maxY = Mathf.Max(margin, Screen.height - panelRect.height - margin);
                panelRect.x = Mathf.Clamp(panelRect.x, margin, maxX);
                panelRect.y = Mathf.Clamp(panelRect.y, margin, maxY);

                if (shouldDrawLink)
                {
                    Vector2 from;
                    if (linkFromBottomCenter)
                    {
                        from = new Vector2(panelRect.x + panelRect.width * 0.5f, panelRect.yMax);
                    }
                    else
                    {
                        // Anchor from whichever horizontal edge is closer to the world target,
                        // so the line never has to cross back over the panel.
                        float panelCenterX = panelRect.x + panelRect.width * 0.5f;
                        float fromX = linkTargetX >= panelCenterX ? panelRect.xMax : panelRect.xMin;
                        from = new Vector2(fromX, panelRect.y + panelRect.height * 0.5f);
                    }
                    DrawConnectingLine(from, new Vector2(linkTargetX, linkTargetY), linkColor, scale);
                }

                if (collapsed)
                    DrawCollapsedPanel(panelRect, entry, panelKey, scale, lineH, barH);
                else
                    DrawASCPanel(panelRect, entry, panelKey, scale, lineH, barH);

                drawn++;
            }

            if (drawn == 0)
            {
                if (discoveredASCs.Count == 0)
                {
                    GUI.Label(new Rect(margin, margin + configH + 4, 300 * scale, lineH), "No ASC found in scene", tooltipStyle);
                }
                else
                {
                    sb.Clear();
                    AppendInt(sb, discoveredASCs.Count);
                    sb.Append(" ASC(s) filtered (MinPriority=");
                    AppendInt(sb, MinPriority);
                    sb.Append(')');
                    GUI.Label(new Rect(margin, margin + configH + 4, 300 * scale, lineH), sb.ToString(), tooltipStyle);
                }
            }
        }

        private void DrawConfigPanel(float baseScale, Rect configRect, float configH, float margin, float lineH)
        {
            float cfgW = 190 * baseScale;
            float cfgH = 510 * baseScale;
            Rect cfgRect = new Rect(Screen.width - cfgW - margin, margin + configH + 2, cfgW, cfgH);
            GUI.Box(cfgRect, GUIContent.none, panelStyle);
            GUILayout.BeginArea(new Rect(cfgRect.x + 8, cfgRect.y + 4, cfgRect.width - 16, cfgRect.height - 8));

            GUILayout.Label("Overlay Settings", cfgHeaderStyle);
            GUILayout.Space(2);

            showAttributes = GUILayout.Toggle(showAttributes, " Attributes");
            showEffects = GUILayout.Toggle(showEffects, " Effects");
            showTags = GUILayout.Toggle(showTags, " Tags");
            showAbilities = GUILayout.Toggle(showAbilities, " Abilities");

            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Alpha:", GUILayout.Width(44 * baseScale));
            float newAlpha = GUILayout.HorizontalSlider(runtimeAlpha, 0.15f, 1f);
            if (Mathf.Abs(newAlpha - runtimeAlpha) > 0.005f) runtimeAlpha = newAlpha;
            sb.Clear(); AppendFloat2(sb, runtimeAlpha);
            GUILayout.Label(sb.ToString(), GUILayout.Width(30 * baseScale));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Scale:", GUILayout.Width(44 * baseScale));
            float newScale = GUILayout.HorizontalSlider(runtimeScale, 0.5f, 1.5f);
            if (Mathf.Abs(newScale - runtimeScale) > 0.005f)
            {
                runtimeScale = newScale;
                InvalidateStyles();
            }
            sb.Clear(); AppendFloat2(sb, runtimeScale);
            GUILayout.Label(sb.ToString(), GUILayout.Width(30 * baseScale));
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            GUILayout.Label("Min Priority:", GUILayout.Width(72 * baseScale));
            sb.Clear(); AppendInt(sb, MinPriority);
            string priStr = GUILayout.TextField(sb.ToString(), GUILayout.Width(36 * baseScale));
            if (int.TryParse(priStr, out int newPri)) MinPriority = newPri;
            GUILayout.EndHorizontal();

            GUILayout.Space(4);

            if (config != null)
            {
                config.PreferTopLeftStackLayout = GUILayout.Toggle(config.PreferTopLeftStackLayout, " Top-Left Stacked Layout");
                if (config.PreferTopLeftStackLayout)
                {
                    config.StackSingleColumn = GUILayout.Toggle(config.StackSingleColumn, " Single Column");
                    config.DrawConnectionLinesInStackedMode = GUILayout.Toggle(config.DrawConnectionLinesInStackedMode, " Draw Connector Lines");

                    GUILayout.BeginHorizontal();
                    GUILayout.Label("Line Alpha:", GUILayout.Width(60 * baseScale));
                    float newLineAlpha = GUILayout.HorizontalSlider(config.StackedConnectionLineAlpha, 0.1f, 1f);
                    if (Mathf.Abs(newLineAlpha - config.StackedConnectionLineAlpha) > 0.005f)
                        config.StackedConnectionLineAlpha = newLineAlpha;
                    sb.Clear(); AppendFloat2(sb, config.StackedConnectionLineAlpha);
                    GUILayout.Label(sb.ToString(), GUILayout.Width(30 * baseScale));
                    GUILayout.EndHorizontal();
                }
                else
                {
                    config.TrackWorldPosition = GUILayout.Toggle(config.TrackWorldPosition, " Track World Position");
                }

                GUILayout.Space(2);
                config.UseHighContrastConnectionLines = GUILayout.Toggle(config.UseHighContrastConnectionLines, " High-Contrast Connector");

                GUILayout.BeginHorizontal();
                GUILayout.Label("Line Width:", GUILayout.Width(60 * baseScale));
                float newLineWidth = GUILayout.HorizontalSlider(config.ConnectionLineThickness, 0.5f, 4f);
                if (Mathf.Abs(newLineWidth - config.ConnectionLineThickness) > 0.005f)
                    config.ConnectionLineThickness = newLineWidth;
                sb.Clear(); AppendFloat2(sb, config.ConnectionLineThickness);
                GUILayout.Label(sb.ToString(), GUILayout.Width(30 * baseScale));
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(4);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Collapse All", GUILayout.Height(18 * baseScale)))
            {
                for (int i = 0; i < discoveredASCs.Count; i++)
                        collapsedPanels.Add(GetPanelKey(discoveredASCs[i]));
            }
            if (GUILayout.Button("Expand All", GUILayout.Height(18 * baseScale)))
            {
                collapsedPanels.Clear();
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(4);
            string configStatus = config != null ? "Config loaded" : "No config (defaults)";
            GUILayout.Label(configStatus, tooltipStyle);
            if (config == null && GUILayout.Button("Reload", GUILayout.Height(16 * baseScale)))
                config = GASOverlayConfig.Load();

            GUILayout.EndArea();
        }

        private void DrawConnectingLine(Vector2 from, Vector2 to, Color color, float scale)
        {
            if (barFillTex == null || color.a <= 0.001f) return;

            Vector2 delta = to - from;
            float distSq = delta.sqrMagnitude;
            if (distSq < 25f) return;

            if ((from.x < 0f && to.x < 0f) ||
                (from.x > Screen.width && to.x > Screen.width) ||
                (from.y < 0f && to.y < 0f) ||
                (from.y > Screen.height && to.y > Screen.height))
            {
                return;
            }

            float dist = Mathf.Sqrt(distSq);
            float baseThickness = Mathf.Max(1f, (config != null ? config.ConnectionLineThickness : 1.2f) * scale);
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            bool useHighContrast = config == null || config.UseHighContrastConnectionLines;

            var matrixBackup = GUI.matrix;
            var savedColor = GUI.color;
            GUIUtility.RotateAroundPivot(angle, from);

            // Draw dark outline (high contrast)
            if (useHighContrast)
            {
                Color darkOutline = config != null ? config.ConnectionLineDarkOutlineColor : new Color(0f, 0f, 0f, 0.85f);
                darkOutline.a *= color.a;
                GUI.color = darkOutline;
                GUI.DrawTexture(new Rect(from.x, from.y - (baseThickness * 2.1f) * 0.5f, dist, baseThickness * 2.1f), barFillTex);

                // Draw light outline
                Color lightOutline = config != null ? config.ConnectionLineLightOutlineColor : new Color(1f, 1f, 1f, 0.65f);
                GUI.color = lightOutline;
                GUI.DrawTexture(new Rect(from.x, from.y - (baseThickness * 1.5f) * 0.5f, dist, baseThickness * 1.5f), barFillTex);
            }

            // Draw main line (straight line - no curves)
            GUI.color = color;
            GUI.DrawTexture(new Rect(from.x, from.y - baseThickness * 0.5f, dist, baseThickness), barFillTex);
            GUI.matrix = matrixBackup;
            GUI.color = savedColor;

            // Endpoint markers improve association speed in dense scenes.
            float markerSize = Mathf.Clamp(baseThickness * 2.2f, 3f, 8f);
            DrawConnectionEndpoint(from, markerSize, color);
            DrawConnectionEndpoint(to, markerSize, color);
        }


        private void DrawConnectionEndpoint(Vector2 position, float size, Color coreColor)
        {
            if (barFillTex == null) return;

            var saved = GUI.color;

            float outer = size + 3f;
            Rect outerRect = new Rect(position.x - outer * 0.5f, position.y - outer * 0.5f, outer, outer);
            GUI.color = new Color(0f, 0f, 0f, Mathf.Clamp01(coreColor.a));
            GUI.DrawTexture(outerRect, barFillTex);

            float inner = size + 1.5f;
            Rect innerRect = new Rect(position.x - inner * 0.5f, position.y - inner * 0.5f, inner, inner);
            GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01(coreColor.a));
            GUI.DrawTexture(innerRect, barFillTex);

            Rect coreRect = new Rect(position.x - size * 0.5f, position.y - size * 0.5f, size, size);
            GUI.color = coreColor;
            GUI.DrawTexture(coreRect, barFillTex);

            GUI.color = saved;
        }

        /// <summary>
        /// Process panel dragging with 0GC design:
        /// - Caches mouse position per-frame to avoid repeated Input.mousePosition calls
        /// - Updates drag offsets for currently dragging panel
        /// - Handles mouse up to stop dragging
        /// Per-frame 0GC cost: one Vector2 update + one dictionary lookup
        /// </summary>
        private void ProcessPanelDragging()
        {
            // Cache IMGUI-space mouse position for hover detection.
            // MouseDrag / MouseUp are handled inside DrawASCPanel and DrawCollapsedPanel via
            // GUIUtility.hotControl so that drag events are reliably captured in the Unity editor.
            cachedMousePos = Event.current.mousePosition;
        }

        private Camera GetOverlayCamera()
        {
            if (Time.unscaledTime - lastCameraLookupTime > CameraLookupInterval || cachedMainCamera == null)
            {
                lastCameraLookupTime = Time.unscaledTime;
                cachedMainCamera = Camera.main;
            }

            return cachedMainCamera;
        }

        #endregion

        #region Collapsed Panel

        private float CalculateCollapsedHeight(AbilitySystemComponent asc, float scale, float lineH, float barH)
        {
            return lineH + 6 * scale;
        }

        private void DrawCollapsedPanel(Rect rect, DiscoveredASC entry, int panelKey, float scale, float lineH, float barH)
        {
            var asc = entry.ASC;
            // Stable control ID for this panel's drag interaction.
            int dragControlID = GUIUtility.GetControlID(FocusType.Passive);

            // Highlight if dragging or hovering (0GC: no allocation)
            bool isPanelHovered = rect.Contains(cachedMousePos);
            if (GUIUtility.hotControl == dragControlID)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.9f);
            }
            else if (isPanelHovered)
            {
                GUI.color = new Color(0.8f, 0.95f, 1f, 0.8f);
            }
            
            GUI.Box(rect, GUIContent.none, panelStyle);
            GUI.color = Color.white;

            float x = rect.x + 6 * scale;
            float y = rect.y + 3 * scale;
            float w = rect.width - 12 * scale;

            sb.Clear();
            sb.Append("\u25B8 <b>").Append(entry.DisplayName).Append("</b>");

            // Select primary attribute: match PrimaryAttributeSubstrings in order, fallback to first
            if (asc.AttributeSets != null)
            {
                GameplayAttribute bestAttr = null;
                bool found = false;
                
                // Use config's primary attribute preferences, or just fallback to first attribute if no config
                var primarySubs = config?.PrimaryAttributeSubstrings;
                int primaryCount = primarySubs != null ? primarySubs.Count : 0;

                foreach (var attrSet in asc.AttributeSets)
                {
                    foreach (var attr in attrSet.GetAttributes())
                    {
                        if (bestAttr == null) bestAttr = attr;
                        // Try matching against config's preference list (if available)
                        if (!found && primaryCount > 0)
                        {
                            string n = attr.Name;
                            for (int pi = 0; pi < primaryCount; pi++)
                            {
                                if (!string.IsNullOrEmpty(primarySubs[pi]) && n.Contains(primarySubs[pi]))
                                {
                                    bestAttr = attr;
                                    found = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                if (bestAttr != null)
                {
                    sb.Append("  <color=#B8FFD0>");
                    sb.Append(GetShortName(bestAttr.Name)).Append(':');
                    AppendInt(sb, Mathf.RoundToInt(bestAttr.CurrentValue));
                    if (System.Math.Abs(bestAttr.BaseValue - bestAttr.CurrentValue) > 0.01f)
                    {
                        sb.Append('/');
                        AppendInt(sb, Mathf.RoundToInt(bestAttr.BaseValue));
                    }
                    sb.Append("</color>");
                }
            }

            int effectCount = asc.ActiveEffects != null ? asc.ActiveEffects.Count : 0;
            var abilities = asc.GetActivatableAbilities();
            int abilityCount = abilities != null ? abilities.Count : 0;
            if (effectCount > 0 || abilityCount > 0)
            {
                sb.Append("  <color=#B7E6FF>");
                if (effectCount > 0) { sb.Append('E'); AppendInt(sb, effectCount); sb.Append(' '); }
                if (abilityCount > 0) { sb.Append('A'); AppendInt(sb, abilityCount); }
                sb.Append("</color>");
            }

            Rect headerRect = new Rect(x, y, w, lineH);
            float toggleW = Mathf.Max(12f * scale, lineH * 0.9f);
            Rect toggleRect = new Rect(x, y, toggleW, lineH);
            GUI.Label(headerRect, sb.ToString(), compactHeaderStyle);

            // Handle mouse interaction
            if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 0 && toggleRect.Contains(Event.current.mousePosition))
                {
                    // Left-click triangle area: expand/collapse only.
                    collapsedPanels.Remove(panelKey);
                    Event.current.Use();
                }
                else if (Event.current.button == 0)  // Left mouse button = drag on header body
                {
                    GUIUtility.hotControl = dragControlID;  // Claim mouse so editor routes drag events here
                    currentlyDraggingPanelKey = panelKey;
                    dragStartMousePos = Event.current.mousePosition;
                    if (!panelDragOffsets.TryGetValue(panelKey, out dragStartPanelOffset))
                    {
                        dragStartPanelOffset = Vector2.zero;
                        panelDragOffsets[panelKey] = Vector2.zero;
                    }
                    Event.current.Use();
                }
                else if (Event.current.button == 1)  // Right mouse button = expand
                {
                    collapsedPanels.Remove(panelKey);
                    Event.current.Use();
                }
            }
            else if (Event.current.type == EventType.MouseDrag && GUIUtility.hotControl == dragControlID)
            {
                panelDragOffsets[panelKey] = dragStartPanelOffset + (Event.current.mousePosition - dragStartMousePos);
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseUp && GUIUtility.hotControl == dragControlID)
            {
                GUIUtility.hotControl = 0;
                currentlyDraggingPanelKey = -1;
                Event.current.Use();
            }
        }

        #endregion

        #region Expanded Panel

        private float CalculatePanelHeight(AbilitySystemComponent asc, float scale, float lineH, float barH)
        {
            float h = lineH + 4 * scale; // header row + separator

            if (showAttributes && asc.AttributeSets != null)
            {
                foreach (var attrSet in asc.AttributeSets)
                {
                    foreach (var _ in attrSet.GetAttributes())
                        h += lineH + 1 * scale; // single-row attribute
                }
                h += 2 * scale;
            }

            if (showEffects && asc.ActiveEffects != null)
            {
                h += lineH; // section header
                h += Mathf.Max(asc.ActiveEffects.Count, 1) * (lineH * 0.9f);
            }

            if (showAbilities)
            {
                var abilities = asc.GetActivatableAbilities();
                if (abilities != null && abilities.Count > 0)
                {
                    h += lineH;
                    h += abilities.Count * (lineH * 0.9f);
                }
            }

            if (showTags)
            {
                h += lineH;
                if (asc.CombinedTags != null)
                {
                    int tagCount = 0;
                    foreach (var _ in asc.CombinedTags.GetExplicitTags()) tagCount++;
                    h += Mathf.Max(tagCount, 1) * (lineH * 0.9f);
                }
            }

            // Extra bottom padding avoids descender clipping on the final row.
            return h + 12 * scale;
        }

        private void DrawASCPanel(Rect rect, DiscoveredASC entry, int panelKey, float scale, float lineH, float barH)
        {
            var asc = entry.ASC;
            // Stable control ID for this panel's drag interaction (sequential, consistent per draw order).
            int dragControlID = GUIUtility.GetControlID(FocusType.Passive);

            // Highlight if dragging or hovering (0GC: no allocation)
            bool isPanelHovered = rect.Contains(cachedMousePos);
            if (GUIUtility.hotControl == dragControlID)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.9f);
            }
            else if (isPanelHovered)
            {
                GUI.color = new Color(0.8f, 0.95f, 1f, 0.8f);
            }
            
            GUI.Box(rect, GUIContent.none, panelStyle);
            GUI.color = Color.white;

            float x = rect.x + 6 * scale;
            float y = rect.y + 3 * scale;
            float w = rect.width - 12 * scale;

            Rect headerRect = new Rect(x, y, w, lineH);
            float toggleW = Mathf.Max(12f * scale, lineH * 0.9f);
            Rect toggleRect = new Rect(x, y, toggleW, lineH);
            sb.Clear();
            sb.Append("\u25BE <b>").Append(entry.DisplayName).Append("</b>");
            if (entry.Priority != 0)
            {
                sb.Append(" <color=#B7E6FF>[P:");
                AppendInt(sb, entry.Priority);
                sb.Append("]</color>");
            }
            GUI.Label(headerRect, sb.ToString(), headerStyle);

            // Handle mouse interaction
            if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
            {
                if (Event.current.button == 0 && toggleRect.Contains(Event.current.mousePosition))
                {
                    // Left-click triangle area: expand/collapse only.
                    collapsedPanels.Add(panelKey);
                    Event.current.Use();
                }
                else if (Event.current.button == 0)  // Left mouse button = drag on header body
                {
                    GUIUtility.hotControl = dragControlID;  // Claim mouse so editor routes drag events here
                    currentlyDraggingPanelKey = panelKey;
                    dragStartMousePos = Event.current.mousePosition;
                    if (!panelDragOffsets.TryGetValue(panelKey, out dragStartPanelOffset))
                    {
                        dragStartPanelOffset = Vector2.zero;
                        panelDragOffsets[panelKey] = Vector2.zero;
                    }
                    Event.current.Use();
                }
                else if (Event.current.button == 1)  // Right mouse button = collapse
                {
                    collapsedPanels.Add(panelKey);
                    Event.current.Use();
                }
            }
            else if (Event.current.type == EventType.MouseDrag && GUIUtility.hotControl == dragControlID)
            {
                panelDragOffsets[panelKey] = dragStartPanelOffset + (Event.current.mousePosition - dragStartMousePos);
                Event.current.Use();
            }
            else if (Event.current.type == EventType.MouseUp && GUIUtility.hotControl == dragControlID)
            {
                GUIUtility.hotControl = 0;
                currentlyDraggingPanelKey = -1;
                Event.current.Use();
            }
            y += lineH;

            DrawHLine(x, y, w, scale, new Color(0.44f, 0.60f, 0.82f, 0.40f));
            y += 3 * scale;

            if (showAttributes && asc.AttributeSets != null)
            {
                BuildAttributeLookup(asc, attributeLookupBuffer);
                foreach (var attrSet in asc.AttributeSets)
                {
                    foreach (var attr in attrSet.GetAttributes())
                    {
                        DrawAttributeRow(x, y, w, lineH, barH, attr, attributeLookupBuffer, scale);
                        y += lineH + 1 * scale;
                    }
                }
                attributeLookupBuffer.Clear();
                y += 1 * scale;
            }

            if (showEffects)
            {
                GUI.Label(new Rect(x, y, w, lineH), "Effects:", subHeaderStyle);
                y += lineH;

                float rowH = lineH * 0.9f;
                if (asc.ActiveEffects != null && asc.ActiveEffects.Count > 0)
                {
                    foreach (var effect in asc.ActiveEffects)
                    {
                        if (effect?.Spec?.Def == null) continue;
                        DrawEffectLine(x, y, w, rowH, effect, scale);
                        y += rowH;
                    }
                }
                else
                {
                    GUI.Label(new Rect(x + 8 * scale, y, w, rowH), "- None", smallLabelStyle);
                    y += rowH;
                }
            }

            if (showAbilities)
            {
                var abilities = asc.GetActivatableAbilities();
                if (abilities != null && abilities.Count > 0)
                {
                    float secH = lineH;
                    GUI.Label(new Rect(x, y, w, secH), "Abilities:", subHeaderStyle);
                    y += secH;

                    float rowH = lineH * 0.9f;
                    foreach (var spec in abilities)
                    {
                        if (spec?.Ability == null) continue;
                        DrawAbilityLine(x, y, w, rowH, asc, spec, scale);
                        y += rowH;
                    }
                }
            }

            if (showTags && asc.CombinedTags != null)
            {
                GUI.Label(new Rect(x, y, w, lineH), "Tags:", subHeaderStyle);
                y += lineH;

                float rowH = lineH * 0.9f;
                bool hasAny = false;
                foreach (var tag in asc.CombinedTags.GetExplicitTags())
                {
                    hasAny = true;
                    string tagName = tag.Name;

                    // Get tag color from config - use config's semantic classification
                    Color tagColor = config != null 
                        ? config.GetTagColor(tagName) 
                        : Color.white;  // Generic fallback (not Sample-specific)
                    string hex = GetHexCached(tagColor);

                    int count = asc.CombinedTags.GetExplicitTagCount(tag);
                    sb.Clear();
                    sb.Append("  <color=").Append(hex).Append(">").Append(tagName).Append("</color>");
                    if (count > 1) { sb.Append(" x"); AppendInt(sb, count); }

                    GUI.Label(new Rect(x, y, w, rowH), sb.ToString(), smallLabelStyle);
                    y += rowH;
                }

                if (!hasAny)
                {
                    GUI.Label(new Rect(x + 8 * scale, y, w, rowH), "- None", smallLabelStyle);
                    y += rowH;
                }
            }
        }

        #endregion

        #region Drawing Helpers

        // Single-row attribute layout: [Name 32%] [Bar 40%] [Value 28%]
        private void DrawAttributeRow(float x, float y, float w, float lineH, float barH, GameplayAttribute attr, Dictionary<string, GameplayAttribute> attributeLookup, float scale)
        {
            string name = GetShortName(attr.Name);

            float nameW = w * 0.32f;
            float barW = w * 0.40f;
            float valW = w * 0.28f;
            float barX = x + nameW;
            float valX = barX + barW + 2 * scale;

            sb.Clear();
            sb.Append("<color=#C7FFCB>").Append(name).Append("</color>");
            GUI.Label(new Rect(x, y, nameW - 2, lineH), sb.ToString(), smallLabelStyle);

            float inlineBarH = Mathf.Max(barH, 6 * scale);
            float barY = y + (lineH - inlineBarH) * 0.5f;

            // Professional resource normalization:
            // 1) Current/MaxAttribute (configured mapping) -> best for Health/Mana/Stamina style bars
            // 2) Current/BaseValue -> stable fallback when no Max attribute exists
            // 3) Dynamic max(Current, Base, 1) -> safe generic fallback for unknown attributes
            bool hasMappedMax = TryResolveMaxValue(attr, attributeLookup, out float mappedMax);
            float denominator;
            if (hasMappedMax)
                denominator = Mathf.Max(mappedMax, 0.0001f);
            else if (attr.BaseValue > 0.0001f)
                denominator = attr.BaseValue;
            else
                denominator = Mathf.Max(attr.BaseValue, attr.CurrentValue, 1f);

            float fill = Mathf.Clamp01(attr.CurrentValue / denominator);

            GUI.DrawTexture(new Rect(barX, barY, barW, inlineBarH), barBgTex);

            // Use a simple, generic bar coloring strategy:
            // - Based on fill percentage, not Sample-specific values
            // - Over-capacity renders with accent, but uses theme colors from config if available
            Color barColor;
            if (attr.CurrentValue > denominator + 0.01f)
            {
                // Over-capacity state - use a neutral accent color
                barColor = config?.ConnectionLineCoreColor ?? new Color(0.6f, 0.7f, 1f, 1f);
            }
            else if (fill <= 0f)
            {
                // Empty
                barColor = Color.gray;
            }
            else if (fill <= 0.33f)
            {
                // Low - use config's debuff color if available (warning indicator)
                barColor = config?.DebuffEffectColor ?? new Color(1f, 0.3f, 0.3f, 1f);
            }
            else if (fill <= 0.66f)
            {
                // Mid - neutral color
                barColor = new Color(0.5f, 0.7f, 1f, 1f);
            }
            else
            {
                // Full - use config's normal effect color if available (healthy indicator)
                barColor = config?.NormalEffectColor ?? new Color(0.3f, 1f, 0.5f, 1f);
            }
            
            var savedColor = GUI.color;
            GUI.color = barColor;
            GUI.DrawTexture(new Rect(barX, barY, barW * fill, inlineBarH), barFillTex);
            GUI.color = savedColor;

            sb.Clear();
            AppendFloat1(sb, attr.CurrentValue);
            if (hasMappedMax || System.Math.Abs(attr.BaseValue - attr.CurrentValue) > 0.01f)
            {
                sb.Append('/');
                AppendFloat1(sb, denominator);
            }
            GUI.Label(new Rect(valX, y, valW, lineH), sb.ToString(), smallValueStyle);
        }

        private static void BuildAttributeLookup(AbilitySystemComponent asc, Dictionary<string, GameplayAttribute> lookup)
        {
            lookup.Clear();
            if (asc == null || asc.AttributeSets == null) return;

            foreach (var set in asc.AttributeSets)
            {
                foreach (var attribute in set.GetAttributes())
                {
                    if (attribute == null || string.IsNullOrEmpty(attribute.Name)) continue;
                    if (!lookup.ContainsKey(attribute.Name))
                        lookup.Add(attribute.Name, attribute);
                }
            }
        }

        private bool TryResolveMaxValue(GameplayAttribute currentAttr, Dictionary<string, GameplayAttribute> attributeLookup, out float maxValue)
        {
            maxValue = 0f;
            if (currentAttr == null || attributeLookup == null || attributeLookup.Count == 0)
                return false;

            if (config != null && config.PreferMaxAttributeForBars)
            {
                if (config.TryGetMappedMaxAttributeName(currentAttr.Name, out string mappedMaxToken))
                {
                    if (TryFindAttributeByPattern(attributeLookup, mappedMaxToken, out var mappedAttr) && mappedAttr.CurrentValue > 0f)
                    {
                        maxValue = mappedAttr.CurrentValue;
                        return true;
                    }
                }
            }

            string shortName = GetShortName(currentAttr.Name);
            if (!string.IsNullOrEmpty(shortName))
            {
                string maxShortName = "Max" + shortName;

                // Prefer exact namespaced match first: Namespace.Health -> Namespace.MaxHealth
                int dot = currentAttr.Name.LastIndexOf('.');
                if (dot >= 0 && dot < currentAttr.Name.Length - 1)
                {
                    string namespacedMax = currentAttr.Name.Substring(0, dot + 1) + maxShortName;
                    if (attributeLookup.TryGetValue(namespacedMax, out var namespacedAttr) && namespacedAttr.CurrentValue > 0f)
                    {
                        maxValue = namespacedAttr.CurrentValue;
                        return true;
                    }
                }

                if (TryFindAttributeByPattern(attributeLookup, maxShortName, out var fallbackAttr) && fallbackAttr.CurrentValue > 0f)
                {
                    maxValue = fallbackAttr.CurrentValue;
                    return true;
                }
            }

            return false;
        }

        private static bool TryFindAttributeByPattern(Dictionary<string, GameplayAttribute> attributeLookup, string pattern, out GameplayAttribute attribute)
        {
            attribute = null;
            if (string.IsNullOrEmpty(pattern)) return false;

            foreach (var kv in attributeLookup)
            {
                if (string.Equals(kv.Key, pattern, System.StringComparison.OrdinalIgnoreCase))
                {
                    attribute = kv.Value;
                    return true;
                }
            }

            foreach (var kv in attributeLookup)
            {
                if (kv.Key.IndexOf(pattern, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    attribute = kv.Value;
                    return true;
                }
            }

            return false;
        }

        private void DrawEffectLine(float x, float y, float w, float lineH, ActiveGameplayEffect effect, float scale)
        {
            sb.Clear();
            sb.Append("  ");

            Color effectColor;
            bool isInhibited = effect.IsInhibited;
            bool isDebuff = config != null && config.IsDebuffEffect(effect.Spec.Def.GrantedTags);
            
            // Get color from config, or use fallback (no hardcoded Sample values)
            if (isDebuff && config != null)
                effectColor = config.DebuffEffectColor;
            else if (isInhibited && config != null)
                effectColor = config.InhibitedEffectColor;
            else if (config != null)
                effectColor = config.NormalEffectColor;
            else
                effectColor = Color.white;  // Generic fallback (not Sample-specific)

            sb.Append("<color=").Append(GetHexCached(effectColor)).Append('>').Append(effect.Spec.Def.Name).Append("</color>");

            if (effect.Spec.Def.DurationPolicy == EDurationPolicy.HasDuration)
            {
                sb.Append(" (");
                AppendFloat1(sb, effect.TimeRemaining);
                sb.Append("s)");
            }
            if (effect.StackCount > 1)
            {
                sb.Append(" [x");
                AppendInt(sb, effect.StackCount);
                sb.Append(']');
            }
            if (effect.IsInhibited)
                sb.Append(" [INH]");  // No hardcoded color in rich text

            GUI.Label(new Rect(x, y, w, lineH), sb.ToString(), smallLabelStyle);
        }

        private void DrawAbilityLine(float x, float y, float w, float lineH, AbilitySystemComponent asc, GameplayAbilitySpec spec, float scale)
        {
            sb.Clear();
            sb.Append("  ");

            string aName = !string.IsNullOrEmpty(spec.Ability.Name) ? spec.Ability.Name : spec.Ability.GetType().Name;

            if (spec.IsActive)
            {
                sb.Append("<color=#3DFFB2>").Append(aName).Append(" [ACTIVE]</color>");
            }
            else if (asc.IsAbilityOnCooldown(spec.Ability))
            {
                sb.Append("<color=#FF9BD6>").Append(aName).Append(" [CD ");
                AppendFloat1(sb, asc.GetCooldownTimeRemaining(spec.Ability));
                sb.Append("s]</color>");
            }
            else
            {
                sb.Append("<color=#999>").Append(aName).Append("</color>");
            }

            GUI.Label(new Rect(x, y, w, lineH), sb.ToString(), smallLabelStyle);
        }

        private void DrawHLine(float x, float y, float w, float scale, Color color)
        {
            var saved = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(x, y, w, 1), barFillTex != null ? barFillTex : Texture2D.whiteTexture);
            GUI.color = saved;
        }

        #endregion

        #region Zero-Alloc Helpers

        private string GetShortName(string fullName)
        {
            if (shortNameCache.TryGetValue(fullName, out string cached))
                return cached;
            int dot = fullName.LastIndexOf('.');
            string shortName = (dot >= 0 && dot < fullName.Length - 1) ? fullName.Substring(dot + 1) : fullName;
            shortNameCache[fullName] = shortName;
            return shortName;
        }

        private string GetHexCached(Color c)
        {
            int key = ((int)(c.r * 255f) << 16) | ((int)(c.g * 255f) << 8) | (int)(c.b * 255f);
            if (hexColorCache.TryGetValue(key, out string hex))
                return hex;
            hex = string.Concat("#", ColorUtility.ToHtmlStringRGB(c));
            hexColorCache[key] = hex;
            return hex;
        }

        private static void AppendInt(StringBuilder sb, int value)
        {
            if (value < 0) { sb.Append('-'); value = -value; }
            if (value == 0) { sb.Append('0'); return; }
            int start = sb.Length;
            while (value > 0) { sb.Append((char)('0' + value % 10)); value /= 10; }
            int end = sb.Length - 1;
            while (start < end)
            {
                char t = sb[start]; sb[start] = sb[end]; sb[end] = t;
                start++; end--;
            }
        }

        private static void AppendFloat1(StringBuilder sb, float value)
        {
            if (value < 0f) { sb.Append('-'); value = -value; }
            int scaled = (int)(value * 10f + 0.5f);
            AppendInt(sb, scaled / 10);
            sb.Append('.');
            sb.Append((char)('0' + scaled % 10));
        }

        private static void AppendFloat2(StringBuilder sb, float value)
        {
            if (value < 0f) { sb.Append('-'); value = -value; }
            int scaled = (int)(value * 100f + 0.5f);
            AppendInt(sb, scaled / 100);
            sb.Append('.');
            sb.Append((char)('0' + (scaled / 10) % 10));
            sb.Append((char)('0' + scaled % 10));
        }

        private static int ComparePriority(DiscoveredASC a, DiscoveredASC b)
        {
            return b.Priority.CompareTo(a.Priority);
        }

        #endregion
    }
}
