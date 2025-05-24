namespace CycloneGames.UIFramework
{
    public class OpeningState : UIWindowState
    {
        public override void OnEnter(UIWindow window)
        {
            // UnityEngine.Debug.Log($"{DEBUG_FLAG} Window '{window.WindowName}' entered OpeningState.");
            // Ensure GameObject is active
            if (!window.gameObject.activeSelf)
            {
                window.gameObject.SetActive(true);
            }
            // Start opening animation, fade in, etc.
            // The window's OnFinishedOpen (or an animation event) would typically transition to OpenedState.
        }

        public override void OnExit(UIWindow window)
        {
            // UnityEngine.Debug.Log($"{DEBUG_FLAG} Window '{window.WindowName}' exited OpeningState.");
        }
    }
}