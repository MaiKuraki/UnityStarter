using CycloneGames.EventBus.Runtime;

namespace CycloneGames.EventBus.Editor
{
    /// <summary>
    /// Editor-only observation point for the debugger window. An EditorWindow has no constructor
    /// injection, so it needs one explicit handoff from the host; this static is that handoff only.
    /// It lives in the Editor assembly, is never compiled into a Player, and holds a borrowed
    /// reference — it does not own or dispose the context. It is an editor tooling compromise, not a
    /// runtime service locator.
    /// </summary>
    internal static class EventBusEditorDiagnostics
    {
        private static EventBusContext _context;

        internal static void Register(EventBusContext context)
        {
            _context = context;
        }

        internal static EventBusContext Current => _context;
    }
}
