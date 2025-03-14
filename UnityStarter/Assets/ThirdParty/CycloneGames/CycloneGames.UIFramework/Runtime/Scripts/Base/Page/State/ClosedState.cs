namespace CycloneGames.UIFramework
{
    public class ClosedState : UIPageState
    {
        public override void OnEnter(UIPage page)
        {
            UnityEngine.Debug.Log($"{DEBUG_FLAG} Closed: {page.PageName}");
        }

        public override void OnExit(UIPage page)
        {
            
        }
    }
}