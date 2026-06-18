using System;
using CycloneGames.RPGFoundation.Interaction.Core;

namespace CycloneGames.RPGFoundation.Interaction.Integrations.Networking
{
    public sealed class InteractionNetworkAuthorityBridge
    {
        private readonly InteractionAuthorityService _authority;

        public InteractionNetworkAuthorityBridge(InteractionAuthorityService authority)
        {
            _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        }

        public InteractionAuthorityService Authority => _authority;

        public InteractionValidationResult Validate(in InteractionNetworkRequest request, int serverTick)
        {
            return _authority.ValidateRequest(request.ToInteractionRequest(), request, serverTick);
        }

        public InteractionValidationResult TryQueue(in InteractionNetworkRequest request, int serverTick)
        {
            return _authority.TryQueueRequest(request.ToInteractionRequest(), request, serverTick);
        }
    }

    public static class InteractionNetworkAuthorityExtensions
    {
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
    }
}
