using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Interfaces
{

    public interface IPopup : IView
    {
        void OnOpen(VisualElement element, object parameter) { }
        void OnReopen();
        void PassParameter(object parameter);
        void OnClose(VisualElement element) { }
    }

}
