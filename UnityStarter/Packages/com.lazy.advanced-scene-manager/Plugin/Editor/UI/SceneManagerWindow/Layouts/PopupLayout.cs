using System;
using AdvancedSceneManager.Editor.UI.Interfaces;
using UnityEditor;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Layouts
{

    abstract class PopupLayout<T> : ViewLayout<T> where T : IView
    {

        public override void OnWindowDisable() => currentPopup.InvokeView(() => currentPopup?.OnWindowDisable());
        public override void OnWindowEnable() => currentPopup.InvokeView(() => currentPopup?.OnWindowEnable());
        public override void OnWindowFocus() => currentPopup.InvokeView(() => currentPopup?.OnWindowFocus());
        public override void OnWindowLostFocus() => currentPopup.InvokeView(() => currentPopup?.OnWindowLostFocus());

        public override void OnAdded() =>
            ReopenPreviousPopup();

        private readonly string persistanceKey = $"ASM.PopupLayout<{typeof(T).Name}>.CurrentPopup";

        void ReopenPreviousPopup()
        {

            var typeName = SessionState.GetString(persistanceKey, "");
            var type = Type.GetType(typeName, false);

            if (typeof(T).IsAssignableFrom(type))
                typeof(PopupLayout<T>).GetMethod(nameof(Reopen)).MakeGenericMethod(type).Invoke(this, Type.EmptyTypes);

        }

        T _currentPopup;
        public T currentPopup
        {
            get => _currentPopup;
            set
            {
                _currentPopup = value;
                var typeName = value?.GetType()?.AssemblyQualifiedName;
                SessionState.SetString(persistanceKey, typeName);
            }
        }

        public bool IsOpen<TViewModel>() where TViewModel : T =>
            currentPopup?.GetType() == typeof(TViewModel);

        public override VisualElement Add<TViewModel>(VisualElement parent = null)
        {

            var view = viewHandler.GetView<TViewModel>();
            if (!view)
                throw new ArgumentException($"Could not find view for '{typeof(TViewModel).Name}'.");

            var templateContainer = view.CloneTree();
            templateContainer.name = typeof(TViewModel).Name;
            (parent ?? contentContainer ?? rootView)?.Add(templateContainer);

            return templateContainer;

        }

        public abstract void Open<TPopup>(object parameter = null) where TPopup : T;
        public abstract void ClosePopup(Action onComplete = null);
        public virtual void Reopen<TPopup>() where TPopup : T
        { }

    }

}
