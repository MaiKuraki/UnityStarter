namespace CycloneGames.UIFramework
{
    public class OpenedState : UIWindowState
    {
        public override void OnEnter(UIWindow window)
        {
            UnityEngine.Debug.Log($"{DEBUG_FLAG} Window '{window.WindowName}' entered OpenedState.");
            // Window is fully visible and interactive.
        }

        public override void OnExit(UIWindow window)
        {
            // UnityEngine.Debug.Log($"{DEBUG_FLAG} Window '{window.WindowName}' exited OpenedState.");
        }
    }
}