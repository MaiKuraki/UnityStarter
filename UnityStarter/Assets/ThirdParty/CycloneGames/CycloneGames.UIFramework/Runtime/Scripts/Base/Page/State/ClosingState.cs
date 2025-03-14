namespace CycloneGames.UIFramework
{
    public class ClosingState : UIPageState
    {
        public override void OnEnter(UIPage page)
        {
            UnityEngine.Debug.Log($"{DEBUG_FLAG} Closing: {page.PageName}");
        }

        public override void OnExit(UIPage page)
        {
            
        }
    }
}