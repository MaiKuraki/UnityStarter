using UnityEngine;

namespace CycloneGames.AnimationRuntime.Editor
{
    /// <summary>Inspector group a finding belongs to.</summary>
    public enum AnimationSetupField : byte
    {
        Binding = 0,
        Setup = 1,
        Frame = 2
    }

    /// <summary>Severity of one authoring-time setup finding.</summary>
    public enum AnimationSetupSeverity : byte
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>One authoring-time finding. Messages are constant literals, so validation allocates nothing.</summary>
    public readonly struct AnimationSetupFinding
    {
        public readonly AnimationSetupField Field;
        public readonly AnimationSetupSeverity Severity;
        public readonly string Message;

        public AnimationSetupFinding(AnimationSetupField field, AnimationSetupSeverity severity, string message)
        {
            Field = field;
            Severity = severity;
            Message = message;
        }
    }

    /// <summary>
    /// Checks authoring fields against the bound Animator and the components on the same GameObject.
    /// No UnityEditor types, so EditMode tests can exercise it without an inspector.
    /// </summary>
    public static class AnimationRuntimeSetupValidator
    {
        public static readonly int MaxFindings = 8;

        public static int Validate(
            Animator animator,
            bool animatorOnThisGameObject,
            AnimationCommitPhase commitPhase,
            RootMotionOwnership ownership,
            bool dispatchClipEvents,
            bool animatorFireEvents,
            bool hasIKPass,
            bool pauseWhenInvisible,
            bool hasRenderer,
            bool hasTimelineProvider,
            AnimationSetupFinding[] findings)
        {
            if (findings == null || findings.Length == 0)
            {
                return 0;
            }

            int count = 0;

            if (animator == null)
            {
                Add(findings, ref count, AnimationSetupField.Binding, AnimationSetupSeverity.Error,
                    "No Animator assigned. The component disables itself in Awake.");
                return count;
            }

            bool animatePhysics = animator.updateMode == AnimatorUpdateMode.AnimatePhysics;
            if (commitPhase == AnimationCommitPhase.FixedUpdate && !animatePhysics)
            {
                Add(findings, ref count, AnimationSetupField.Frame, AnimationSetupSeverity.Warning,
                    "Commit phase is FixedUpdate but Animator.updateMode is not AnimatePhysics.");
            }
            else if (commitPhase != AnimationCommitPhase.FixedUpdate && animatePhysics)
            {
                Add(findings, ref count, AnimationSetupField.Frame, AnimationSetupSeverity.Warning,
                    "Animator.updateMode is AnimatePhysics but the commit phase is not FixedUpdate.");
            }

            if (commitPhase == AnimationCommitPhase.LateUpdate && hasIKPass)
            {
                Add(findings, ref count, AnimationSetupField.Frame, AnimationSetupSeverity.Warning,
                    "An IK pass is registered but commits run in LateUpdate: OnAnimatorIK runs first and sees the previous pose. Use Update when IK must see the current pose.");
            }

            if (ownership == RootMotionOwnership.Backend && !animatorOnThisGameObject)
            {
                Add(findings, ref count, AnimationSetupField.Frame, AnimationSetupSeverity.Error,
                    "Backend root motion needs the Animator on this GameObject.");
            }

            if (dispatchClipEvents && animatorFireEvents)
            {
                Add(findings, ref count, AnimationSetupField.Setup, AnimationSetupSeverity.Warning,
                    "Animator.fireEvents is on and Dispatch Clip Events is on; every clip event fires twice.");
            }

            if (dispatchClipEvents && hasTimelineProvider)
            {
                Add(findings, ref count, AnimationSetupField.Setup, AnimationSetupSeverity.Warning,
                    "A timeline provider is present. Disable Dispatch Clip Events to avoid double dispatch.");
            }

            if (pauseWhenInvisible && !hasRenderer)
            {
                Add(findings, ref count, AnimationSetupField.Frame, AnimationSetupSeverity.Warning,
                    "Pause When Invisible is on but no Renderer on this GameObject; OnBecameInvisible never fires.");
            }

            return count;
        }

        private static void Add(
            AnimationSetupFinding[] findings,
            ref int count,
            AnimationSetupField field,
            AnimationSetupSeverity severity,
            string message)
        {
            if (count < findings.Length)
            {
                findings[count++] = new AnimationSetupFinding(field, severity, message);
            }
        }
    }
}
