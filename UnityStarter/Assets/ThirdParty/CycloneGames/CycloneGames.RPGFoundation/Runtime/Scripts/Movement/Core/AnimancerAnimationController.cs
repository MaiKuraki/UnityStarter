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
        private readonly object _animancerComponent;
        private readonly Dictionary<int, string> _hashToNameMap;
        private readonly bool _useAnimatorMode;
        private readonly bool _isValid;
        private readonly HashSet<int> _validParameterHashes;

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

            // Cache valid parameter hashes from Animator Controller if using Animator mode
            // Note: We can only cache parameters in Editor mode, in Runtime we'll use try-catch
            _validParameterHashes = new HashSet<int>();
#if UNITY_EDITOR
            if (_useAnimatorMode && _animator != null && _animator.runtimeAnimatorController != null)
            {
                var controller = _animator.runtimeAnimatorController as UnityEditor.Animations.AnimatorController;
                if (controller != null)
                {
                    foreach (var param in controller.parameters)
                    {
                        _validParameterHashes.Add(param.nameHash);
                    }
                }
            }
#endif

            _isValid = _useAnimatorMode || (_animancerComponent != null && IsAnimancerComponentValid());
        }

        private static Animator ExtractAnimatorFromAnimancer(UnityEngine.Object component)
        {
            if (component == null) return null;

#if ANIMANCER_PRESENT
            if (component is HybridAnimancerComponent hybrid)
            {
                return hybrid.Animator;
            }
            else if (component is AnimancerComponent regular)
            {
                return regular.Animator;
            }
            return null;
#else
            try
            {
                var type = component.GetType();
                var animatorProperty = type.GetProperty("Animator",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);

                if (animatorProperty != null)
                {
                    return animatorProperty.GetValue(component) as Animator;
                }
            }
            catch (System.Exception)
            {
                // Silently fail - component may not have Animator property
                // This is expected for AnimancerComponent (Parameters mode)
            }

            return null;
#endif
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

#if ANIMANCER_PRESENT
            if (_animancerComponent is AnimancerComponent animancer)
            {
                return animancer.Parameters;
            }
            return null;
#else
            try
            {
                var type = _animancerComponent.GetType();
                var parametersProperty = type.GetProperty("Parameters",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);

                return parametersProperty?.GetValue(_animancerComponent);
            }
            catch (System.Exception)
            {
                return null;
            }
#endif
        }

        private void SetParameterValue<T>(int parameterHash, T value)
        {
            if (!_hashToNameMap.TryGetValue(parameterHash, out string parameterName))
            {
                return;
            }

            var parameters = GetParametersProperty();
            if (parameters == null) return;

#if ANIMANCER_PRESENT
            if (parameters is ParameterDictionary paramDict)
            {
                paramDict.SetValue(parameterName, value);
            }
#else
            try
            {
                var setValueMethod = parameters.GetType().GetMethod("SetValue",
                    new[] { typeof(string), typeof(T) });

                if (setValueMethod != null)
                {
                    setValueMethod.Invoke(parameters, new object[] { parameterName, value });
                }
            }
            catch (System.Exception)
            {

            }
#endif
        }

        /// <summary>
        /// Checks if a parameter exists in the Animator Controller.
        /// If using Parameters mode, checks if the parameter name exists in the hash map.
        /// </summary>
        private bool IsParameterValid(int parameterHash)
        {
            if (_useAnimatorMode)
            {
                // If we have cached valid hashes (Editor mode), check against them
                if (_validParameterHashes.Count > 0)
                {
                    return _validParameterHashes.Contains(parameterHash);
                }
                // In Runtime mode, if we don't have cached parameters, check if we have the name in our map
                // If we have the name, it means the parameter should exist in Parameters mode
                // We'll try Parameters mode as fallback if Animator Controller doesn't have it
                // This prevents warnings when Animator Controller doesn't have the parameter
                return false; // Return false to trigger fallback to Parameters mode
            }
            else
            {
                // In Parameters mode, check if we have the parameter name in our map
                return _hashToNameMap.ContainsKey(parameterHash);
            }
        }

        public void SetFloat(int parameterHash, float value)
        {
            if (!_isValid) return;

            if (_useAnimatorMode)
            {
                // Check if parameter exists before setting to avoid warnings
                if (IsParameterValid(parameterHash))
                {
                    _animator.SetFloat(parameterHash, value);
                }
                // If parameter doesn't exist in Animator Controller, try Parameters mode as fallback
                else if (_hashToNameMap.ContainsKey(parameterHash))
                {
                    SetParameterValue(parameterHash, value);
                }
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
        }

        public void SetTrigger(int parameterHash)
        {
            if (!_isValid) return;

            if (_useAnimatorMode)
            {
                if (IsParameterValid(parameterHash))
                {
                    _animator.SetTrigger(parameterHash);
                }
                else if (_hashToNameMap.ContainsKey(parameterHash))
                {
                    SetParameterValue(parameterHash, true);
                }
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