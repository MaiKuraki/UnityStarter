using System;
using AdvancedSceneManager.Editor.UI.Interfaces;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI
{

    abstract class ViewLayout<T> : ViewModel where T : IView
    {

        public VisualElement rootView => GetRootView();
        public virtual VisualElement contentContainer { get; }

        protected virtual VisualElement GetRootView()
        {

            if (createWrapper is not null)
            {

                if (wrapper is null)
                {
                    wrapper = createWrapper.Invoke();
                    rootVisualElement.Add(wrapper);
                    wrapper.RegisterCallback<DetachFromPanelEvent>(e => wrapper = null);
                }

                return getWrapperContentContainer.Invoke(wrapper);

            }
            else
            {
                return contentContainer ?? rootVisualElement;
            }

        }

        public void SetView(VisualTreeAsset view) =>
            SetView(view.CloneTree());

        public override void SetView(VisualElement view)
        {
            rootView?.Add(view);
            base.SetView(view);
        }

        VisualElement wrapper;
        Func<VisualElement> createWrapper;
        Func<VisualElement, VisualElement> getWrapperContentContainer;

        public void SetWrapper(Func<VisualElement> createElement, Func<VisualElement, VisualElement> getContentContainer = null)
        {
            createWrapper = createElement;
            getWrapperContentContainer = element => (getContentContainer(element)) ?? element;
        }

        public virtual VisualElement AddAndInvoke<TViewModel>(VisualElement parent = null) where TViewModel : T
        {

            var returnValue = Add<TViewModel>(parent);

            DependencyInjection.DependencyInjectionUtility.GetService<TViewModel>().OnAdded();

            return returnValue;

        }

        public abstract VisualElement Add<TViewModel>(VisualElement parent = null) where TViewModel : T;

        protected VisualElement Instantiate<TViewModel>() where TViewModel : T
        {

            var viewAsset = viewHandler.GetView<TViewModel>();

            if (!viewAsset)
                throw new ArgumentException($"Could not find VisualTreeAsset for '{typeof(TViewModel).Name}'.");

            var view = viewAsset.CloneTree();
            view.name = typeof(TViewModel).Name;

            var model = viewHandler.Get<TViewModel>();
            model.SetView(view);

            return view;

        }

    }

}
