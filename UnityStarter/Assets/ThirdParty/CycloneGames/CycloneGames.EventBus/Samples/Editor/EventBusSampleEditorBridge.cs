using UnityEditor;
using CycloneGames.EventBus.Editor;
using CycloneGames.EventBus.Runtime;

namespace CycloneGames.EventBus.Sample.Editor
{
    /// <summary>
    /// Hands the sample playground's context to the debugger window while the sample is running.
    /// Registers only when nothing else is registered, and clears only its own registration, so it
    /// never stomps a host project's context.
    /// </summary>
    internal static class EventBusSampleEditorBridge
    {
        private static EventBusContext _registered;

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.update += Sync;
        }

        private static void Sync()
        {
            EventBusContext active = EventBusSampleBootstrap.ActiveContext;

            if (_registered != null && (active != _registered || _registered.IsDisposed))
            {
                if (ReferenceEquals(EventBusEditorDiagnostics.Current, _registered))
                {
                    EventBusEditorDiagnostics.Clear();
                }

                _registered = null;
            }

            if (_registered == null && active != null && !active.IsDisposed
                && EventBusEditorDiagnostics.Current == null)
            {
                EventBusEditorDiagnostics.Register(active);
                _registered = active;
            }
        }
    }
}
