using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.RPGFoundation.Interaction.Core;
using CycloneGames.RPGFoundation.Interaction.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Interaction.Tests.Editor
{
    /// <summary>
    /// Verifies that priority tiers act as a hard ordering, not a soft score bonus. Before the
    /// tiered query, a crowded cell truncated candidates by traversal order, so a high-priority
    /// target could be dropped before scoring ever saw it.
    /// </summary>
    public sealed class InteractionPriorityTests
    {
        private GameObject _systemObject;
        private InteractionSystem _system;

        [SetUp]
        public void SetUp()
        {
            _systemObject = new GameObject("InteractionSystemTest");
            _system = _systemObject.AddComponent<InteractionSystem>();
            _system.Initialize(is2DMode: false, cellSize: 10f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_systemObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_systemObject);
            }
        }

        [Test]
        public void Priority_HighTierIsIsolatedFromCrowdedLowTier()
        {
            var low = new StubInteractable[200];
            for (int i = 0; i < low.Length; i++)
            {
                low[i] = new StubInteractable { Priority = 0, Position = Vector3.zero };
                _system.Register(low[i]);
            }

            var high = new StubInteractable { Priority = 7, Position = Vector3.zero };
            _system.Register(high);

            Assert.That(_system.OccupiedTiers, Is.EqualTo((1u << 7) | 1u),
                "tiers 0 and 7 must both be marked occupied");

            var results = new int[8];

            int highCount = _system.QuerySlots(Vector3.zero, 5f, results, results.Length, 1u << 7);
            Assert.That(highCount, Is.EqualTo(1));
            Assert.That(_system.GetInteractable(results[0]), Is.SameAs(high));

            int lowCount = _system.QuerySlots(Vector3.zero, 5f, results, results.Length, 1u);
            Assert.That(lowCount, Is.EqualTo(8), "the low tier alone must still honour the caller budget");
            for (int i = 0; i < lowCount; i++)
            {
                Assert.That(_system.GetInteractable(results[i]), Is.Not.SameAs(high),
                    "a low-tier query must never surface a high-tier target");
            }
        }

        [Test]
        public void Priority_TierBitmapTracksRegisterReclassAndUnregister()
        {
            var item = new StubInteractable { Priority = 0, Position = Vector3.zero };

            _system.Register(item);
            Assert.That(_system.OccupiedTiers, Is.EqualTo(1u));

            item.Priority = 3;
            _system.UpdatePosition(item);
            Assert.That(_system.OccupiedTiers, Is.EqualTo(1u << 3),
                "changing priority must move the target between tiers");

            item.Priority = 31;
            _system.UpdatePosition(item);
            Assert.That(_system.OccupiedTiers, Is.EqualTo(1u << 31),
                "the top tier must be reachable");

            _system.Unregister(item);
            Assert.That(_system.OccupiedTiers, Is.Zero, "unregistering the last target must clear its tier");
        }

        [Test]
        public void Priority_OutOfRangeValuesClampInsteadOfWrapping()
        {
            var negative = new StubInteractable { Priority = -5, Position = Vector3.zero };
            var oversized = new StubInteractable { Priority = 999, Position = Vector3.zero };

            _system.Register(negative);
            _system.Register(oversized);

            Assert.That(_system.OccupiedTiers, Is.EqualTo((1u << 31) | 1u),
                "priority must clamp into [0,31]; wrapping would silently change which target wins");
        }

        [Test]
        public void Priority_RegistrationIsIdempotent()
        {
            var item = new StubInteractable { Priority = 2, Position = Vector3.zero };
            _system.Register(item);
            _system.Register(item);

            var results = new int[8];
            Assert.That(_system.QuerySlots(Vector3.zero, 5f, results, results.Length, 1u << 2), Is.EqualTo(1),
                "double registration must not create duplicate grid entries");
        }

        private sealed class StubInteractable : IInteractable
        {
            public int Priority { get; set; }
            public Vector3 Position { get; set; }
            public float InteractionDistance { get; set; } = 10f;
            public InteractionChannel Channel { get; set; } = InteractionChannel.All;

            public string InteractionPrompt => string.Empty;
            public InteractionPromptData? PromptData => null;
            public bool IsInteractable => true;
            public bool AutoInteract => false;
            public bool IsInteracting => false;
            public InteractionStateType CurrentState => InteractionStateType.Idle;
            public IReadOnlyList<IInteractionRequirement> Requirements => Array.Empty<IInteractionRequirement>();
            public float InteractionProgress => 0f;
            public IReadOnlyList<InteractionAction> Actions => Array.Empty<InteractionAction>();
            public InstigatorHandle CurrentInstigator => null;
            public float HoldDuration => 0f;
            public float MaxInteractionRange => 0f;
            public bool IsBusy => false;

            public event Action<IInteractable, InteractionStateType> OnStateChanged { add { } remove { } }
            public event Action<IInteractable, float> OnProgressChanged { add { } remove { } }
            public event Action<IInteractable, InteractionCancelReason> OnInteractionCancelled { add { } remove { } }

            public UniTask<bool> TryInteractAsync(CancellationToken cancellationToken = default) =>
                UniTask.FromResult(false);

            public UniTask<bool> TryInteractAsync(string actionId, CancellationToken cancellationToken = default) =>
                UniTask.FromResult(false);

            public UniTask<bool> TryInteractAsync(InstigatorHandle instigator, string actionId, CancellationToken cancellationToken = default) =>
                UniTask.FromResult(false);

            public bool CanInteract(InstigatorHandle instigator) => true;
            public void OnFocus() { }
            public void OnDefocus() { }
            public void ForceEndInteraction(InteractionCancelReason reason = InteractionCancelReason.Manual) { }
        }
    }
}
