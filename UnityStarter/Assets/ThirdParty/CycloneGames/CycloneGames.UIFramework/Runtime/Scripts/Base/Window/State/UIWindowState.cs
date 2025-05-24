namespace CycloneGames.UIFramework
{
    // CAUTION: if you modify this interface name,
    // don't forget modify the link.xml file located in the CycloneGames.UIFramework/Scripts/Framework folder
    // (This comment is from original code, ensure link.xml is checked if this name changes)
    public interface IUIWindowState
    {
        void OnEnter(UIWindow window);
        void OnExit(UIWindow window);
        void Update(UIWindow window);
    }

    public abstract class UIWindowState : IUIWindowState
    {
        protected const string DEBUG_FLAG = "[UIWindowState]"; // Changed from UIPageState
        public abstract void OnEnter(UIWindow window);
        public abstract void OnExit(UIWindow window);
        public virtual void Update(UIWindow window) { } // Default empty update
    }
}