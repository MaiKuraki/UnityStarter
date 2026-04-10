using UnityEngine;
using CycloneGames.Logger;

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

        #region Focus
        public void SetFocus(Actor NewFocus)
        {
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
            bIsRunningAI = true;
        }

        public virtual void StopAI()
        {
            bIsRunningAI = false;
        }

        public bool IsRunningAI() => bIsRunningAI;
        #endregion

        protected override void OnPossess(Pawn InPawn)
        {
            base.OnPossess(InPawn);
            InitPlayerState();
            if (bStartAILogicOnPossess) RunAI();
        }

        protected override void OnUnPossess()
        {
            StopAI();
            ClearFocus();
            base.OnUnPossess();
        }

        protected override void Update()
        {
            base.Update();
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