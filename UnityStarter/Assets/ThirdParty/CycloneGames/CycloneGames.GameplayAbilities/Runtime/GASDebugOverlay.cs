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
        #region Singleton

        private static GASDebugOverlay s_Instance;

        public static bool IsActive => s_Instance != null && s_Instance.enabled;

        public static void Toggle()
        {
            if (s_Instance != null)
            {
                s_Instance.enabled = !s_Instance.enabled;
                return;
            }

            var go = new GameObject("[GAS Debug Overlay]");
            s_Instance = go.AddComponent<GASDebugOverlay>();
            DontDestroyOnLoad(go);
        }

        public static void Destroy()
        {
            if (s_Instance != null)
            {
                Destroy(s_Instance.gameObject);
                s_Instance = null;
            }
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

        private bool showConfig;

        private bool showAttributes = true;
        private bool showEffects = true;
        private bool showTags = true;
        private bool showAbilities = true;
        private float runtimeAlpha = 0.55f;
        private float runtimeScale = 1f;
        private readonly HashSet<int> collapsedPanels = new HashSet<int>();
        private GASOverlayConfig config;

        private readonly StringBuilder sb = new StringBuilder(512);
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
                normal = { textColor = new Color(0.95f, 0.95f, 0.95f) },
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
                normal = { textColor = new Color(0.6f, 0.75f, 0.9f) },
                richText = true
            };

            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = baseFontSize,
                normal = { textColor = new Color(0.8f, 0.8f, 0.8f) },
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
                normal = { textColor = new Color(0.9f, 0.85f, 0.6f) },
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
                normal = { textColor = new Color(0.55f, 0.55f, 0.55f) },
                alignment = TextAnchor.UpperLeft
            };

            cfgHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.RoundToInt(11 * baseScale),
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.6f, 0.75f, 0.9f) },
                richText = true
            };

            if (barBgTex == null)
                barBgTex = MakeTex(1, 1, new Color(0.15f, 0.15f, 0.15f, 0.9f));
            if (barFillTex == null)
                barFillTex = MakeTex(1, 1, Color.white);
            if (panelBgTex == null)
            {
                lastAlphaForTex = runtimeAlpha;
                panelBgTex = MakeTex(1, 1, new Color(0.06f, 0.07f, 0.10f, runtimeAlpha));
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
            panelBgTex = MakeTex(1, 1, new Color(0.06f, 0.07f, 0.10f, runtimeAlpha));
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
            float widthRatio = config != null ? config.PanelWidthRatio : 0.20f;
            float minW = (config != null ? config.MinPanelWidth : 200f) * scale;
            float maxW = (config != null ? config.MaxPanelWidth : 360f) * scale;
            float panelWidth = Mathf.Clamp(Screen.width * widthRatio, minW, maxW);
            float margin = 6 * baseScale;
            float panelSpacing = 4 * scale;
            float lineH = 16 * scale;
            float barH = 8 * scale;
            int maxPanels = config != null ? config.MaxPanels : 8;
            bool trackWorld = config != null && config.TrackWorldPosition;

            // Config button uses baseScale only --stays fixed regardless of runtimeScale
            float configW = 44 * baseScale;
            float configH = 18 * baseScale;
            Rect configRect = new Rect(Screen.width - configW - margin, margin, configW, configH);
            if (GUI.Button(configRect, showConfig ? "X" : "GAS", configBtnStyle))
                showConfig = !showConfig;

            if (showConfig)
                DrawConfigPanel(baseScale, configRect, configH, margin, lineH);

            Camera cam = Camera.main;
            float fallbackX = margin;
            float fallbackY = margin;
            int drawn = 0;

            for (int i = 0; i < discoveredASCs.Count && drawn < maxPanels; i++)
            {
                var entry = discoveredASCs[i];
                if (entry.ASC == null) continue;
                if (entry.Priority < MinPriority) continue;

                int panelKey = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(entry.ASC);
                bool collapsed = collapsedPanels.Contains(panelKey);

                float panelHeight = collapsed
                    ? CalculateCollapsedHeight(entry.ASC, scale, lineH, barH)
                    : CalculatePanelHeight(entry.ASC, scale, lineH, barH);
                if (panelHeight <= 0) continue;

                Rect panelRect;

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

                    DrawConnectingLine(
                        new Vector2(panelRect.x + panelRect.width * 0.5f, panelRect.yMax),
                        new Vector2(screenPos.x, screenY),
                        new Color(0.5f, 0.7f, 1f, 0.3f * runtimeAlpha), scale);
                }
                else
                {
                    if (fallbackY + panelHeight > Screen.height - margin && drawn > 0)
                    {
                        fallbackX += panelWidth + panelSpacing;
                        fallbackY = margin;
                        if (fallbackX + panelWidth > Screen.width * 0.85f) break;
                    }
                    panelRect = new Rect(fallbackX, fallbackY, panelWidth, panelHeight);
                    fallbackY += panelHeight + panelSpacing;
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
            float cfgH = 260 * baseScale;
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

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Collapse All", GUILayout.Height(18 * baseScale)))
            {
                for (int i = 0; i < discoveredASCs.Count; i++)
                    collapsedPanels.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(discoveredASCs[i].ASC));
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
            float dist = Vector2.Distance(from, to);
            if (dist < 5f) return;

            var savedColor = GUI.color;
            GUI.color = color;

            float thickness = Mathf.Max(1f, 1.5f * scale);
            Vector2 dir = (to - from).normalized;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

            var matrixBackup = GUI.matrix;
            GUIUtility.RotateAroundPivot(angle, from);
            GUI.DrawTexture(new Rect(from.x, from.y - thickness * 0.5f, dist, thickness), barFillTex);
            GUI.matrix = matrixBackup;

            GUI.color = savedColor;
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
            GUI.Box(rect, GUIContent.none, panelStyle);

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
                var primarySubs = config != null ? config.PrimaryAttributeSubstrings : null;
                int primaryCount = primarySubs != null ? primarySubs.Count : 0;

                foreach (var attrSet in asc.AttributeSets)
                {
                    foreach (var attr in attrSet.GetAttributes())
                    {
                        if (bestAttr == null) bestAttr = attr;
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
                    sb.Append("  <color=#88CC88>");
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
                sb.Append("  <color=#8899AA>");
                if (effectCount > 0) { sb.Append('E'); AppendInt(sb, effectCount); sb.Append(' '); }
                if (abilityCount > 0) { sb.Append('A'); AppendInt(sb, abilityCount); }
                sb.Append("</color>");
            }

            Rect headerRect = new Rect(x, y, w, lineH);
            GUI.Label(headerRect, sb.ToString(), compactHeaderStyle);

            if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
            {
                collapsedPanels.Remove(panelKey);
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
                    h += Mathf.Max(tagCount, 1) * (lineH * 0.8f);
                }
            }

            return h + 8 * scale;
        }

        private void DrawASCPanel(Rect rect, DiscoveredASC entry, int panelKey, float scale, float lineH, float barH)
        {
            var asc = entry.ASC;
            GUI.Box(rect, GUIContent.none, panelStyle);

            float x = rect.x + 6 * scale;
            float y = rect.y + 3 * scale;
            float w = rect.width - 12 * scale;

            Rect headerRect = new Rect(x, y, w, lineH);
            sb.Clear();
            sb.Append("\u25BE <b>").Append(entry.DisplayName).Append("</b>");
            if (entry.Priority != 0)
            {
                sb.Append(" <color=#8899AA>[P:");
                AppendInt(sb, entry.Priority);
                sb.Append("]</color>");
            }
            GUI.Label(headerRect, sb.ToString(), headerStyle);

            if (Event.current.type == EventType.MouseDown && headerRect.Contains(Event.current.mousePosition))
            {
                collapsedPanels.Add(panelKey);
                Event.current.Use();
            }
            y += lineH;

            DrawHLine(x, y, w, scale, new Color(0.4f, 0.5f, 0.6f, 0.4f));
            y += 3 * scale;

            if (showAttributes && asc.AttributeSets != null)
            {
                foreach (var attrSet in asc.AttributeSets)
                {
                    foreach (var attr in attrSet.GetAttributes())
                    {
                        DrawAttributeRow(x, y, w, lineH, barH, attr, scale);
                        y += lineH + 1 * scale;
                    }
                }
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

                float rowH = lineH * 0.8f;
                bool hasAny = false;
                foreach (var tag in asc.CombinedTags.GetExplicitTags())
                {
                    hasAny = true;
                    string tagName = tag.Name;

                    Color tagColor = config != null
                        ? config.GetTagColor(tagName)
                        : new Color(0.8f, 0.8f, 0.8f);
                    string hex = GetHexCached(tagColor);

                    int count = asc.CombinedTags.GetExplicitTagCount(tag);
                    sb.Clear();
                    sb.Append("  <color=").Append(hex).Append(">").Append(tagName).Append("</color>");
                    if (count > 1) { sb.Append(" x"); AppendInt(sb, count); }

                    GUI.Label(new Rect(x, y, w, rowH), sb.ToString(), smallLabelStyle);
                    y += rowH;
                }

                if (!hasAny)
                    GUI.Label(new Rect(x + 8 * scale, y, w, rowH), "- None", smallLabelStyle);
            }
        }

        #endregion

        #region Drawing Helpers

        // Single-row attribute layout: [Name 32%] [Bar 40%] [Value 28%]
        private void DrawAttributeRow(float x, float y, float w, float lineH, float barH, GameplayAttribute attr, float scale)
        {
            string name = GetShortName(attr.Name);

            float nameW = w * 0.32f;
            float barW = w * 0.40f;
            float valW = w * 0.28f;
            float barX = x + nameW;
            float valX = barX + barW + 2 * scale;

            sb.Clear();
            sb.Append("<color=#E0C060>").Append(name).Append("</color>");
            GUI.Label(new Rect(x, y, nameW - 2, lineH), sb.ToString(), smallLabelStyle);

            float inlineBarH = Mathf.Max(barH, 6 * scale);
            float barY = y + (lineH - inlineBarH) * 0.5f;
            float maxVal = Mathf.Max(attr.BaseValue, attr.CurrentValue, 1f);
            float fill = maxVal > 0 ? Mathf.Clamp01(attr.CurrentValue / maxVal) : 0f;

            GUI.DrawTexture(new Rect(barX, barY, barW, inlineBarH), barBgTex);

            Color barColor = attr.CurrentValue <= attr.BaseValue
                ? Color.Lerp(new Color(0.9f, 0.2f, 0.2f), new Color(0.2f, 0.8f, 0.3f), fill)
                : new Color(0.3f, 0.7f, 0.9f);
            var savedColor = GUI.color;
            GUI.color = barColor;
            GUI.DrawTexture(new Rect(barX, barY, barW * fill, inlineBarH), barFillTex);
            GUI.color = savedColor;

            sb.Clear();
            AppendFloat1(sb, attr.CurrentValue);
            if (System.Math.Abs(attr.BaseValue - attr.CurrentValue) > 0.01f)
            {
                sb.Append('/');
                AppendFloat1(sb, attr.BaseValue);
            }
            GUI.Label(new Rect(valX, y, valW, lineH), sb.ToString(), smallValueStyle);
        }

        private void DrawEffectLine(float x, float y, float w, float lineH, ActiveGameplayEffect effect, float scale)
        {
            sb.Clear();
            sb.Append("  ");

            Color effectColor;
            if (config != null)
            {
                bool isDebuff = config.IsDebuffEffect(effect.Spec.Def.GrantedTags);
                effectColor = isDebuff ? config.DebuffEffectColor
                    : effect.IsInhibited ? config.InhibitedEffectColor
                    : config.NormalEffectColor;
            }
            else
            {
                effectColor = effect.IsInhibited
                    ? new Color(0.8f, 0.8f, 0.27f)
                    : new Color(0.6f, 0.87f, 0.67f);
            }

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
                sb.Append(" <color=#CCCC44>[INH]</color>");

            GUI.Label(new Rect(x, y, w, lineH), sb.ToString(), smallLabelStyle);
        }

        private void DrawAbilityLine(float x, float y, float w, float lineH, AbilitySystemComponent asc, GameplayAbilitySpec spec, float scale)
        {
            sb.Clear();
            sb.Append("  ");

            string aName = !string.IsNullOrEmpty(spec.Ability.Name) ? spec.Ability.Name : spec.Ability.GetType().Name;

            if (spec.IsActive)
            {
                sb.Append("<color=#66DD88>").Append(aName).Append(" [ACTIVE]</color>");
            }
            else if (asc.IsAbilityOnCooldown(spec.Ability))
            {
                sb.Append("<color=#DDAA44>").Append(aName).Append(" [CD ");
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
