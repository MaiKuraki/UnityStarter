using System;
using Cysharp.Threading.Tasks;
using CycloneGames.RPGFoundation.Runtime.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Tests.Editor
{
    public sealed class InteractableTests
    {
        private GameObject _gameObject;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }
        }

        [Test]
        public void SetInteractionSystem_RegistersAndUnregistersWithExplicitSystem()
        {
            _gameObject = new GameObject("InteractableTests_SetInteractionSystem");
            _gameObject.SetActive(false);
            var interactable = _gameObject.AddComponent<InteractionTestInteractable>();
            var system = new TestInteractionSystem();

            interactable.SetInteractionSystem(system, registerImmediately: false);
            interactable.InvokeOnEnableForTest();

            Assert.That(system.RegisterCalls, Is.EqualTo(1));
            Assert.That(system.LastRegistered, Is.SameAs(interactable));

            interactable.SetInteractionSystem(null);

            Assert.That(system.UnregisterCalls, Is.EqualTo(1));
            Assert.That(system.LastUnregistered, Is.SameAs(interactable));
        }

        [Test]
        public void CanInteract_ReturnsFalseWhenRequirementBlocks()
        {
            _gameObject = new GameObject("InteractableTests_CanInteract");
            _gameObject.SetActive(false);
            var requirement = _gameObject.AddComponent<InteractionTestRequirement>();
            requirement.IsAllowed = false;
            var interactable = _gameObject.AddComponent<InteractionTestInteractable>();
            interactable.SetInteractionSystem(new TestInteractionSystem(), registerImmediately: false);
            interactable.InvokeAwakeForTest();

            Assert.That(interactable.CanInteract(null), Is.False);
        }

        [Test]
        public void TryInteractAsync_ReturnsFalseWhenRequirementBlocks()
        {
            _gameObject = new GameObject("InteractableTests_TryInteract");
            _gameObject.SetActive(false);
            var requirement = _gameObject.AddComponent<InteractionTestRequirement>();
            requirement.IsAllowed = false;
            var interactable = _gameObject.AddComponent<InteractionTestInteractable>();
            interactable.SetInteractionSystem(new TestInteractionSystem(), registerImmediately: false);
            interactable.InvokeAwakeForTest();

            bool result = interactable.TryInteractAsync().GetAwaiter().GetResult();

            Assert.That(result, Is.False);
            Assert.That(interactable.CurrentState, Is.EqualTo(InteractionStateType.Idle));
            Assert.That(interactable.IsInteracting, Is.False);
        }

        private sealed class TestInteractionSystem : IInteractionSystem
        {
            public SpatialHashGrid SpatialGrid => null;
            public bool Is2DMode => false;
            public int RegisterCalls { get; private set; }
            public int UnregisterCalls { get; private set; }
            public IInteractable LastRegistered { get; private set; }
            public IInteractable LastUnregistered { get; private set; }

            public event Action<IInteractable, InstigatorHandle> OnAnyInteractionStarted
            {
                add { }
                remove { }
            }

            public event Action<IInteractable, InstigatorHandle, bool> OnAnyInteractionCompleted
            {
                add { }
                remove { }
            }

            public void Initialize()
            {
            }

            public void Initialize(bool is2DMode, float cellSize = 10f)
            {
            }

            public void Register(IInteractable interactable)
            {
                RegisterCalls++;
                LastRegistered = interactable;
            }

            public void Unregister(IInteractable interactable)
            {
                UnregisterCalls++;
                LastUnregistered = interactable;
            }

            public void UpdatePosition(IInteractable interactable)
            {
            }

            public UniTask ProcessInteractionAsync(IInteractable target)
            {
                return UniTask.CompletedTask;
            }

            public UniTask ProcessInteractionAsync(IInteractable target, InstigatorHandle instigator)
            {
                return UniTask.CompletedTask;
            }

            public void RegisterDistanceMonitor(IInteractable target, InstigatorHandle instigator, float maxRange)
            {
            }

            public void UnregisterDistanceMonitor(IInteractable target)
            {
            }

            public void Dispose()
            {
            }
        }
    }

    internal sealed class InteractionTestInteractable : Interactable
    {
        public void InvokeAwakeForTest()
        {
            Awake();
        }

        public void InvokeOnEnableForTest()
        {
            OnEnable();
        }
    }

    internal sealed class InteractionTestRequirement : MonoBehaviour, IInteractionRequirement
    {
        public bool IsAllowed { get; set; } = true;
        public string FailureReason => "Blocked";

        public bool IsMet(IInteractable target, InstigatorHandle instigator)
        {
            return IsAllowed;
        }
    }
}
