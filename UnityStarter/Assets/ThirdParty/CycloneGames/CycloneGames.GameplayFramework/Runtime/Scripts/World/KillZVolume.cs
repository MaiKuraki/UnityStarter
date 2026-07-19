using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Volume that kills any Actor entering it. Supports both 3D (BoxCollider) and 2D (BoxCollider2D).
    /// </summary>
    public class KillZVolume : Actor
    {
        protected override void Awake()
        {
            base.Awake();

            var collider3D = GetComponent<BoxCollider>();
            if (collider3D != null) collider3D.isTrigger = true;

            var collider2D = GetComponent<BoxCollider2D>();
            if (collider2D != null) collider2D.isTrigger = true;
        }

        protected virtual void OnTriggerEnter(Collider other)
        {
            if (other == null) return;
            Actor otherActor = other.attachedRigidbody != null
                ? other.attachedRigidbody.GetComponent<Actor>()
                : other.GetComponentInParent<Actor>();
            if (otherActor != null)
            {
                otherActor.FellOutOfWorld();
            }
        }

        protected virtual void OnTriggerEnter2D(Collider2D other)
        {
            if (other == null) return;
            Actor otherActor = other.attachedRigidbody != null
                ? other.attachedRigidbody.GetComponent<Actor>()
                : other.GetComponentInParent<Actor>();
            if (otherActor != null)
            {
                otherActor.FellOutOfWorld();
            }
        }
    }
}
