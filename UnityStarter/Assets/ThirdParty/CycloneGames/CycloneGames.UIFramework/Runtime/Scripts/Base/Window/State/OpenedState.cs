using CycloneGames.Logger;

namespace CycloneGames.UIFramework.Runtime
{
    public class OpenedState : UIWindowState
    {
        public override void OnEnter(UIWindow window)
        {
            CLogger.LogInfo($"{DEBUG_FLAG} Window '{window.WindowName}' entered OpenedState.");
            // Window is fully visible and interactive.
        }

        public override void OnExit(UIWindow window)
        {
            // CLogger.LogInfo($"{DEBUG_FLAG} Window '{window.WindowName}' exited OpenedState.");
        }
    }
}