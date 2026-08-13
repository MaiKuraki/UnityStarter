using System;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>
    /// Post-update Unity PlayerLoop bridge for ActorTickPhase.LateUpdate. Its default execution
    /// order places framework camera output after ordinary MonoBehaviour LateUpdate callbacks.
    /// </summary>
    [AddComponentMenu("")]
    [DefaultExecutionOrder(9999)]
    [DisallowMultipleComponent]
    public sealed class GameplayWorldLateTickDriver : MonoBehaviour
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
                throw new InvalidOperationException(
                    "GameplayWorldLateTickDriver is already bound to another host.");
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

        private void LateUpdate()
        {
            if (host == null || !host.isActiveAndEnabled)
            {
                return;
            }

            host.DispatchWorldTick(ActorTickPhase.LateUpdate, Time.deltaTime);
        }

        private void OnDestroy()
        {
            host = null;
        }
    }
}
