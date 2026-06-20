using System;

namespace CycloneGames.Networking.Security
{
    public sealed class NetworkSecurityPipelineOptions
    {
        public MessageSecurityPolicyRegistry MessagePolicies { get; set; } = new MessageSecurityPolicyRegistry();
        public RateLimiter RateLimiter { get; set; }
        public INetworkReplayProtector ReplayProtector { get; set; } = new NetworkReplayGuardProtector();
        public INetworkMessageSigner MessageSigner { get; set; } = NoopNetworkMessageSigner.Instance;
        public INetworkCryptoProvider CryptoProvider { get; set; } = NoopNetworkCryptoProvider.Instance;
        public INetworkAntiCheatSignalSink AntiCheatSignalSink { get; set; } = NoopNetworkAntiCheatSignalSink.Instance;
        public bool EnableRateLimiting { get; set; }
        public bool ReportRejectedMessages { get; set; } = true;
    }

    public readonly struct NetworkSecurityPipelineResult
    {
        public readonly MessageSecurityResult Result;
        public readonly string Reason;

        public NetworkSecurityPipelineResult(MessageSecurityResult result, string reason = "")
        {
            Result = result;
            Reason = reason ?? string.Empty;
        }

        public bool Accepted
        {
            get
            {
                return Result == MessageSecurityResult.Valid;
            }
        }

        public static NetworkSecurityPipelineResult Accept()
        {
            return new NetworkSecurityPipelineResult(MessageSecurityResult.Valid);
        }

        public static NetworkSecurityPipelineResult Reject(MessageSecurityResult result, string reason)
        {
            return new NetworkSecurityPipelineResult(result, reason);
        }
    }

    public sealed class NetworkSecurityPipeline
    {
        private readonly MessageSecurityPolicyRegistry _policies;
        private readonly RateLimiter _rateLimiter;
        private readonly INetworkReplayProtector _replayProtector;
        private readonly INetworkMessageSigner _messageSigner;
        private readonly INetworkCryptoProvider _cryptoProvider;
        private readonly INetworkAntiCheatSignalSink _antiCheatSignalSink;
        private readonly bool _enableRateLimiting;
        private readonly bool _reportRejectedMessages;

        public NetworkSecurityPipeline()
            : this(new NetworkSecurityPipelineOptions())
        {
        }

        public NetworkSecurityPipeline(NetworkSecurityPipelineOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _policies = options.MessagePolicies ?? new MessageSecurityPolicyRegistry();
            _rateLimiter = options.RateLimiter;
            _replayProtector = options.ReplayProtector ?? new NetworkReplayGuardProtector();
            _messageSigner = options.MessageSigner ?? NoopNetworkMessageSigner.Instance;
            _cryptoProvider = options.CryptoProvider ?? NoopNetworkCryptoProvider.Instance;
            _antiCheatSignalSink = options.AntiCheatSignalSink ?? NoopNetworkAntiCheatSignalSink.Instance;
            _enableRateLimiting = options.EnableRateLimiting;
            _reportRejectedMessages = options.ReportRejectedMessages;
        }

        public MessageSecurityPolicyRegistry Policies
        {
            get
            {
                return _policies;
            }
        }

        public INetworkMessageSigner MessageSigner
        {
            get
            {
                return _messageSigner;
            }
        }

        public INetworkCryptoProvider CryptoProvider
        {
            get
            {
                return _cryptoProvider;
            }
        }

        public NetworkSecurityPipelineResult ValidateInbound(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> payload,
            ReadOnlySpan<byte> signature,
            bool transportEncrypted,
            float currentTime,
            int rateLimitBytes = -1)
        {
            if (!envelope.IsValid || payload.Length != envelope.PayloadLength)
            {
                return Reject(connection, envelope, MessageSecurityResult.MalformedEnvelope, currentTime, "Message envelope is malformed.");
            }

            if (envelope.Version > NetworkMessageEnvelope.CurrentVersion)
            {
                return Reject(connection, envelope, MessageSecurityResult.UnsupportedVersion, currentTime, "Message protocol version is not supported.");
            }

            MessageSecurityPolicy policy = _policies.GetPolicy(envelope.MessageId);
            if (!IsDirectionAllowed(policy.AllowedDirections, envelope.Direction))
            {
                return Reject(connection, envelope, MessageSecurityResult.DirectionRejected, currentTime, "Message direction is not allowed.");
            }

            if (payload.Length > policy.MaxPayloadSize)
            {
                return Reject(connection, envelope, MessageSecurityResult.PayloadTooLarge, currentTime, "Message payload exceeds security policy budget.");
            }

            if (policy.RequireAuthenticatedConnection && (connection == null || !connection.IsAuthenticated))
            {
                return Reject(connection, envelope, MessageSecurityResult.AuthenticationRequired, currentTime, "Authenticated connection is required.");
            }

            if (policy.RequireEncryptedTransport && !IsEncryptionSatisfied(envelope, transportEncrypted))
            {
                return Reject(connection, envelope, MessageSecurityResult.EncryptionRequired, currentTime, "Encrypted transport or payload encryption is required.");
            }

            if (_enableRateLimiting && _rateLimiter != null)
            {
                int connectionId = connection != null ? connection.ConnectionId : 0;
                int chargedBytes = rateLimitBytes >= 0 ? rateLimitBytes : payload.Length;
                if (!_rateLimiter.TryConsume(connectionId, chargedBytes, currentTime))
                {
                    return Reject(connection, envelope, MessageSecurityResult.RateLimited, currentTime, "Connection exceeded network message rate limit.");
                }
            }

            if (policy.RequireSignature)
            {
                if (!_messageSigner.IsEnabled || signature.Length == 0)
                {
                    return Reject(connection, envelope, MessageSecurityResult.SignatureRequired, currentTime, "Message signature is required.");
                }

                if (!_messageSigner.TryVerify(connection, envelope, payload, signature))
                {
                    return Reject(connection, envelope, MessageSecurityResult.SignatureRejected, currentTime, "Message signature verification failed.");
                }
            }

            if (policy.EnableReplayProtection)
            {
                if (connection == null
                    || !_replayProtector.TryAccept(new NetworkReplayContext(
                        connection.ConnectionId,
                        envelope.MessageId,
                        envelope.Sequence,
                        currentTime)))
                {
                    return Reject(connection, envelope, MessageSecurityResult.ReplayRejected, currentTime, "Message sequence was rejected by replay protection.");
                }
            }

            return NetworkSecurityPipelineResult.Accept();
        }

        public bool TryProtectPayload(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> plaintext,
            Span<byte> protectedPayload,
            out int writtenBytes)
        {
            return _cryptoProvider.TryProtect(connection, envelope, plaintext, protectedPayload, out writtenBytes);
        }

        public bool TryUnprotectPayload(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> protectedPayload,
            Span<byte> plaintext,
            out int writtenBytes)
        {
            return _cryptoProvider.TryUnprotect(connection, envelope, protectedPayload, plaintext, out writtenBytes);
        }

        public bool TrySign(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> payload,
            Span<byte> signature,
            out int writtenBytes)
        {
            return _messageSigner.TrySign(connection, envelope, payload, signature, out writtenBytes);
        }

        public void RemoveConnection(int connectionId)
        {
            _rateLimiter?.RemoveConnection(connectionId);
            _replayProtector.RemoveConnection(connectionId);
        }

        public void ClearState()
        {
            _rateLimiter?.Clear();
            _replayProtector.Clear();
        }

        private bool IsEncryptionSatisfied(in NetworkMessageEnvelope envelope, bool transportEncrypted)
        {
            return transportEncrypted
                   || (_cryptoProvider.IsEnabled && (envelope.Flags & NetworkMessageFlags.Encrypted) != 0);
        }

        private NetworkSecurityPipelineResult Reject(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            MessageSecurityResult result,
            double currentTime,
            string reason)
        {
            if (_reportRejectedMessages)
            {
                _antiCheatSignalSink.Report(new NetworkAntiCheatSignal(
                    GetSignalId(result),
                    GetSeverity(result),
                    connection != null ? connection.ConnectionId : 0,
                    connection != null ? connection.PlayerId : 0UL,
                    envelope.MessageId,
                    envelope.Sequence,
                    currentTime,
                    reason));
            }

            return NetworkSecurityPipelineResult.Reject(result, reason);
        }

        private static NetworkAntiCheatSignalId GetSignalId(MessageSecurityResult result)
        {
            return result switch
            {
                MessageSecurityResult.SignatureRequired => NetworkAntiCheatSignalIds.SignatureRejected,
                MessageSecurityResult.SignatureRejected => NetworkAntiCheatSignalIds.SignatureRejected,
                MessageSecurityResult.ReplayRejected => NetworkAntiCheatSignalIds.ReplayRejected,
                MessageSecurityResult.RateLimited => NetworkAntiCheatSignalIds.RateLimited,
                _ => NetworkAntiCheatSignalIds.MessageRejected
            };
        }

        private static NetworkReadinessSeverity GetSeverity(MessageSecurityResult result)
        {
            return result switch
            {
                MessageSecurityResult.SignatureRejected => NetworkReadinessSeverity.Required,
                MessageSecurityResult.ReplayRejected => NetworkReadinessSeverity.Required,
                MessageSecurityResult.RateLimited => NetworkReadinessSeverity.Warning,
                _ => NetworkReadinessSeverity.Warning
            };
        }

        private static bool IsDirectionAllowed(NetworkMessageDirectionMask mask, NetworkMessageDirection direction)
        {
            return direction switch
            {
                NetworkMessageDirection.ClientToServer => (mask & NetworkMessageDirectionMask.ClientToServer) != 0,
                NetworkMessageDirection.ServerToClient => (mask & NetworkMessageDirectionMask.ServerToClient) != 0,
                NetworkMessageDirection.ServerBroadcast => (mask & NetworkMessageDirectionMask.ServerBroadcast) != 0,
                NetworkMessageDirection.PeerToPeer => (mask & NetworkMessageDirectionMask.PeerToPeer) != 0,
                _ => false
            };
        }
    }

    public static class NetworkSecurityPipelineRuntimeContextExtensions
    {
        public static INetworkRuntimeContextBuilder AddSecurityPipeline(
            this INetworkRuntimeContextBuilder builder,
            NetworkSecurityPipeline pipeline)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            builder.AddService(pipeline ?? throw new ArgumentNullException(nameof(pipeline)));
            return builder;
        }

        public static bool TryGetSecurityPipeline(
            this INetworkRuntimeContext context,
            out NetworkSecurityPipeline pipeline)
        {
            if (context != null && context.TryGetService(out pipeline))
            {
                return true;
            }

            pipeline = null;
            return false;
        }
    }
}
