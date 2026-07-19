using System;
using System.Collections.Generic;

namespace CycloneGames.InputSystem.Runtime
{
    public enum InputManagerInitializationStatus
    {
        Success = 0,
        EmptyContent,
        NotMainThread,
        ActivePlayers,
        Disposed,
        ParseFailed,
        ValidationFailed,
        InputSystemPreflightFailed,
        JoinInProgress,
        ConfigurationOperationInProgress
    }

    public enum InputConfigurationPreflightStatus
    {
        Success = 0,
        NotMainThread,
        Failed
    }

    public enum InputConfigurationPreflightIssueCode
    {
        SchemaValidationFailed = 0,
        InvalidControlPath,
        UnknownControlLayout,
        IncompatibleControlLayout,
        UnknownInteraction,
        InvalidInteractionParameters,
        UnknownProcessor,
        InvalidProcessorParameters,
        UnknownComposite,
        InvalidCompositeParameters,
        InvalidCompositePart,
        UnsupportedPlacement,
        InvalidControlScheme,
        ConstructionFailed,
        ResolutionFailed,
        CleanupFailed
    }

    public sealed class InputConfigurationPreflightIssue
    {
        internal InputConfigurationPreflightIssue(
            InputConfigurationPreflightIssueCode code,
            string path,
            string message)
        {
            Code = code;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public InputConfigurationPreflightIssueCode Code { get; }
        public string Path { get; }
        public string Message { get; }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Path) ? Message : $"{Path}: {Message}";
        }
    }

    public sealed class InputConfigurationPreflightResult
    {
        internal InputConfigurationPreflightResult(
            InputConfigurationPreflightStatus status,
            List<InputConfigurationPreflightIssue> issues,
            bool wasTruncated = false)
        {
            Status = status;
            Issues = (issues ?? new List<InputConfigurationPreflightIssue>()).AsReadOnly();
            WasTruncated = wasTruncated;
        }

        public bool IsSuccess => Status == InputConfigurationPreflightStatus.Success;
        public InputConfigurationPreflightStatus Status { get; }
        public IReadOnlyList<InputConfigurationPreflightIssue> Issues { get; }
        public bool WasTruncated { get; }
    }

    public sealed class InputManagerInitializationResult
    {
        internal InputManagerInitializationResult(
            InputManagerInitializationStatus status,
            string message,
            InputConfigurationValidationResult validation,
            InputConfigurationPreflightResult preflight = null)
        {
            Status = status;
            Message = message ?? string.Empty;
            Validation = validation;
            Preflight = preflight;
        }

        public bool IsSuccess => Status == InputManagerInitializationStatus.Success;
        public InputManagerInitializationStatus Status { get; }
        public string Message { get; }
        public InputConfigurationValidationResult Validation { get; }
        public InputConfigurationPreflightResult Preflight { get; }
        public bool WasMigrated => Validation != null && Validation.WasMigrated;
    }

    /// <summary>
    /// Versioned, storage-agnostic per-player Unity binding override profile.
    /// </summary>
    [Serializable]
    public sealed class InputBindingOverrideProfile
    {
        public const int CurrentSchemaVersion = 1;
        public int SchemaVersion = CurrentSchemaVersion;
        public List<InputBindingOverrideEntry> Players = new List<InputBindingOverrideEntry>();
    }

    [Serializable]
    public sealed class InputBindingOverrideEntry
    {
        public int PlayerId;
        public string OverridesJson;
    }
}
