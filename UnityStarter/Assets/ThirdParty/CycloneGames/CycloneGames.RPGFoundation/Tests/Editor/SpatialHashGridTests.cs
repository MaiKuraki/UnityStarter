using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.RPGFoundation.Runtime.Interaction;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Tests.Editor
{
    public sealed class SpatialHashGridTests
    {
        private SpatialHashGrid _grid;

        [TearDown]
        public void TearDown()
        {
            _grid?.Dispose();
            _grid = null;
        }

        [Test]
        public void QueryRadiusNonAlloc_UsesCallerCapacityWhenGrowthIsDisabled()
        {
            _grid = new SpatialHashGrid(cellSize: 1f, initialCapacity: 4);
            var a = new TestInteractable(new Vector3(0f, 0f, 0f));
            var b = new TestInteractable(new Vector3(0.1f, 0f, 0f));
            var c = new TestInteractable(new Vector3(0.2f, 0f, 0f));
            var results = new List<IInteractable>(2);

            _grid.Insert(a);
            _grid.Insert(b);
            _grid.Insert(c);

            int count = _grid.QueryRadiusNonAlloc(Vector3.zero, 2f, results, maxResults: 8, allowBufferGrowth: false);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(results.Count, Is.EqualTo(2));
            Assert.That(results.Capacity, Is.EqualTo(2));
        }

        [Test]
        public void QueryRadiusNonAlloc_RespectsMaxResults()
        {
            _grid = new SpatialHashGrid(cellSize: 1f, initialCapacity: 4);
            var results = new List<IInteractable>(4);

            _grid.Insert(new TestInteractable(new Vector3(0f, 0f, 0f)));
            _grid.Insert(new TestInteractable(new Vector3(0.1f, 0f, 0f)));
            _grid.Insert(new TestInteractable(new Vector3(0.2f, 0f, 0f)));

            int count = _grid.QueryRadiusNonAlloc(Vector3.zero, 2f, results, maxResults: 1);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(results.Count, Is.EqualTo(1));
        }

        [Test]
        public void UpdatePosition_UsesXYPlaneWhen2DModeIsRequested()
        {
            _grid = new SpatialHashGrid(cellSize: 1f, initialCapacity: 2);
            var item = new TestInteractable(new Vector3(0f, 0f, 99f));
            var results = new List<IInteractable>(4);

            _grid.Insert(item, is2D: true);

            Assert.That(_grid.QueryRadiusNonAlloc(new Vector3(0f, 0f, 0f), 0.5f, results, is2D: true), Is.EqualTo(1));

            item.SetPosition(new Vector3(0f, 5f, 99f));
            _grid.UpdatePosition(item, is2D: true);

            Assert.That(_grid.QueryRadiusNonAlloc(new Vector3(0f, 0f, 0f), 0.5f, results, is2D: true), Is.Zero);
            Assert.That(_grid.QueryRadiusNonAlloc(new Vector3(0f, 5f, 0f), 0.5f, results, is2D: true), Is.EqualTo(1));
        }

        [Test]
        public void Remove_ClearsItemFromFutureQueries()
        {
            _grid = new SpatialHashGrid(cellSize: 1f, initialCapacity: 2);
            var item = new TestInteractable(Vector3.zero);
            var results = new List<IInteractable>(4);

            _grid.Insert(item);
            _grid.Remove(item);

            Assert.That(_grid.ItemCount, Is.Zero);
            Assert.That(_grid.QueryRadiusNonAlloc(Vector3.zero, 1f, results), Is.Zero);
        }

        [Test]
        public void QueryRadiusNonAlloc_ThrowsForNullResults()
        {
            _grid = new SpatialHashGrid();

            Assert.Throws<ArgumentNullException>(() => _grid.QueryRadiusNonAlloc(Vector3.zero, 1f, null));
        }

        private sealed class TestInteractable : IInteractable
        {
            private Vector3 _position;

            public TestInteractable(Vector3 position)
            {
                _position = position;
            }

            public string InteractionPrompt => "Test";
            public InteractionPromptData? PromptData => null;
            public bool IsInteractable => true;
            public bool AutoInteract => false;
            public bool IsInteracting => false;
            public int Priority => 0;
            public Vector3 Position => _position;
            public float InteractionDistance => 1f;
            public InteractionStateType CurrentState => InteractionStateType.Idle;
            public InteractionChannel Channel => InteractionChannel.Channel0;
            public IReadOnlyList<IInteractionRequirement> Requirements => Array.Empty<IInteractionRequirement>();
            public float InteractionProgress => 0f;
            public IReadOnlyList<InteractionAction> Actions => Array.Empty<InteractionAction>();
            public InstigatorHandle CurrentInstigator => null;
            public float HoldDuration => 0f;
            public float MaxInteractionRange => 0f;
            public bool IsBusy => false;

            public event Action<IInteractable, InteractionStateType> OnStateChanged
            {
                add { }
                remove { }
            }

            public event Action<IInteractable, float> OnProgressChanged
            {
                add { }
                remove { }
            }

            public event Action<IInteractable, InteractionCancelReason> OnInteractionCancelled
            {
                add { }
                remove { }
            }

            public void SetPosition(Vector3 position)
            {
                _position = position;
            }

            public UniTask<bool> TryInteractAsync(CancellationToken cancellationToken = default)
            {
                return UniTask.FromResult(true);
            }

            public UniTask<bool> TryInteractAsync(string actionId, CancellationToken cancellationToken = default)
            {
                return UniTask.FromResult(true);
            }

            public UniTask<bool> TryInteractAsync(InstigatorHandle instigator, string actionId, CancellationToken cancellationToken = default)
            {
                return UniTask.FromResult(true);
            }

            public bool CanInteract(InstigatorHandle instigator)
            {
                return true;
            }

            public void OnFocus()
            {
            }

            public void OnDefocus()
            {
            }

            public void ForceEndInteraction(InteractionCancelReason reason = InteractionCancelReason.Manual)
            {
            }
        }
    }
}
