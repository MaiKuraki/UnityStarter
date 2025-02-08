namespace CycloneGames.UIFramework
{
    public class OpeningState : UIPageState
    {
        public override void OnEnter(UIPage page)
        {
            UnityEngine.Debug.Log($"{DEBUG_FLAG} Opening: {page.PageName}");
        }

        public override void OnExit(UIPage page)
        {
            
        }
    }
}