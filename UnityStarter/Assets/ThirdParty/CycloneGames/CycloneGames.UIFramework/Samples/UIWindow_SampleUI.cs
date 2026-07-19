using UnityEngine;

namespace CycloneGames.UIFramework.Runtime.Samples
{
    public interface ISampleUIView
    {
        void SetListener(ISampleUIViewListener listener);
        void SetStatus(string status);
    }

    public interface ISampleUIViewListener : IUIViewListener
    {
        void OnPrimaryAction();
    }

    public sealed class UIWindow_SampleUI : UIWindow, ISampleUIView
    {
        private ISampleUIViewListener _listener;

        public void SetListener(ISampleUIViewListener listener)
        {
            _listener = listener;
        }

        public void SetStatus(string status)
        {
            Debug.Log($"[UIFrameworkSample] {status}", this);
        }

        // This method can be connected directly to a UnityEvent in the Inspector.
        public void UICmd_PrimaryAction()
        {
            _listener?.OnPrimaryAction();
        }

        protected override void OnDestroy()
        {
            _listener = null;
            base.OnDestroy();
        }
    }

    public sealed class SampleUIPresenter : UIPresenter<ISampleUIView>, ISampleUIViewListener
    {
        protected override void OnViewBound()
        {
            View.SetListener(this);
        }

        public override void OnViewOpened()
        {
            View.SetStatus("MVP presenter is ready.");
        }

        public void OnPrimaryAction()
        {
            View.SetStatus("Primary action handled by the presenter.");
        }

        public override void Dispose()
        {
            View?.SetListener(null);
            base.Dispose();
        }
    }
}
