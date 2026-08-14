using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Base class for AI-controlled pawns with focus management and behavior tree hooks.
    /// </summary>
    public class AIController : Controller
    {
        [SerializeField] private bool bStartAILogicOnPossess = true;

        private Actor focusActor;
        private Vector3? focalPoint;
        private bool bIsRunningAI;

        protected override void Awake()
        {
            base.Awake();
            EnsureActorTickConfiguration();
        }

        #region Focus
        public void SetFocus(Actor newFocus)
        {
            AssertActorOwnerThread();
            if (newFocus != null && World != null && !ReferenceEquals(newFocus.World, World))
            {
                throw new System.InvalidOperationException("AI focus must belong to the same World.");
            }

            focusActor = newFocus;
            focalPoint = null;
        }

        public void SetFocalPoint(Vector3 point)
        {
            AssertActorOwnerThread();
            focalPoint = point;
            focusActor = null;
        }

        public Actor GetFocusActor()
        {
            AssertActorOwnerThread();
            return focusActor;
        }

        public Vector3 GetFocalPoint()
        {
            AssertActorOwnerThread();
            if (focusActor != null) return focusActor.GetActorLocation();
            return focalPoint ?? GetActorLocation();
        }

        public void ClearFocus()
        {
            AssertActorOwnerThread();
            focusActor = null;
            focalPoint = null;
        }
        #endregion

        #region AI Logic
        /// <summary>
        /// Override to start custom AI logic (behavior tree, state machine, etc.)
        /// Called automatically on possess if bStartAILogicOnPossess is true.
        /// </summary>
        public virtual void RunAI()
        {
            AssertActorOwnerThread();
            EnsureActorTickConfiguration();
            bIsRunningAI = true;
            SetActorTickEnabled(true);
        }

        public virtual void StopAI()
        {
            AssertActorOwnerThread();
            bIsRunningAI = false;
            SetActorTickEnabled(false);
        }

        public bool IsRunningAI()
        {
            AssertActorOwnerThread();
            return bIsRunningAI;
        }

        private void EnsureActorTickConfiguration()
        {
            if (TickPhase == ActorTickPhase.None)
            {
                ConfigureActorTick(ActorTickPhase.Update, startWithTickEnabled: false);
            }
        }
        #endregion

        protected override void OnPossess(Pawn InPawn)
        {
            base.OnPossess(InPawn);
            if (bStartAILogicOnPossess) RunAI();
        }

        protected override void OnUnPossess()
        {
            StopAI();
            ClearFocus();
            base.OnUnPossess();
        }

        protected override void OnWorldUnbound(EndPlayReason reason)
        {
            var terminalExceptions = new TerminalExceptionAccumulator();
            try
            {
                base.OnWorldUnbound(reason);
            }
            catch (System.Exception exception)
            {
                terminalExceptions.HandleAndLog(
                    exception,
                    "AIController base Controller cleanup failed while unbinding from its World.");
            }

            try
            {
                StopAI();
            }
            catch (System.Exception exception)
            {
                terminalExceptions.HandleAndLog(
                    exception,
                    "AIController StopAI callback failed while unbinding from its World.");
            }

            bIsRunningAI = false;
            try
            {
                SetActorTickEnabled(false);
            }
            catch (System.Exception exception)
            {
                terminalExceptions.HandleAndLog(
                    exception,
                    "AIController failed to disable Actor Tick while unbinding from its World.");
            }

            focusActor = null;
            focalPoint = null;
            terminalExceptions.ThrowIfCaptured();
        }

        protected override void Tick(float deltaSeconds)
        {
            _ = deltaSeconds;
            if (!bIsRunningAI || GetPawn() == null) return;

            // Auto-rotate toward focus target
            if (focusActor != null || focalPoint.HasValue)
            {
                Vector3 target = GetFocalPoint();
                Vector3 dir = target - GetActorLocation();
                dir.y = 0;
                if (dir.sqrMagnitude > 0.001f)
                {
                    SetControlRotation(Quaternion.LookRotation(dir));
                }
            }
        }
    }
}
