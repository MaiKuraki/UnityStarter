using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Sample.PureUnity
{
    /// <summary>
    /// Minimal opt-in Actor Tick sample. Add this component to a scene GameObject to rotate it
    /// only while its World is playing.
    /// </summary>
    public sealed class UnitySampleRotatingActor : Actor
    {
        [SerializeField] private Vector3 RotationAxis = Vector3.up;
        [SerializeField, Min(0f)] private float DegreesPerSecond = 45f;

        protected override void Awake()
        {
            base.Awake();
            ConfigureActorTick(ActorTickPhase.Update, startWithTickEnabled: true);
        }

        protected override void Tick(float deltaSeconds)
        {
            if (RotationAxis.sqrMagnitude <= 0.0001f || DegreesPerSecond <= 0f)
            {
                return;
            }

            transform.Rotate(
                RotationAxis.normalized,
                DegreesPerSecond * deltaSeconds,
                Space.Self);
        }
    }
}
