using AdvancedSceneManager.Editor.UI.Interfaces;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Layouts
{

    class VerticalLayout<T> : ViewLayout<T> where T : IView
    {

        public override VisualElement Add<TViewModel>(VisualElement parent = null)
        {

            VisualElement view;
            try
            {
                view = Instantiate<TViewModel>();
                view.name = typeof(TViewModel).Name;
                (parent ?? contentContainer ?? rootView)?.Add(view);
            }
            catch (System.Exception ex)
            {
                view = ViewUtility.ExceptionBox(ex, 0, 0, $"Unable to instantiate '{typeof(TViewModel).Name}':");
                (parent ?? contentContainer ?? rootView)?.Add(view);
                Debug.LogException(ex);
            }

            return view;

        }

        public void Add(Button element) =>
             view.Add(element);

    }

}
