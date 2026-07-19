using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Security
{
    public enum NetworkAuthorityValidationStatus : byte
    {
        Invalid = 0,
        Accepted = 1,
        Rejected = 2,
        Corrected = 3,
        Deferred = 4
    }

    public readonly struct NetworkAuthorityValidationContext
    {
        public readonly int ServerTick;
        public readonly double ServerTimeSeconds;
        public readonly string RuleSetId;
        public readonly NetworkRuntimeProfile RuntimeProfile;

        public NetworkAuthorityValidationContext(
            int serverTick,
            double serverTimeSeconds,
            string ruleSetId = "",
            NetworkRuntimeProfile runtimeProfile = null)
        {
            ServerTick = serverTick;
            ServerTimeSeconds = serverTimeSeconds;
            RuleSetId = ruleSetId ?? string.Empty;
            RuntimeProfile = runtimeProfile;
        }
    }

    public readonly struct NetworkAuthorityValidationResult
    {
        public readonly NetworkAuthorityValidationStatus Status;
        public readonly NetworkReadinessSeverity Severity;
        public readonly string Reason;

        public NetworkAuthorityValidationResult(
            NetworkAuthorityValidationStatus status,
            NetworkReadinessSeverity severity = NetworkReadinessSeverity.Info,
            string reason = "")
        {
            Status = status;
            Severity = severity;
            Reason = reason ?? string.Empty;
        }

        public bool IsAccepted
        {
            get
            {
                return Status == NetworkAuthorityValidationStatus.Accepted
                       || Status == NetworkAuthorityValidationStatus.Corrected;
            }
        }

        public static NetworkAuthorityValidationResult Accept()
        {
            return new NetworkAuthorityValidationResult(NetworkAuthorityValidationStatus.Accepted);
        }

        public static NetworkAuthorityValidationResult Reject(string reason, NetworkReadinessSeverity severity = NetworkReadinessSeverity.Warning)
        {
            return new NetworkAuthorityValidationResult(NetworkAuthorityValidationStatus.Rejected, severity, reason);
        }

        public static NetworkAuthorityValidationResult Correct(string reason = "")
        {
            return new NetworkAuthorityValidationResult(NetworkAuthorityValidationStatus.Corrected, NetworkReadinessSeverity.Info, reason);
        }

        public static NetworkAuthorityValidationResult Defer(string reason = "")
        {
            return new NetworkAuthorityValidationResult(NetworkAuthorityValidationStatus.Deferred, NetworkReadinessSeverity.Info, reason);
        }
    }

    public interface INetworkAuthoritativeValidator<TCommand, TState>
    {
        NetworkAuthorityValidationResult Validate(
            INetConnection connection,
            in TCommand command,
            in TState serverState,
            in NetworkAuthorityValidationContext context);
    }

    public sealed class AcceptAllNetworkAuthoritativeValidator<TCommand, TState> : INetworkAuthoritativeValidator<TCommand, TState>
    {
        public static readonly AcceptAllNetworkAuthoritativeValidator<TCommand, TState> Instance =
            new AcceptAllNetworkAuthoritativeValidator<TCommand, TState>();

        private AcceptAllNetworkAuthoritativeValidator()
        {
        }

        public NetworkAuthorityValidationResult Validate(
            INetConnection connection,
            in TCommand command,
            in TState serverState,
            in NetworkAuthorityValidationContext context)
        {
            return NetworkAuthorityValidationResult.Accept();
        }
    }

    public sealed class CompositeNetworkAuthoritativeValidator<TCommand, TState> : INetworkAuthoritativeValidator<TCommand, TState>
    {
        private readonly List<INetworkAuthoritativeValidator<TCommand, TState>> _validators;

        public CompositeNetworkAuthoritativeValidator(int capacity = 4)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _validators = new List<INetworkAuthoritativeValidator<TCommand, TState>>(capacity);
        }

        public int Count
        {
            get
            {
                return _validators.Count;
            }
        }

        public CompositeNetworkAuthoritativeValidator<TCommand, TState> Add(INetworkAuthoritativeValidator<TCommand, TState> validator)
        {
            if (validator == null)
            {
                throw new ArgumentNullException(nameof(validator));
            }

            _validators.Add(validator);
            return this;
        }

        public NetworkAuthorityValidationResult Validate(
            INetConnection connection,
            in TCommand command,
            in TState serverState,
            in NetworkAuthorityValidationContext context)
        {
            for (int i = 0; i < _validators.Count; i++)
            {
                NetworkAuthorityValidationResult result = _validators[i].Validate(connection, command, serverState, context);
                if (!result.IsAccepted)
                {
                    return result;
                }
            }

            return NetworkAuthorityValidationResult.Accept();
        }
    }
}
