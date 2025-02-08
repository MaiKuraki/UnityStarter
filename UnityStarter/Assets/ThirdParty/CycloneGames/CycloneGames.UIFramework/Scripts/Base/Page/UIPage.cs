using UnityEngine;

namespace CycloneGames.UIFramework
{
    public class UIPage : MonoBehaviour
    {
        [SerializeField, Header("Priority Override"), Range(-100, 400)] private int priority;
        public int Priority => priority;
        private string pageName;
        public string PageName => pageName;
        private IUIPageState currentState;
        private UILayer parentLayer;

        public void SetPageName(string NewPageName)
        {
            pageName = NewPageName;
        }
        public void SetUILayer(UILayer layer)
        {
            parentLayer = layer;
        }

        public void ClosePage()
        {
            OnStartClose();

            // TODO: maybe move to the end of closing animation
            OnFinishedClose();
        }

        private void ChangeState(IUIPageState newState)
        {
            currentState?.OnExit(this);
            currentState = newState;
            currentState?.OnEnter(this);
        }

        protected virtual void OnStartOpen()
        {
            ChangeState(new OpeningState());
        }

        protected virtual void OnFinishedOpen()
        {
            ChangeState(new OpenedState());
        }

        protected virtual void OnStartClose()
        {
            ChangeState(new ClosingState());
        }

        protected virtual void OnFinishedClose()
        {
            ChangeState(new ClosedState());
            if (parentLayer != null)
            {
                parentLayer.RemovePage(pageName);
            }
            Destroy(gameObject);
        }

        protected virtual void Awake()
        {

        }

        protected virtual void Start()
        {
            // TODO: maybe move to the start of opening animation
            OnStartOpen();

            // TODO: maybe move to the end of opening animation
            OnFinishedOpen();
        }

        protected virtual void Update()
        {
            currentState?.Update(this);
        }

        protected virtual void OnDestroy()
        {
            if(parentLayer != null)
            {
                parentLayer.RemovePage(pageName);
                parentLayer = null;
            }
        }
    }
}