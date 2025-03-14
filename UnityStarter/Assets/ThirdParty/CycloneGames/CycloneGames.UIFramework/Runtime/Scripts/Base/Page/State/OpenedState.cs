namespace CycloneGames.UIFramework
{
    public class OpenedState : UIPageState
    {
        public override void OnEnter(UIPage page)
        {
            UnityEngine.Debug.Log($"{DEBUG_FLAG} Opened: {page.PageName}");
        }

        public override void OnExit(UIPage page)
        {
            
        }
    }
}