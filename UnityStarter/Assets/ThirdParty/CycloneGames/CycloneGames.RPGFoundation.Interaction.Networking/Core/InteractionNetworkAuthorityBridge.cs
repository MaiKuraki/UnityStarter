using System;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Networking
{
    /// <summary>
    /// Bridges inbound network interaction requests to the authority service.
    ///
    /// Trust boundary: <see cref="InteractionNetworkRequest.InstigatorPosition"/> is
    /// client-reported and therefore untrusted. The two-parameter overloads
    /// (<see cref="Validate(in InteractionNetworkRequest, int)"/> etc.) validate
    /// against that claimed position and exist for products that deliberately run
    /// client-informative / server-permissive interaction rules. Servers that
    /// enforce range authoritatively must resolve the instigator position from
    /// their own simulation and call the server-position overloads
    /// (<see cref="Validate(in InteractionNetworkRequest, InteractionVector3, int)"/> etc.).
    /// </summary>
    public sealed class InteractionNetworkAuthorityBridge
    {
        private readonly InteractionAuthorityService _authority;

        public InteractionNetworkAuthorityBridge(InteractionAuthorityService authority)
        {
            _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        }

        public InteractionAuthorityService Authority => _authority;

        /// <summary>Validates against the client-reported position. See class remarks on trust.</summary>
        public InteractionValidationResult Validate(in InteractionNetworkRequest request, int serverTick)
        {
            return _authority.ValidateRequest(request.ToInteractionRequest(), request, serverTick);
        }

        /// <summary>Validates against the client-reported position. See class remarks on trust.</summary>
        public InteractionValidationResult TryQueue(in InteractionNetworkRequest request, int serverTick)
        {
            return _authority.TryQueueRequest(request.ToInteractionRequest(), request, serverTick);
        }

        /// <summary>
        /// Validates against a server-resolved instigator position; the
        /// client-reported position in the request is ignored for range checks.
        /// </summary>
        public InteractionValidationResult Validate(in InteractionNetworkRequest request, InteractionVector3 serverInstigatorPosition, int serverTick)
        {
            return _authority.ValidateRequest(request.ToInteractionRequest(), serverInstigatorPosition, serverTick);
        }

        /// <summary>
        /// Queues against a server-resolved instigator position; the
        /// client-reported position in the request is ignored for range checks.
        /// </summary>
        public InteractionValidationResult TryQueue(in InteractionNetworkRequest request, InteractionVector3 serverInstigatorPosition, int serverTick)
        {
            return _authority.TryQueueRequest(request.ToInteractionRequest(), serverInstigatorPosition, serverTick);
        }
    }

    public static class InteractionNetworkAuthorityExtensions
    {
        /// <summary>Validates against the client-reported position; untrusted for range enforcement.</summary>
        public static InteractionValidationResult ValidateNetworkRequest(
            this InteractionAuthorityService authority,
            in InteractionNetworkRequest request,
            int serverTick)
        {
            if (authority == null)
            {
                throw new ArgumentNullException(nameof(authority));
            }

            return authority.ValidateRequest(request.ToInteractionRequest(), request, serverTick);
        }

        /// <summary>Validates against a server-resolved instigator position.</summary>
        public static InteractionValidationResult ValidateNetworkRequest(
            this InteractionAuthorityService authority,
            in InteractionNetworkRequest request,
            InteractionVector3 serverInstigatorPosition,
            int serverTick)
        {
            if (authority == null)
            {
                throw new ArgumentNullException(nameof(authority));
            }

            return authority.ValidateRequest(request.ToInteractionRequest(), serverInstigatorPosition, serverTick);
        }

        /// <summary>Queues against the client-reported position; untrusted for range enforcement.</summary>
        public static InteractionValidationResult TryQueueNetworkRequest(
            this InteractionAuthorityService authority,
            in InteractionNetworkRequest request,
            int serverTick)
        {
            if (authority == null)
            {
                throw new ArgumentNullException(nameof(authority));
            }

            return authority.TryQueueRequest(request.ToInteractionRequest(), request, serverTick);
        }

        /// <summary>Queues against a server-resolved instigator position.</summary>
        public static InteractionValidationResult TryQueueNetworkRequest(
            this InteractionAuthorityService authority,
            in InteractionNetworkRequest request,
            InteractionVector3 serverInstigatorPosition,
            int serverTick)
        {
            if (authority == null)
            {
                throw new ArgumentNullException(nameof(authority));
            }

            return authority.TryQueueRequest(request.ToInteractionRequest(), serverInstigatorPosition, serverTick);
        }
    }
}
