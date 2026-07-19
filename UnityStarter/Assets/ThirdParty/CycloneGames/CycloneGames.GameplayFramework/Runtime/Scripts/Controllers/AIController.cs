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
        public void SetFocus(Actor NewFocus)
        {
            if (NewFocus != null && World != null && !ReferenceEquals(NewFocus.World, World))
            {
                throw new System.InvalidOperationException("AI focus must belong to the same World.");
            }

            focusActor = NewFocus;
            focalPoint = null;
        }

        public void SetFocalPoint(Vector3 Point)
        {
            focalPoint = Point;
            focusActor = null;
        }

        public Actor GetFocusActor() => focusActor;

        public Vector3 GetFocalPoint()
        {
            if (focusActor != null) return focusActor.GetActorLocation();
            return focalPoint ?? GetActorLocation();
        }

        public void ClearFocus()
        {
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
            EnsureActorTickConfiguration();
            bIsRunningAI = true;
            SetActorTickEnabled(true);
        }

        public virtual void StopAI()
        {
            bIsRunningAI = false;
            SetActorTickEnabled(false);
        }

        public bool IsRunningAI() => bIsRunningAI;

        private void EnsureActorTickConfiguration()
        {
            if (TickPhase != ActorTickPhase.Update || IsTickEnabledAtStart)
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
            try
            {
                base.OnWorldUnbound(reason);
            }
            finally
            {
                StopAI();
                ClearFocus();
            }
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
