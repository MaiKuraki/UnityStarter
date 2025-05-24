namespace CycloneGames.UIFramework
{
    public class ClosingState : UIWindowState
    {
        public override void OnEnter(UIWindow window)
        {
            // UnityEngine.Debug.Log($"{DEBUG_FLAG} Window '{window.WindowName}' entered ClosingState.");
            // Start closing animation, fade out, etc.
            // The window's OnFinishedClose (or an animation event) would typically trigger Destroy(gameObject)
        }

        public override void OnExit(UIWindow window)
        {
            // UnityEngine.Debug.Log($"{DEBUG_FLAG} Window '{window.WindowName}' exited ClosingState.");
        }
    }
}