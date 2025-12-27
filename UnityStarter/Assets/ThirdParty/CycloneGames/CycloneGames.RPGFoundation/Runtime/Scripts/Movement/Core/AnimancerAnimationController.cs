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
        private readonly object _cachedParameters;  // Cached to avoid per-frame reflection

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

            // Only use Animator mode if Animator has a Controller assigned
            // Without a Controller, there are no parameters to set
            _useAnimatorMode = _animator != null && _animator.isActiveAndEnabled && _animator.runtimeAnimatorController != null;

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

            // Cache Parameters property once to avoid repeated reflection
            _cachedParameters = CacheParametersProperty();
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

        private object CacheParametersProperty()
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

        private object GetParametersProperty()
        {
            return _cachedParameters;
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
        /// In Editor: validates against cached parameter hashes.
        /// In Runtime: trusts Animator mode if available (returns true to use Animator API).
        /// </summary>
        private bool IsParameterValid(int parameterHash)
        {
            if (_useAnimatorMode)
            {
                // Editor mode: check against cached valid hashes
                if (_validParameterHashes.Count > 0)
                {
                    return _validParameterHashes.Contains(parameterHash);
                }
                // Runtime mode: trust Animator API directly, let Unity handle invalid parameters
                return true;
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