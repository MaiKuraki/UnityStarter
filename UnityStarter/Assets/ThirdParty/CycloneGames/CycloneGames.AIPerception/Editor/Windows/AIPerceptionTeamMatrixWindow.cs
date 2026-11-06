using System.Collections.Generic;
using CycloneGames.AIPerception.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.AIPerception.Editor
{
    /// <summary>
    /// Grid editor for the team attitude table. Rows are observers, columns are targets, and clicking a
    /// cell cycles Friendly -> Neutral -> Hostile. Edits go to the selected live manager, or to a
    /// <see cref="PerceptionTeamAttitudeAsset"/> when one is assigned so the table can live in the project.
    /// </summary>
    public sealed class AIPerceptionTeamMatrixWindow : EditorWindow
    {
        private const string MenuPath = "Tools/CycloneGames/AI Perception/Team Matrix";
        private const float CellSize = 30f;
        private const float HeaderWidth = 46f;
        private const float ColumnHeaderHeight = 18f;
        private const float ScrollbarSize = 14f;
        private const float FooterHeight = 74f;
        private const int MaximumUndoDepth = 512;

        private static readonly Color FriendlyColor = new Color(0.30f, 0.68f, 0.38f, 1f);
        private static readonly Color NeutralColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color HostileColor = new Color(0.78f, 0.30f, 0.28f, 1f);
        private static readonly Color SelfCellColor = new Color(0.28f, 0.42f, 0.62f, 1f);
        private static readonly Color HeaderBandColor = new Color(0.22f, 0.22f, 0.22f, 0.55f);
        private static readonly Color HoverBandColor = new Color(0.35f, 0.55f, 0.85f, 0.45f);

        private readonly struct Edit
        {
            public readonly int Observer;
            public readonly int Target;
            public readonly PerceptionTeamAttitude Previous;

            public Edit(int observer, int target, PerceptionTeamAttitude previous)
            {
                Observer = observer;
                Target = target;
                Previous = previous;
            }
        }

        [SerializeField] private int _teamCount = 8;
        [SerializeField] private int _jumpTarget;
        [SerializeField] private int _scrollRow;
        [SerializeField] private int _scrollColumn;
        private int _visibleRows = 8;
        private int _visibleColumns = 8;

        /// <summary>Rows drawn this frame. The last page can be shorter than the viewport.</summary>
        private int DrawnRows => Mathf.Clamp(_teamCount - _scrollRow, 0, _visibleRows);

        private int DrawnColumns => Mathf.Clamp(_teamCount - _scrollColumn, 0, _visibleColumns);
        [SerializeField] private int _managerIndex;
        [SerializeField] private PerceptionTeamAttitudeAsset _asset;

        private readonly List<SensorManager> _managers = new List<SensorManager>(4);
        private readonly List<Edit> _undo = new List<Edit>(MaximumUndoDepth);
        private SensorManager _manager;
        private int _configuredPairs = -1;
        private int _assetVersion = -1;
        private GUIStyle _cellStyle;
        private int _hoverRow = -1;
        private int _hoverColumn = -1;
        private string _managerLabel = "none";

        [MenuItem(MenuPath, false, 200)]
        private static void Open()
        {
            OpenFromMenu();
        }

        internal static void OpenFromMenu()
        {
            var window = GetWindow<AIPerceptionTeamMatrixWindow>();
            window.titleContent = new GUIContent("Team Matrix");
            window.minSize = new Vector2(560f, 340f);
            window.Show();
        }

        private GUIStyle CellStyle
        {
            get
            {
                if (_cellStyle != null)
                {
                    return _cellStyle;
                }

                // EditorStyles.miniButton carries margins, padding and a fixed height, which makes the
                // drawn button smaller than the cell rect it is given.
                _cellStyle = new GUIStyle(EditorStyles.miniButton)
                {
                    margin = new RectOffset(0, 0, 0, 0),
                    padding = new RectOffset(0, 0, 0, 0),
                    fixedHeight = 0f,
                    fixedWidth = 0f
                };
                return _cellStyle;
            }
        }

        private void OnEnable()
        {
            _configuredPairs = -1;
            RefreshManagers();
            EditorApplication.update -= PollAssetChanges;
            EditorApplication.update += PollAssetChanges;
        }

        private void OnDisable()
        {
            EditorApplication.update -= PollAssetChanges;
        }

        /// <summary>Repaints when the bound asset changes from its own inspector.</summary>
        private void PollAssetChanges()
        {
            if (_asset == null || _asset.Version == _assetVersion)
            {
                return;
            }

            _assetVersion = _asset.Version;
            _configuredPairs = -1;
            Repaint();
        }

        private void OnFocus()
        {
            RefreshManagers();
        }

        /// <summary>
        /// Managers attached to the active registry. <see cref="SensorManager.Instance"/> is not used
        /// because it creates a manager when none exists.
        /// </summary>
        private void RefreshManagers()
        {
            _managers.Clear();
            if (PerceptibleRegistry.HasInstance)
            {
                PerceptibleRegistry registry = PerceptibleRegistry.Instance;
                for (int i = 0; i < registry.SensorManagerCount; i++)
                {
                    _managers.Add(registry.GetSensorManager(i));
                }
            }

            if (_managers.Count == 0 && SensorManager.HasInstance)
            {
                _managers.Add(SensorManager.Instance);
            }

            _managerIndex = Mathf.Clamp(_managerIndex, 0, Mathf.Max(0, _managers.Count - 1));
            _manager = _managers.Count > 0 ? _managers[_managerIndex] : null;
            _managerLabel = _manager != null
                ? $"#{_managerIndex} · {_manager.SensorCount} sensors"
                : "none";
            _configuredPairs = -1;
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawTargetRow();

            bool hasTarget = _asset != null || (_manager != null && !_manager.IsDisposed);
            if (!hasTarget)
            {
                EditorGUILayout.HelpBox(
                    "No SensorManager in this session and no asset assigned. Add a PerceptionManagerComponent, " +
                    "create a Team Attitude Table asset, or construct a manager from code.",
                    MessageType.Info);
                return;
            }

            ComputeColumns();
            DrawHeaderBand();

            // Split the remaining space once so the row count and the footer cannot disagree.
            Rect remaining = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            Rect gridArea = new Rect(remaining.x, remaining.y, remaining.width, Mathf.Max(0f, remaining.height - FooterHeight));
            Rect footerArea = new Rect(remaining.x, remaining.yMax - FooterHeight, remaining.width, FooterHeight);

            ComputeViewport(gridArea);
            DrawMatrix(gridArea);
            DrawFooter(footerArea);

            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Z &&
                (Event.current.control || Event.current.command))
            {
                UndoLast();
                Repaint();
                Event.current.Use();
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60f)))
                {
                    RefreshManagers();
                }

                GUILayout.Space(4f);
                GUILayout.Label("Target", EditorStyles.miniLabel, GUILayout.Width(38f));
                if (_managers.Count > 1 && _asset == null)
                {
                    int picked = EditorGUILayout.Popup(
                        _managerIndex,
                        BuildManagerLabels(),
                        EditorStyles.toolbarPopup,
                        GUILayout.Width(150f));
                    if (picked != _managerIndex)
                    {
                        _managerIndex = picked;
                        _manager = _managers[_managerIndex];
                        _managerLabel = $"#{_managerIndex} \u00b7 {_manager.SensorCount} sensors";
                        _configuredPairs = -1;
                        _undo.Clear();
                    }
                }
                else
                {
                    GUILayout.Label(
                        _asset != null ? $"asset \u00b7 {_asset.name}" : _managerLabel,
                        EditorStyles.miniLabel,
                        GUILayout.Width(140f));
                }

                GUILayout.FlexibleSpace();

                GUILayout.Label("Teams", EditorStyles.miniLabel, GUILayout.Width(40f));
                _teamCount = Mathf.Clamp(
                    EditorGUILayout.DelayedIntField(_teamCount, EditorStyles.toolbarTextField, GUILayout.Width(36f)),
                    2,
                    PerceptionTeam.TeamCount);

                GUILayout.Space(6f);
                GUILayout.Label("Jump", EditorStyles.miniLabel, GUILayout.Width(32f));
                int jump = Mathf.Clamp(
                    EditorGUILayout.DelayedIntField(_jumpTarget, EditorStyles.toolbarTextField, GUILayout.Width(36f)),
                    0,
                    _teamCount - 1);

                if (jump != _jumpTarget || GUILayout.Button("Go", EditorStyles.toolbarButton, GUILayout.Width(28f)))
                {
                    _jumpTarget = jump;
                    _scrollRow = jump;
                    _scrollColumn = jump;
                    Repaint();
                }

                using (new EditorGUI.DisabledScope(_undo.Count == 0))
                {
                    if (GUILayout.Button($"Undo ({_undo.Count})", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                    {
                        UndoLast();
                    }
                }
            }
        }

        private void DrawTargetRow()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                _asset = (PerceptionTeamAttitudeAsset)EditorGUILayout.ObjectField(
                    _asset,
                    typeof(PerceptionTeamAttitudeAsset),
                    false,
                    GUILayout.Width(170f));

                using (new EditorGUI.DisabledScope(_asset == null))
                {
                    if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(50f)))
                    {
                        EditorUtility.SetDirty(_asset);
                        AssetDatabase.SaveAssets();
                    }
                }

                using (new EditorGUI.DisabledScope(_manager == null || _asset == null))
                {
                    if (GUILayout.Button("Apply", EditorStyles.toolbarButton, GUILayout.Width(56f)))
                    {
                        _asset.ApplyTo(_manager);
                        _configuredPairs = -1;
                        _undo.Clear();
                    }

                    if (GUILayout.Button("Capture", EditorStyles.toolbarButton, GUILayout.Width(66f)))
                    {
                        _asset.CaptureFrom(_manager);
                        EditorUtility.SetDirty(_asset);
                        _configuredPairs = -1;
                        _undo.Clear();
                    }
                }

                GUILayout.FlexibleSpace();

                if (_asset != null)
                {
                    if (GUILayout.Button("Same team", EditorStyles.toolbarButton, GUILayout.Width(86f)))
                    {
                        _asset.SetSameTeamFriendly();
                        EditorUtility.SetDirty(_asset);
                        _configuredPairs = -1;
                        _undo.Clear();
                    }

                    if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50f)))
                    {
                        _asset.ResetTo(PerceptionTeamAttitude.Neutral, false);
                        EditorUtility.SetDirty(_asset);
                        _configuredPairs = -1;
                        _undo.Clear();
                    }

                }
                else if (_manager != null)
                {
                    if (GUILayout.Button("Same team", EditorStyles.toolbarButton, GUILayout.Width(86f)))
                    {
                        _manager.SetSameTeamFriendly();
                        _configuredPairs = -1;
                        _undo.Clear();
                    }

                    if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50f)))
                    {
                        _manager.ClearTeamAttitudes();
                        _configuredPairs = -1;
                        _undo.Clear();
                    }
                }
            }
        }

        private string[] BuildManagerLabels()
        {
            var labels = new string[_managers.Count];
            for (int i = 0; i < _managers.Count; i++)
            {
                labels[i] = $"#{i} \u00b7 {_managers[i].SensorCount} sensors";
            }

            return labels;
        }

        /// <summary>Axis caption and legend. Laid out with explicit rects so the swatches share a baseline.</summary>
        private void DrawHeaderBand()
        {
            float width = Mathf.Max(HeaderWidth + CellSize * _visibleColumns, position.width - 26f);
            Rect row = GUILayoutUtility.GetRect(width, 38f, GUILayout.ExpandWidth(false));

            GUI.Label(
                new Rect(row.x, row.y, width, 17f),
                $"Observer team \u2193 / Target team \u2192   ({_teamCount} teams)",
                EditorStyles.miniBoldLabel);

            float x = row.x;
            x = DrawLegendItem(x, row.y + 20f, "Friendly", FriendlyColor);
            x = DrawLegendItem(x, row.y + 20f, "Neutral", NeutralColor);
            DrawLegendItem(x, row.y + 20f, "Hostile", HostileColor);
        }

        private static float DrawLegendItem(float x, float y, string label, Color color)
        {
            const float swatch = 11f;
            const float gap = 5f;
            EditorGUI.DrawRect(new Rect(x, y + 3f, swatch, swatch), color);
            var labelRect = new Rect(x + swatch + gap, y, 60f, 17f);
            GUI.Label(labelRect, label, EditorStyles.miniLabel);
            return labelRect.xMax + 10f;
        }

        /// <summary>
        /// Grid drawn with an integer viewport offset. There is no scroll view, so the layout rect and the
        /// mouse position share one coordinate space and the hover hit-test stays exact.
        /// </summary>
        /// <summary>Rows are derived from the space the layout already reserved.</summary>
        /// <summary>Column count depends on window width only, so it is resolved before drawing.</summary>
        private void ComputeColumns()
        {
            _visibleColumns = Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Max(CellSize * 2f, position.width - 26f - HeaderWidth - ScrollbarSize) / CellSize),
                2,
                PerceptionTeam.TeamCount);
            _scrollColumn = Mathf.Clamp(_scrollColumn, 0, Mathf.Max(0, _teamCount - _visibleColumns));
        }

        private void ComputeViewport(Rect gridArea)
        {
            float rowsSpace = gridArea.height - ColumnHeaderHeight - ScrollbarSize;
            _visibleRows = Mathf.Clamp(
                Mathf.FloorToInt(Mathf.Max(CellSize * 2f, rowsSpace) / CellSize),
                2,
                PerceptionTeam.TeamCount);

            _scrollRow = Mathf.Clamp(_scrollRow, 0, Mathf.Max(0, _teamCount - _visibleRows));
            _scrollColumn = Mathf.Clamp(_scrollColumn, 0, Mathf.Max(0, _teamCount - _visibleColumns));
        }


        private void DrawMatrix(Rect area)
        {
            bool needsVertical = _teamCount > _visibleRows;
            bool needsHorizontal = _teamCount > _visibleColumns;

            UpdateHover(area);

            DrawColumnHeaders(area);
            DrawRowHeaders(area);

            for (int row = 0; row < DrawnRows; row++)
            {
                for (int column = 0; column < DrawnColumns; column++)
                {
                    DrawCell(area, row, column);
                }
            }

            int maxScroll = _teamCount - _visibleRows;
            if (needsVertical)
            {
                var bar = new Rect(area.x + HeaderWidth + CellSize * _visibleColumns, area.y + ColumnHeaderHeight,
                    ScrollbarSize, CellSize * Mathf.Max(1, DrawnRows));
                _scrollRow = Mathf.Clamp(
                    Mathf.RoundToInt(GUI.VerticalScrollbar(bar, _scrollRow, Mathf.Max(1, DrawnRows), 0f, _teamCount)),
                    0,
                    Mathf.Max(0, maxScroll));
            }

            int maxColumnScroll = _teamCount - _visibleColumns;
            if (needsHorizontal)
            {
                var bar = new Rect(area.x + HeaderWidth, area.y + ColumnHeaderHeight + CellSize * _visibleRows,
                    CellSize * Mathf.Max(1, DrawnColumns), ScrollbarSize);
                _scrollColumn = Mathf.Clamp(
                    Mathf.RoundToInt(GUI.HorizontalScrollbar(bar, _scrollColumn, Mathf.Max(1, DrawnColumns), 0f, _teamCount)),
                    0,
                    Mathf.Max(0, maxColumnScroll));
            }
        }

        /// <summary>
        /// Single source of truth for grid geometry. Headers, cells and the hit-test all derive from it,
        /// so they cannot drift apart.
        /// </summary>
        private Rect GridCell(Rect area, int row, int column, float inset = 0f)
        {
            return new Rect(
                area.x + HeaderWidth + column * CellSize + inset,
                area.y + ColumnHeaderHeight + row * CellSize + inset,
                CellSize - inset * 2f,
                CellSize - inset * 2f);
        }

        private void DrawColumnHeaders(Rect area)
        {
            EditorGUI.DrawRect(
                new Rect(area.x, area.y, HeaderWidth + CellSize * _visibleColumns, ColumnHeaderHeight),
                HeaderBandColor);

            for (int column = 0; column < DrawnColumns; column++)
            {
                var cell = new Rect(area.x + HeaderWidth + column * CellSize, area.y, CellSize, ColumnHeaderHeight);
                if (column == _hoverColumn)
                {
                    EditorGUI.DrawRect(cell, HoverBandColor);
                }

                GUI.Label(cell, $"T{_scrollColumn + column}", EditorStyles.centeredGreyMiniLabel);
            }
        }

        private void DrawRowHeaders(Rect area)
        {
            for (int row = 0; row < DrawnRows; row++)
            {
                var cell = new Rect(area.x, area.y + ColumnHeaderHeight + row * CellSize, HeaderWidth, CellSize);
                if (row == _hoverRow)
                {
                    EditorGUI.DrawRect(cell, HoverBandColor);
                }

                GUI.Label(cell, $"T{_scrollRow + row}", EditorStyles.centeredGreyMiniLabel);
            }
        }

        /// <summary>
        /// Hit-test in window space. Nothing is drawn inside a GUI group, so the rect returned by the
        /// layout and the mouse position share one coordinate system.
        /// </summary>
        private void UpdateHover(Rect area)
        {
            if (Event.current.type == EventType.ScrollWheel && area.Contains(Event.current.mousePosition))
            {
                _scrollRow = Mathf.Clamp(
                    _scrollRow + (Event.current.delta.y > 0f ? 2 : -2),
                    0,
                    Mathf.Max(0, _teamCount - _visibleRows));
                Event.current.Use();
                Repaint();
                return;
            }

            EventType type = Event.current.type;
            if (type != EventType.Repaint && type != EventType.MouseMove && type != EventType.MouseDrag)
            {
                return;
            }

            int row = -1;
            int column = -1;
            Vector2 mouse = Event.current.mousePosition;

            float localX = mouse.x - area.x - HeaderWidth;
            float localY = mouse.y - area.y - ColumnHeaderHeight;
            if (localX >= 0f && localY >= 0f)
            {
                int candidateColumn = Mathf.FloorToInt(localX / CellSize);
                int candidateRow = Mathf.FloorToInt(localY / CellSize);
                if (candidateColumn < DrawnColumns && candidateRow < DrawnRows)
                {
                    column = candidateColumn;
                    row = candidateRow;
                }
            }

            if (row == _hoverRow && column == _hoverColumn)
            {
                return;
            }

            _hoverRow = row;
            _hoverColumn = column;
            Repaint();
        }

        private void DrawCell(Rect area, int row, int column)
        {
            int observer = _scrollRow + row;
            int target = _scrollColumn + column;

            // No inset: the header uses the full cell box, so the button must too. The 1 px border of
            // the style already separates neighbours.
            Rect rect = GridCell(area, row, column);

            PerceptionTeamAttitude attitude = ReadCell(observer, target);
            Color previous = GUI.backgroundColor;
            GUI.backgroundColor = observer == target ? SelfCellColor : ColorFor(attitude);

            if (GUI.Button(rect, ShortLabel(attitude), CellStyle))
            {
                WriteCell(observer, target, Next(attitude));
            }

            GUI.backgroundColor = previous;
        }

        private void DrawFooter(Rect area)
        {
            if (_configuredPairs < 0)
            {
                _configuredPairs = CountConfiguredPairs();
            }

            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 0f;
            string range = $"showing T{_scrollColumn}\u2026T{_scrollColumn + Mathf.Max(0, DrawnColumns - 1)} \u00d7 " +
                           $"T{_scrollRow}\u2026T{_scrollRow + Mathf.Max(0, DrawnRows - 1)}";
            string hovered = _hoverRow >= 0
                ? $"T{_scrollRow + _hoverRow} \u2192 T{_scrollColumn + _hoverColumn}: " +
                  $"{ReadCell(_scrollRow + _hoverRow, _scrollColumn + _hoverColumn)}"
                : "hover a cell to inspect it, click to cycle, Ctrl+Z to undo";
            GUI.Label(new Rect(area.x, area.y, area.width, 18f), $"{range}   \u00b7   {hovered}", EditorStyles.miniBoldLabel);

            int hidden = CountRelationsOutsideRange();
            string hiddenNote = hidden > 0
                ? $" {hidden} relation(s) reference teams above {_teamCount - 1} and are not shown."
                : string.Empty;

            string target = _asset != null ? $"asset '{_asset.name}'" : $"manager {_managerLabel}";
            EditorGUI.HelpBox(
                new Rect(area.x, area.y + 19f, area.width, area.height - 19f),
                $"{_configuredPairs} of {PerceptionTeam.TeamCount * PerceptionTeam.TeamCount} pairs differ from Neutral in {target}. " +
                "Rows are observers, columns are targets, so A->B and B->A are independent. " +
                "A sensor only detects attitudes selected in its Affiliation mask." + hiddenNote,
                MessageType.None);
            EditorGUIUtility.labelWidth = previousLabelWidth;
        }

        private PerceptionTeamAttitude ReadCell(int observer, int target)
        {
            if (_asset != null)
            {
                return _asset.GetAttitude(observer, target);
            }

            return _manager != null ? _manager.GetTeamAttitude(observer, target) : PerceptionTeamAttitude.Neutral;
        }

        private void WriteCell(int observer, int target, PerceptionTeamAttitude attitude)
        {
            PerceptionTeamAttitude previous = ReadCell(observer, target);
            if (previous == attitude)
            {
                return;
            }

            if (_undo.Count >= MaximumUndoDepth)
            {
                _undo.RemoveAt(0);
            }

            _undo.Add(new Edit(observer, target, previous));

            if (_asset != null)
            {
                _asset.SetAttitude(observer, target, attitude);
                EditorUtility.SetDirty(_asset);
            }
            else if (_manager != null)
            {
                _manager.SetTeamAttitude(observer, target, attitude);
            }

            _configuredPairs = -1;
        }

        private void UndoLast()
        {
            if (_undo.Count == 0)
            {
                return;
            }

            Edit edit = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);

            if (_asset != null)
            {
                _asset.SetAttitude(edit.Observer, edit.Target, edit.Previous);
                EditorUtility.SetDirty(_asset);
            }
            else if (_manager != null)
            {
                _manager.SetTeamAttitude(edit.Observer, edit.Target, edit.Previous);
            }

            _configuredPairs = -1;
            Repaint();
        }

        /// <summary>
        /// Counts overrides whose teams fall outside the configured range. They stay in the data, so the
        /// footer has to say so instead of letting the view silently hide them.
        /// </summary>
        private int CountRelationsOutsideRange()
        {
            if (_asset == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < _asset.RelationCount; i++)
            {
                PerceptionTeamAttitudeAsset.Relation relation = _asset.Relations[i];
                if (relation.ObserverTeam >= _teamCount || relation.TargetTeam >= _teamCount)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountConfiguredPairs()
        {
            int count = 0;
            for (int observer = 0; observer < PerceptionTeam.TeamCount; observer++)
            {
                for (int target = 0; target < PerceptionTeam.TeamCount; target++)
                {
                    if (ReadCell(observer, target) != PerceptionTeamAttitude.Neutral)
                    {
                        count++;
                    }
                }
            }

            return count;
        }

        private static Color ColorFor(PerceptionTeamAttitude attitude)
        {
            switch (attitude)
            {
                case PerceptionTeamAttitude.Friendly:
                    return FriendlyColor;
                case PerceptionTeamAttitude.Hostile:
                    return HostileColor;
                default:
                    return NeutralColor;
            }
        }

        private static string ShortLabel(PerceptionTeamAttitude attitude)
        {
            switch (attitude)
            {
                case PerceptionTeamAttitude.Friendly:
                    return "F";
                case PerceptionTeamAttitude.Hostile:
                    return "H";
                default:
                    return "N";
            }
        }

        private static PerceptionTeamAttitude Next(PerceptionTeamAttitude attitude)
        {
            switch (attitude)
            {
                case PerceptionTeamAttitude.Friendly:
                    return PerceptionTeamAttitude.Neutral;
                case PerceptionTeamAttitude.Neutral:
                    return PerceptionTeamAttitude.Hostile;
                default:
                    return PerceptionTeamAttitude.Friendly;
            }
        }
    }
}
