using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement2D.States
{
    public abstract class MovementStateBase2D
    {
        public abstract Movement.MovementStateType StateType { get; }

        public virtual void OnEnter(ref MovementContext2D context) { }

        public virtual void OnUpdate(ref MovementContext2D context, out float2 velocity)
        {
            velocity = float2.zero;
        }

        public virtual void OnFixedUpdate(ref MovementContext2D context) { }

        public virtual void OnExit(ref MovementContext2D context) { }

        public virtual MovementStateBase2D EvaluateTransition(ref MovementContext2D context)
        {
            return null;
        }
    }
}