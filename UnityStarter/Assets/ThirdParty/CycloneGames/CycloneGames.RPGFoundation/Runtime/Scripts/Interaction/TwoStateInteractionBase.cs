using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public class TwoStateInteractionBase : MonoBehaviour, ITwoStateInteraction
    {
        [SerializeField] private bool startActivated;

        private bool _isActivated;
        public bool IsActivated => _isActivated;

        protected virtual void Awake()
        {
            _isActivated = startActivated;
        }

        public virtual void ActivateState()
        {
            _isActivated = true;
        }

        public virtual void DeactivateState()
        {
            _isActivated = false;
        }

        public void ToggleState()
        {
            if (_isActivated) DeactivateState();
            else ActivateState();
        }
    }
}