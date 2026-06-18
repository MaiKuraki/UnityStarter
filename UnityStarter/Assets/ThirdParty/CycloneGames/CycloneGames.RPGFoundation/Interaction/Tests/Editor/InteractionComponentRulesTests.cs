using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.RPGFoundation.Interaction.Editor;
using CycloneGames.RPGFoundation.Interaction.Runtime;
using NUnit.Framework;
using UnityEngine;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Tests.Editor
{
    public sealed class InteractionComponentRulesTests
    {
        private readonly List<InteractionComponentRuleIssue> _issues = new(8);
        private GameObject _gameObject;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }

            _issues.Clear();
        }

        [Test]
        public void CollectIssues_FlagsDuplicateInteractableRoles()
        {
            _gameObject = CreateInactiveGameObject("InteractionComponentRules_DuplicateTargets");
            _gameObject.AddComponent<InteractionTestInteractable>();
            _gameObject.AddComponent<RuleTestInteractable>();

            InteractionComponentRules.CollectIssues(_gameObject, _issues);

            Assert.That(HasIssue(InteractionComponentRuleSeverity.Error, "Multiple IInteractable"), Is.True);
        }

        [Test]
        public void CollectIssues_FlagsInteractionSystemMixedWithTarget()
        {
            _gameObject = CreateInactiveGameObject("InteractionComponentRules_SystemMixedWithTarget");
            _gameObject.AddComponent<InteractionSystem>();
            _gameObject.AddComponent<InteractionTestInteractable>();

            InteractionComponentRules.CollectIssues(_gameObject, _issues);

            Assert.That(HasIssue(InteractionComponentRuleSeverity.Error, "InteractionSystem is a scene/world root service"), Is.True);
        }

        [Test]
        public void CollectIssues_TwoStateHelperWithInteractableIsGuidanceOnly()
        {
            _gameObject = CreateInactiveGameObject("InteractionComponentRules_TwoStateGuidance");
            _gameObject.AddComponent<InteractionTestInteractable>();
            _gameObject.AddComponent<TwoStateInteractionBase>();

            InteractionComponentRules.CollectIssues(_gameObject, _issues);

            Assert.That(HasIssue(InteractionComponentRuleSeverity.Info, "TwoStateInteractionBase only stores toggle state"), Is.True);
            Assert.That(HasIssue(InteractionComponentRuleSeverity.Error, "TwoStateInteractionBase"), Is.False);
        }

        [Test]
        public void RuntimeInteractionComponents_DisallowMultipleComponents()
        {
            AssertHasDisallowMultipleComponent(typeof(Interactable));
            AssertHasDisallowMultipleComponent(typeof(InteractionDetector));
            AssertHasDisallowMultipleComponent(typeof(InteractionSystem));
            AssertHasDisallowMultipleComponent(typeof(PooledEffect));
            AssertHasDisallowMultipleComponent(typeof(TwoStateInteractionBase));
        }

        private static GameObject CreateInactiveGameObject(string name)
        {
            var gameObject = new GameObject(name);
            gameObject.SetActive(false);
            return gameObject;
        }

        private bool HasIssue(InteractionComponentRuleSeverity severity, string messageFragment)
        {
            for (int i = 0; i < _issues.Count; i++)
            {
                InteractionComponentRuleIssue issue = _issues[i];
                if (issue.Severity == severity && issue.Message.IndexOf(messageFragment, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AssertHasDisallowMultipleComponent(Type type)
        {
            object[] attributes = type.GetCustomAttributes(typeof(DisallowMultipleComponent), true);
            Assert.That(attributes.Length, Is.GreaterThan(0), type.Name + " should disallow duplicate component instances.");
        }

        private sealed class RuleTestInteractable : MonoBehaviour, IInteractable
        {
            private static readonly InteractionAction[] EmptyActions = Array.Empty<InteractionAction>();
            private static readonly IInteractionRequirement[] EmptyRequirements = Array.Empty<IInteractionRequirement>();

            public string InteractionPrompt => "Test";
            public InteractionPromptData? PromptData => null;
            public bool IsInteractable => true;
            public bool AutoInteract => false;
            public bool IsInteracting => false;
            public int Priority => 0;
            public float InteractionDistance => 1f;
            public Vector3 Position => transform.position;
            public InteractionStateType CurrentState => InteractionStateType.Idle;
            public InteractionChannel Channel => InteractionChannel.Channel0;
            public float InteractionProgress => 0f;
            public InteractionCancelReason LastCancelReason => InteractionCancelReason.Manual;
            public IReadOnlyList<InteractionAction> Actions => EmptyActions;
            public IReadOnlyList<IInteractionRequirement> Requirements => EmptyRequirements;
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

            public void ForceEndInteraction(InteractionCancelReason reason = InteractionCancelReason.Manual)
            {
            }

            public void OnFocus()
            {
            }

            public void OnDefocus()
            {
            }
        }
    }
}
