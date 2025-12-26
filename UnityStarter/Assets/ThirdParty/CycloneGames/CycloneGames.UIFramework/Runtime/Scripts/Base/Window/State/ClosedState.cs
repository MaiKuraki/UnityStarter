using CycloneGames.Logger;

namespace CycloneGames.UIFramework.Runtime
{
    public class ClosedState : UIWindowState
    {
        public override void OnEnter(UIWindow window)
        {
            CLogger.LogInfo($"{DEBUG_FLAG} Window '{window.WindowName}' entered ClosedState.");
            // Typically, a closed window might disable its GameObject or specific components.
            // This is often handled by the window itself before or after changing to this state.
            if (window.gameObject.activeSelf)
            {
                // window.gameObject.SetActive(false); // Example: ensure it's inactive
            }
        }

        public override void OnExit(UIWindow window)
        {
            // CLogger.LogInfo($"{DEBUG_FLAG} Window '{window.WindowName}' exited ClosedState.");
        }
    }
}