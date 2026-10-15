using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using CycloneGames.EventBus.Core;
using CycloneGames.EventBus.Runtime;

namespace CycloneGames.EventBus.Editor
{
    /// <summary>
    /// Live diagnostics window for a context registered through <see cref="EventBusEditorDiagnostics"/>.
    ///
    /// The default tab is <b>Issues</b>: a severity-sorted list of every problem, naming the owning
    /// bus or container, so the reader never has to jump banner -&gt; row -&gt; expand. A click on an
    /// issue row navigates to the owning bus (expanded) or the owning container.
    ///
    /// Performance model: a retained model rebuilt only on a snapshot tick (0.5 s cadence or a
    /// manual refresh), never per repaint. Every displayed string is formatted during the rebuild,
    /// so OnGUI repaints (mouse move, scroll, focus) perform no managed allocation. The bus row list
    /// is virtualized — only rows intersecting the scroll viewport are drawn — so the cost of a
    /// repaint is a function of the window height, not of the number of buses. Mini trend bars are
    /// drawn on the Repaint event only. Subscriber rows are capped per bus; the counter aggregates
    /// stay exact while the row list is a bounded sample. Read-only by design.
    ///
    /// <b>Layout-section invariant</b> (see BeginLayoutSection): between the start of a Layout pass and
    /// the end of the following Repaint pass, no state that determines the NUMBER of controls may
    /// change. Repaint() may be requested at any time — the auto-refresh tick fires <i>between</i>
    /// passes — and a Repaint reuses the Layout pass's tree, so it must observe the same control count
    /// as the Layout that produced it. All data rebuilds and row-range computation therefore happen in
    /// the Layout pass only; the draw passes read frozen state.
    /// </summary>
    public sealed class EventBusDebuggerWindow : EditorWindow
    {
        private const double RefreshIntervalSeconds = 0.5d;
        private const float RowHeight = 20f;
        private const float HeaderHeight = 22f;
        private const int MaxSubscriberRowsPerBus = 512;
        private const int TrendLength = 32;
        private const int FaultMessagePreviewLength = 60;

        // Horizontal chrome subtracted from currentViewWidth to reach the table viewport: Editor margins
        // (~30 px) plus the scroll view's vertical scrollbar (~13-14 px), which is reserved whenever the
        // content overflows vertically. The table width is clamped against that real content width, so a
        // fitting table never shows a horizontal scrollbar or truncates its first column.
        private const float TableViewportDeduction = 44f;

        // How long the arrival pulse lasts: the selection outline is brighter/thicker for this long,
        // then settles into the persistent outline (which never expires).
        private const double FlashSeconds = 1.5d;

        // Tab indices. Issues is the default so the window opens on the answer, not on the raw table.
        private const int IssuesTab = 0;
        private const int BusesTab = 1;
        private const int ContainersTab = 2;
        private const int SummaryTab = 3;

        private static readonly string[] Tabs = { "Issues", "Buses", "Containers", "Summary" };

        private static readonly Color PillBuses = new Color(0.6f, 0.6f, 0.6f);
        private static readonly Color PillSubs = new Color(0.2f, 0.7f, 0.3f);
        private static readonly Color PillRate = new Color(0.2f, 0.5f, 1.0f);
        private static readonly Color PillWarning = new Color(0.9f, 0.6f, 0.1f);
        private static readonly Color PillError = new Color(0.9f, 0.25f, 0.2f);

        private static readonly Color RowEven = new Color(0.5f, 0.5f, 0.5f, 0.06f);
        private static readonly Color RowOdd = new Color(0.5f, 0.5f, 0.5f, 0.02f);
        private static readonly Color RowWarning = new Color(0.9f, 0.6f, 0.1f, 0.12f);
        private static readonly Color RowError = new Color(0.9f, 0.25f, 0.2f, 0.14f);
        private static readonly Color RowSelected = new Color(0.25f, 0.6f, 1.0f, 0.30f);
        private static readonly Color AccentBar = new Color(0.25f, 0.6f, 1f, 1f);
        private static readonly Color AccentFlash = new Color(0.55f, 0.85f, 1f, 1f);
        private static readonly Color TextDim = new Color(0.55f, 0.55f, 0.55f);
        private static readonly Color TextWarning = new Color(1f, 0.75f, 0.25f);
        private static readonly Color TextError = new Color(1f, 0.42f, 0.38f);
        private static readonly Color StateOk = new Color(0.3f, 0.7f, 0.4f);

        // ------------------------------------------------------------------ table columns
        // Titles and tooltips live with the window; widths, minimums and the flexible-column index
        // live in EventBusTableLayout (pure math, unit-tested). One flexible column absorbs surplus;
        // when the window is too narrow, the other columns are squeezed in proportion to their slack
        // above their own minimum. Widths are in-memory only (no EditorPrefs); right-click a header
        // to reset that table.

        // The last entry of Buses/Issues is an invisible filler column: empty header, no tooltip,
        // nothing drawn. It is the flexible (absorbing) column, and it MUST stay last so every real
        // column's right edge follows the mouse when dragged.
        private static readonly string[] BusHeaderTitles =
            { "Event Type", "State", "Subs", "Tomb", "Publishes", "/s", "Err", "Peak", "Cap", "Depth", "" };
        private static readonly string[] BusHeaderTooltips =
        {
            "Event struct type. Click a row to expand its subscribers. Click a header to sort.",
            "Health badge: OK / WARN / ERR, from errors, re-entrancy drops, destroyed targets and tombstones.",
            "Live subscribers.",
            "Dead slots awaiting compaction, +delta since the last refresh. A steadily climbing value means churn.",
            "Dispatch entries since construction.",
            "Publishes per second, measured between refreshes.",
            "Subscriber exceptions, +delta since the last refresh.",
            "Highest live subscriber count observed; use it to size initialCapacity.",
            "Backing array length. Grows on subscribe, never shrinks.",
            "Current dispatch nesting depth; zero when idle.",
            "",
        };

        private static readonly string[] ContainerHeaderTitles =
            { "Container", "State", "Cap", "Pending", "Dropped", "Rejected", "Closed", "Note" };
        private static readonly string[] ContainerHeaderTooltips =
        {
            "The registered container instance.",
            "Health badge: OK / WARN / ERR, from dropped and refused writes.",
            "Fixed capacity.",
            "Events buffered and awaiting a drain, +delta since the last refresh.",
            "Confirmed loss (events discarded unread), +delta.",
            "Refused writes (backpressure), +delta.",
            "Closed to new writes.",
            "What the container is (batch buffer / cross-thread bridge / command queue / scheduler). Fills the remaining width.",
        };

        private static readonly string[] IssueHeaderTitles =
            { "Severity", "Category", "Subject", "Where", "Value", "Delta", "Fix", "Trend", "" };
        private static readonly string[] IssueHeaderTooltips =
        {
            "ERR / WARN / INFO.",
            "Leak, subscriber error, re-entrancy drop, tombstone churn, or container backpressure.",
            "The event type or container the issue belongs to.",
            "The offender: method (declaring type), or the container name.",
            "The measurement; for a subscriber error, the exception type and message.",
            "Change since the previous snapshot.",
            "How to fix it.",
            "The last 32 snapshots of this issue's metric.",
            "",
        };

        private const float ResizeHandleWidth = 7f;

        // Per-window user widths (what the user dragged to), clones of the layout defaults so two
        // windows never share mutable state and a drag in one table never leaks into another.
        private readonly float[] _busWidths = (float[])EventBusTableLayout.BusDefaults.Clone();
        private readonly float[] _issueWidths = (float[])EventBusTableLayout.IssueDefaults.Clone();
        private readonly float[] _containerWidths = (float[])EventBusTableLayout.ContainerDefaults.Clone();

        // Per-window display widths (post-squeeze). Layout writes ONLY here, never into the user
        // widths above, so narrowing then widening the window restores the user's sizes.
        private readonly float[] _busRender = (float[])EventBusTableLayout.BusDefaults.Clone();
        private readonly float[] _issueRender = (float[])EventBusTableLayout.IssueDefaults.Clone();
        private readonly float[] _containerRender = (float[])EventBusTableLayout.ContainerDefaults.Clone();

        // Column-drag state: -1 when idle, otherwise the index being resized.
        private int _dragColumn = -1;
        private float _dragStartX;
        private float _dragStartWidth;

        // Sort state: -1 = registration order.
        private int _sortColumn = -1;
        private bool _sortAscending = true;

        private readonly List<BusEntry> _buses = new List<BusEntry>();
        private readonly Dictionary<string, BusEntry> _busCache = new Dictionary<string, BusEntry>();
        private readonly List<ContainerEntry> _containers = new List<ContainerEntry>();
        private readonly List<ContainerEntry> _containerCache = new List<ContainerEntry>();
        private readonly List<IssueRow> _issues = new List<IssueRow>();
        private readonly List<EventSubscriberInfo> _subscriberScratch = new List<EventSubscriberInfo>(64);
        private readonly Dictionary<string, long> _previousPublishes = new Dictionary<string, long>();
        private readonly HashSet<string> _expanded = new HashSet<string>();
        private readonly StringBuilder _copyBuilder = new StringBuilder(2048);
        private readonly StringBuilder _warnBuilder = new StringBuilder(256);
        private readonly GUIContent _cellContent = new GUIContent();

        // Flat, filtered, expanded row list for the Buses tab. Rebuilt only when the model changes
        // (snapshot tick, filter edit, expand toggle), never per repaint, so repaints stay
        // allocation-free.
        private readonly List<RowItem> _flatRows = new List<RowItem>(256);
        private bool _rowsDirty = true;

        private Vector2 _scrollPos;
        private string _searchFilter = string.Empty;
        private int _selectedTab = IssuesTab;
        private bool _autoRefresh = true;
        private bool _isVisible;
        private bool _stylesBuilt;
        private double _nextRefresh;
        private double _lastSnapshotTime;
        private EventBusContext _lastContext;

        // Persistent selection (bus TypeName or container Name). Set by clicking a row or by navigating
        // from Issues; cleared on a tab change. It does NOT expire: the reader must be able to look away
        // and still see where they were. In-memory only (no EditorPrefs).
        private string _selectedBusKey;
        private string _selectedContainerKey;

        // Arrival pulse: the selection outline is brighter/thicker until this time, then settles into
        // the persistent outline. Set only by Issues navigation.
        private double _flashUntil;

        // Pending "scroll this row into view" target (bus TypeName / container Name). The owning tab's
        // draw pass resolves it to a content Y and stores it in _pendingScrollContentY; the next
        // Layout pass applies it. Nothing here changes layout state mid-draw.
        private string _scrollTargetBus;
        private string _scrollTargetContainer;

        // ------------------------------------------------------------------ pending layout state
        // IMGUI requires the Layout and Repaint passes of a frame to allocate the SAME controls.
        // Anything that can change that count (tab switch, expand/collapse, scroll jump) must therefore
        // NOT be applied mid-draw: the draw code records intent here and ApplyPendingState applies it at
        // the top of the next Layout pass.
        private const int NoPendingTab = -1;
        private const float NoPendingScroll = -1f;
        private int _pendingTab = NoPendingTab;
        private string _pendingExpandKey;
        private bool _pendingExpandToggle;
        private float _pendingScrollContentY = NoPendingScroll;
        private bool _pendingRowsDirty;

        // Navigation's selection, applied AFTER the tab switch (see ApplyPendingState): the switch
        // clears the previous selection ("leaving the tab clears it"), so a selection applied before it
        // would be wiped by the navigation's own tab change.
        private string _pendingSelectBusKey;
        private string _pendingSelectContainerKey;

        // Issues ordering: deterministic by default; "Freeze order" pins the previous order so rows do
        // not jump under the reader. In-memory only.
        private bool _freezeIssues;
        private string[] _previousIssueOrder;

        // Row-virtualisation geometry. _viewportHeight is a deterministic upper bound (position.height),
        // never a GetLastRect() result — Layout and Repaint must compute the same row range.
        private float _viewportHeight;
        private float _rowsStartContentY;

        // Layout-section bookkeeping (see the class-level invariant). _snapshotDirty is raised from
        // EditorApplication.update (between passes) and consumed in the Layout pass; _rowRange* are the
        // virtualised row bounds, computed in the Layout pass and reused verbatim by Repaint.
        private bool _snapshotDirty = true;
        private int _rowRangeFirst;
        private int _rowRangeLast;

        // Summary strings, preformatted per snapshot.
        private string _pillBusesText;
        private string _pillSubsText;
        private string _pillRateText;
        private string _pillTombText;
        private string _pillErrText;
        private string _pillDropText;
        private string _pillDeadText;
        private string _pillBackpressureText;
        private int _deadSubscriberCount;
        private int _issueCount;
        private EventBusDiagnosticsSnapshot _aggregate;

        // Per-category warning summaries (banner text), preformatted per snapshot.
        private string _warnLeakText;
        private string _warnErrText;
        private string _warnDropText;
        private string _warnTombText;
        private string _warnBackpressureText;

        // Container aggregates, preformatted per snapshot.
        private long _containerPending;
        private long _containerDropped;
        private long _containerRejected;
        private int _containerClosedCount;
        private int _containerCount;

        private GUIStyle _rowStyle;
        private GUIStyle _numericStyle;
        private GUIStyle _headerCellStyle;
        private GUIStyle _headerNumericStyle;
        private GUIStyle _subStyle;
        private GUIStyle _badgeStyle;
        private GUIStyle _pillStyle;
        private GUIStyle _summaryLabelStyle;
        private GUIStyle _summaryValueStyle;
        private GUILayoutOption[] _pillOptions;

        // Class (not struct) so the subscriber list and the trend survive across snapshots.
        private sealed class BusEntry
        {
            public string TypeName;
            public string DisplayName;
            public string SubsText;
            public string TombText;
            public string PublishText;
            public string RateText;
            public string ErrorText;
            public string PeakText;
            public string CapText;
            public string DepthText;
            public string DroppedText;
            public string StateText;
            public int StateSeverity; // 0 = ERR, 1 = WARN, 2 = OK
            // The bus's own PublishErrorPolicy name, read per-bus from IEventBusFaultInspection.
            public string PolicyName = "-";
            public bool HasTombstones;
            public bool HasErrors;
            public bool HasDropped;
            public bool HasDeadSubscribers;
            public int DeadCount;
            public string FirstDeadLocation;
            public int OverflowSubscriberCount;
            public string OverflowText;

            // Numeric mirrors for sorting (the *Text fields are formatted for display).
            public int SubsNum;
            public int TombNum;
            public long PublishNum;
            public double RateNum;
            public long ErrorNum;
            public long DroppedNum;
            public int PeakNum;
            public int CapNum;
            public int DepthNum;

            // Deltas since the previous snapshot, with the previous values that produced them.
            public bool DeltasInitialized;
            public long PrevErrors;
            public int PrevTomb;
            public long PrevDropped;
            public int PrevDead;
            public int ErrorDelta;
            public int TombDelta;
            public int DroppedDelta;
            public int DeadDelta;

            // Last fault, read from IEventBusFaultInspection.
            public bool HasFault;
            public int FaultCount;
            public string FaultTypeName;
            public string FaultMessage;
            public string FaultSubscriber;
            public string FaultDeclaring;

            // Recent problem metric (errors + re-entrancy drops), one sample per snapshot.
            public readonly List<int> Trend = new List<int>(TrendLength);

            public readonly List<SubRow> Subs = new List<SubRow>();
        }

        private struct SubRow
        {
            public string MethodText;
            public string TargetText;
            public bool IsDeadTarget;
        }

        private sealed class ContainerEntry
        {
            public string Name;
            public string CapacityText;
            public string PendingText;
            public string DroppedText;
            public string RejectedText;
            public string ClosedText;
            public string NoteText;
            public string StateText;
            public int StateSeverity;
            public long DroppedNum;
            public long RejectedNum;
            public int PendingNum;
            public bool Closed;

            public bool DeltasInitialized;
            public long PrevDropped;
            public long PrevRejected;
            public int PrevPending;
            public int DroppedDelta;
            public int RejectedDelta;
            public int PendingDelta;

            public readonly List<int> Trend = new List<int>(TrendLength);
        }

        // One row on the Issues tab. Built on the snapshot tick, sorted by severity then delta.
        private sealed class IssueRow
        {
            public int Severity; // 0 = ERR, 1 = WARN, 2 = INFO
            public string CategoryText;
            public string SubjectText;
            public string LocationText;
            public string ValueText;
            public string ValueTooltip;
            public string DeltaText;
            public int DeltaMagnitude;
            public string RemediationText;
            public byte TargetTab;
            public string TargetKey;
            public List<int> Trend;
        }

        // One drawable row in the Buses tab. Kind: 0 bus, 1 subscriber, 2 overflow, 3 dead-hint.
        private struct RowItem
        {
            public byte Kind;
            public int BusIndex;
            public int SubIndex;
        }

        [MenuItem("Tools/CycloneGames/EventBus/Debugger")]
        private static void Open()
        {
            var window = GetWindow<EventBusDebuggerWindow>("EventBus Debugger");
            window.minSize = new Vector2(760, 420);
            window.Show();
        }

        private void OnEnable()
        {
            _isVisible = true;
            _nextRefresh = 0d;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            _isVisible = false;
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnBecameVisible()
        {
            _isVisible = true;
            _nextRefresh = 0d;
        }

        private void OnBecameInvisible()
        {
            _isVisible = false;
        }

        private void OnEditorUpdate()
        {
            if (!_isVisible || !_autoRefresh || !Application.isPlaying)
            {
                return;
            }

            if (EditorApplication.timeSinceStartup >= _nextRefresh)
            {
                _nextRefresh = EditorApplication.timeSinceStartup + RefreshIntervalSeconds;

                // Do NOT rebuild here: this runs BETWEEN the Layout and Repaint passes, so rebuilding now
                // would make the Repaint pass disagree with Layout about the control count. Mark dirty;
                // the next Layout pass rebuilds (see BeginLayoutSection).
                _snapshotDirty = true;
                Repaint();
            }
        }

        private void BuildStyles()
        {
            if (_stylesBuilt)
            {
                return;
            }

            _stylesBuilt = true;

            _rowStyle = new GUIStyle(EditorStyles.label)
            {
                padding = new RectOffset(4, 4, 2, 2),
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft,
            };

            _numericStyle = new GUIStyle(_rowStyle)
            {
                alignment = TextAnchor.MiddleRight,
            };

            _headerCellStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                padding = new RectOffset(4, 4, 2, 2),
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft,
            };

            // Right-aligned header for numeric/badge columns, so a narrow column's header sits on the
            // same side as its values and the eye can pair them.
            _headerNumericStyle = new GUIStyle(_headerCellStyle)
            {
                alignment = TextAnchor.MiddleRight,
            };

            _subStyle = new GUIStyle(EditorStyles.label)
            {
                font = EditorStyles.miniFont,
                padding = new RectOffset(4, 4, 2, 2),
                clipping = TextClipping.Clip,
                alignment = TextAnchor.MiddleLeft,
            };

            _badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(2, 2, 1, 1),
            };

            _pillStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(6, 6, 1, 1),
            };
            _pillOptions = new[] { GUILayout.ExpandWidth(false) };

            _summaryLabelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
            };

            _summaryValueStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
            };
        }

        private void OnGUI()
        {
            BuildStyles();

            EventBusContext context = EventBusEditorDiagnostics.Current;
            if (context == null)
            {
                _lastContext = null;
                DrawPlaceholder(
                    "No context registered. Call EventBusEditorDiagnostics.Register(context) from "
                    + "editor-only code to observe a context.",
                    MessageType.Info);
                return;
            }

            if (context.IsDisposed)
            {
                // Drop the borrowed reference: keeping it would pin the context (and its buses)
                // for the Editor process lifetime.
                EventBusEditorDiagnostics.Clear();
                _lastContext = null;
                DrawPlaceholder(
                    "The observed EventBusContext was disposed. The reference has been released; "
                    + "register a new context to observe it.",
                    MessageType.Warning);
                return;
            }

            // Deterministic viewport height for row virtualisation: position.height is an upper bound on
            // the scroll view's height and is identical in the Layout and Repaint passes (a GetLastRect()
            // result is not — it returns the previous frame's rect during Layout). Drawing a few extra
            // rows is harmless: the trailing spacer (total - lastVisible) keeps the content height and
            // the scroll range exactly right.
            _viewportHeight = Mathf.Max(1f, position.height);

            // Everything that can change the NUMBER of controls is applied here, in the Layout pass,
            // before the first allocation. The Repaint pass reuses it verbatim.
            BeginLayoutSection();

            DrawTopBar();

            GUILayout.Space(4f);
            int newTab = GUILayout.Toolbar(_selectedTab, Tabs, EditorStyles.toolbarButton, GUILayout.Height(22f));

            if (newTab != _selectedTab)
            {
                // Deferred to the next Layout pass: switching the tab replaces the whole content
                // structure, which must not change between this frame's Layout and Repaint passes.
                _pendingTab = newTab;
                Repaint();
            }

            GUILayout.Space(2f);

            // Banners sit above the scroll view (always visible) and are omitted on the Issues tab,
            // which lists the same problems in full.
            if (_selectedTab != IssuesTab)
            {
                DrawWarnings();
                GUILayout.Space(2f);
            }

            // Compute the active table's display widths BEFORE the scroll view, so the horizontal
            // position can be clamped against the real content width: a fitting table must never show a
            // horizontal scrollbar or cut off its first column, and a stale horizontal offset is reset.
            float tableViewport = Mathf.Max(480f, EditorGUIUtility.currentViewWidth - TableViewportDeduction);
            float tableViewWidth = PrecomputeActiveTable(tableViewport);
            if (tableViewWidth <= tableViewport + 0.5f)
            {
                _scrollPos.x = 0f;
            }

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, true, true);

            switch (_selectedTab)
            {
                case IssuesTab:
                    DrawIssuesTab();
                    break;
                case BusesTab:
                    DrawHeader();
                    // The scroll content is header + rows (banners are outside), so rows begin at a
                    // known content offset.
                    _rowsStartContentY = HeaderHeight;
                    DrawBusRows();
                    break;
                case SummaryTab:
                    DrawSummaryTab();
                    break;
                default:
                    DrawContainersTab();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        // Applies queued layout-state changes at the top of a Layout pass, before the first
        // GUILayoutUtility allocation, so this frame's Layout and Repaint passes see the same structure.
        // Layout event only: a click runs after this frame's Layout and before its Repaint, and applying
        // it on that Repaint would make the two passes of one frame disagree on the control count.
        private void ApplyPendingState()
        {
            if (Event.current == null || Event.current.type != EventType.Layout)
            {
                return;
            }

            bool changed = false;

            if (_pendingTab != NoPendingTab)
            {
                if (_pendingTab != _selectedTab)
                {
                    _selectedTab = _pendingTab;
                    _scrollPos = Vector2.zero;
                    // The highlight is tab-specific; leaving the tab clears it.
                    _selectedBusKey = null;
                    _selectedContainerKey = null;
                    changed = true;
                }

                _pendingTab = NoPendingTab;
            }

            // Selection must land AFTER the tab switch: the switch above clears the selection
            // ("leaving the tab clears it"), so applying it first would wipe the navigation's own
            // selection — the cross-tab navigation then showed no selection at all.
            if (_pendingSelectBusKey != null)
            {
                _selectedBusKey = _pendingSelectBusKey;
                _pendingSelectBusKey = null;
                changed = true;
            }

            if (_pendingSelectContainerKey != null)
            {
                _selectedContainerKey = _pendingSelectContainerKey;
                _pendingSelectContainerKey = null;
                changed = true;
            }

            if (_pendingExpandKey != null)
            {
                if (_pendingExpandToggle)
                {
                    _expanded.Add(_pendingExpandKey);
                }
                else
                {
                    _expanded.Remove(_pendingExpandKey);
                }

                _pendingExpandKey = null;
                _rowsDirty = true;
                changed = true;
            }

            if (_pendingScrollContentY >= 0f)
            {
                _scrollPos.y = _pendingScrollContentY;
                _pendingScrollContentY = NoPendingScroll;
                changed = true;
            }

            if (_pendingRowsDirty)
            {
                // Filter/sort changes the row list (and, for a filter, the row count). Setting
                // _rowsDirty here means the rebuild happens in this Layout pass, so the Repaint pass
                // sees the same list.
                _pendingRowsDirty = false;
                _rowsDirty = true;
                changed = true;
            }

            if (changed)
            {
                Repaint();
            }
        }

        // The Layout section: everything that can change the NUMBER of controls is applied here, before
        // the first allocation, and ONLY in the Layout pass. The Repaint pass reuses this state verbatim,
        // so both passes allocate the same controls. See the class-level invariant.
        private void BeginLayoutSection()
        {
            if (Event.current == null || Event.current.type != EventType.Layout)
            {
                return;
            }

            EventBusContext context = EventBusEditorDiagnostics.Current;
            if (context != null && !context.IsDisposed)
            {
                if (context != _lastContext)
                {
                    _lastContext = context;
                    _previousPublishes.Clear();
                    _snapshotDirty = true;
                }

                if (_snapshotDirty || _lastSnapshotTime == 0d)
                {
                    RefreshSnapshot();
                }
            }

            _snapshotDirty = false;

            // Tab / expand / scroll / filter changes queued during the previous frame's drawing.
            ApplyPendingState();

            // Rebuild the Buses flat row list here, never from a draw pass.
            if (_rowsDirty)
            {
                BuildFlatRows();
                _rowsDirty = false;
            }
        }

        // Fills the active tab's render scratch array and returns the table width. Called before the
        // scroll view; the draw passes then read the scratch array instead of recomputing.
        private float PrecomputeActiveTable(float viewport)
        {
            switch (_selectedTab)
            {
                case IssuesTab:
                    return EventBusTableLayout.LayoutColumns(
                        _issueWidths, EventBusTableLayout.IssueMins, EventBusTableLayout.IssueFlex,
                        viewport, _issueRender);
                case BusesTab:
                    return EventBusTableLayout.LayoutColumns(
                        _busWidths, EventBusTableLayout.BusMins, EventBusTableLayout.BusFlex,
                        viewport, _busRender);
                case ContainersTab:
                    return EventBusTableLayout.LayoutColumns(
                        _containerWidths, EventBusTableLayout.ContainerMins, EventBusTableLayout.ContainerFlex,
                        viewport, _containerRender);
                default:
                    return viewport;
            }
        }

        private void DrawPlaceholder(string message, MessageType type)
        {
            EditorGUILayout.HelpBox(message, type);
            if (GUILayout.Button("Refresh", GUILayout.Width(90f)))
            {
                _nextRefresh = 0d;
                Repaint();
            }
        }

        // ------------------------------------------------------------------ top bar

        private void DrawTopBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                {
                    // Deferred: rebuilding mid-draw would change this frame's Repaint control count.
                    _snapshotDirty = true;
                    Repaint();
                }

                bool auto = GUILayout.Toggle(_autoRefresh, "Auto", EditorStyles.toolbarButton, GUILayout.Width(48f));
                if (auto != _autoRefresh)
                {
                    _autoRefresh = auto;
                    _nextRefresh = 0d;
                }

                GUILayout.Space(10f);

                DrawStatPill(_pillBusesText, PillBuses);
                GUILayout.Space(4f);
                DrawStatPill(_pillSubsText, PillSubs);
                GUILayout.Space(4f);
                DrawStatPill(_pillRateText, PillRate);
                if (_aggregate.TombstoneCount > 0)
                {
                    GUILayout.Space(4f);
                    DrawStatPill(_pillTombText, PillWarning);
                }

                if (_aggregate.SubscriberErrorCount > 0)
                {
                    GUILayout.Space(4f);
                    DrawStatPill(_pillErrText, PillError);
                }

                if (_aggregate.DroppedReentrantCount > 0)
                {
                    GUILayout.Space(4f);
                    DrawStatPill(_pillDropText, PillError);
                }

                if (_deadSubscriberCount > 0)
                {
                    GUILayout.Space(4f);
                    DrawStatPill(_pillDeadText, PillError);
                }

                // Container backpressure: refused writes are a warning, confirmed loss an error.
                if (_containerDropped + _containerRejected > 0)
                {
                    GUILayout.Space(4f);
                    DrawStatPill(
                        _pillBackpressureText,
                        _containerDropped > 0 ? PillError : PillWarning,
                        "Writes refused across registered containers — a producer is outrunning its "
                        + "drain. Per-container detail on the Containers tab. Refused writes are not "
                        + "lost events: the caller may retry.");
                }

                // Issues-only: pin the row order so the reader can watch one row's numbers change.
                if (_selectedTab == IssuesTab)
                {
                    GUILayout.Space(8f);
                    bool freeze = GUILayout.Toggle(
                        _freezeIssues, "Freeze order", EditorStyles.toolbarButton, GUILayout.Width(96f));
                    if (freeze != _freezeIssues)
                    {
                        _freezeIssues = freeze;
                    }
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Copy TSV", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildTsv();
                }

                if (GUILayout.Button("Copy JSON", EditorStyles.toolbarButton, GUILayout.Width(76f)))
                {
                    EditorGUIUtility.systemCopyBuffer = BuildJson();
                }

                GUILayout.Space(8f);
                GUILayout.Label("Filter:", GUILayout.Width(38f));
                string newFilter = EditorGUILayout.TextField(
                    _searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(180f));
                if (newFilter != _searchFilter)
                {
                    _searchFilter = newFilter;
                    // A filter changes which rows match (i.e. the row count): defer the rebuild so the
                    // Layout/Repaint passes of any frame stay consistent.
                    _pendingRowsDirty = true;
                }

                if (GUILayout.Button("x", EditorStyles.toolbarButton, GUILayout.Width(20f)))
                {
                    _searchFilter = string.Empty;
                    _pendingRowsDirty = true;
                    GUI.FocusControl(null);
                }
            }
        }

        private void DrawStatPill(string text, Color color)
        {
            DrawStatPill(text, color, null);
        }

        // The tooltip is optional: most pills are self-explanatory, but a term such as "Backpressure"
        // needs an explanation for a reader who has not met it. The shared GUIContent keeps the label
        // allocation-free.
        private void DrawStatPill(string text, Color color, string tooltip)
        {
            Color previous = GUI.color;
            GUI.color = color;
            _cellContent.text = text;
            _cellContent.tooltip = tooltip;
            GUILayout.Label(_cellContent, _pillStyle, _pillOptions);
            GUI.color = previous;
        }

        // ------------------------------------------------------------------ warnings

        private void DrawWarnings()
        {
            if (_deadSubscriberCount > 0)
            {
                EditorGUILayout.HelpBox(
                    _deadSubscriberCount + " destroyed subscriber target(s) still subscribed "
                    + "(" + _warnLeakText + "). This is a leak: release those subscriptions through a "
                    + "scope or OnDestroy.",
                    MessageType.Error);
            }

            if (_aggregate.SubscriberErrorCount > 0)
            {
                EditorGUILayout.HelpBox(
                    "Subscriber(s) threw and were handled by the error policy (" + _warnErrText + "). "
                    + "The exception type, message and offender are on the Issues tab.",
                    MessageType.Warning);
            }

            if (_aggregate.DroppedReentrantCount > 0)
            {
                EditorGUILayout.HelpBox(
                    "Publishes were dropped at the re-entrancy ceiling (" + _warnDropText + "): a "
                    + "handler is publishing its own event type. Break the cycle.",
                    MessageType.Warning);
            }

            if (_aggregate.TombstoneCount > 0)
            {
                EditorGUILayout.HelpBox(
                    "Tombstones (dead slots) present (" + _warnTombText + "). A climbing value means "
                    + "subscription churn: gate the subscription with an activity flag.",
                    MessageType.Info);
            }

            if (_containerDropped > 0)
            {
                EditorGUILayout.HelpBox(
                    "Container(s) dropped events — confirmed loss, discarded unread (" + _warnBackpressureText + ").",
                    MessageType.Error);
            }
            else if (_containerRejected > 0)
            {
                EditorGUILayout.HelpBox(
                    "Container(s) refused writes — backpressure, caller may retry (" + _warnBackpressureText + ").",
                    MessageType.Warning);
            }

            if (_issueCount > 0
                && GUILayout.Button("Open Issues (" + _issueCount + ")", GUILayout.Width(180f)))
            {
                // Tab switch is structural: defer it. ApplyPendingState also resets _scrollPos when it
                // actually switches, so we must not touch _scrollPos here (that would change the
                // virtualized row count mid-draw).
                _pendingTab = IssuesTab;
                Repaint();
            }
        }

        // ------------------------------------------------------------------ issues tab

        private void DrawIssuesTab()
        {
            float tableWidth = Sum(_issueRender);

            if (_issues.Count == 0)
            {
                Rect quiet = RowRect(tableWidth);
                DrawColoredLabel(quiet, "No issues detected \u2014 all counters are quiet.", _rowStyle, StateOk);
                return;
            }

            DrawTableHeader(
                IssueHeaderTitles, IssueHeaderTooltips, EventBusTableLayout.IssueTextColumns, _issueWidths, _issueRender,
                EventBusTableLayout.IssueMins, EventBusTableLayout.IssueMaxes,
                EventBusTableLayout.IssueFlex, false,
                () => Array.Copy(EventBusTableLayout.IssueDefaults, _issueWidths, _issueWidths.Length));

            for (int index = 0; index < _issues.Count; index++)
            {
                IssueRow row = _issues[index];
                Rect rect = RowRect(tableWidth);
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(rect, (index & 1) == 0 ? RowEven : RowOdd);
                }

                if (HandleRowClick(rect))
                {
                    NavigateTo(row);
                }

                Color severityColor = SeverityColor(row.Severity);
                float x = rect.x;
                DrawBadge(new Rect(x, rect.y, _issueRender[0], rect.height), SeverityText(row.Severity), severityColor);
                x += _issueRender[0];

                DrawColoredLabel(new Rect(x, rect.y, _issueRender[1], rect.height),
                    row.CategoryText, _subStyle, severityColor, row.CategoryText);
                x += _issueRender[1];

                DrawColoredLabel(new Rect(x, rect.y, _issueRender[2], rect.height),
                    row.SubjectText, _rowStyle, Color.white, row.SubjectText);
                x += _issueRender[2];

                DrawColoredLabel(new Rect(x, rect.y, _issueRender[3], rect.height),
                    row.LocationText, _subStyle, TextDim, row.LocationText);
                x += _issueRender[3];

                _cellContent.text = row.ValueText;
                _cellContent.tooltip = row.ValueTooltip;
                GUI.Label(new Rect(x, rect.y, _issueRender[4], rect.height), _cellContent, _subStyle);
                x += _issueRender[4];

                DrawColoredLabel(new Rect(x, rect.y, _issueRender[5], rect.height),
                    row.DeltaText, _numericStyle, row.DeltaMagnitude > 0 ? TextWarning : TextDim);
                x += _issueRender[5];

                DrawColoredLabel(new Rect(x, rect.y, _issueRender[6], rect.height),
                    row.RemediationText, _subStyle, Color.white, row.RemediationText);
                x += _issueRender[6];

                DrawTrend(new Rect(x, rect.y + 3f, _issueRender[7] - 8f, rect.height - 6f), row.Trend);
            }
        }

        private void NavigateTo(IssueRow row)
        {
            // Called mid-draw (row click). Only records intent; ApplyPendingState applies it at the top
            // of the next Layout pass, so the Layout/Repaint structure stays consistent.
            if (row.TargetTab == BusesTab)
            {
                _pendingTab = BusesTab;
                _pendingExpandKey = row.TargetKey;
                _pendingExpandToggle = true;
                _pendingSelectBusKey = row.TargetKey;
                _pendingSelectContainerKey = null;
                _scrollTargetBus = row.TargetKey;
            }
            else if (row.TargetTab == ContainersTab)
            {
                _pendingTab = ContainersTab;
                _pendingSelectContainerKey = row.TargetKey;
                _pendingSelectBusKey = null;
                _scrollTargetContainer = row.TargetKey;
            }

            // Arrival pulse, then the persistent outline. Colour-only, so it is safe to set mid-draw.
            _flashUntil = EditorApplication.timeSinceStartup + FlashSeconds;
            Repaint();
        }

        private static string SeverityText(int severity)
        {
            return severity == 0 ? "ERR" : severity == 1 ? "WARN" : "INFO";
        }

        private static Color SeverityColor(int severity)
        {
            return severity == 0 ? TextError : severity == 1 ? TextWarning : TextDim;
        }

        // ------------------------------------------------------------------ buses tab

        private void DrawHeader()
        {
            // The render widths were computed in OnGUI (PrecomputeActiveTable) before the scroll view.
            DrawTableHeader(
                BusHeaderTitles, BusHeaderTooltips, EventBusTableLayout.BusTextColumns, _busWidths, _busRender,
                EventBusTableLayout.BusMins, EventBusTableLayout.BusMaxes,
                EventBusTableLayout.BusFlex, true,
                () => Array.Copy(EventBusTableLayout.BusDefaults, _busWidths, _busWidths.Length));
        }

        // Shared header for all three tables: labels, separators, drag-to-resize (every column but
        // the flexible one), reset-on-right-click, and optional click-to-sort. Drawn from the RENDER
        // widths (post-squeeze); a resize writes back to the USER widths, so the two stay
        // independent and a narrow window never destroys the user's column sizes.
        private void DrawTableHeader(
            string[] titles,
            string[] tooltips,
            int[] textColumns,
            float[] userWidths,
            float[] renderWidths,
            float[] mins,
            float[] maxes,
            int flexColumn,
            bool supportsSort,
            Action resetColumns)
        {
            float tableWidth = Sum(renderWidths);
            Rect headerRect = GUILayoutUtility.GetRect(tableWidth, tableWidth, HeaderHeight, HeaderHeight, GUIStyle.none);

            if (Event.current.type == EventType.Repaint)
            {
                EditorStyles.toolbar.Draw(headerRect, GUIContent.none, false, false, false, false);
            }

            if (Event.current.type == EventType.ContextClick && headerRect.Contains(Event.current.mousePosition))
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Reset Columns"), false, () =>
                {
                    resetColumns();
                    Repaint();
                });
                menu.ShowAsContext();
                Event.current.Use();
            }

            float x = headerRect.x;
            for (int column = 0; column < titles.Length; column++)
            {
                Rect cellRect = new Rect(x, headerRect.y, renderWidths[column], headerRect.height);

                string title = titles[column];
                if (supportsSort && _sortColumn == column)
                {
                    title += _sortAscending ? "  \u25B2" : "  \u25BC";
                }

                _cellContent.text = title;
                _cellContent.tooltip = tooltips[column];
                GUI.Label(cellRect, _cellContent,
                    EventBusTableLayout.IsTextColumn(textColumns, column) ? _headerCellStyle : _headerNumericStyle);

                DrawColumnSeparator(cellRect);

                Rect handle = new Rect(
                    cellRect.xMax - ResizeHandleWidth * 0.5f, cellRect.y, ResizeHandleWidth, cellRect.height);

                // Every real column's right edge resizes that column; the flexible column is the
                // invisible filler at the end and owns no handle (its width is recomputed each frame).
                if (column != flexColumn)
                {
                    HandleColumnResize(column, cellRect, userWidths, mins, maxes);
                }

                Event current = Event.current;
                if (supportsSort && column != flexColumn
                    && current.type == EventType.MouseDown && current.button == 0
                    && cellRect.Contains(current.mousePosition) && !handle.Contains(current.mousePosition))
                {
                    if (_sortColumn == column)
                    {
                        _sortAscending = !_sortAscending;
                    }
                    else
                    {
                        _sortColumn = column;
                        _sortAscending = true;
                    }

                    // Sorting reorders but does not change the row count; still defer the rebuild to the
                    // next Layout pass so no draw pass ever mutates the row list mid-frame.
                    _pendingRowsDirty = true;
                    current.Use();
                    Repaint();
                }

                x += renderWidths[column];
            }
        }

        private void DrawColumnSeparator(Rect cellRect)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            EditorGUI.DrawRect(
                new Rect(cellRect.xMax - 1f, cellRect.y + 3f, 1f, cellRect.height - 6f),
                new Color(0.5f, 0.5f, 0.5f, 0.35f));
        }

        private void HandleColumnResize(int column, Rect cellRect, float[] widths, float[] mins, float[] maxes)
        {
            Rect handle = new Rect(
                cellRect.xMax - ResizeHandleWidth * 0.5f, cellRect.y, ResizeHandleWidth, cellRect.height);
            EditorGUIUtility.AddCursorRect(handle, MouseCursor.ResizeHorizontal);

            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && handle.Contains(current.mousePosition))
            {
                _dragColumn = column;
                _dragStartX = current.mousePosition.x;
                _dragStartWidth = widths[column];
                current.Use();
            }
            else if (current.type == EventType.MouseDrag && _dragColumn == column)
            {
                float width = _dragStartWidth + (current.mousePosition.x - _dragStartX);
                // Cap the drag at the column's maximum: a 2-3 character badge column must not be
                // draggable into a giant bar.
                widths[column] = Mathf.Clamp(width, mins[column], maxes[column]);
                current.Use();
                Repaint();
            }
            else if (current.type == EventType.MouseUp && _dragColumn == column)
            {
                _dragColumn = -1;
                current.Use();
            }
        }

        // Draws the selection overlay ON TOP of whatever status background the row already has, so a
        // selected ERR row keeps its red fill and is still unmistakable: a thick left accent plus a 2px
        // border (6px / 3px and brighter during the arrival pulse). Colour-only; Repaint event only.
        private static void DrawSelectionOverlay(Rect rect, bool flash)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            float accent = flash ? 6f : 4f;
            float border = flash ? 3f : 2f;
            Color color = flash ? AccentFlash : AccentBar;

            EditorGUI.DrawRect(new Rect(rect.x, rect.y, accent, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, border), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - border, rect.width, border), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - border, rect.y, border, rect.height), color);
        }

        private static float Sum(float[] widths)
        {
            float total = 0f;
            for (int index = 0; index < widths.Length; index++)
            {
                total += widths[index];
            }

            return total;
        }

        private void DrawBusRows()
        {
            // The header already computed _busRender this frame, and BeginLayoutSection already rebuilt
            // _flatRows if needed — this pass only reads.
            float tableWidth = Sum(_busRender);

            int total = _flatRows.Count;
            if (total == 0)
            {
                EditorGUILayout.HelpBox(
                    string.IsNullOrEmpty(_searchFilter)
                        ? "No buses registered on this context yet."
                        : "No bus matches the current filter.",
                    MessageType.Info);
                return;
            }

            // Bring a row navigated to from the Issues tab into view. The target Y becomes pending and is
            // applied at the top of the next Layout pass — never mid-draw (content offset + row index).
            if (_scrollTargetBus != null)
            {
                for (int index = 0; index < _flatRows.Count; index++)
                {
                    RowItem candidate = _flatRows[index];
                    if (candidate.Kind == 0 && _buses[candidate.BusIndex].TypeName == _scrollTargetBus)
                    {
                        float rowTop = _rowsStartContentY + index * RowHeight;
                        float target = ScrollIntoView(rowTop, rowTop + RowHeight);
                        if (target >= 0f)
                        {
                            _pendingScrollContentY = target;
                        }

                        break;
                    }
                }

                _scrollTargetBus = null;
                Repaint();
            }

            // Virtualization: only rows intersecting the scroll viewport are drawn, with a spacer rect
            // above and below so the content keeps its full height. The bounds are computed in the Layout
            // pass ONLY (after the scroll view has settled _scrollPos) and reused verbatim by Repaint —
            // recomputing them during Repaint is what made the two passes allocate different counts.
            if (Event.current.type == EventType.Layout)
            {
                RecomputeBusRowRange(total);
            }

            int firstVisible = _rowRangeFirst;
            int lastVisible = _rowRangeLast;

            if (firstVisible > 0)
            {
                float skip = firstVisible * RowHeight;
                GUILayoutUtility.GetRect(tableWidth, tableWidth, skip, skip, GUIStyle.none);
            }

            for (int row = firstVisible; row < lastVisible; row++)
            {
                RowItem item = _flatRows[row];
                switch (item.Kind)
                {
                    case 1:
                        DrawSubscriberRow(_buses[item.BusIndex].Subs[item.SubIndex], tableWidth, row);
                        break;
                    case 2:
                        DrawOverflowRow(_buses[item.BusIndex], tableWidth);
                        break;
                    case 3:
                        DrawHintRow(tableWidth);
                        break;
                    default:
                        DrawBusRow(_buses[item.BusIndex], tableWidth, row);
                        break;
                }
            }

            if (lastVisible < total)
            {
                float skip = (total - lastVisible) * RowHeight;
                GUILayoutUtility.GetRect(tableWidth, tableWidth, skip, skip, GUIStyle.none);
            }
        }

        // Scroll-into-view with minimal movement: -1 when the row is already fully visible, otherwise
        // the smallest shift that brings it in (flush to the bottom, or one row below the top). The
        // table header lives inside the scroll content and therefore scrolls out of view by design.
        private float ScrollIntoView(float rowTop, float rowBottom)
        {
            float viewTop = _scrollPos.y;
            float viewBottom = _scrollPos.y + _viewportHeight;

            if (rowTop >= viewTop && rowBottom <= viewBottom)
            {
                return -1f; // already fully visible
            }

            if (rowBottom > viewBottom)
            {
                return Mathf.Max(0f, rowBottom - _viewportHeight);
            }

            return Mathf.Max(0f, rowTop - RowHeight);
        }

        // Computes the virtualised row bounds. Layout pass only; the result is cached in _rowRange* and
        // reused by the Repaint pass so both passes draw exactly the same rows.
        private void RecomputeBusRowRange(int total)
        {
            int first = 0;
            int last = total;
            if (_viewportHeight > 1f)
            {
                // Buses content = header + rows, so rows start at HeaderHeight.
                first = Mathf.Clamp(
                    Mathf.FloorToInt((_scrollPos.y - HeaderHeight) / RowHeight), 0, total);
                last = Mathf.Clamp(
                    Mathf.CeilToInt((_scrollPos.y + _viewportHeight - HeaderHeight) / RowHeight) + 1, 0, total);
                if (last < first)
                {
                    last = first;
                }
            }

            _rowRangeFirst = first;
            _rowRangeLast = last;
        }

        private void BuildFlatRows()
        {
            _flatRows.Clear();
            for (int busIndex = 0; busIndex < _buses.Count; busIndex++)
            {
                BusEntry entry = _buses[busIndex];
                if (!MatchesFilter(entry))
                {
                    continue;
                }

                _flatRows.Add(new RowItem { Kind = 0, BusIndex = busIndex });

                if (!_expanded.Contains(entry.TypeName))
                {
                    continue;
                }

                for (int subIndex = 0; subIndex < entry.Subs.Count; subIndex++)
                {
                    _flatRows.Add(new RowItem { Kind = 1, BusIndex = busIndex, SubIndex = subIndex });
                }

                if (entry.OverflowSubscriberCount > 0)
                {
                    _flatRows.Add(new RowItem { Kind = 2, BusIndex = busIndex });
                }

                if (entry.HasDeadSubscribers)
                {
                    _flatRows.Add(new RowItem { Kind = 3, BusIndex = busIndex });
                }
            }
        }

        private void DrawBusRow(BusEntry entry, float tableWidth, int rowIndex)
        {
            Rect rect = RowRect(tableWidth);

            bool selected = _selectedBusKey != null && entry.TypeName == _selectedBusKey;

            // A selected row is filled BLUE (RowSelected) instead of its status/zebra fill: selection is
            // an explicit navigation act, so it wins visually. The row's health is still carried by the
            // State badge column, so no status information is lost. The outline is drawn on top below.
            Color background = selected
                ? RowSelected
                : entry.HasErrors || entry.HasDropped || entry.HasDeadSubscribers
                    ? RowError
                    : entry.HasTombstones
                        ? RowWarning
                        : (rowIndex & 1) == 0 ? RowEven : RowOdd;

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, background);
            }

            if (selected)
            {
                DrawSelectionOverlay(rect, EditorApplication.timeSinceStartup < _flashUntil);
            }

            if (HandleRowClick(rect))
            {
                // Clicking a row selects it AND toggles its subscribers. Selection is colour-only; the
                // expand changes the row count so it is deferred to the next Layout pass.
                _selectedBusKey = entry.TypeName;
                _pendingExpandKey = entry.TypeName;
                _pendingExpandToggle = !_expanded.Contains(entry.TypeName);
                Repaint();
            }

            float x = rect.x;
            _cellContent.text = (_expanded.Contains(entry.TypeName) ? "\u25BE " : "\u25B8 ") + entry.DisplayName;
            _cellContent.tooltip = entry.TypeName + "\nClick to expand the subscriber list.";
            GUI.Label(new Rect(x, rect.y, _busRender[0], rect.height), _cellContent, _rowStyle);
            x += _busRender[0];

            DrawBadge(new Rect(x, rect.y, _busRender[1], rect.height), entry.StateText,
                entry.StateSeverity == 0 ? TextError : entry.StateSeverity == 1 ? TextWarning : StateOk);
            x += _busRender[1];

            DrawNumericCell(rect, ref x, 2, entry.SubsText, Color.white, "Live subscribers.");
            DrawNumericCell(rect, ref x, 3, entry.TombText,
                entry.HasTombstones ? TextWarning : Color.white, BusHeaderTooltips[3]);
            DrawNumericCell(rect, ref x, 4, entry.PublishText, Color.white, BusHeaderTooltips[4]);
            DrawNumericCell(rect, ref x, 5, entry.RateText, Color.white, BusHeaderTooltips[5]);
            DrawNumericCell(rect, ref x, 6, entry.ErrorText,
                entry.HasErrors ? TextError : Color.white, BusHeaderTooltips[6]);
            DrawNumericCell(rect, ref x, 7, entry.PeakText, Color.white, BusHeaderTooltips[7]);
            DrawNumericCell(rect, ref x, 8, entry.CapText, Color.white, BusHeaderTooltips[8]);
            DrawNumericCell(rect, ref x, 9, entry.DepthText, Color.white, BusHeaderTooltips[9]);
        }

        private void DrawSubscriberRow(in SubRow sub, float tableWidth, int rowIndex)
        {
            Rect rect = RowRect(tableWidth);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(rect, sub.IsDeadTarget
                    ? RowError
                    : (rowIndex & 1) == 0 ? RowEven : RowOdd);
            }

            float x = rect.x + 18f;
            float nameWidth = _busRender[0] - 18f;

            Color color = sub.IsDeadTarget ? TextError : TextDim;
            DrawColoredLabel(new Rect(x, rect.y, nameWidth, rect.height), sub.MethodText, _subStyle, color, sub.MethodText);
            x += nameWidth;

            // The target text owns every real column after the first (not just Tomb+Publishes), so a
            // long target like "SampleLeakDemoSubscriber  DESTROYED" is not truncated at the default width.
            float targetWidth = 0f;
            for (int column = 1; column < EventBusTableLayout.BusFlex; column++)
            {
                targetWidth += _busRender[column];
            }

            DrawColoredLabel(new Rect(x, rect.y, targetWidth, rect.height), sub.TargetText, _subStyle, color, sub.TargetText);
        }

        private void DrawOverflowRow(BusEntry entry, float tableWidth)
        {
            Rect rect = RowRect(tableWidth);
            DrawColoredLabel(rect, entry.OverflowText, _subStyle, TextDim);
        }

        // Trailing hint under a bus that has destroyed-but-subscribed targets.
        private void DrawHintRow(float tableWidth)
        {
            Rect rect = RowRect(tableWidth);
            DrawColoredLabel(rect,
                "   \u2192 DESTROYED subscriptions above are never released; dispose them via a "
                + "SubscriptionScope or OnDestroy.",
                _subStyle, TextError);
        }

        private static Rect RowRect(float tableWidth)
        {
            return GUILayoutUtility.GetRect(tableWidth, tableWidth, RowHeight, RowHeight, GUIStyle.none);
        }

        private void DrawNumericCell(Rect rowRect, ref float x, int column, string text, Color color, string tooltip)
        {
            _cellContent.text = text;
            _cellContent.tooltip = tooltip;
            Color previous = GUI.color;
            GUI.color = color;
            GUI.Label(new Rect(x, rowRect.y, _busRender[column], rowRect.height), _cellContent, _numericStyle);
            GUI.color = previous;
            x += _busRender[column];
        }

        private void DrawColoredLabel(Rect rect, string text, GUIStyle style, Color color)
        {
            DrawColoredLabel(rect, text, style, color, null);
        }

        // The tooltip is the full text, so a truncated cell stays readable on hover. The string is
        // preformatted during the snapshot, which keeps this allocation-free per repaint.
        private void DrawColoredLabel(Rect rect, string text, GUIStyle style, Color color, string tooltip)
        {
            _cellContent.text = text;
            _cellContent.tooltip = tooltip;
            Color previous = GUI.color;
            GUI.color = color;
            GUI.Label(rect, _cellContent, style);
            GUI.color = previous;
        }

        private void DrawBadge(Rect rect, string text, Color color)
        {
            Rect inner = new Rect(rect.x + 4f, rect.y + 3f, rect.width - 8f, rect.height - 6f);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(inner, new Color(color.r, color.g, color.b, 0.85f));
            }

            DrawColoredLabel(inner, text, _badgeStyle, Color.white);
        }

        // Draws up to 32 bars (oldest left, newest right) inside the rect. Repaint event only.
        private static void DrawTrend(Rect rect, List<int> trend)
        {
            if (Event.current.type != EventType.Repaint || trend == null || trend.Count == 0)
            {
                return;
            }

            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.18f));

            int count = Mathf.Min(trend.Count, TrendLength);
            int max = 1;
            for (int index = trend.Count - count; index < trend.Count; index++)
            {
                if (trend[index] > max)
                {
                    max = trend[index];
                }
            }

            float barWidth = rect.width / TrendLength;
            int firstSlot = TrendLength - count;
            for (int index = 0; index < count; index++)
            {
                int value = trend[trend.Count - count + index];
                if (value <= 0)
                {
                    continue;
                }

                float height = rect.height * (value / (float)max);
                if (height < 1f)
                {
                    height = 1f;
                }

                float x = rect.x + (firstSlot + index) * barWidth;
                EditorGUI.DrawRect(new Rect(x, rect.yMax - height, Mathf.Max(1f, barWidth - 1f), height), TextWarning);
            }
        }

        private static bool HandleRowClick(Rect rect)
        {
            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition))
            {
                current.Use();
                return true;
            }

            return false;
        }

        // ------------------------------------------------------------------ summary tab

        private void DrawSummaryTab()
        {
            EventBusDiagnosticsSnapshot s = _aggregate;

            EditorGUILayout.LabelField("Aggregate", EditorStyles.boldLabel);
            DrawSummaryRow("Active buses", s.ActiveBusCount.ToString());
            DrawSummaryRow("Live subscriptions", s.SubscriptionCount.ToString());
            DrawSummaryRow("Peak subscriptions (max across buses)", s.PeakSubscriptionCount.ToString());
            DrawSummaryRow("Publishes (total)", s.PublishCount.ToString());
            DrawSummaryRow("Publish rate", _pillRateText);
            DrawSummaryRow("Scopes", s.ScopeCount.ToString());
            GUILayout.Space(6f);

            EditorGUILayout.LabelField("Health", EditorStyles.boldLabel);
            DrawSummaryRow("Open issues", _issueCount.ToString(),
                _issueCount > 0 ? (Color?)TextWarning : null);
            DrawSummaryRow("Tombstones", s.TombstoneCount.ToString(),
                s.TombstoneCount > 0 ? (Color?)TextWarning : null);
            DrawSummaryRow("Subscriber errors", s.SubscriberErrorCount.ToString(),
                s.SubscriberErrorCount > 0 ? (Color?)TextError : null);
            DrawSummaryRow("Dropped (re-entrancy ceiling)", s.DroppedReentrantCount.ToString(),
                s.DroppedReentrantCount > 0 ? (Color?)TextError : null);
            DrawSummaryRow("Destroyed subscriber targets", _deadSubscriberCount.ToString(),
                _deadSubscriberCount > 0 ? (Color?)TextError : null);
            GUILayout.Space(6f);

            EditorGUILayout.LabelField("Backpressure", EditorStyles.boldLabel);
            DrawSummaryRow("Registered containers", _containerCount.ToString());
            DrawSummaryRow("Pending events", _containerPending.ToString());
            DrawSummaryRow("Refused writes (rejected)", _containerRejected.ToString(),
                _containerRejected > 0 ? (Color?)TextWarning : null);
            DrawSummaryRow("Confirmed loss (dropped)", _containerDropped.ToString(),
                _containerDropped > 0 ? (Color?)TextError : null);
            DrawSummaryRow("Closed containers", _containerClosedCount.ToString());
            GUILayout.Space(2f);

            EditorGUILayout.LabelField("Reading the numbers", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "The Issues tab is the entry point: it lists every problem sorted by severity, names "
                + "the event or container, the offender and a fix, and its rows click through to the "
                + "owning tab. Start there, not here.",
                MessageType.None);
            EditorGUILayout.HelpBox(
                "Peak subscriptions is the high-water mark across all buses; use it to size "
                + "initialCapacity. Tombstones are dead slots awaiting automatic compaction; a "
                + "steadily climbing value means subscribers are churning and should be gated with an "
                + "activity flag instead.",
                MessageType.None);
            EditorGUILayout.HelpBox(
                "Backpressure is where the bus counters are blind. A refused write (rejected) is a "
                + "full queue/stream — a producer outrunning its drain; the caller may still retry, so "
                + "nothing is lost yet. A dropped count is a confirmed loss of events that entered a "
                + "container and were discarded unread.",
                MessageType.None);
        }

        private void DrawSummaryRow(string label, string value, Color? valueColor = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(label, _summaryLabelStyle, GUILayout.Width(260f));
                Color previous = GUI.color;
                if (valueColor.HasValue)
                {
                    GUI.color = valueColor.Value;
                }

                GUILayout.Label(value, _summaryValueStyle, GUILayout.ExpandWidth(false));
                GUI.color = previous;
                GUILayout.FlexibleSpace();
            }
        }

        // ------------------------------------------------------------------ containers tab

        private void DrawContainersTab()
        {
            if (_containers.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No containers registered. Call context.RegisterContainer(queue | stream | "
                    + "publisher) to surface its backpressure counters here.",
                    MessageType.Info);
                return;
            }

            float tableWidth = Sum(_containerRender);

            // Bring a row navigated to from the Issues tab into view. Pending, applied next Layout pass.
            if (_scrollTargetContainer != null)
            {
                for (int index = 0; index < _containers.Count; index++)
                {
                    if (_containers[index].Name == _scrollTargetContainer)
                    {
                        float rowTop = HeaderHeight + index * RowHeight;
                        float target = ScrollIntoView(rowTop, rowTop + RowHeight);
                        if (target >= 0f)
                        {
                            _pendingScrollContentY = target;
                        }

                        break;
                    }
                }

                _scrollTargetContainer = null;
                Repaint();
            }

            DrawTableHeader(
                ContainerHeaderTitles, ContainerHeaderTooltips, EventBusTableLayout.ContainerTextColumns,
                _containerWidths, _containerRender,
                EventBusTableLayout.ContainerMins, EventBusTableLayout.ContainerMaxes,
                EventBusTableLayout.ContainerFlex, false,
                () => Array.Copy(EventBusTableLayout.ContainerDefaults, _containerWidths, _containerWidths.Length));

            for (int index = 0; index < _containers.Count; index++)
            {
                ContainerEntry entry = _containers[index];
                Rect rect = RowRect(tableWidth);

                bool selected = _selectedContainerKey != null && _selectedContainerKey == entry.Name;
                Color background = selected
                    ? RowSelected
                    : entry.StateSeverity == 0 ? RowError
                    : entry.StateSeverity == 1 ? RowWarning
                    : (index & 1) == 0 ? RowEven : RowOdd;
                if (Event.current.type == EventType.Repaint)
                {
                    EditorGUI.DrawRect(rect, background);
                }

                if (selected)
                {
                    DrawSelectionOverlay(rect, EditorApplication.timeSinceStartup < _flashUntil);
                }

                if (HandleRowClick(rect))
                {
                    // Clicking a container row selects it; colour-only, no structure change.
                    _selectedContainerKey = entry.Name;
                    Repaint();
                }

                float x = rect.x;
                DrawColoredLabel(new Rect(x, rect.y, _containerRender[0], rect.height),
                    entry.Name, _rowStyle, Color.white, entry.Name);
                x += _containerRender[0];

                DrawBadge(new Rect(x, rect.y, _containerRender[1], rect.height), entry.StateText,
                    entry.StateSeverity == 0 ? TextError : entry.StateSeverity == 1 ? TextWarning : StateOk);
                x += _containerRender[1];

                DrawColoredLabel(new Rect(x, rect.y, _containerRender[2], rect.height),
                    entry.CapacityText, _numericStyle, Color.white);
                x += _containerRender[2];

                DrawColoredLabel(new Rect(x, rect.y, _containerRender[3], rect.height),
                    entry.PendingText, _numericStyle, TextDim);
                x += _containerRender[3];

                DrawColoredLabel(new Rect(x, rect.y, _containerRender[4], rect.height),
                    entry.DroppedText, _numericStyle, entry.DroppedNum == 0 ? TextDim : TextError);
                x += _containerRender[4];

                DrawColoredLabel(new Rect(x, rect.y, _containerRender[5], rect.height),
                    entry.RejectedText, _numericStyle, entry.RejectedNum == 0 ? TextDim : TextWarning);
                x += _containerRender[5];

                DrawColoredLabel(new Rect(x, rect.y, _containerRender[6], rect.height),
                    entry.ClosedText, _numericStyle, TextDim);
                x += _containerRender[6];

                DrawColoredLabel(new Rect(x, rect.y, _containerRender[7], rect.height),
                    entry.NoteText, _subStyle, TextDim, entry.NoteText);
            }
        }

        // ------------------------------------------------------------------ snapshot

        private void RefreshSnapshot()
        {
            EventBusContext context = EventBusEditorDiagnostics.Current;
            if (context == null || context.IsDisposed)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            double elapsed = _lastSnapshotTime > 0d ? now - _lastSnapshotTime : 0d;
            _lastSnapshotTime = now;

            _aggregate = context.GetDiagnosticsSnapshot();
            _pillBusesText = "Buses " + _aggregate.ActiveBusCount;
            _pillSubsText = "Subs " + _aggregate.SubscriptionCount;
            _pillTombText = "Tomb " + _aggregate.TombstoneCount;
            _pillErrText = "Errors " + _aggregate.SubscriberErrorCount;
            _pillDropText = "Dropped " + _aggregate.DroppedReentrantCount;

            IReadOnlyList<IEventBusDiagnostics> buses = context.GetRegisteredBuses();

            _deadSubscriberCount = 0;
            long totalRateCount = 0;

            _buses.Clear();
            for (int index = 0; index < buses.Count; index++)
            {
                IEventBusDiagnostics bus = buses[index];
                EventBusSnapshot snapshot = bus.GetSnapshot();
                BusEntry entry = GetOrCreateEntry(bus.EventTypeName);

                _previousPublishes.TryGetValue(entry.TypeName, out long previousPublish);
                _previousPublishes[entry.TypeName] = snapshot.PublishCount;
                long delta = snapshot.PublishCount - previousPublish;
                totalRateCount += delta;

                entry.RateNum = elapsed > 0d ? delta / elapsed : 0d;
                entry.RateText = elapsed > 0d ? FormatRate(entry.RateNum) : "-";
                entry.SubsNum = snapshot.SubscriptionCount;
                entry.TombNum = snapshot.TombstoneCount;
                entry.PublishNum = snapshot.PublishCount;
                entry.ErrorNum = snapshot.SubscriberErrorCount;
                entry.DroppedNum = snapshot.DroppedReentrantCount;
                entry.PeakNum = snapshot.PeakSubscriptionCount;
                entry.CapNum = snapshot.Capacity;
                entry.DepthNum = snapshot.DispatchDepth;

                RefreshSubscribers(bus, entry);

                // Deltas relative to the previous snapshot (zero on the first, and after a reset).
                if (entry.DeltasInitialized)
                {
                    entry.ErrorDelta = (int)(entry.ErrorNum - entry.PrevErrors);
                    entry.TombDelta = entry.TombNum - entry.PrevTomb;
                    entry.DroppedDelta = (int)(entry.DroppedNum - entry.PrevDropped);
                    entry.DeadDelta = entry.DeadCount - entry.PrevDead;
                }
                else
                {
                    entry.ErrorDelta = entry.TombDelta = entry.DroppedDelta = entry.DeadDelta = 0;
                    entry.DeltasInitialized = true;
                }

                entry.PrevErrors = entry.ErrorNum;
                entry.PrevTomb = entry.TombNum;
                entry.PrevDropped = entry.DroppedNum;
                entry.PrevDead = entry.DeadCount;

                entry.SubsText = snapshot.SubscriptionCount.ToString();
                entry.TombText = entry.TombDelta != 0
                    ? snapshot.TombstoneCount + " " + FormatDelta(entry.TombDelta)
                    : snapshot.TombstoneCount.ToString();
                entry.PublishText = snapshot.PublishCount.ToString();
                entry.ErrorText = entry.ErrorDelta != 0
                    ? snapshot.SubscriberErrorCount + " " + FormatDelta(entry.ErrorDelta)
                    : snapshot.SubscriberErrorCount.ToString();
                entry.PeakText = snapshot.PeakSubscriptionCount.ToString();
                entry.CapText = snapshot.Capacity.ToString();
                entry.DepthText = snapshot.DispatchDepth.ToString();
                entry.DroppedText = snapshot.DroppedReentrantCount.ToString();

                entry.HasTombstones = snapshot.TombstoneCount > 0;
                entry.HasErrors = snapshot.SubscriberErrorCount > 0;
                entry.HasDropped = snapshot.DroppedReentrantCount > 0;

                // Fault detail and policy, read per-bus from IEventBusFaultInspection. The policy is
                // a property of the bus (configurable per registration), so it must never be taken
                // from the context.
                if (bus is IEventBusFaultInspection faultInspection)
                {
                    entry.PolicyName = faultInspection.PublishErrorPolicy.ToString();
                    if (faultInspection.TryGetLastFault(out EventBusFaultInfo fault))
                    {
                        entry.HasFault = true;
                        entry.FaultCount = fault.Count;
                        entry.FaultTypeName = fault.ExceptionTypeName;
                        entry.FaultMessage = fault.Message;
                        entry.FaultSubscriber = fault.SubscriberName;
                        entry.FaultDeclaring = fault.DeclaringTypeName;
                    }
                    else
                    {
                        entry.HasFault = false;
                    }
                }
                else
                {
                    entry.HasFault = false;
                    entry.PolicyName = "-";
                }

                int severity = entry.HasErrors || entry.HasDropped || entry.HasDeadSubscribers
                    ? 0
                    : entry.HasTombstones ? 1 : 2;
                entry.StateSeverity = severity;
                entry.StateText = SeverityText(severity);

                PushTrend(entry.Trend, entry.ErrorNum + entry.DroppedNum);

                _buses.Add(entry);
            }

            ApplySort();

            _pillRateText = elapsed > 0d ? FormatRate(totalRateCount / elapsed) + "/s" : "-";
            _pillDeadText = "Dead " + _deadSubscriberCount;

            RefreshContainers(context);
            BuildIssues();

            _rowsDirty = true;
        }

        private void RefreshContainers(EventBusContext context)
        {
            IReadOnlyList<IEventBusContainerDiagnostics> containers = context.GetRegisteredContainers();
            _containers.Clear();

            long pending = 0;
            long dropped = 0;
            long rejected = 0;
            int closed = 0;

            for (int index = 0; index < containers.Count; index++)
            {
                EventBusContainerSnapshot snapshot = containers[index].GetContainerSnapshot();
                ContainerEntry entry = GetOrCreateContainerEntry(index);
                entry.Name = snapshot.Name;
                entry.CapacityText = snapshot.Capacity.ToString();
                entry.PendingNum = snapshot.PendingCount;
                entry.DroppedNum = snapshot.DroppedCount;
                entry.RejectedNum = snapshot.RejectedCount;
                entry.Closed = snapshot.IsClosed;
                entry.NoteText = ContainerNote(snapshot.Name);

                if (entry.DeltasInitialized)
                {
                    entry.PendingDelta = entry.PendingNum - entry.PrevPending;
                    entry.DroppedDelta = (int)(entry.DroppedNum - entry.PrevDropped);
                    entry.RejectedDelta = (int)(entry.RejectedNum - entry.PrevRejected);
                }
                else
                {
                    entry.PendingDelta = entry.DroppedDelta = entry.RejectedDelta = 0;
                    entry.DeltasInitialized = true;
                }

                entry.PrevPending = entry.PendingNum;
                entry.PrevDropped = entry.DroppedNum;
                entry.PrevRejected = entry.RejectedNum;

                entry.PendingText = entry.PendingDelta != 0
                    ? entry.PendingNum + " " + FormatDelta(entry.PendingDelta)
                    : entry.PendingNum.ToString();
                entry.DroppedText = entry.DroppedDelta != 0
                    ? entry.DroppedNum + " " + FormatDelta(entry.DroppedDelta)
                    : entry.DroppedNum.ToString();
                entry.RejectedText = entry.RejectedDelta != 0
                    ? entry.RejectedNum + " " + FormatDelta(entry.RejectedDelta)
                    : entry.RejectedNum.ToString();
                entry.ClosedText = entry.Closed ? "yes" : "-";

                int severity = entry.DroppedNum > 0
                    ? 0
                    : entry.RejectedNum > 0 ? 1 : entry.Closed ? 2 : 2;
                // Closed with no pressure is informational, not a warning: keep OK unless it closed.
                if (entry.DroppedNum == 0 && entry.RejectedNum == 0)
                {
                    severity = entry.Closed ? 1 : 2;
                }

                entry.StateSeverity = severity;
                entry.StateText = SeverityText(severity);

                PushTrend(entry.Trend, entry.RejectedNum + entry.DroppedNum);

                _containers.Add(entry);
                pending += snapshot.PendingCount;
                dropped += snapshot.DroppedCount;
                rejected += snapshot.RejectedCount;
                if (snapshot.IsClosed)
                {
                    closed++;
                }
            }

            _containerCount = containers.Count;
            _containerPending = pending;
            _containerDropped = dropped;
            _containerRejected = rejected;
            _containerClosedCount = closed;
            _pillBackpressureText = "Backpressure " + (rejected + dropped);
        }

        // Builds the severity-sorted issue list from the bus and container entries.
        private void BuildIssues()
        {
            _issues.Clear();

            for (int index = 0; index < _buses.Count; index++)
            {
                BusEntry entry = _buses[index];
                string subject = entry.DisplayName;

                if (entry.DeadCount > 0)
                {
                    _issues.Add(new IssueRow
                    {
                        Severity = 0,
                        CategoryText = "Leak",
                        SubjectText = subject,
                        LocationText = entry.FirstDeadLocation ?? "-",
                        ValueText = entry.DeadCount + " destroyed target(s)",
                        ValueTooltip = "Subscribers whose Unity target was destroyed but never unsubscribed.",
                        DeltaMagnitude = Mathf.Abs(entry.DeadDelta),
                        DeltaText = FormatDelta(entry.DeadDelta),
                        RemediationText = "destroyed subscriber is never released \u2014 dispose via a scope or OnDestroy",
                        TargetTab = BusesTab,
                        TargetKey = entry.TypeName,
                        Trend = entry.Trend,
                    });
                }

                if (entry.ErrorNum > 0)
                {
                    string preview = Truncate(entry.FaultMessage, FaultMessagePreviewLength);
                    _issues.Add(new IssueRow
                    {
                        Severity = 0,
                        CategoryText = "Subscriber error",
                        SubjectText = subject,
                        LocationText = (entry.FaultSubscriber ?? "?") + " (" + (entry.FaultDeclaring ?? "?") + ")",
                        ValueText = entry.ErrorNum + " \u00B7 " + (entry.FaultTypeName ?? "?") + ": " + preview,
                        ValueTooltip = entry.FaultMessage,
                        DeltaMagnitude = Mathf.Abs(entry.ErrorDelta),
                        DeltaText = FormatDelta(entry.ErrorDelta),
                        RemediationText = "check the subscriber body; the fault is routed by PublishErrorPolicy=" + entry.PolicyName,
                        TargetTab = BusesTab,
                        TargetKey = entry.TypeName,
                        Trend = entry.Trend,
                    });
                }

                if (entry.DroppedNum > 0)
                {
                    _issues.Add(new IssueRow
                    {
                        Severity = 1,
                        CategoryText = "Reentrant drop",
                        SubjectText = subject,
                        LocationText = subject,
                        ValueText = entry.DroppedNum + " publish(es) dropped at the depth ceiling",
                        ValueTooltip = "A handler publishes its own event type, directly or through a cycle.",
                        DeltaMagnitude = Mathf.Abs(entry.DroppedDelta),
                        DeltaText = FormatDelta(entry.DroppedDelta),
                        RemediationText = "a handler publishes its own event type \u2014 break the cycle",
                        TargetTab = BusesTab,
                        TargetKey = entry.TypeName,
                        Trend = entry.Trend,
                    });
                }

                if (entry.TombNum > 0)
                {
                    _issues.Add(new IssueRow
                    {
                        Severity = 2,
                        CategoryText = "Tombstone churn",
                        SubjectText = subject,
                        LocationText = subject,
                        ValueText = entry.TombNum + " dead slot(s) awaiting compaction",
                        ValueTooltip = "Dead slots left by unsubscribe.",
                        DeltaMagnitude = Mathf.Abs(entry.TombDelta),
                        DeltaText = FormatDelta(entry.TombDelta),
                        RemediationText = "subscription churn \u2014 gate the subscription with an activity flag",
                        TargetTab = BusesTab,
                        TargetKey = entry.TypeName,
                        Trend = entry.Trend,
                    });
                }
            }

            for (int index = 0; index < _containers.Count; index++)
            {
                ContainerEntry entry = _containers[index];

                if (entry.DroppedNum > 0)
                {
                    _issues.Add(new IssueRow
                    {
                        Severity = 0,
                        CategoryText = "Container loss",
                        SubjectText = entry.Name,
                        LocationText = entry.Name,
                        ValueText = entry.DroppedNum + " event(s) dropped; " + entry.PendingNum + " pending",
                        ValueTooltip = "Confirmed loss: events entered the container and were discarded unread by Clear.",
                        DeltaMagnitude = Mathf.Abs(entry.DroppedDelta),
                        DeltaText = FormatDelta(entry.DroppedDelta),
                        RemediationText = "confirmed loss \u2014 events entered the container and were discarded unread",
                        TargetTab = ContainersTab,
                        TargetKey = entry.Name,
                        Trend = entry.Trend,
                    });
                }
                else if (entry.RejectedNum > 0)
                {
                    _issues.Add(new IssueRow
                    {
                        Severity = 1,
                        CategoryText = "Backpressure",
                        SubjectText = entry.Name,
                        LocationText = entry.Name,
                        ValueText = entry.RejectedNum + " refused write(s); " + entry.PendingNum + " pending",
                        ValueTooltip = "Refused write: the container is full. The caller may retry; nothing is lost yet.",
                        DeltaMagnitude = Mathf.Abs(entry.RejectedDelta),
                        DeltaText = FormatDelta(entry.RejectedDelta),
                        RemediationText = "refused write \u2014 a producer outruns its drain; the caller may retry",
                        TargetTab = ContainersTab,
                        TargetKey = entry.Name,
                        Trend = entry.Trend,
                    });
                }
                else if (entry.Closed)
                {
                    _issues.Add(new IssueRow
                    {
                        Severity = 2,
                        CategoryText = "Container closed",
                        SubjectText = entry.Name,
                        LocationText = entry.Name,
                        ValueText = entry.PendingNum + " pending at close",
                        ValueTooltip = "The container was closed to new writes.",
                        DeltaMagnitude = 0,
                        DeltaText = FormatDelta(0),
                        RemediationText = "confirm the close was intended and the remainder was drained",
                        TargetTab = ContainersTab,
                        TargetKey = entry.Name,
                        Trend = entry.Trend,
                    });
                }
            }

            ApplyIssueOrder();
            _issueCount = _issues.Count;
            BuildWarningSummaries();
        }

        // Orders _issues for display: deterministic (severity / subject / category) by default, or the
        // previous order when "Freeze order" is on, with new issues appended. The logic is a pure,
        // unit-tested function (EventBusIssueOrdering); delta is deliberately not a sort key.
        private void ApplyIssueOrder()
        {
            int count = _issues.Count;
            if (count == 0)
            {
                _previousIssueOrder = Array.Empty<string>();
                return;
            }

            var keys = new string[count];
            var severities = new int[count];
            var subjects = new string[count];
            var categories = new string[count];
            for (int index = 0; index < count; index++)
            {
                IssueRow row = _issues[index];
                keys[index] = StableKey(row);
                severities[index] = row.Severity;
                subjects[index] = row.SubjectText;
                categories[index] = row.CategoryText;
            }

            int[] order = EventBusIssueOrdering.Order(
                keys, severities, subjects, categories, _previousIssueOrder, _freezeIssues);

            var reordered = new List<IssueRow>(count);
            for (int position = 0; position < order.Length; position++)
            {
                reordered.Add(_issues[order[position]]);
            }

            _issues.Clear();
            _issues.AddRange(reordered);

            // Remember the display order's stable keys for the next snapshot's freeze match.
            _previousIssueOrder = new string[count];
            for (int position = 0; position < count; position++)
            {
                _previousIssueOrder[position] = keys[order[position]];
            }
        }

        // Identity for freeze matching: two rows are "the same issue" when category, subject and
        // location match; values and deltas are allowed to change.
        private static string StableKey(IssueRow row)
        {
            return row.CategoryText + "\u0001" + row.SubjectText + "\u0001" + row.LocationText;
        }

        private void BuildWarningSummaries()
        {
            _warnLeakText = Summarize("Leak");
            _warnErrText = Summarize("Subscriber error");
            _warnDropText = Summarize("Reentrant drop");
            _warnTombText = Summarize("Tombstone churn");
            _warnBackpressureText = Combine(Summarize("Container loss"), Summarize("Backpressure"));
        }

        // "TypeA x3, TypeB x1" for the issues of one category (distinct subjects, capped at 4).
        private string Summarize(string category)
        {
            _warnBuilder.Clear();
            int distinct = 0;
            for (int index = 0; index < _issues.Count; index++)
            {
                if (_issues[index].CategoryText != category)
                {
                    continue;
                }

                string subject = _issues[index].SubjectText;
                bool seen = false;
                for (int earlier = 0; earlier < index; earlier++)
                {
                    if (_issues[earlier].CategoryText == category && _issues[earlier].SubjectText == subject)
                    {
                        seen = true;
                        break;
                    }
                }

                if (seen)
                {
                    continue;
                }

                int count = 0;
                for (int scan = 0; scan < _issues.Count; scan++)
                {
                    if (_issues[scan].CategoryText == category && _issues[scan].SubjectText == subject)
                    {
                        count++;
                    }
                }

                if (distinct > 0)
                {
                    _warnBuilder.Append(", ");
                }

                _warnBuilder.Append(subject).Append(" \u00D7").Append(count);
                distinct++;
                if (distinct >= 4)
                {
                    _warnBuilder.Append(", \u2026");
                    break;
                }
            }

            return _warnBuilder.ToString();
        }

        private static string Combine(string first, string second)
        {
            if (string.IsNullOrEmpty(first))
            {
                return second;
            }

            if (string.IsNullOrEmpty(second))
            {
                return first;
            }

            return first + ", " + second;
        }

        private BusEntry GetOrCreateEntry(string typeName)
        {
            if (_busCache.TryGetValue(typeName, out BusEntry cached))
            {
                return cached;
            }

            var entry = new BusEntry { TypeName = typeName };
            int lastDot = typeName.LastIndexOf('.');
            entry.DisplayName = lastDot >= 0 && lastDot < typeName.Length - 1
                ? typeName.Substring(lastDot + 1)
                : typeName;
            _busCache[typeName] = entry;
            return entry;
        }

        private ContainerEntry GetOrCreateContainerEntry(int index)
        {
            if (index < _containerCache.Count)
            {
                return _containerCache[index];
            }

            var entry = new ContainerEntry();
            _containerCache.Add(entry);
            return entry;
        }

        private void ApplySort()
        {
            if (_sortColumn < 0)
            {
                return;
            }

            int column = _sortColumn;
            int direction = _sortAscending ? 1 : -1;
            _buses.Sort((a, b) => direction * CompareByColumn(a, b, column));
        }

        private static int CompareByColumn(BusEntry a, BusEntry b, int column)
        {
            switch (column)
            {
                case 0: return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
                case 1: return a.StateSeverity.CompareTo(b.StateSeverity);
                case 2: return a.SubsNum.CompareTo(b.SubsNum);
                case 3: return a.TombNum.CompareTo(b.TombNum);
                case 4: return a.PublishNum.CompareTo(b.PublishNum);
                case 5: return a.RateNum.CompareTo(b.RateNum);
                case 6: return a.ErrorNum.CompareTo(b.ErrorNum);
                case 7: return a.PeakNum.CompareTo(b.PeakNum);
                case 8: return a.CapNum.CompareTo(b.CapNum);
                case 9: return a.DepthNum.CompareTo(b.DepthNum);
                default: return 0;
            }
        }

        private void RefreshSubscribers(IEventBusDiagnostics bus, BusEntry entry)
        {
            entry.Subs.Clear();
            entry.HasDeadSubscribers = false;
            entry.OverflowSubscriberCount = 0;
            entry.DeadCount = 0;
            entry.FirstDeadLocation = null;

            if (!(bus is IEventBusSubscriberInspection inspection))
            {
                return;
            }

            _subscriberScratch.Clear();
            inspection.CopySubscribersTo(_subscriberScratch);

            int rowCount = Math.Min(_subscriberScratch.Count, MaxSubscriberRowsPerBus);

            // Two passes so DESTROYED (leaked) subscribers surface at the top of the list; the dead
            // rows are what the reader is looking for.
            for (int pass = 0; pass < 2; pass++)
            {
                bool wantDead = pass == 0;
                for (int index = 0; index < rowCount; index++)
                {
                    EventSubscriberInfo info = _subscriberScratch[index];

                    string targetText;
                    bool dead = false;
                    if (info.Target == null)
                    {
                        targetText = "(static)";
                    }
                    else if (info.Target is UnityEngine.Object unityTarget && !unityTarget)
                    {
                        // Unity fake-null: a destroyed object still compares non-null against null.
                        targetText = info.Target.GetType().Name + "  DESTROYED";
                        dead = true;
                    }
                    else
                    {
                        targetText = info.Target.GetType().Name;
                    }

                    if (dead != wantDead)
                    {
                        continue;
                    }

                    if (dead)
                    {
                        entry.DeadCount++;
                        entry.HasDeadSubscribers = true;
                        _deadSubscriberCount++;
                        if (entry.FirstDeadLocation == null)
                        {
                            entry.FirstDeadLocation = info.MethodName + " (" + info.DeclaringTypeName + ")";
                        }
                    }

                    entry.Subs.Add(new SubRow
                    {
                        MethodText = "\u21B3 " + info.MethodName + "  (" + info.DeclaringTypeName + ")",
                        TargetText = targetText,
                        IsDeadTarget = dead,
                    });
                }
            }

            entry.OverflowSubscriberCount = _subscriberScratch.Count - rowCount;
            if (entry.OverflowSubscriberCount > 0)
            {
                entry.OverflowText = "    + " + entry.OverflowSubscriberCount
                    + " more subscribers (row cap " + MaxSubscriberRowsPerBus + "; counts stay exact)";
            }
        }

        private bool MatchesFilter(BusEntry entry)
        {
            if (string.IsNullOrEmpty(_searchFilter))
            {
                return true;
            }

            if (entry.DisplayName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0
                || entry.TypeName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            for (int index = 0; index < entry.Subs.Count; index++)
            {
                SubRow sub = entry.Subs[index];
                if (sub.MethodText.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0
                    || sub.TargetText.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void PushTrend(List<int> trend, long value)
        {
            trend.Add(value > int.MaxValue ? int.MaxValue : (int)value);
            if (trend.Count > TrendLength)
            {
                trend.RemoveAt(0);
            }
        }

        private static string FormatDelta(long delta)
        {
            if (delta == 0)
            {
                return "\u00B7";
            }

            return delta > 0 ? "+" + delta : delta.ToString();
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            return value.Length > max ? value.Substring(0, max) + "\u2026" : value;
        }

        private static string ContainerNote(string name)
        {
            if (name.StartsWith("EventStream", StringComparison.Ordinal))
            {
                return "same-thread batch buffer (write, then flush)";
            }

            if (name.StartsWith("MpscEventQueue", StringComparison.Ordinal))
            {
                return "cross-thread bridge (producers enqueue, owner drains)";
            }

            if (name.StartsWith("InProcessCommandPublisher", StringComparison.Ordinal))
            {
                return "command queue (directed, one handler)";
            }

            if (name.StartsWith("EventBusPump", StringComparison.Ordinal))
            {
                return "frame scheduler (no buffer of its own)";
            }

            return string.Empty;
        }

        // ------------------------------------------------------------------ copy

        private string BuildTsv()
        {
            StringBuilder builder = _copyBuilder.Clear();
            builder.AppendLine("EventType\tState\tSubs\tTombstones\tPublishes\tRate/s\tErrors\tDropped\tPeak\tCapacity\tDepth");

            for (int index = 0; index < _buses.Count; index++)
            {
                BusEntry entry = _buses[index];
                if (!MatchesFilter(entry))
                {
                    continue;
                }

                builder.Append(entry.TypeName).Append('\t')
                    .Append(entry.StateText).Append('\t')
                    .Append(entry.SubsText).Append('\t')
                    .Append(entry.TombText).Append('\t')
                    .Append(entry.PublishText).Append('\t')
                    .Append(entry.RateText).Append('\t')
                    .Append(entry.ErrorText).Append('\t')
                    .Append(entry.DroppedText).Append('\t')
                    .Append(entry.PeakText).Append('\t')
                    .Append(entry.CapText).Append('\t')
                    .Append(entry.DepthText).Append('\n');
            }

            return builder.ToString();
        }

        private string BuildJson()
        {
            StringBuilder builder = _copyBuilder.Clear();
            builder.Append('[');

            bool first = true;
            for (int index = 0; index < _buses.Count; index++)
            {
                BusEntry entry = _buses[index];
                if (!MatchesFilter(entry))
                {
                    continue;
                }

                if (!first)
                {
                    builder.Append(',');
                }

                first = false;
                builder.Append("{\"eventType\":\"").Append(EscapeJson(entry.TypeName))
                    .Append("\",\"state\":\"").Append(entry.StateText)
                    .Append("\",\"subs\":").Append(entry.SubsNum)
                    .Append(",\"tombstones\":").Append(entry.TombNum)
                    .Append(",\"publishes\":").Append(entry.PublishNum)
                    .Append(",\"errors\":").Append(entry.ErrorNum)
                    .Append(",\"dropped\":").Append(entry.DroppedNum)
                    .Append(",\"peak\":").Append(entry.PeakNum)
                    .Append(",\"capacity\":").Append(entry.CapNum)
                    .Append(",\"depth\":").Append(entry.DepthNum)
                    .Append('}');
            }

            builder.Append(']');
            return builder.ToString();
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string FormatRate(double ratePerSecond)
        {
            if (ratePerSecond >= 1000d)
            {
                return (ratePerSecond / 1000d).ToString("F1") + "k";
            }

            return ratePerSecond >= 100d
                ? ratePerSecond.ToString("F0")
                : ratePerSecond.ToString("F1");
        }
    }
}
