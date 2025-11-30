using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Runtime.Movement.States
{
    public abstract class MovementStateBase
    {
        public abstract MovementStateType StateType { get; }

        public virtual void OnEnter(ref MovementContext context) { }

        public virtual void OnUpdate(ref MovementContext context, out float3 displacement) 
        {
            displacement = float3.zero;
        }

        public virtual void OnExit(ref MovementContext context) { }

        public virtual MovementStateBase EvaluateTransition(ref MovementContext context)
        {
            return null;
        }

        protected quaternion CalculateRotationTowards(float3 direction, float3 worldUp, quaternion currentRotation, float rotationSpeed, float deltaTime)
        {
            if (math.lengthsq(direction) < 0.0001f)
            {
                float3 currentUp = math.mul(currentRotation, new float3(0, 1, 0));
                
                if (math.lengthsq(currentUp - worldUp) > 0.001f)
                {
                    UnityEngine.Quaternion unityToUp = UnityEngine.Quaternion.FromToRotation(currentUp, worldUp);
                    quaternion toUp = unityToUp;
                    quaternion targetRotation = math.mul(toUp, currentRotation);
                    return math.slerp(currentRotation, targetRotation, rotationSpeed * deltaTime);
                }
                
                return currentRotation;
            }
            else
            {
                quaternion targetRotation = quaternion.LookRotation(direction, worldUp);
                return math.slerp(currentRotation, targetRotation, rotationSpeed * deltaTime);
            }
        }
    }
}
