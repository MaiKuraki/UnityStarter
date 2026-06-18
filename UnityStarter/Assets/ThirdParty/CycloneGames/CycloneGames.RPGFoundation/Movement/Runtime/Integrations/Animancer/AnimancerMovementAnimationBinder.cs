using System.Collections.Generic;
using Animancer;
using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Core;
using CycloneGames.RPGFoundation.Movement.Runtime;
using CycloneGames.RPGFoundation.Movement.Runtime.Movement2D;

namespace CycloneGames.RPGFoundation.Movement.Integrations.Animancer
{
    [DisallowMultipleComponent]
    public sealed class AnimancerMovementAnimationBinder : MonoBehaviour
    {
        [SerializeField] private AnimancerComponent AnimancerComponent;
        [SerializeField] private MovementComponent Movement3D;
        [SerializeField] private MovementComponent2D Movement2D;

        private void Awake()
        {
            if (Movement3D == null)
            {
                Movement3D = GetComponent<MovementComponent>();
            }

            if (Movement2D == null)
            {
                Movement2D = GetComponent<MovementComponent2D>();
            }

            if (AnimancerComponent == null)
            {
                AnimancerComponent = ResolveAnimancerComponent();
            }

            if (AnimancerComponent == null)
            {
                Debug.LogWarning("[AnimancerMovementAnimationBinder] AnimancerComponent is not assigned.");
                return;
            }

            Bind3D();
            Bind2D();
        }

        private AnimancerComponent ResolveAnimancerComponent()
        {
            if (Movement3D != null && Movement3D.ExternalAnimationComponent is AnimancerComponent movement3DAnimancer)
            {
                return movement3DAnimancer;
            }

            if (Movement2D != null && Movement2D.ExternalAnimationComponent is AnimancerComponent movement2DAnimancer)
            {
                return movement2DAnimancer;
            }

            return GetComponent<AnimancerComponent>();
        }

        private void Bind3D()
        {
            if (Movement3D == null)
            {
                return;
            }

            Dictionary<int, string> parameterMap = CreateParameterNameMap(Movement3D.Config);
            var controller = new AnimancerAnimationController(AnimancerComponent, parameterMap);
            Movement3D.SetExternalAnimationController(controller, AnimancerComponent.Animator, AnimancerComponent is HybridAnimancerComponent);
        }

        private void Bind2D()
        {
            if (Movement2D == null)
            {
                return;
            }

            Dictionary<int, string> parameterMap = CreateParameterNameMap(Movement2D.Config);
            AddParameterToMap(parameterMap, Movement2D.Config.verticalSpeedParameter);
            AddParameterToMap(parameterMap, Movement2D.Config.inputXParameter);
            AddParameterToMap(parameterMap, Movement2D.Config.inputYParameter);

            var controller = new AnimancerAnimationController(AnimancerComponent, parameterMap);
            Movement2D.SetExternalAnimationController(controller, AnimancerComponent.Animator, AnimancerComponent is HybridAnimancerComponent);
        }

        private static Dictionary<int, string> CreateParameterNameMap(IMovementConfigReadOnly config)
        {
            var map = new Dictionary<int, string>();
            if (config == null)
            {
                return map;
            }

            AddParameterToMap(map, config.movementSpeedParameter);
            AddParameterToMap(map, config.isGroundedParameter);
            AddParameterToMap(map, config.jumpTrigger);
            return map;
        }

        private static void AddParameterToMap(Dictionary<int, string> map, string parameterName)
        {
            if (string.IsNullOrEmpty(parameterName))
            {
                return;
            }

            int hash = AnimationParameterCache.GetHash(parameterName);
            map[hash] = parameterName;
        }
    }
}
