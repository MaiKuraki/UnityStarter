using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CycloneGames.EventBus.Core;
using CycloneGames.EventBus.Runtime;

namespace CycloneGames.EventBus.Editor
{
    /// <summary>
    /// Read-only diagnostics window for a context registered through
    /// <see cref="EventBusEditorDiagnostics"/>. It uses a retained model: the bus list and counts are
    /// rebuilt only on an explicit refresh (or when the registered context changes), never per
    /// OnGUI repaint. It is read-only by design — triggering a generic test publish would require
    /// constructing an arbitrary <typeparamref name="T"/> by reflection, which this package bans for
    /// IL2CPP/AOT safety.
    /// </summary>
    public sealed class EventBusDebuggerWindow : EditorWindow
    {
        private EventBusContext _lastContext;
        private readonly List<Entry> _entries = new List<Entry>();
        private EventBusDiagnosticsSnapshot _snapshot;
        private bool _needsRebuild = true;

        private struct Entry
        {
            public string TypeName;
            public EventBusSnapshot Snapshot;
        }

        [MenuItem("Tools/CycloneGames/EventBus/Debugger")]
        private static void Open()
        {
            GetWindow<EventBusDebuggerWindow>("CycloneGames EventBus");
        }

        private void OnGUI()
        {
            EventBusContext context = EventBusEditorDiagnostics.Current;

            if (context == null)
            {
                _lastContext = null;
                _entries.Clear();
                EditorGUILayout.HelpBox(
                    "No context registered. Call EventBusEditorDiagnostics.Register(context) from "
                    + "editor-only code to observe a context.",
                    MessageType.Info);
                return;
            }

            if (context != _lastContext)
            {
                _lastContext = context;
                _needsRebuild = true;
            }

            if (_needsRebuild)
            {
                Rebuild(context);
                _needsRebuild = false;
            }

            EditorGUILayout.LabelField("Active buses", _snapshot.ActiveBusCount.ToString());
            EditorGUILayout.LabelField("Scopes", _snapshot.ScopeCount.ToString());
            EditorGUILayout.LabelField("Subscriptions", _snapshot.SubscriptionCount.ToString());
            EditorGUILayout.LabelField("Tombstones", _snapshot.TombstoneCount.ToString());
            EditorGUILayout.LabelField("Publish count", _snapshot.PublishCount.ToString());

            if (GUILayout.Button("Refresh"))
            {
                _needsRebuild = true;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Buses", EditorStyles.boldLabel);
            for (int index = 0; index < _entries.Count; index++)
            {
                Entry entry = _entries[index];
                EditorGUILayout.LabelField(
                    entry.TypeName,
                    $"subs={entry.Snapshot.SubscriptionCount}, "
                    + $"tombstones={entry.Snapshot.TombstoneCount}, "
                    + $"publishes={entry.Snapshot.PublishCount}, "
                    + $"depth={entry.Snapshot.DispatchDepth}");
            }
        }

        private void Rebuild(EventBusContext context)
        {
            _snapshot = context.GetDiagnosticsSnapshot();

            _entries.Clear();
            IReadOnlyList<IEventBusDiagnostics> buses = context.GetRegisteredBuses();
            for (int index = 0; index < buses.Count; index++)
            {
                IEventBusDiagnostics bus = buses[index];
                _entries.Add(new Entry
                {
                    TypeName = bus.EventTypeName,
                    Snapshot = bus.GetSnapshot(),
                });
            }
        }
    }
}
