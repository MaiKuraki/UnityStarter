using System;

namespace CycloneGames.Networking.Security
{
    public sealed class NetworkSecurityPipelineOptions
    {
        public MessageSecurityPolicyRegistry MessagePolicies { get; set; } = new MessageSecurityPolicyRegistry();
        public RateLimiter RateLimiter { get; set; }
        public NetworkReplayGuard ReplayGuard { get; set; } = new NetworkReplayGuard();
        public INetworkMessageSigner MessageSigner { get; set; } = NoopNetworkMessageSigner.Instance;
        public INetworkAntiCheatSignalSink AntiCheatSignalSink { get; set; } = NoopNetworkAntiCheatSignalSink.Instance;
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
        private readonly NetworkReplayGuard _replayGuard;
        private readonly INetworkMessageSigner _messageSigner;
        private readonly INetworkAntiCheatSignalSink _antiCheatSignalSink;
        private readonly bool _reportRejectedMessages;

        public NetworkSecurityPipeline(NetworkSecurityPipelineOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            _policies = options.MessagePolicies
                ?? throw new ArgumentException("Message policies must be explicitly configured.", nameof(options));
            _rateLimiter = options.RateLimiter;
            _replayGuard = options.ReplayGuard
                ?? throw new ArgumentException("Replay guard must be explicitly configured.", nameof(options));
            _messageSigner = options.MessageSigner
                ?? throw new ArgumentException("Message signer must be explicitly configured.", nameof(options));
            _antiCheatSignalSink = options.AntiCheatSignalSink
                ?? throw new ArgumentException("Anti-cheat signal sink must be explicitly configured.", nameof(options));
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

        public NetworkSecurityPipelineResult ValidateInbound(
            INetConnection connection,
            in NetworkMessageEnvelope envelope,
            ReadOnlySpan<byte> payload,
            ReadOnlySpan<byte> signature,
            bool transportEncrypted,
            double currentTime,
            int rateLimitBytes)
        {
            if (rateLimitBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(rateLimitBytes));

            if (!envelope.IsValid || payload.Length != envelope.PayloadLength)
            {
                return Reject(connection, envelope, MessageSecurityResult.MalformedEnvelope, currentTime, "Message envelope is malformed.");
            }

            if (envelope.Version < NetworkWireProtocol.MinSupportedVersion
                || envelope.Version > NetworkWireProtocol.CurrentVersion)
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

            if (policy.RequireEncryptedTransport && !transportEncrypted)
            {
                return Reject(connection, envelope, MessageSecurityResult.EncryptionRequired, currentTime, "Encrypted transport is required.");
            }

            if (_rateLimiter != null)
            {
                int connectionId = connection != null ? connection.ConnectionId : 0;
                if (!_rateLimiter.TryConsume(connectionId, rateLimitBytes, currentTime))
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
                    || !_replayGuard.TryAccept(
                        connection.ConnectionId,
                        envelope.MessageId,
                        envelope.Sequence,
                        currentTime))
                {
                    return Reject(connection, envelope, MessageSecurityResult.ReplayRejected, currentTime, "Message sequence was rejected by replay protection.");
                }
            }

            return NetworkSecurityPipelineResult.Accept();
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
            _replayGuard.RemoveConnection(connectionId);
        }

        public void ClearState()
        {
            _rateLimiter?.Clear();
            _replayGuard.Clear();
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
