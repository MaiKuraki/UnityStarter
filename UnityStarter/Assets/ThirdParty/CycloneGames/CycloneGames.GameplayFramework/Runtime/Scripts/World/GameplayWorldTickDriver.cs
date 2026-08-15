using System;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Early Unity PlayerLoop bridge for Update and FixedUpdate phases. LateUpdate uses a
    /// separate post-update driver so camera output observes ordinary LateUpdate motion.
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(-9999)]
    [DisallowMultipleComponent]
    public sealed class GameplayWorldTickDriver : MonoBehaviour
    {
        private GameplayWorldHost host;

        internal void Bind(GameplayWorldHost targetHost)
        {
            if (targetHost == null)
            {
                throw new ArgumentNullException(nameof(targetHost));
            }

            if (host != null && !ReferenceEquals(host, targetHost))
            {
                throw new InvalidOperationException("GameplayWorldTickDriver is already bound to another host.");
            }

            host = targetHost;
        }

        internal void Unbind(GameplayWorldHost targetHost)
        {
            if (ReferenceEquals(host, targetHost))
            {
                host = null;
            }
        }

        private void Update()
        {
            Dispatch(ActorTickPhase.Update, Time.deltaTime);
        }

        private void FixedUpdate()
        {
            Dispatch(ActorTickPhase.FixedUpdate, Time.fixedDeltaTime);
        }

        private void Dispatch(ActorTickPhase phase, float deltaSeconds)
        {
            if (host == null || !host.isActiveAndEnabled)
            {
                return;
            }

            host.DispatchWorldTick(phase, deltaSeconds);
        }

        private void OnDestroy()
        {
            host = null;
        }
    }
}
