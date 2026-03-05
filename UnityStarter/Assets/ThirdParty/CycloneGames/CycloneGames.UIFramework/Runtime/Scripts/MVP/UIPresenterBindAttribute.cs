using System;

namespace CycloneGames.UIFramework.Runtime
{
    /// <summary>
    /// Attribute used to associate a UIPresenter with a specific UIWindow name.
    /// This allows the framework to automatically instantiate and bind the Presenter
    /// when the corresponding window is opened, without the View needing to know about it.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class UIPresenterBindAttribute : Attribute
    {
        public string WindowName { get; }

        /// <summary>
        /// Binds the Presenter to a specific window name (Asset/Prefab name).
        /// </summary>
        public UIPresenterBindAttribute(string windowName)
        {
            WindowName = windowName;
        }

        /// <summary>
        /// Binds the Presenter to a View Type. 
        /// The framework uses the Type's Name as the WindowName (e.g. nameof(MyWindowView)).
        /// Use this to avoid magic strings if your window's logical name matches its View class name.
        /// </summary>
        public UIPresenterBindAttribute(Type windowType)
        {
            WindowName = windowType.Name;
        }
    }
}
