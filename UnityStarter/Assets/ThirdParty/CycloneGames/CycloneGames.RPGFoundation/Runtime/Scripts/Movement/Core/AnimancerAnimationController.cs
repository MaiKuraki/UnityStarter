using System.Collections.Generic;
using UnityEngine;
#if ANIMANCER_PRESENT
using Animancer;
#endif

namespace CycloneGames.RPGFoundation.Runtime.Movement
{
    /// <summary>
    /// Animancer adapter for IAnimationController interface.
    /// Supports two modes:
    /// 1. Animator-based (HybridAnimancerComponent): Uses parameter hashes via Animator API
    /// 2. Parameters-based (AnimancerComponent): Uses string names via Animancer's ParameterDictionary
    /// </summary>
    public sealed class AnimancerAnimationController : IAnimationController
    {
        private readonly Animator _animator;
#if ANIMANCER_PRESENT
        private readonly object _animancerComponent;
        private readonly ParameterDictionary _parameters;
#endif
        private readonly Dictionary<int, string> _hashToNameMap;
        private readonly bool _useAnimatorMode;
        private readonly bool _isValid;
        private readonly HashSet<int> _validParameterHashes;

        // Track triggered parameters for auto-reset in Parameters mode
        private readonly HashSet<int> _triggeredParameters;

        public bool IsValid => _isValid;

        public AnimancerAnimationController(UnityEngine.Object animancerComponent, Dictionary<int, string> parameterNameMap = null)
        {
#if ANIMANCER_PRESENT
            _animancerComponent = animancerComponent;
            _animator = ExtractAnimatorFromAnimancer(animancerComponent);
            _hashToNameMap = parameterNameMap ?? new Dictionary<int, string>();

            _useAnimatorMode = _animator != null && _animator.isActiveAndEnabled && _animator.runtimeAnimatorController != null;

            _validParameterHashes = new HashSet<int>();
            _triggeredParameters = new HashSet<int>();

#if UNITY_EDITOR
            if (_useAnimatorMode && _animator != null && _animator.runtimeAnimatorController != null)
            {
                var controller = _animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                if (controller != null)
                {
                    for (int i = 0; i < controller.parameters.Length; i++)
                    {
                        _validParameterHashes.Add(controller.parameters[i].nameHash);
                    }
                }
            }
#endif

            _isValid = _useAnimatorMode || (animancerComponent != null && IsAnimancerComponentValid());

            if (animancerComponent is AnimancerComponent ac)
            {
                _parameters = ac.Parameters;
            }
#else
            _animator = null;
            _hashToNameMap = parameterNameMap ?? new Dictionary<int, string>();
            _useAnimatorMode = false;
            _isValid = false;
            _validParameterHashes = new HashSet<int>();
            _triggeredParameters = new HashSet<int>();
            UnityEngine.Debug.LogWarning(
                "[AnimancerAnimationController] Animancer package is not installed. " +
                "The assigned animancerComponent will be ignored. Install com.kybernetik.animancer to enable Animancer support.");
#endif
        }

#if ANIMANCER_PRESENT
        private static Animator ExtractAnimatorFromAnimancer(UnityEngine.Object component)
        {
            if (component == null) return null;

            if (component is HybridAnimancerComponent hybrid)
            {
                return hybrid.Animator;
            }
            else if (component is AnimancerComponent regular)
            {
                return regular.Animator;
            }
            return null;
        }

        private bool IsAnimancerComponentValid()
        {
            if (_animancerComponent == null) return false;
            var component = _animancerComponent as MonoBehaviour;
            return component != null && component.isActiveAndEnabled;
        }

        private bool IsParameterValid(int parameterHash)
        {
            if (_useAnimatorMode)
            {
                if (_validParameterHashes.Count > 0)
                {
                    return _validParameterHashes.Contains(parameterHash);
                }
                return true;
            }
            return _hashToNameMap.ContainsKey(parameterHash);
        }

        private void SetParameterValue<T>(int parameterHash, T value)
        {
            if (_parameters == null) return;
            if (!_hashToNameMap.TryGetValue(parameterHash, out string parameterName)) return;

            _parameters.SetValue(parameterName, value);
        }
#endif

        public void SetFloat(int parameterHash, float value)
        {
            if (!_isValid) return;
#if ANIMANCER_PRESENT
            ResetTriggeredParameters();

            if (_useAnimatorMode)
            {
                if (IsParameterValid(parameterHash))
                {
                    _animator.SetFloat(parameterHash, value);
                }
                else if (_hashToNameMap.ContainsKey(parameterHash))
                {
                    SetParameterValue(parameterHash, value);
                }
            }
            else
            {
                SetParameterValue(parameterHash, value);
            }
#endif
        }

        public void SetBool(int parameterHash, bool value)
        {
            if (!_isValid) return;
#if ANIMANCER_PRESENT
            ResetTriggeredParameters();

            if (_useAnimatorMode)
            {
                if (IsParameterValid(parameterHash))
                {
                    _animator.SetBool(parameterHash, value);
                }
                else if (_hashToNameMap.ContainsKey(parameterHash))
                {
                    SetParameterValue(parameterHash, value);
                }
            }
            else
            {
                SetParameterValue(parameterHash, value);
            }
#endif
        }

        public void SetTrigger(int parameterHash)
        {
            if (!_isValid) return;
#if ANIMANCER_PRESENT
            if (_useAnimatorMode)
            {
                if (IsParameterValid(parameterHash))
                {
                    _animator.SetTrigger(parameterHash);
                }
                else if (_hashToNameMap.ContainsKey(parameterHash))
                {
                    SetParameterValue(parameterHash, true);
                    _triggeredParameters.Add(parameterHash);
                }
            }
            else
            {
                SetParameterValue(parameterHash, true);
                _triggeredParameters.Add(parameterHash);
            }
#endif
        }

        /// <summary>
        /// In Parameters mode, resets previously triggered bool parameters to false.
        /// This simulates Animator's trigger auto-reset behavior.
        /// </summary>
        private void ResetTriggeredParameters()
        {
#if ANIMANCER_PRESENT
            if (_triggeredParameters.Count == 0) return;

            var toReset = new HashSet<int>(_triggeredParameters);
            _triggeredParameters.Clear();

            foreach (int hash in toReset)
            {
                SetParameterValue(hash, false);
            }
#endif
        }
    }
}
