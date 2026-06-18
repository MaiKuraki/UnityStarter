using System;
using System.Collections.Generic;
using CycloneGames.RPGFoundation.Runtime.Interaction;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    public enum InteractionComponentRuleSeverity
    {
        Info,
        Warning,
        Error
    }

    public readonly struct InteractionComponentRuleIssue
    {
        public readonly InteractionComponentRuleSeverity Severity;
        public readonly string Message;
        public readonly Object Target;

        public InteractionComponentRuleIssue(InteractionComponentRuleSeverity severity, string message, Object target)
        {
            Severity = severity;
            Message = message;
            Target = target;
        }
    }

    public static class InteractionComponentRules
    {
        private static readonly List<MonoBehaviour> s_componentBuffer = new(16);
        private static readonly List<InteractionComponentRuleIssue> s_drawBuffer = new(8);

        public static void CollectIssues(GameObject gameObject, List<InteractionComponentRuleIssue> issues)
        {
            if (issues == null)
            {
                throw new ArgumentNullException(nameof(issues));
            }

            issues.Clear();
            if (gameObject == null)
            {
                return;
            }

            s_componentBuffer.Clear();
            gameObject.GetComponents(s_componentBuffer);

            int interactableCount = 0;
            int detectorCount = 0;
            int systemCount = 0;
            int pooledEffectCount = 0;
            int twoStateHelperCount = 0;

            Component firstInteractable = null;
            Component firstDetector = null;
            Component firstSystem = null;
            Component firstPooledEffect = null;
            Component firstTwoStateHelper = null;
            bool hasExactTwoStateBase = false;

            for (int i = 0; i < s_componentBuffer.Count; i++)
            {
                MonoBehaviour component = s_componentBuffer[i];
                if (component == null)
                {
                    continue;
                }

                if (component is IInteractable)
                {
                    interactableCount++;
                    firstInteractable ??= component;
                }

                if (component is InteractionDetector)
                {
                    detectorCount++;
                    firstDetector ??= component;
                }

                if (component is InteractionSystem)
                {
                    systemCount++;
                    firstSystem ??= component;
                }

                if (component is PooledEffect)
                {
                    pooledEffectCount++;
                    firstPooledEffect ??= component;
                }

                if (component is TwoStateInteractionBase twoStateHelper)
                {
                    twoStateHelperCount++;
                    firstTwoStateHelper ??= twoStateHelper;
                    if (component.GetType() == typeof(TwoStateInteractionBase))
                    {
                        hasExactTwoStateBase = true;
                    }
                }
            }

            s_componentBuffer.Clear();

            if (interactableCount > 1)
            {
                issues.Add(new InteractionComponentRuleIssue(
                    InteractionComponentRuleSeverity.Error,
                    "Multiple IInteractable implementations are attached to the same GameObject. Keep exactly one concrete target component; for example, use PickableItem instead of adding Interactable next to it.",
                    firstInteractable != null ? firstInteractable : gameObject));
            }

            AddDuplicateRoleIssue(issues, detectorCount, "InteractionDetector", firstDetector, gameObject);
            AddDuplicateRoleIssue(issues, systemCount, "InteractionSystem", firstSystem, gameObject);
            AddDuplicateRoleIssue(issues, pooledEffectCount, "PooledEffect", firstPooledEffect, gameObject);
            AddDuplicateRoleIssue(issues, twoStateHelperCount, "TwoStateInteractionBase", firstTwoStateHelper, gameObject);

            if (systemCount > 0 && (interactableCount > 0 || detectorCount > 0 || pooledEffectCount > 0 || twoStateHelperCount > 0))
            {
                issues.Add(new InteractionComponentRuleIssue(
                    InteractionComponentRuleSeverity.Error,
                    "InteractionSystem is a scene/world root service. Do not combine it with target, detector, two-state, or pooled-effect components on the same GameObject.",
                    firstSystem != null ? firstSystem : gameObject));
            }

            if (detectorCount > 0 && interactableCount > 0)
            {
                issues.Add(new InteractionComponentRuleIssue(
                    InteractionComponentRuleSeverity.Warning,
                    "InteractionDetector is an instigator-side scanner. Keep it on player, camera, AI controller, or sensor objects instead of target objects.",
                    firstDetector != null ? firstDetector : gameObject));
            }

            if (pooledEffectCount > 0 && (interactableCount > 0 || detectorCount > 0 || systemCount > 0 || twoStateHelperCount > 0))
            {
                issues.Add(new InteractionComponentRuleIssue(
                    InteractionComponentRuleSeverity.Warning,
                    "PooledEffect belongs on effect prefabs or effect instances. Keep pooled VFX lifecycle separate from interaction targets, detectors, systems, and state helpers.",
                    firstPooledEffect != null ? firstPooledEffect : gameObject));
            }

            if (hasExactTwoStateBase && interactableCount == 0)
            {
                issues.Add(new InteractionComponentRuleIssue(
                    InteractionComponentRuleSeverity.Warning,
                    "TwoStateInteractionBase stores toggle state but does not make this GameObject detectable. Pair it with one Interactable or a concrete Interactable subclass.",
                    firstTwoStateHelper != null ? firstTwoStateHelper : gameObject));
            }
            else if (hasExactTwoStateBase)
            {
                issues.Add(new InteractionComponentRuleIssue(
                    InteractionComponentRuleSeverity.Info,
                    "TwoStateInteractionBase only stores toggle state. Ensure an Interactable action calls ToggleState(), or use a concrete Interactable subclass that owns the toggle behavior.",
                    firstTwoStateHelper != null ? firstTwoStateHelper : gameObject));
            }
        }

        public static void DrawIssuesFor(Object[] inspectedTargets)
        {
            if (inspectedTargets == null || inspectedTargets.Length == 0)
            {
                return;
            }

            bool multipleTargets = inspectedTargets.Length > 1;
            for (int i = 0; i < inspectedTargets.Length; i++)
            {
                if (inspectedTargets[i] is not Component component || component == null)
                {
                    continue;
                }

                s_drawBuffer.Clear();
                CollectIssues(component.gameObject, s_drawBuffer);

                for (int issueIndex = 0; issueIndex < s_drawBuffer.Count; issueIndex++)
                {
                    InteractionComponentRuleIssue issue = s_drawBuffer[issueIndex];
                    string message = multipleTargets
                        ? component.gameObject.name + ": " + issue.Message
                        : issue.Message;
                    EditorGUILayout.HelpBox(message, ToMessageType(issue.Severity));
                }
            }

            s_drawBuffer.Clear();
        }

        private static void AddDuplicateRoleIssue(
            List<InteractionComponentRuleIssue> issues,
            int count,
            string roleName,
            Component firstComponent,
            GameObject gameObject)
        {
            if (count <= 1)
            {
                return;
            }

            issues.Add(new InteractionComponentRuleIssue(
                InteractionComponentRuleSeverity.Error,
                "Multiple " + roleName + " components are attached to the same GameObject. Keep one owner for this role and move extra behavior to child objects or dedicated components.",
                firstComponent != null ? firstComponent : gameObject));
        }

        private static MessageType ToMessageType(InteractionComponentRuleSeverity severity)
        {
            return severity switch
            {
                InteractionComponentRuleSeverity.Error => MessageType.Error,
                InteractionComponentRuleSeverity.Warning => MessageType.Warning,
                _ => MessageType.Info
            };
        }
    }
}
