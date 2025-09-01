using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    //  TODO: Implement 2D version
    public class KillZVolume : Actor
    {
        private const string DEBUG_FLAG = "<color=#FF4B4B>[KillZ Volume]</color>";
        protected override void Awake()
        {
            base.Awake();

            BoxCollider collision = GetComponent<BoxCollider>();
            if (collision) collision.isTrigger = true;
        }

        protected virtual void OnTriggerEnter(Collider other)
        {
            //  Target Actor require a 'Collision' component and 'Rigidbody' component
            CLogger.LogInfo($"{DEBUG_FLAG} {other.gameObject.name} Enter Kill Z");
            if (!other) return;
            Actor otherActor = other.GetComponent<Actor>();
            if (otherActor != null)
            {
                otherActor.FellOutOfWorld();
            }
        }
    }
}