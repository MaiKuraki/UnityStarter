using UnityEngine;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// A MonoBehaviour wrapper to host the pure C# AbilitySystemComponent.
    /// This component should be attached to any GameObject that needs to use the ability system.
    /// </summary>
    public class AbilitySystemComponentHolder : MonoBehaviour
    {
        public AbilitySystemComponent AbilitySystemComponent { get; private set; }

        void Awake()
        {
            // For this sample, we manually create a factory.
            // In a real project, this would likely come from a DI container like VContainer or Zenject.
            var effectContextFactory = new GameplayEffectContextFactory();
            AbilitySystemComponent = new AbilitySystemComponent(effectContextFactory);
        }

        // It's good practice to provide a Tick method that can be called by a central manager
        // or by another component on this GameObject (like the Character class).
        public void Tick(float deltaTime)
        {
            AbilitySystemComponent?.Tick(deltaTime, true); // Assuming server/single-player context.
        }

        // Ensure the pure C# class is disposed of when the MonoBehaviour is destroyed.
        private void OnDestroy()
        {
            AbilitySystemComponent?.Dispose();
        }
    }
}