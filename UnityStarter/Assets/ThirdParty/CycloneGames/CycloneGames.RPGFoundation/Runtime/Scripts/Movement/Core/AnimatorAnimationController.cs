using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Unity Animator adapter for IAnimationController interface.
    /// </summary>
    public sealed class AnimatorAnimationController : IAnimationController
    {
        private readonly Animator _animator;
        private readonly bool _isValid;

        public bool IsValid => _isValid;

        public AnimatorAnimationController(Animator animator)
        {
            _animator = animator;
            _isValid = animator != null && animator.isActiveAndEnabled;
        }

        public void SetFloat(int parameterHash, float value)
        {
            if (_isValid)
            {
                _animator.SetFloat(parameterHash, value);
            }
        }

        public void SetBool(int parameterHash, bool value)
        {
            if (_isValid)
            {
                _animator.SetBool(parameterHash, value);
            }
        }

        public void SetTrigger(int parameterHash)
        {
            if (_isValid)
            {
                _animator.SetTrigger(parameterHash);
            }
        }
    }
}