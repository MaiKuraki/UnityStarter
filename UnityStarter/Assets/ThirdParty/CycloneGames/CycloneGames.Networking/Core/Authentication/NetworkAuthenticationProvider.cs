using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Authentication
{
    public enum NetworkAuthenticationStatus : byte
    {
        Accepted,
        Rejected,
        Pending,
        Unsupported,
        InvalidCredentials,
        Expired,
        ProviderUnavailable
    }

    public readonly struct NetworkPrincipal
    {
        public readonly ulong PlayerId;
        public readonly string UserId;
        public readonly string DisplayName;
        public readonly string ProviderId;
        public readonly int TrustLevel;

        public NetworkPrincipal(
            ulong playerId,
            string userId,
            string displayName = "",
            string providerId = "",
            int trustLevel = 0)
        {
            PlayerId = playerId;
            UserId = userId ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            ProviderId = providerId ?? string.Empty;
            TrustLevel = trustLevel;
        }

        public bool IsValid
        {
            get
            {
                return PlayerId != 0UL || !string.IsNullOrEmpty(UserId);
            }
        }
    }

    public readonly struct NetworkAuthenticationContext
    {
        public readonly string Scheme;
        public readonly int ProtocolVersion;
        public readonly ulong ClientNonce;
        public readonly NetworkRuntimeProfile RuntimeProfile;
        public readonly NetworkNodeCapabilities ClientCapabilities;

        public NetworkAuthenticationContext(
            string scheme,
            int protocolVersion,
            ulong clientNonce = 0UL,
            NetworkRuntimeProfile runtimeProfile = null,
            NetworkNodeCapabilities clientCapabilities = null)
        {
            Scheme = scheme ?? string.Empty;
            ProtocolVersion = protocolVersion;
            ClientNonce = clientNonce;
            RuntimeProfile = runtimeProfile;
            ClientCapabilities = clientCapabilities;
        }
    }

    public readonly struct NetworkAuthenticationResult
    {
        public readonly NetworkAuthenticationStatus Status;
        public readonly NetworkPrincipal Principal;
        public readonly string Reason;

        public NetworkAuthenticationResult(
            NetworkAuthenticationStatus status,
            in NetworkPrincipal principal,
            string reason = "")
        {
            Status = status;
            Principal = principal;
            Reason = reason ?? string.Empty;
        }

        public bool IsAccepted
        {
            get
            {
                return Status == NetworkAuthenticationStatus.Accepted && Principal.IsValid;
            }
        }

        public static NetworkAuthenticationResult Accept(in NetworkPrincipal principal)
        {
            return new NetworkAuthenticationResult(NetworkAuthenticationStatus.Accepted, principal);
        }

        public static NetworkAuthenticationResult Reject(string reason, NetworkAuthenticationStatus status = NetworkAuthenticationStatus.Rejected)
        {
            return new NetworkAuthenticationResult(status, default, reason);
        }

        public static NetworkAuthenticationResult Pending(string reason = "")
        {
            return new NetworkAuthenticationResult(NetworkAuthenticationStatus.Pending, default, reason);
        }
    }

    public interface INetworkAuthenticationProvider
    {
        NetworkAuthenticationResult Authenticate(
            INetConnection connection,
            ReadOnlySpan<byte> credentials,
            in NetworkAuthenticationContext context);
    }

    public sealed class RejectAllNetworkAuthenticationProvider : INetworkAuthenticationProvider
    {
        public static readonly RejectAllNetworkAuthenticationProvider Instance = new RejectAllNetworkAuthenticationProvider();

        private RejectAllNetworkAuthenticationProvider()
        {
        }

        public NetworkAuthenticationResult Authenticate(
            INetConnection connection,
            ReadOnlySpan<byte> credentials,
            in NetworkAuthenticationContext context)
        {
            return NetworkAuthenticationResult.Reject(
                "No authentication provider accepted the connection.",
                NetworkAuthenticationStatus.ProviderUnavailable);
        }
    }

    public sealed class NetworkAuthenticationProviderChain : INetworkAuthenticationProvider
    {
        private const int DEFAULT_PROVIDER_CAPACITY = 4;

        private readonly List<INetworkAuthenticationProvider> _providers;

        public NetworkAuthenticationProviderChain(int capacity = DEFAULT_PROVIDER_CAPACITY)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _providers = new List<INetworkAuthenticationProvider>(capacity);
        }

        public int Count
        {
            get
            {
                return _providers.Count;
            }
        }

        public NetworkAuthenticationProviderChain Add(INetworkAuthenticationProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            _providers.Add(provider);
            return this;
        }

        public NetworkAuthenticationResult Authenticate(
            INetConnection connection,
            ReadOnlySpan<byte> credentials,
            in NetworkAuthenticationContext context)
        {
            for (int i = 0; i < _providers.Count; i++)
            {
                NetworkAuthenticationResult result = _providers[i].Authenticate(connection, credentials, context);
                if (result.Status != NetworkAuthenticationStatus.Unsupported
                    && result.Status != NetworkAuthenticationStatus.ProviderUnavailable)
                {
                    return result;
                }
            }

            return RejectAllNetworkAuthenticationProvider.Instance.Authenticate(connection, credentials, context);
        }
    }

    public static class NetworkAuthenticationRuntimeContextExtensions
    {
        public static INetworkRuntimeContextBuilder AddAuthenticationProvider(
            this INetworkRuntimeContextBuilder builder,
            INetworkAuthenticationProvider provider)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.AddService(provider ?? throw new ArgumentNullException(nameof(provider)));
            return builder;
        }

        public static bool TryGetAuthenticationProvider(
            this INetworkRuntimeContext context,
            out INetworkAuthenticationProvider provider)
        {
            if (context != null && context.TryGetService(out provider))
            {
                return true;
            }

            provider = null;
            return false;
        }
    }
}
