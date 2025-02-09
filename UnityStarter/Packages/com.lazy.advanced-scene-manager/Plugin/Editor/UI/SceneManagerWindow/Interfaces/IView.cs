using AdvancedSceneManager.DependencyInjection;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Interfaces
{

    public interface IView : DependencyInjectionUtility.IInjectable
    {

        VisualElement view { get; }
        void SetView(VisualElement view);

        bool isAdded { get; }

        void OnAdded();
        void OnRemoved();

        void ApplyAppearanceSettings();
        void OnWindowEnable();
        void OnWindowDisable();
        void OnWindowFocus();
        void OnWindowLostFocus();
        void OnGUI();

    }

}
