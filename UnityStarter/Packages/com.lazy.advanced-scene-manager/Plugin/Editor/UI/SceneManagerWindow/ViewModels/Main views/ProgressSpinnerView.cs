using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views
{

    class ProgressSpinnerView : ViewModel, IView
    {

        IVisualElementScheduledItem rotateAnimation;
        VisualElement spinner;

        public override void OnAdded()
        {
            DisableTemplateContainer();
            spinner = view.Q("progress-spinner");
            view.SetVisible(false);
        }

        public void Start()
        {
            view.SetVisible(true);
            view.Fade(1);
            rotateAnimation = spinner.Rotate();
        }

        public void Stop()
        {
            view.Fade(0, onComplete: () =>
            {
                view.SetVisible(false);
                rotateAnimation.Pause();
            });
        }

    }

}
