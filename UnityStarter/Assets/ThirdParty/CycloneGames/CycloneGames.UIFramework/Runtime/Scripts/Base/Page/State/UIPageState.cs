namespace CycloneGames.UIFramework
{
    // CAUTION: if you modify this interface name,
    //          don't forget modify the link.xml file located in the CycloneGames.UIFramework/Scripts/Framework folder
    public interface IUIPageState
    {
        void OnEnter(UIPage page);
        void OnExit(UIPage page);
        void Update(UIPage page);
    }

    public abstract class UIPageState : IUIPageState
    {
        protected const string DEBUG_FLAG = "[UIPageState]";
        public abstract void OnEnter(UIPage page);

        public abstract void OnExit(UIPage page);

        public virtual void Update(UIPage page) { }
    }
}