using UnityEngine;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// Hosts a pure C# AbilitySystemComponent on a GameObject and drives it from the Unity loop.
    /// </summary>
    [AddComponentMenu("CycloneGames/GameplayAbilities/Ability System Holder")]
    public class AbilitySystemComponentHolder : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("True when this peer owns authoritative simulation for the component.")]
        private bool runsServerSimulation = true;

        public AbilitySystemComponent AbilitySystemComponent { get; private set; }

        public void Initialize(GASRuntimeContext runtimeContext)
        {
            if (runtimeContext == null)
            {
                throw new System.ArgumentNullException(nameof(runtimeContext));
            }

            if (AbilitySystemComponent != null)
            {
                throw new System.InvalidOperationException("AbilitySystemComponentHolder is already initialized.");
            }

            AbilitySystemComponent = new AbilitySystemComponent(runtimeContext);
        }

        public void Tick(float deltaTime)
        {
            AbilitySystemComponent?.Tick(deltaTime, runsServerSimulation);
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        public void Shutdown()
        {
            AbilitySystemComponent?.Dispose();
            AbilitySystemComponent = null;
        }
    }
}
