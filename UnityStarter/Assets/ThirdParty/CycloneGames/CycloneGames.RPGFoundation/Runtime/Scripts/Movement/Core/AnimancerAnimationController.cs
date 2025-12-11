using System.Collections.Generic;
using UnityEngine;

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
        private readonly object _animancerComponent;
        private readonly Dictionary<int, string> _hashToNameMap;
        private readonly bool _useAnimatorMode;
        private readonly bool _isValid;

        public bool IsValid => _isValid;

        /// <summary>
        /// Creates an adapter for Animancer component.
        /// Automatically detects if Animator is available (HybridAnimancerComponent) or uses Parameters system.
        /// </summary>
        /// <param name="animancerComponent">AnimancerComponent or HybridAnimancerComponent instance</param>
        /// <param name="parameterNameMap">Optional: Map of parameter hashes to names for Parameters mode</param>
        public AnimancerAnimationController(UnityEngine.Object animancerComponent, Dictionary<int, string> parameterNameMap = null)
        {
            _animancerComponent = animancerComponent;
            _animator = ExtractAnimatorFromAnimancer(animancerComponent);
            _hashToNameMap = parameterNameMap ?? new Dictionary<int, string>();

            // Prefer Animator mode if available (HybridAnimancerComponent)
            _useAnimatorMode = _animator != null && _animator.isActiveAndEnabled;

            _isValid = _useAnimatorMode || (_animancerComponent != null && IsAnimancerComponentValid());
        }

        private static Animator ExtractAnimatorFromAnimancer(UnityEngine.Object component)
        {
            if (component == null) return null;

            var type = component.GetType();
            var animatorProperty = type.GetProperty("Animator",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            if (animatorProperty != null)
            {
                return animatorProperty.GetValue(component) as Animator;
            }

            return null;
        }

        private bool IsAnimancerComponentValid()
        {
            if (_animancerComponent == null) return false;

            var component = _animancerComponent as MonoBehaviour;
            return component != null && component.isActiveAndEnabled;
        }

        private object GetParametersProperty()
        {
            if (_animancerComponent == null) return null;

            var type = _animancerComponent.GetType();
            var parametersProperty = type.GetProperty("Parameters",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            return parametersProperty?.GetValue(_animancerComponent);
        }

        private void SetParameterValue<T>(int parameterHash, T value)
        {
            if (!_hashToNameMap.TryGetValue(parameterHash, out string parameterName))
            {
                // Fallback: Try to get name from AnimationParameterCache if available
                // This requires storing reverse mapping, which we don't have
                // So we'll just skip if name not found
                return;
            }

            var parameters = GetParametersProperty();
            if (parameters == null) return;

            var setValueMethod = parameters.GetType().GetMethod("SetValue",
                new[] { typeof(string), typeof(T) });

            if (setValueMethod != null)
            {
                setValueMethod.Invoke(parameters, new object[] { parameterName, value });
            }
        }

        public void SetFloat(int parameterHash, float value)
        {
            if (!_isValid) return;

            if (_useAnimatorMode)
            {
                _animator.SetFloat(parameterHash, value);
            }
            else
            {
                SetParameterValue(parameterHash, value);
            }
        }

        public void SetBool(int parameterHash, bool value)
        {
            if (!_isValid) return;

            if (_useAnimatorMode)
            {
                _animator.SetBool(parameterHash, value);
            }
            else
            {
                SetParameterValue(parameterHash, value);
            }
        }

        public void SetTrigger(int parameterHash)
        {
            if (!_isValid) return;

            if (_useAnimatorMode)
            {
                _animator.SetTrigger(parameterHash);
            }
            else
            {
                // For triggers in Parameters mode, we set a bool to true
                // Note: Animancer Parameters don't have native trigger support
                // This is a workaround - you may need to handle triggers differently
                SetParameterValue(parameterHash, true);
            }
        }
    }
}