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

        [Test]
        public void TryInteractAsync_ResetsToIdleWhenUserCodeThrows()
        {
            _gameObject = new GameObject("InteractableTests_Exception");
            _gameObject.SetActive(false);
            var interactable = _gameObject.AddComponent<ThrowingInteractionTestInteractable>();
            interactable.SetInteractionSystem(new TestInteractionSystem(), registerImmediately: false);
            interactable.InvokeAwakeForTest();

            bool result = interactable.TryInteractAsync().GetAwaiter().GetResult();

            Assert.That(result, Is.False);
            Assert.That(interactable.CurrentState, Is.EqualTo(InteractionStateType.Idle));
            Assert.That(interactable.IsInteracting, Is.False);
            Assert.That(interactable.LastCancelReason, Is.EqualTo(InteractionCancelReason.Faulted));
            Assert.That(interactable.ReportedException, Is.TypeOf<InvalidOperationException>());
            Assert.That(interactable.ReportedException.Message, Is.EqualTo("boom"));
        }

        [Test]
        public void TryInteractAsync_ReturnsFalseWhenActionIsUnknownOrDisabled()
        {
            _gameObject = new GameObject("InteractableTests_Actions");
            _gameObject.SetActive(false);
            var interactable = _gameObject.AddComponent<InteractionTestInteractable>();
            interactable.SetInteractionSystem(new TestInteractionSystem(), registerImmediately: false);
            interactable.SetActionsForTest(
                new InteractionAction("open", "Open"),
                new InteractionAction("locked", "Locked") { IsEnabled = false });
            interactable.InvokeAwakeForTest();

            Assert.That(interactable.TryInteractAsync("missing").GetAwaiter().GetResult(), Is.False);
            Assert.That(interactable.TryInteractAsync("locked").GetAwaiter().GetResult(), Is.False);
            Assert.That(interactable.TryInteractAsync("open").GetAwaiter().GetResult(), Is.True);
        }

        [Test]
        public void StableIdHash_IsDeterministicForAuthoringId()
        {
            _gameObject = new GameObject("InteractableTests_StableId");
            _gameObject.SetActive(false);
            var interactable = _gameObject.AddComponent<InteractionTestInteractable>();
            interactable.SetStableIdForTest("door.main.001");

            Assert.That(interactable.HasStableId, Is.True);
            Assert.That(interactable.StableIdHash, Is.EqualTo(InteractionStableId.Hash64("door.main.001")));
        }

        [Test]
        public void InteractionQueue_RejectsDuplicatesAndCapacityOverflow()
        {
            var queue = new InteractionQueue(capacity: 1);
            var request = new InteractionRequest(1, 100UL, 200UL, "open", tick: 10);

            Assert.That(queue.TryEnqueue(request), Is.True);
            Assert.That(queue.CountQueuedForInstigator(100UL), Is.EqualTo(1));
            Assert.That(queue.TryEnqueue(request), Is.False);
            Assert.That(queue.TryEnqueue(new InteractionRequest(2, 100UL, 201UL, "open", tick: 11)), Is.False);

            Assert.That(queue.TryDequeue(out InteractionRequest dequeued), Is.True);
            Assert.That(dequeued.RequestId, Is.EqualTo(1));
            Assert.That(queue.CountQueuedForInstigator(100UL), Is.EqualTo(0));
            Assert.That(queue.TryEnqueue(request), Is.True);
            Assert.That(queue.CancelStable(100UL), Is.EqualTo(1));
            Assert.That(queue.Count, Is.EqualTo(0));
        }

        [Test]
        public void InteractionQueue_AllowsSameRequestIdFromDifferentStableInstigators()
        {
            var queue = new InteractionQueue(capacity: 2);

            Assert.That(queue.TryEnqueue(new InteractionRequest(1, 100UL, 200UL, "open", tick: 10)), Is.True);
            Assert.That(queue.TryEnqueue(new InteractionRequest(1, 101UL, 200UL, "open", tick: 10)), Is.True);
            Assert.That(queue.CountQueuedForInstigator(100UL), Is.EqualTo(1));
            Assert.That(queue.CountQueuedForInstigator(101UL), Is.EqualTo(1));
        }

        private sealed class TestInteractionSystem : IInteractionSystem
        {
            public SpatialHashGrid SpatialGrid => null;
            public bool Is2DMode => false;
            public int WorldId => 0;
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

        public void SetActionsForTest(params InteractionAction[] testActions)
        {
            actions = testActions;
        }

        public void SetStableIdForTest(string testStableId)
        {
            stableId = testStableId;
        }

        protected override UniTask OnDoInteractAsync(System.Threading.CancellationToken ct)
        {
            return UniTask.CompletedTask;
        }
    }

    internal sealed class ThrowingInteractionTestInteractable : Interactable
    {
        public Exception ReportedException { get; private set; }

        public void InvokeAwakeForTest()
        {
            Awake();
        }

        protected override UniTask OnDoInteractAsync(System.Threading.CancellationToken ct)
        {
            throw new InvalidOperationException("boom");
        }

        protected override void ReportInteractionException(Exception exception)
        {
            ReportedException = exception;
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
