using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using VYaml.Serialization;

namespace CycloneGames.InputSystem.Runtime
{
    public enum InputConfigurationIssueCode
    {
        None = 0,
        NullConfiguration,
        UnsupportedSchemaVersion,
        NullCollection,
        LimitExceeded,
        InvalidValue,
        DuplicatePlayerId,
        DuplicateContext,
        DuplicateActionIdentity,
        InvalidActionId,
        ActionIdCollision
    }

    public readonly struct InputConfigurationIssue
    {
        public InputConfigurationIssue(InputConfigurationIssueCode code, string path, string message)
        {
            Code = code;
            Path = path ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public InputConfigurationIssueCode Code { get; }
        public string Path { get; }
        public string Message { get; }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Path) ? Message : $"{Path}: {Message}";
        }
    }

    /// <summary>
    /// Bounds configuration-driven allocation and iteration before runtime objects are created.
    /// </summary>
    public sealed class InputConfigurationLimits
    {
        public static InputConfigurationLimits Default { get; } = new InputConfigurationLimits();

        public InputConfigurationLimits(
            int maxPlayers = 8,
            int maxContextsPerPlayer = 32,
            int maxActionsPerContext = 128,
            int maxBindingsPerAction = 16,
            int maxTotalActionsPerPlayer = 1024,
            int maxStringLength = 256,
            int maxLongPressMilliseconds = 600000,
            int minContextPriority = -100000,
            int maxContextPriority = 100000,
            int maxControlSchemesPerPlayer = 16,
            int maxDeviceRequirementsPerScheme = 16,
            int maxCompositesPerAction = 16,
            int maxPartsPerComposite = 16)
        {
            if (maxPlayers <= 0) throw new ArgumentOutOfRangeException(nameof(maxPlayers));
            if (maxContextsPerPlayer <= 0) throw new ArgumentOutOfRangeException(nameof(maxContextsPerPlayer));
            if (maxActionsPerContext <= 0) throw new ArgumentOutOfRangeException(nameof(maxActionsPerContext));
            if (maxBindingsPerAction <= 0) throw new ArgumentOutOfRangeException(nameof(maxBindingsPerAction));
            if (maxTotalActionsPerPlayer <= 0) throw new ArgumentOutOfRangeException(nameof(maxTotalActionsPerPlayer));
            if (maxStringLength <= 0) throw new ArgumentOutOfRangeException(nameof(maxStringLength));
            if (maxLongPressMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(maxLongPressMilliseconds));
            if (minContextPriority > maxContextPriority) throw new ArgumentOutOfRangeException(nameof(minContextPriority));
            if (maxControlSchemesPerPlayer <= 0) throw new ArgumentOutOfRangeException(nameof(maxControlSchemesPerPlayer));
            if (maxDeviceRequirementsPerScheme <= 0) throw new ArgumentOutOfRangeException(nameof(maxDeviceRequirementsPerScheme));
            if (maxCompositesPerAction <= 0) throw new ArgumentOutOfRangeException(nameof(maxCompositesPerAction));
            if (maxPartsPerComposite <= 0) throw new ArgumentOutOfRangeException(nameof(maxPartsPerComposite));

            MaxPlayers = maxPlayers;
            MaxContextsPerPlayer = maxContextsPerPlayer;
            MaxActionsPerContext = maxActionsPerContext;
            MaxBindingsPerAction = maxBindingsPerAction;
            MaxTotalActionsPerPlayer = maxTotalActionsPerPlayer;
            MaxStringLength = maxStringLength;
            MaxLongPressMilliseconds = maxLongPressMilliseconds;
            MinContextPriority = minContextPriority;
            MaxContextPriority = maxContextPriority;
            MaxControlSchemesPerPlayer = maxControlSchemesPerPlayer;
            MaxDeviceRequirementsPerScheme = maxDeviceRequirementsPerScheme;
            MaxCompositesPerAction = maxCompositesPerAction;
            MaxPartsPerComposite = maxPartsPerComposite;
        }

        public int MaxPlayers { get; }
        public int MaxContextsPerPlayer { get; }
        public int MaxActionsPerContext { get; }
        public int MaxBindingsPerAction { get; }
        public int MaxTotalActionsPerPlayer { get; }
        public int MaxStringLength { get; }
        public int MaxLongPressMilliseconds { get; }
        public int MinContextPriority { get; }
        public int MaxContextPriority { get; }
        public int MaxControlSchemesPerPlayer { get; }
        public int MaxDeviceRequirementsPerScheme { get; }
        public int MaxCompositesPerAction { get; }
        public int MaxPartsPerComposite { get; }
    }

    public sealed class InputConfigurationValidationResult
    {
        internal InputConfigurationValidationResult(
            InputConfiguration configuration,
            RuntimeInputConfiguration runtimeConfiguration,
            bool wasMigrated,
            List<InputConfigurationIssue> issues)
        {
            Configuration = configuration;
            RuntimeConfiguration = runtimeConfiguration;
            WasMigrated = wasMigrated;
            Issues = issues.AsReadOnly();
        }

        public bool IsValid => Issues.Count == 0 && RuntimeConfiguration != null;
        public bool WasMigrated { get; }

        /// <summary>
        /// Deep-cloned, migrated DTO suitable for persistence. It is never the caller-owned instance.
        /// </summary>
        public InputConfiguration Configuration { get; }

        /// <summary>
        /// Immutable runtime snapshot created only after validation succeeds.
        /// </summary>
        internal RuntimeInputConfiguration RuntimeConfiguration { get; }

        public IReadOnlyList<InputConfigurationIssue> Issues { get; }
    }

    internal sealed class RuntimeInputConfiguration
    {
        internal RuntimeInputConfiguration(InputConfiguration source)
        {
            SchemaVersion = source.SchemaVersion;
            SchemaFingerprint = source.SchemaFingerprint;
            JoinAction = source.JoinAction == null ? null : new RuntimeActionBindingConfig(source.JoinAction);

            var slots = new RuntimePlayerSlotConfig[source.PlayerSlots.Count];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = new RuntimePlayerSlotConfig(source.PlayerSlots[i]);
            }

            PlayerSlots = Array.AsReadOnly(slots);
        }

        public int SchemaVersion { get; }
        public string SchemaFingerprint { get; }
        public IReadOnlyList<RuntimePlayerSlotConfig> PlayerSlots { get; }
        public RuntimeActionBindingConfig JoinAction { get; }
    }

    internal sealed class RuntimePlayerSlotConfig
    {
        internal RuntimePlayerSlotConfig(PlayerSlotConfig source)
        {
            PlayerId = source.PlayerId;
            JoinAction = source.JoinAction == null ? null : new RuntimeActionBindingConfig(source.JoinAction);
            DefaultControlScheme = source.DefaultControlScheme;

            var contexts = new RuntimeContextDefinitionConfig[source.Contexts.Count];
            for (int i = 0; i < contexts.Length; i++)
            {
                contexts[i] = new RuntimeContextDefinitionConfig(source.Contexts[i]);
            }

            Contexts = Array.AsReadOnly(contexts);

            int schemeCount = source.ControlSchemes == null ? 0 : source.ControlSchemes.Count;
            var schemes = new RuntimeControlSchemeConfig[schemeCount];
            for (int i = 0; i < schemeCount; i++)
            {
                schemes[i] = new RuntimeControlSchemeConfig(source.ControlSchemes[i]);
            }

            ControlSchemes = Array.AsReadOnly(schemes);
        }

        public int PlayerId { get; }
        public RuntimeActionBindingConfig JoinAction { get; }
        public IReadOnlyList<RuntimeContextDefinitionConfig> Contexts { get; }
        public IReadOnlyList<RuntimeControlSchemeConfig> ControlSchemes { get; }
        public string DefaultControlScheme { get; }
    }

    internal sealed class RuntimeContextDefinitionConfig
    {
        internal RuntimeContextDefinitionConfig(ContextDefinitionConfig source)
        {
            Name = source.Name;
            ActionMap = source.ActionMap;
            Priority = source.Priority;
            BlocksLowerPriority = source.BlocksLowerPriority;

            var bindings = new RuntimeActionBindingConfig[source.Bindings.Count];
            for (int i = 0; i < bindings.Length; i++)
            {
                bindings[i] = new RuntimeActionBindingConfig(source.Bindings[i]);
            }

            Bindings = Array.AsReadOnly(bindings);
        }

        public string Name { get; }
        public string ActionMap { get; }
        public int Priority { get; }
        public bool BlocksLowerPriority { get; }
        public IReadOnlyList<RuntimeActionBindingConfig> Bindings { get; }
    }

    internal sealed class RuntimeActionBindingConfig
    {
        internal RuntimeActionBindingConfig(ActionBindingConfig source)
        {
            Type = source.Type;
            ActionName = source.ActionName;
            ExpectedControlType = source.ExpectedControlType;
            Interactions = source.Interactions;
            Processors = source.Processors;
            BindingGroups = source.BindingGroups;
            UpdateMode = source.UpdateMode;
            LongPressMs = source.LongPressMs;
            LongPressValueThreshold = source.LongPressValueThreshold;

            var paths = source.DeviceBindings == null ? Array.Empty<string>() : source.DeviceBindings.ToArray();
            DeviceBindings = Array.AsReadOnly(paths);

            int compositeCount = source.CompositeBindings == null ? 0 : source.CompositeBindings.Count;
            var composites = new RuntimeCompositeBindingConfig[compositeCount];
            for (int i = 0; i < compositeCount; i++)
            {
                composites[i] = new RuntimeCompositeBindingConfig(source.CompositeBindings[i]);
            }

            CompositeBindings = Array.AsReadOnly(composites);
        }

        public ActionValueType Type { get; }
        public string ActionName { get; }
        public IReadOnlyList<string> DeviceBindings { get; }
        public IReadOnlyList<RuntimeCompositeBindingConfig> CompositeBindings { get; }
        public string ExpectedControlType { get; }
        public string Interactions { get; }
        public string Processors { get; }
        public string BindingGroups { get; }
        public InputUpdateMode UpdateMode { get; }
        public int LongPressMs { get; }
        public float LongPressValueThreshold { get; }
    }

    internal sealed class RuntimeControlSchemeConfig
    {
        internal RuntimeControlSchemeConfig(ControlSchemeConfig source)
        {
            Name = source.Name;
            BindingGroup = source.BindingGroup;
            var requirements = new RuntimeControlSchemeDeviceRequirementConfig[source.DeviceRequirements.Count];
            for (int i = 0; i < requirements.Length; i++)
            {
                requirements[i] = new RuntimeControlSchemeDeviceRequirementConfig(source.DeviceRequirements[i]);
            }

            DeviceRequirements = Array.AsReadOnly(requirements);
        }

        public string Name { get; }
        public string BindingGroup { get; }
        public IReadOnlyList<RuntimeControlSchemeDeviceRequirementConfig> DeviceRequirements { get; }
    }

    internal readonly struct RuntimeControlSchemeDeviceRequirementConfig
    {
        internal RuntimeControlSchemeDeviceRequirementConfig(ControlSchemeDeviceRequirementConfig source)
        {
            ControlPath = source.ControlPath;
            IsOptional = source.IsOptional;
            IsOr = source.IsOr;
        }

        public string ControlPath { get; }
        public bool IsOptional { get; }
        public bool IsOr { get; }
    }

    internal sealed class RuntimeCompositeBindingConfig
    {
        internal RuntimeCompositeBindingConfig(CompositeBindingConfig source)
        {
            Name = source.Name;
            Parameters = source.Parameters;
            BindingGroups = source.BindingGroups;
            var parts = new RuntimeCompositePartBindingConfig[source.Parts.Count];
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = new RuntimeCompositePartBindingConfig(source.Parts[i]);
            }

            Parts = Array.AsReadOnly(parts);
        }

        public string Name { get; }
        public string Parameters { get; }
        public string BindingGroups { get; }
        public IReadOnlyList<RuntimeCompositePartBindingConfig> Parts { get; }
    }

    internal readonly struct RuntimeCompositePartBindingConfig
    {
        internal RuntimeCompositePartBindingConfig(CompositePartBindingConfig source)
        {
            Name = source.Name;
            Path = source.Path;
            Processors = source.Processors;
            Interactions = source.Interactions;
        }

        public string Name { get; }
        public string Path { get; }
        public string Processors { get; }
        public string Interactions { get; }
    }

    public static class InputConfigurationValidator
    {
        private const int MaxValidationIssueCount = 256;

        public static InputConfigurationValidationResult ValidateAndPrepare(
            InputConfiguration source,
            InputConfigurationLimits limits = null)
        {
            limits ??= InputConfigurationLimits.Default;
            var issues = new List<InputConfigurationIssue>();
            if (source == null)
            {
                issues.Add(new InputConfigurationIssue(
                    InputConfigurationIssueCode.NullConfiguration,
                    string.Empty,
                    "Configuration is null."));
                return new InputConfigurationValidationResult(null, null, false, issues);
            }

            if (!TryPreflightShape(source, limits, out InputConfigurationIssue shapeIssue))
            {
                issues.Add(shapeIssue);
                return new InputConfigurationValidationResult(null, null, false, issues);
            }

            InputConfiguration clone = InputConfigurationCloner.DeepClone(source);
            bool wasMigrated = false;
            if (clone.SchemaVersion == 0)
            {
                MigrateLegacyInlineComposites(clone, limits);
                clone.SchemaVersion = InputConfiguration.CurrentSchemaVersion;
                wasMigrated = true;

                if (!TryPreflightShape(clone, limits, out InputConfigurationIssue migratedShapeIssue))
                {
                    issues.Add(migratedShapeIssue);
                    return new InputConfigurationValidationResult(clone, null, true, issues);
                }
            }
            else if (clone.SchemaVersion < 0 || clone.SchemaVersion > InputConfiguration.CurrentSchemaVersion)
            {
                Add(issues, InputConfigurationIssueCode.UnsupportedSchemaVersion, "schemaVersion",
                    $"Schema version {clone.SchemaVersion} is not supported. Current version is {InputConfiguration.CurrentSchemaVersion}.");
            }

            ValidateConfiguration(clone, limits, issues);
            RuntimeInputConfiguration runtime = issues.Count == 0 ? new RuntimeInputConfiguration(clone) : null;
            return new InputConfigurationValidationResult(clone, runtime, wasMigrated, issues);
        }

        private static void MigrateLegacyInlineComposites(
            InputConfiguration configuration,
            InputConfigurationLimits limits)
        {
            MigrateLegacyInlineComposites(configuration.JoinAction, limits);
            List<PlayerSlotConfig> players = configuration.PlayerSlots;
            if (players == null) return;
            for (int playerIndex = 0; playerIndex < players.Count; playerIndex++)
            {
                PlayerSlotConfig player = players[playerIndex];
                if (player == null) continue;
                MigrateLegacyInlineComposites(player.JoinAction, limits);
                List<ContextDefinitionConfig> contexts = player.Contexts;
                if (contexts == null) continue;
                for (int contextIndex = 0; contextIndex < contexts.Count; contextIndex++)
                {
                    List<ActionBindingConfig> actions = contexts[contextIndex]?.Bindings;
                    if (actions == null) continue;
                    for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
                    {
                        MigrateLegacyInlineComposites(actions[actionIndex], limits);
                    }
                }
            }
        }

        private static void MigrateLegacyInlineComposites(
            ActionBindingConfig action,
            InputConfigurationLimits limits)
        {
            List<string> bindings = action?.DeviceBindings;
            if (bindings == null || bindings.Count == 0) return;

            List<string> retainedBindings = null;
            for (int bindingIndex = 0; bindingIndex < bindings.Count; bindingIndex++)
            {
                string binding = bindings[bindingIndex];
                if (!TryParseLegacyInline2DVector(binding, limits.MaxStringLength, out CompositeBindingConfig composite))
                {
                    retainedBindings?.Add(binding);
                    continue;
                }

                if (retainedBindings == null)
                {
                    retainedBindings = new List<string>(bindings.Count - 1);
                    for (int previousIndex = 0; previousIndex < bindingIndex; previousIndex++)
                    {
                        retainedBindings.Add(bindings[previousIndex]);
                    }
                }

                action.CompositeBindings ??= new List<CompositeBindingConfig>();
                action.CompositeBindings.Add(composite);
            }

            if (retainedBindings != null) action.DeviceBindings = retainedBindings;
        }

        private static bool TryParseLegacyInline2DVector(
            string binding,
            int maxStringLength,
            out CompositeBindingConfig composite)
        {
            composite = null;
            const string prefix = "2DVector(";
            if (string.IsNullOrEmpty(binding) || binding.Length > maxStringLength ||
                !binding.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !binding.EndsWith(")", StringComparison.Ordinal))
                return false;

            string mode = null;
            var parts = new List<CompositePartBindingConfig>(4);
            int seenNames = 0;
            int cursor = prefix.Length;
            int contentEnd = binding.Length - 1;
            int segmentCount = 0;
            while (cursor < contentEnd)
            {
                if (++segmentCount > 5) return false;
                int commaIndex = binding.IndexOf(',', cursor, contentEnd - cursor);
                int segmentEnd = commaIndex < 0 ? contentEnd : commaIndex;
                int segmentStart = cursor;
                TrimRange(binding, ref segmentStart, ref segmentEnd);
                if (segmentStart >= segmentEnd) return false;

                int equalsIndex = binding.IndexOf('=', segmentStart, segmentEnd - segmentStart);
                if (equalsIndex <= segmentStart || equalsIndex >= segmentEnd - 1 ||
                    binding.IndexOf('=', equalsIndex + 1, segmentEnd - equalsIndex - 1) >= 0)
                    return false;

                int nameStart = segmentStart;
                int nameEnd = equalsIndex;
                int valueStart = equalsIndex + 1;
                int valueEnd = segmentEnd;
                TrimRange(binding, ref nameStart, ref nameEnd);
                TrimRange(binding, ref valueStart, ref valueEnd);
                if (nameStart >= nameEnd || valueStart >= valueEnd) return false;

                int nameFlag;
                string normalizedName = null;
                if (RangeEquals(binding, nameStart, nameEnd, "mode")) nameFlag = 1;
                else if (RangeEquals(binding, nameStart, nameEnd, "up")) { nameFlag = 2; normalizedName = "up"; }
                else if (RangeEquals(binding, nameStart, nameEnd, "down")) { nameFlag = 4; normalizedName = "down"; }
                else if (RangeEquals(binding, nameStart, nameEnd, "left")) { nameFlag = 8; normalizedName = "left"; }
                else if (RangeEquals(binding, nameStart, nameEnd, "right")) { nameFlag = 16; normalizedName = "right"; }
                else return false;
                if ((seenNames & nameFlag) != 0) return false;
                seenNames |= nameFlag;

                string value = binding.Substring(valueStart, valueEnd - valueStart);
                if (nameFlag == 1)
                {
                    mode = value;
                }
                else
                {
                    parts.Add(new CompositePartBindingConfig { Name = normalizedName, Path = value });
                }

                if (commaIndex < 0) break;
                cursor = commaIndex + 1;
                if (cursor >= contentEnd) return false;
            }

            if (parts.Count == 0) return false;
            composite = new CompositeBindingConfig
            {
                Name = "2DVector",
                Parameters = mode == null ? null : "mode=" + mode,
                Parts = parts
            };
            return true;
        }

        private static void TrimRange(string value, ref int start, ref int end)
        {
            while (start < end && char.IsWhiteSpace(value[start])) start++;
            while (end > start && char.IsWhiteSpace(value[end - 1])) end--;
        }

        private static bool RangeEquals(string value, int start, int end, string expected)
        {
            int length = end - start;
            return length == expected.Length &&
                   string.Compare(value, start, expected, 0, length, StringComparison.OrdinalIgnoreCase) == 0;
        }

        private static bool TryPreflightShape(
            InputConfiguration configuration,
            InputConfigurationLimits limits,
            out InputConfigurationIssue issue)
        {
            issue = default;
            List<PlayerSlotConfig> players = configuration.PlayerSlots;
            if (players != null && players.Count > limits.MaxPlayers)
            {
                return FailShapeLimit(
                    out issue,
                    "playerSlots",
                    $"Player slot count exceeds the limit of {limits.MaxPlayers}.");
            }

            if (configuration.JoinAction != null &&
                !TryPreflightActionShape(configuration.JoinAction, limits, -1, -1, -1, true, out issue))
            {
                return false;
            }

            if (players == null)
            {
                return true;
            }

            for (int playerIndex = 0; playerIndex < players.Count; playerIndex++)
            {
                PlayerSlotConfig player = players[playerIndex];
                if (player == null)
                {
                    continue;
                }

                List<ControlSchemeConfig> controlSchemes = player.ControlSchemes;
                if (controlSchemes != null)
                {
                    if (controlSchemes.Count > limits.MaxControlSchemesPerPlayer)
                    {
                        return FailShapeLimit(
                            out issue,
                            $"playerSlots[{playerIndex}].controlSchemes",
                            $"Control scheme count exceeds the limit of {limits.MaxControlSchemesPerPlayer}.");
                    }

                    for (int schemeIndex = 0; schemeIndex < controlSchemes.Count; schemeIndex++)
                    {
                        ControlSchemeConfig scheme = controlSchemes[schemeIndex];
                        List<ControlSchemeDeviceRequirementConfig> requirements = scheme?.DeviceRequirements;
                        if (requirements != null && requirements.Count > limits.MaxDeviceRequirementsPerScheme)
                        {
                            return FailShapeLimit(
                                out issue,
                                $"playerSlots[{playerIndex}].controlSchemes[{schemeIndex}].deviceRequirements",
                                $"Device requirement count exceeds the limit of {limits.MaxDeviceRequirementsPerScheme}.");
                        }
                    }
                }

                if (player.JoinAction != null &&
                    !TryPreflightActionShape(player.JoinAction, limits, playerIndex, -1, -1, true, out issue))
                {
                    return false;
                }

                List<ContextDefinitionConfig> contexts = player.Contexts;
                if (contexts == null)
                {
                    continue;
                }

                if (contexts.Count > limits.MaxContextsPerPlayer)
                {
                    return FailShapeLimit(
                        out issue,
                        $"playerSlots[{playerIndex}].contexts",
                        $"Context count exceeds the limit of {limits.MaxContextsPerPlayer}.");
                }

                int totalActions = 0;
                for (int contextIndex = 0; contextIndex < contexts.Count; contextIndex++)
                {
                    ContextDefinitionConfig context = contexts[contextIndex];
                    List<ActionBindingConfig> actions = context?.Bindings;
                    if (actions == null)
                    {
                        continue;
                    }

                    if (actions.Count > limits.MaxActionsPerContext)
                    {
                        return FailShapeLimit(
                            out issue,
                            $"playerSlots[{playerIndex}].contexts[{contextIndex}].bindings",
                            $"Action count exceeds the per-context limit of {limits.MaxActionsPerContext}.");
                    }

                    if (actions.Count > limits.MaxTotalActionsPerPlayer - totalActions)
                    {
                        return FailShapeLimit(
                            out issue,
                            $"playerSlots[{playerIndex}].contexts",
                            $"Total action count exceeds the per-player limit of {limits.MaxTotalActionsPerPlayer}.");
                    }

                    totalActions += actions.Count;
                    for (int actionIndex = 0; actionIndex < actions.Count; actionIndex++)
                    {
                        ActionBindingConfig action = actions[actionIndex];
                        if (action != null &&
                            !TryPreflightActionShape(
                                action,
                                limits,
                                playerIndex,
                                contextIndex,
                                actionIndex,
                                false,
                                out issue))
                        {
                            return false;
                        }
                    }
                }
            }

            return true;
        }

        private static bool TryPreflightActionShape(
            ActionBindingConfig action,
            InputConfigurationLimits limits,
            int playerIndex,
            int contextIndex,
            int actionIndex,
            bool isJoinAction,
            out InputConfigurationIssue issue)
        {
            issue = default;
            int directBindingCount = action.DeviceBindings?.Count ?? 0;
            if (directBindingCount > limits.MaxBindingsPerAction)
            {
                return FailShapeLimit(
                    out issue,
                    GetActionShapePath(playerIndex, contextIndex, actionIndex, isJoinAction) + ".deviceBindings",
                    $"Binding count exceeds the per-action limit of {limits.MaxBindingsPerAction}.");
            }

            List<CompositeBindingConfig> composites = action.CompositeBindings;
            if (composites == null)
            {
                return true;
            }

            if (composites.Count > limits.MaxCompositesPerAction)
            {
                return FailShapeLimit(
                    out issue,
                    GetActionShapePath(playerIndex, contextIndex, actionIndex, isJoinAction) + ".compositeBindings",
                    $"Composite count exceeds the limit of {limits.MaxCompositesPerAction}.");
            }

            int totalParts = 0;
            int remainingBindingCapacity = limits.MaxBindingsPerAction - directBindingCount;
            for (int compositeIndex = 0; compositeIndex < composites.Count; compositeIndex++)
            {
                CompositeBindingConfig composite = composites[compositeIndex];
                List<CompositePartBindingConfig> parts = composite?.Parts;
                if (parts == null)
                {
                    continue;
                }

                if (parts.Count > limits.MaxPartsPerComposite)
                {
                    return FailShapeLimit(
                        out issue,
                        GetActionShapePath(playerIndex, contextIndex, actionIndex, isJoinAction) +
                        $".compositeBindings[{compositeIndex}].parts",
                        $"Composite part count exceeds the limit of {limits.MaxPartsPerComposite}.");
                }

                if (parts.Count > remainingBindingCapacity - totalParts)
                {
                    return FailShapeLimit(
                        out issue,
                        GetActionShapePath(playerIndex, contextIndex, actionIndex, isJoinAction),
                        $"Total direct and composite-part binding count exceeds the limit of {limits.MaxBindingsPerAction}.");
                }

                totalParts += parts.Count;
            }

            return true;
        }

        private static string GetActionShapePath(
            int playerIndex,
            int contextIndex,
            int actionIndex,
            bool isJoinAction)
        {
            if (playerIndex < 0)
            {
                return "joinAction";
            }

            if (isJoinAction)
            {
                return $"playerSlots[{playerIndex}].joinAction";
            }

            return $"playerSlots[{playerIndex}].contexts[{contextIndex}].bindings[{actionIndex}]";
        }

        private static bool FailShapeLimit(
            out InputConfigurationIssue issue,
            string path,
            string message)
        {
            issue = new InputConfigurationIssue(InputConfigurationIssueCode.LimitExceeded, path, message);
            return false;
        }

        private static void ValidateConfiguration(
            InputConfiguration configuration,
            InputConfigurationLimits limits,
            List<InputConfigurationIssue> issues)
        {
            ValidateOptionalString(configuration.SchemaFingerprint, "schemaFingerprint", limits, issues);
            if (configuration.PlayerSlots == null)
            {
                Add(issues, InputConfigurationIssueCode.NullCollection, "playerSlots", "Player slots collection is null.");
                return;
            }

            if (configuration.PlayerSlots.Count > limits.MaxPlayers)
            {
                Add(issues, InputConfigurationIssueCode.LimitExceeded, "playerSlots",
                    $"Player slot count exceeds the limit of {limits.MaxPlayers}.");
            }

            if (configuration.JoinAction != null)
            {
                ValidateJoinAction(configuration.JoinAction, "joinAction", limits, issues, null);
            }

            var playerIds = new HashSet<int>();
            int playerCount = configuration.PlayerSlots.Count;
            for (int playerIndex = 0; playerIndex < playerCount; playerIndex++)
            {
                string playerPath = $"playerSlots[{playerIndex}]";
                PlayerSlotConfig player = configuration.PlayerSlots[playerIndex];
                if (player == null)
                {
                    Add(issues, InputConfigurationIssueCode.InvalidValue, playerPath, "Player slot is null.");
                    continue;
                }

                if (!playerIds.Add(player.PlayerId))
                {
                    Add(issues, InputConfigurationIssueCode.DuplicatePlayerId, $"{playerPath}.playerId",
                        $"Player ID {player.PlayerId} is duplicated.");
                }
                if (player.PlayerId < 0)
                {
                    Add(issues, InputConfigurationIssueCode.InvalidValue, $"{playerPath}.playerId",
                        "Player ID must be non-negative.");
                }

                if (player.JoinAction != null)
                {
                    // Validated below with the player's declared binding groups.
                }

                ValidatePlayer(player, playerPath, limits, issues);
            }
        }

        private static void ValidatePlayer(
            PlayerSlotConfig player,
            string playerPath,
            InputConfigurationLimits limits,
            List<InputConfigurationIssue> issues)
        {
            HashSet<string> controlSchemeGroups = ValidateControlSchemes(player, playerPath, limits, issues);
            if (player.JoinAction != null)
            {
                ValidateJoinAction(player.JoinAction, $"{playerPath}.joinAction", limits, issues, controlSchemeGroups);
            }

            if (player.Contexts == null)
            {
                Add(issues, InputConfigurationIssueCode.NullCollection, $"{playerPath}.contexts", "Contexts collection is null.");
                return;
            }

            if (player.Contexts.Count > limits.MaxContextsPerPlayer)
            {
                Add(issues, InputConfigurationIssueCode.LimitExceeded, $"{playerPath}.contexts",
                    $"Context count exceeds the limit of {limits.MaxContextsPerPlayer}.");
            }

            var contextNames = new HashSet<string>(StringComparer.Ordinal);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var actionIds = new Dictionary<int, string>();
            int totalActions = 0;
            int contextCount = player.Contexts.Count;
            for (int contextIndex = 0; contextIndex < contextCount; contextIndex++)
            {
                string contextPath = $"{playerPath}.contexts[{contextIndex}]";
                ContextDefinitionConfig context = player.Contexts[contextIndex];
                if (context == null)
                {
                    Add(issues, InputConfigurationIssueCode.InvalidValue, contextPath, "Context is null.");
                    continue;
                }

                bool validName = ValidateRequiredString(context.Name, $"{contextPath}.name", limits, issues);
                bool validMap = ValidateRequiredString(context.ActionMap, $"{contextPath}.actionMap", limits, issues);
                if (validName && !contextNames.Add(context.Name))
                {
                    Add(issues, InputConfigurationIssueCode.DuplicateContext, $"{contextPath}.name",
                        $"Context name '{context.Name}' is duplicated for this player.");
                }

                if (context.Priority < limits.MinContextPriority || context.Priority > limits.MaxContextPriority)
                {
                    Add(issues, InputConfigurationIssueCode.LimitExceeded, $"{contextPath}.priority",
                        $"Priority must be between {limits.MinContextPriority} and {limits.MaxContextPriority}.");
                }

                if (context.Bindings == null)
                {
                    Add(issues, InputConfigurationIssueCode.NullCollection, $"{contextPath}.bindings", "Bindings collection is null.");
                    continue;
                }

                if (context.Bindings.Count > limits.MaxActionsPerContext)
                {
                    Add(issues, InputConfigurationIssueCode.LimitExceeded, $"{contextPath}.bindings",
                        $"Action count exceeds the per-context limit of {limits.MaxActionsPerContext}.");
                }

                totalActions += context.Bindings.Count;
                int actionCount = context.Bindings.Count;
                for (int actionIndex = 0; actionIndex < actionCount; actionIndex++)
                {
                    string actionPath = $"{contextPath}.bindings[{actionIndex}]";
                    ActionBindingConfig action = context.Bindings[actionIndex];
                    if (action == null)
                    {
                        Add(issues, InputConfigurationIssueCode.InvalidValue, actionPath, "Action binding is null.");
                        continue;
                    }

                    bool validAction = ValidateAction(action, actionPath, limits, issues, controlSchemeGroups);
                    if (!validName || !validMap || !validAction)
                    {
                        continue;
                    }

                    string identity = context.Name + "\u001f" + context.ActionMap + "\u001f" + action.ActionName;
                    if (!identities.Add(identity))
                    {
                        Add(issues, InputConfigurationIssueCode.DuplicateActionIdentity, actionPath,
                            $"Action identity '{context.Name}/{context.ActionMap}/{action.ActionName}' is duplicated.");
                    }

                    int actionId = InputHashUtility.GetActionId(context.Name, context.ActionMap, action.ActionName);
                    string displayIdentity = context.Name + "/" + context.ActionMap + "/" + action.ActionName;
                    if (actionId == 0)
                    {
                        Add(issues, InputConfigurationIssueCode.InvalidActionId, actionPath,
                            $"Action identity '{displayIdentity}' produced the reserved ID zero.");
                    }
                    else if (actionIds.TryGetValue(actionId, out string existingIdentity) &&
                        !string.Equals(existingIdentity, displayIdentity, StringComparison.Ordinal))
                    {
                        Add(issues, InputConfigurationIssueCode.ActionIdCollision, actionPath,
                            $"Action ID collision between '{existingIdentity}' and '{displayIdentity}'.");
                    }
                    else
                    {
                        actionIds[actionId] = displayIdentity;
                    }
                }
            }

            if (totalActions > limits.MaxTotalActionsPerPlayer)
            {
                Add(issues, InputConfigurationIssueCode.LimitExceeded, $"{playerPath}.contexts",
                    $"Total action count exceeds the per-player limit of {limits.MaxTotalActionsPerPlayer}.");
            }
        }

        private static HashSet<string> ValidateControlSchemes(
            PlayerSlotConfig player,
            string playerPath,
            InputConfigurationLimits limits,
            List<InputConfigurationIssue> issues)
        {
            ValidateOptionalString(player.DefaultControlScheme, $"{playerPath}.defaultControlScheme", limits, issues);
            if (player.ControlSchemes == null || player.ControlSchemes.Count == 0)
            {
                if (!string.IsNullOrEmpty(player.DefaultControlScheme))
                {
                    Add(issues, InputConfigurationIssueCode.InvalidValue, $"{playerPath}.defaultControlScheme",
                        "A default control scheme requires a non-empty controlSchemes collection.");
                }

                return null;
            }

            if (player.ControlSchemes.Count > limits.MaxControlSchemesPerPlayer)
            {
                Add(issues, InputConfigurationIssueCode.LimitExceeded, $"{playerPath}.controlSchemes",
                    $"Control scheme count exceeds the limit of {limits.MaxControlSchemesPerPlayer}.");
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            var groups = new HashSet<string>(StringComparer.Ordinal);
            for (int schemeIndex = 0; schemeIndex < player.ControlSchemes.Count; schemeIndex++)
            {
                string schemePath = $"{playerPath}.controlSchemes[{schemeIndex}]";
                ControlSchemeConfig scheme = player.ControlSchemes[schemeIndex];
                if (scheme == null)
                {
                    Add(issues, InputConfigurationIssueCode.InvalidValue, schemePath, "Control scheme is null.");
                    continue;
                }

                bool validName = ValidateRequiredString(scheme.Name, $"{schemePath}.name", limits, issues);
                bool validGroup = ValidateRequiredString(scheme.BindingGroup, $"{schemePath}.bindingGroup", limits, issues);
                if (validName && !names.Add(scheme.Name))
                {
                    Add(issues, InputConfigurationIssueCode.InvalidValue, $"{schemePath}.name",
                        $"Control scheme name '{scheme.Name}' is duplicated.");
                }

                if (validGroup && !groups.Add(scheme.BindingGroup))
                {
                    Add(issues, InputConfigurationIssueCode.InvalidValue, $"{schemePath}.bindingGroup",
                        $"Binding group '{scheme.BindingGroup}' is duplicated.");
                }

                if (scheme.DeviceRequirements == null)
                {
                    Add(issues, InputConfigurationIssueCode.NullCollection, $"{schemePath}.deviceRequirements",
                        "Device requirements collection is null.");
                    continue;
                }

                if (scheme.DeviceRequirements.Count == 0)
                {
                    Add(issues, InputConfigurationIssueCode.InvalidValue, $"{schemePath}.deviceRequirements",
                        "At least one device requirement is required.");
                }
                else if (scheme.DeviceRequirements.Count > limits.MaxDeviceRequirementsPerScheme)
                {
                    Add(issues, InputConfigurationIssueCode.LimitExceeded, $"{schemePath}.deviceRequirements",
                        $"Device requirement count exceeds the limit of {limits.MaxDeviceRequirementsPerScheme}.");
                }

                for (int requirementIndex = 0; requirementIndex < scheme.DeviceRequirements.Count; requirementIndex++)
                {
                    ControlSchemeDeviceRequirementConfig requirement = scheme.DeviceRequirements[requirementIndex];
                    string requirementPath = $"{schemePath}.deviceRequirements[{requirementIndex}]";
                    if (requirement == null)
                    {
                        Add(issues, InputConfigurationIssueCode.InvalidValue, requirementPath, "Device requirement is null.");
                        continue;
                    }

                    ValidateRequiredString(requirement.ControlPath, $"{requirementPath}.controlPath", limits, issues);
                    if (requirementIndex == 0 && requirement.IsOr)
                    {
                        Add(issues, InputConfigurationIssueCode.InvalidValue, $"{requirementPath}.isOr",
                            "The first device requirement cannot be an OR requirement.");
                    }
                }
            }

            if (!string.IsNullOrEmpty(player.DefaultControlScheme) && !names.Contains(player.DefaultControlScheme))
            {
                Add(issues, InputConfigurationIssueCode.InvalidValue, $"{playerPath}.defaultControlScheme",
                    $"Default control scheme '{player.DefaultControlScheme}' was not found.");
            }


            return groups;
        }

        private static bool ValidateAction(
            ActionBindingConfig action,
            string path,
            InputConfigurationLimits limits,
            List<InputConfigurationIssue> issues,
            HashSet<string> declaredBindingGroups)
        {
            bool valid = ValidateRequiredString(action.ActionName, $"{path}.action", limits, issues);
            ValidateOptionalString(action.ExpectedControlType, $"{path}.expectedControlType", limits, issues);
            ValidateOptionalString(action.Interactions, $"{path}.interactions", limits, issues);
            ValidateOptionalString(action.Processors, $"{path}.processors", limits, issues);
            if (ValidateOptionalString(action.BindingGroups, $"{path}.bindingGroups", limits, issues))
            {
                ValidateBindingGroups(action.BindingGroups, $"{path}.bindingGroups", declaredBindingGroups, issues);
            }

            if (!IsValidActionValueType(action.Type))
            {
                Add(issues, InputConfigurationIssueCode.InvalidValue, $"{path}.type", "Action value type is invalid.");
                valid = false;
            }

            if (!IsValidUpdateMode(action.UpdateMode))
            {
                Add(issues, InputConfigurationIssueCode.InvalidValue, $"{path}.updateMode", "Input update mode is invalid.");
                valid = false;
            }

            if (action.LongPressMs < 0 || action.LongPressMs > limits.MaxLongPressMilliseconds)
            {
                Add(issues, InputConfigurationIssueCode.LimitExceeded, $"{path}.longPressMs",
                    $"Long-press duration must be between 0 and {limits.MaxLongPressMilliseconds} milliseconds.");
                valid = false;
            }

            if (float.IsNaN(action.LongPressValueThreshold) || float.IsInfinity(action.LongPressValueThreshold) ||
                action.LongPressValueThreshold < 0f || action.LongPressValueThreshold > 1f)
            {
                Add(issues, InputConfigurationIssueCode.InvalidValue, $"{path}.longPressValueThreshold",
                    "Long-press value threshold must be finite and between 0 and 1.");
                valid = false;
            }
            else if (action.Type == ActionValueType.Float && action.LongPressMs > 0 &&
                     action.LongPressValueThreshold <= 0f)
            {
                Add(issues, InputConfigurationIssueCode.InvalidValue, $"{path}.longPressValueThreshold",
                    "Float long-press threshold must be greater than 0 when long press is enabled.");
                valid = false;
            }

            int directBindingCount = action.DeviceBindings == null ? 0 : action.DeviceBindings.Count;
            int compositeCount = action.CompositeBindings == null ? 0 : action.CompositeBindings.Count;
            if (directBindingCount == 0 && compositeCount == 0)
            {
                Add(issues, InputConfigurationIssueCode.InvalidValue, path,
                    "At least one direct device binding or composite binding is required.");
                valid = false;
            }

            if (directBindingCount > limits.MaxBindingsPerAction)
            {
                Add(issues, InputConfigurationIssueCode.LimitExceeded, $"{path}.deviceBindings",
                    $"Binding count exceeds the per-action limit of {limits.MaxBindingsPerAction}.");
                valid = false;
            }

            var directBindings = new HashSet<string>(StringComparer.Ordinal);
            var stableSelectors = new HashSet<string>(StringComparer.Ordinal);
            int bindingCount = directBindingCount;
            for (int bindingIndex = 0; bindingIndex < bindingCount; bindingIndex++)
            {
                string binding = action.DeviceBindings[bindingIndex];
                if (!ValidateRequiredString(binding,
                        $"{path}.deviceBindings[{bindingIndex}]", limits, issues))
                {
                    valid = false;
                }
                else if (!directBindings.Add(binding))
                {
                    Add(issues, InputConfigurationIssueCode.InvalidValue, $"{path}.deviceBindings[{bindingIndex}]",
                        $"Duplicate device binding '{binding}' is not allowed.");
                    valid = false;
                }
                else
                {
                    stableSelectors.Add("D\u001f" + binding);
                }

                if (!string.IsNullOrEmpty(binding) && binding.StartsWith("2DVector(", StringComparison.OrdinalIgnoreCase))
                {
                    Add(issues, InputConfigurationIssueCode.InvalidValue, $"{path}.deviceBindings[{bindingIndex}]",
                        "Inline composite syntax is not supported. Use compositeBindings instead.");
                    valid = false;
                }
            }

            if (compositeCount > limits.MaxCompositesPerAction)
            {
                Add(issues, InputConfigurationIssueCode.LimitExceeded, $"{path}.compositeBindings",
                    $"Composite count exceeds the limit of {limits.MaxCompositesPerAction}.");
                valid = false;
            }

            int totalParts = 0;
            for (int compositeIndex = 0; compositeIndex < compositeCount; compositeIndex++)
            {
                string compositePath = $"{path}.compositeBindings[{compositeIndex}]";
                CompositeBindingConfig composite = action.CompositeBindings[compositeIndex];
                if (composite == null)
                {
                    Add(issues, InputConfigurationIssueCode.InvalidValue, compositePath, "Composite binding is null.");
                    valid = false;
                    continue;
                }

                bool compositeNameValid =
                    ValidateRequiredString(composite.Name, $"{compositePath}.name", limits, issues);
                valid &= compositeNameValid;
                bool compositeParametersValid =
                    ValidateOptionalString(composite.Parameters, $"{compositePath}.parameters", limits, issues);
                if (ValidateOptionalString(
                        composite.BindingGroups,
                        $"{compositePath}.bindingGroups",
                        limits,
                        issues))
                {
                    ValidateBindingGroups(
                        composite.BindingGroups,
                        $"{compositePath}.bindingGroups",
                        declaredBindingGroups,
                        issues);
                }

                string compositeRoot = null;
                if (compositeNameValid && compositeParametersValid)
                {
                    compositeRoot = string.IsNullOrEmpty(composite.Parameters)
                        ? composite.Name
                        : composite.Name + "(" + composite.Parameters + ")";
                }

                if (compositeRoot != null && !stableSelectors.Add("C\u001f" + compositeRoot))
                {
                    Add(issues, InputConfigurationIssueCode.InvalidValue, compositePath,
                        $"Composite selector '{compositeRoot}' is duplicated and cannot be rebound stably.");
                    valid = false;
                }
                if (composite.Parts == null)
                {
                    Add(issues, InputConfigurationIssueCode.NullCollection, $"{compositePath}.parts",
                        "Composite parts collection is null.");
                    valid = false;
                    continue;
                }

                if (composite.Parts.Count == 0)
                {
                    Add(issues, InputConfigurationIssueCode.InvalidValue, $"{compositePath}.parts",
                        "At least one composite part is required.");
                    valid = false;
                }
                else if (composite.Parts.Count > limits.MaxPartsPerComposite)
                {
                    Add(issues, InputConfigurationIssueCode.LimitExceeded, $"{compositePath}.parts",
                        $"Composite part count exceeds the limit of {limits.MaxPartsPerComposite}.");
                    valid = false;
                }

                totalParts += composite.Parts.Count;
                var partNames = new HashSet<string>(StringComparer.Ordinal);
                var partPaths = new HashSet<string>(StringComparer.Ordinal);
                for (int partIndex = 0; partIndex < composite.Parts.Count; partIndex++)
                {
                    string partPath = $"{compositePath}.parts[{partIndex}]";
                    CompositePartBindingConfig part = composite.Parts[partIndex];
                    if (part == null)
                    {
                        Add(issues, InputConfigurationIssueCode.InvalidValue, partPath, "Composite part is null.");
                        valid = false;
                        continue;
                    }

                    bool validPartName = ValidateRequiredString(part.Name, $"{partPath}.name", limits, issues);
                    bool validPartPath = ValidateRequiredString(part.Path, $"{partPath}.path", limits, issues);
                    ValidateOptionalString(part.Processors, $"{partPath}.processors", limits, issues);
                    ValidateOptionalString(part.Interactions, $"{partPath}.interactions", limits, issues);
                    if (!string.IsNullOrEmpty(part.Interactions))
                    {
                        Add(issues, InputConfigurationIssueCode.InvalidValue, $"{partPath}.interactions",
                            "Composite-part interactions are not supported. Configure interactions on the action.");
                        valid = false;
                    }
                    if (validPartName && !partNames.Add(part.Name))
                    {
                        Add(issues, InputConfigurationIssueCode.InvalidValue, $"{partPath}.name",
                            $"Duplicate composite part name '{part.Name}' is not allowed.");
                        valid = false;
                    }

                    if (validPartPath && !partPaths.Add(part.Path))
                    {
                        Add(issues, InputConfigurationIssueCode.InvalidValue, $"{partPath}.path",
                            $"Duplicate composite part path '{part.Path}' is not allowed.");
                        valid = false;
                    }

                    if (validPartName && validPartPath &&
                        !stableSelectors.Add("P\u001f" + compositeRoot + "\u001f" + part.Name + "\u001f" + part.Path))
                    {
                        Add(issues, InputConfigurationIssueCode.InvalidValue, partPath,
                            "Composite part selector is duplicated and cannot be rebound stably.");
                        valid = false;
                    }
                }
            }

            if (directBindingCount + totalParts > limits.MaxBindingsPerAction)
            {
                Add(issues, InputConfigurationIssueCode.LimitExceeded, path,
                    $"Total direct and composite-part binding count exceeds the limit of {limits.MaxBindingsPerAction}.");
                valid = false;
            }

            return valid;
        }

        private static void ValidateJoinAction(
            ActionBindingConfig action,
            string path,
            InputConfigurationLimits limits,
            List<InputConfigurationIssue> issues,
            HashSet<string> declaredBindingGroups)
        {
            ValidateAction(action, path, limits, issues, declaredBindingGroups);
            if (action.Type != ActionValueType.Button)
                Add(issues, InputConfigurationIssueCode.InvalidValue, $"{path}.type", "Join action must be Button.");
            if (action.UpdateMode != InputUpdateMode.EventDriven)
                Add(issues, InputConfigurationIssueCode.InvalidValue, $"{path}.updateMode", "Join action must be EventDriven.");
            if (action.LongPressMs != 0)
                Add(issues, InputConfigurationIssueCode.InvalidValue, $"{path}.longPressMs", "Join action does not support long press.");
            if (action.CompositeBindings != null && action.CompositeBindings.Count != 0)
                Add(issues, InputConfigurationIssueCode.InvalidValue, $"{path}.compositeBindings", "Join action does not support composite bindings.");
            if (action.DeviceBindings == null || action.DeviceBindings.Count == 0)
                Add(issues, InputConfigurationIssueCode.InvalidValue, $"{path}.deviceBindings", "Join action requires a direct device binding.");
            if (!string.IsNullOrEmpty(action.Interactions))
                Add(issues, InputConfigurationIssueCode.InvalidValue, $"{path}.interactions", "Join action does not support custom interactions.");
            if (!string.IsNullOrEmpty(action.Processors))
                Add(issues, InputConfigurationIssueCode.InvalidValue, $"{path}.processors", "Join action does not support custom processors.");
            if (!string.IsNullOrEmpty(action.ExpectedControlType) &&
                !string.Equals(action.ExpectedControlType, "Button", StringComparison.Ordinal))
                Add(issues, InputConfigurationIssueCode.InvalidValue, $"{path}.expectedControlType", "Join action expectedControlType must be Button when specified.");
        }

        private static void ValidateBindingGroups(
            string value,
            string path,
            HashSet<string> declaredBindingGroups,
            List<InputConfigurationIssue> issues)
        {
            if (string.IsNullOrEmpty(value) || declaredBindingGroups == null) return;
            string[] groups = value.Split(';');
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < groups.Length; i++)
            {
                string group = groups[i].Trim();
                if (group.Length == 0 || !seen.Add(group))
                {
                    Add(issues, InputConfigurationIssueCode.InvalidValue, path,
                        "Binding groups must be a non-empty, unique semicolon-separated list.");
                    continue;
                }

                if (!declaredBindingGroups.Contains(group))
                {
                    Add(issues, InputConfigurationIssueCode.InvalidValue, path,
                        $"Binding group '{group}' does not match a declared control scheme bindingGroup.");
                }
            }
        }

        private static bool ValidateRequiredString(
            string value,
            string path,
            InputConfigurationLimits limits,
            List<InputConfigurationIssue> issues)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                Add(issues, InputConfigurationIssueCode.InvalidValue, path, "Value is required.");
                return false;
            }

            if (value.Length > limits.MaxStringLength)
            {
                Add(issues, InputConfigurationIssueCode.LimitExceeded, path,
                    $"String length exceeds the limit of {limits.MaxStringLength}.");
                return false;
            }

            if (ContainsForbiddenTechnicalCharacter(value))
            {
                Add(issues, InputConfigurationIssueCode.InvalidValue, path,
                    "Value contains a control, format, separator, private-use, or unpaired surrogate character.");
                return false;
            }

            return true;
        }

        private static bool ValidateOptionalString(
            string value,
            string path,
            InputConfigurationLimits limits,
            List<InputConfigurationIssue> issues)
        {
            if (value != null && value.Length > limits.MaxStringLength)
            {
                Add(issues, InputConfigurationIssueCode.LimitExceeded, path,
                    $"String length exceeds the limit of {limits.MaxStringLength}.");
                return false;
            }

            if (!string.IsNullOrEmpty(value) && ContainsForbiddenTechnicalCharacter(value))
            {
                Add(issues, InputConfigurationIssueCode.InvalidValue, path,
                    "Value contains a control, format, separator, private-use, or unpaired surrogate character.");
                return false;
            }

            return true;
        }

        internal static bool ContainsForbiddenTechnicalCharacter(string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (char.IsHighSurrogate(current))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1])) return true;
                    UnicodeCategory pairCategory = CharUnicodeInfo.GetUnicodeCategory(value, i);
                    if (pairCategory == UnicodeCategory.Control ||
                        pairCategory == UnicodeCategory.Format ||
                        pairCategory == UnicodeCategory.PrivateUse ||
                        pairCategory == UnicodeCategory.LineSeparator ||
                        pairCategory == UnicodeCategory.ParagraphSeparator)
                    {
                        return true;
                    }

                    i++;
                    continue;
                }

                if (char.IsLowSurrogate(current)) return true;
                UnicodeCategory category = char.GetUnicodeCategory(value, i);
                if (category == UnicodeCategory.Control ||
                    category == UnicodeCategory.Format ||
                    category == UnicodeCategory.PrivateUse ||
                    category == UnicodeCategory.LineSeparator ||
                    category == UnicodeCategory.ParagraphSeparator)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsValidActionValueType(ActionValueType value)
        {
            return value == ActionValueType.Button || value == ActionValueType.Vector2 || value == ActionValueType.Float;
        }

        private static bool IsValidUpdateMode(InputUpdateMode value)
        {
            return value == InputUpdateMode.EventDriven || value == InputUpdateMode.Polling;
        }

        private static void Add(
            List<InputConfigurationIssue> issues,
            InputConfigurationIssueCode code,
            string path,
            string message)
        {
            if (issues.Count >= MaxValidationIssueCount) return;
            issues.Add(new InputConfigurationIssue(code, path, message));
        }
    }

    internal static class InputConfigurationCloner
    {
        public static InputConfiguration DeepClone(InputConfiguration source)
        {
            if (source == null) return null;

            return new InputConfiguration
            {
                SchemaVersion = source.SchemaVersion,
                SchemaFingerprint = source.SchemaFingerprint,
                PlayerSlots = ClonePlayers(source.PlayerSlots),
                JoinAction = CloneAction(source.JoinAction)
            };
        }

        private static List<PlayerSlotConfig> ClonePlayers(List<PlayerSlotConfig> source)
        {
            if (source == null) return null;
            var result = new List<PlayerSlotConfig>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                PlayerSlotConfig player = source[i];
                result.Add(player == null
                    ? null
                    : new PlayerSlotConfig
                    {
                        PlayerId = player.PlayerId,
                        JoinAction = CloneAction(player.JoinAction),
                        Contexts = CloneContexts(player.Contexts),
                        ControlSchemes = CloneControlSchemes(player.ControlSchemes),
                        DefaultControlScheme = player.DefaultControlScheme
                    });
            }

            return result;
        }

        private static List<ContextDefinitionConfig> CloneContexts(List<ContextDefinitionConfig> source)
        {
            if (source == null) return null;
            var result = new List<ContextDefinitionConfig>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                ContextDefinitionConfig context = source[i];
                result.Add(context == null
                    ? null
                    : new ContextDefinitionConfig
                    {
                        Name = context.Name,
                        ActionMap = context.ActionMap,
                        Priority = context.Priority,
                        BlocksLowerPriority = context.BlocksLowerPriority,
                        Bindings = CloneActions(context.Bindings)
                    });
            }

            return result;
        }

        private static List<ActionBindingConfig> CloneActions(List<ActionBindingConfig> source)
        {
            if (source == null) return null;
            var result = new List<ActionBindingConfig>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                result.Add(CloneAction(source[i]));
            }

            return result;
        }

        private static ActionBindingConfig CloneAction(ActionBindingConfig source)
        {
            if (source == null) return null;
            return new ActionBindingConfig
            {
                Type = source.Type,
                ActionName = source.ActionName,
                DeviceBindings = source.DeviceBindings == null ? null : new List<string>(source.DeviceBindings),
                CompositeBindings = CloneCompositeBindings(source.CompositeBindings),
                ExpectedControlType = source.ExpectedControlType,
                Interactions = source.Interactions,
                Processors = source.Processors,
                BindingGroups = source.BindingGroups,
                UpdateMode = source.UpdateMode,
                LongPressMs = source.LongPressMs,
                LongPressValueThreshold = source.LongPressValueThreshold
            };
        }

        private static List<ControlSchemeConfig> CloneControlSchemes(List<ControlSchemeConfig> source)
        {
            if (source == null) return null;
            var result = new List<ControlSchemeConfig>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                ControlSchemeConfig scheme = source[i];
                result.Add(scheme == null
                    ? null
                    : new ControlSchemeConfig
                    {
                        Name = scheme.Name,
                        BindingGroup = scheme.BindingGroup,
                        DeviceRequirements = CloneDeviceRequirements(scheme.DeviceRequirements)
                    });
            }

            return result;
        }

        private static List<ControlSchemeDeviceRequirementConfig> CloneDeviceRequirements(
            List<ControlSchemeDeviceRequirementConfig> source)
        {
            if (source == null) return null;
            var result = new List<ControlSchemeDeviceRequirementConfig>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                ControlSchemeDeviceRequirementConfig requirement = source[i];
                result.Add(requirement == null
                    ? null
                    : new ControlSchemeDeviceRequirementConfig
                    {
                        ControlPath = requirement.ControlPath,
                        IsOptional = requirement.IsOptional,
                        IsOr = requirement.IsOr
                    });
            }

            return result;
        }

        private static List<CompositeBindingConfig> CloneCompositeBindings(List<CompositeBindingConfig> source)
        {
            if (source == null) return null;
            var result = new List<CompositeBindingConfig>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                CompositeBindingConfig composite = source[i];
                result.Add(composite == null
                    ? null
                    : new CompositeBindingConfig
                    {
                        Name = composite.Name,
                        Parameters = composite.Parameters,
                        BindingGroups = composite.BindingGroups,
                        Parts = CloneCompositeParts(composite.Parts)
                    });
            }

            return result;
        }

        private static List<CompositePartBindingConfig> CloneCompositeParts(List<CompositePartBindingConfig> source)
        {
            if (source == null) return null;
            var result = new List<CompositePartBindingConfig>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                CompositePartBindingConfig part = source[i];
                result.Add(part == null
                    ? null
                    : new CompositePartBindingConfig
                    {
                        Name = part.Name,
                        Path = part.Path,
                        Processors = part.Processors,
                        Interactions = part.Interactions
                    });
            }

            return result;
        }
    }

    /// <summary>
    /// Performs bounded lexical and schema-key checks before YAML materialization.
    /// The accepted subset excludes ambiguous YAML features and rejects unknown or
    /// duplicate keys independently in every input-configuration mapping scope.
    /// </summary>
    public static class InputConfigurationYamlPreflight
    {
        private const int MaxLineCount = 16384;
        private const int MaxLineLength = 4096;
        private const int MaxIndentation = 64;
        private const int MaxStructuralTokenCount = 65536;
        private const int MaxNestingDepth = 64;
        private const int MaximumDiagnosticKeyLength = 64;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private enum SchemaMappingType : byte
        {
            None = 0,
            Root,
            PlayerSlot,
            Context,
            Action,
            ControlScheme,
            DeviceRequirement,
            Composite,
            CompositePart
        }

        private enum SchemaPath : byte
        {
            Root = 0,
            PlayerSlot,
            Context,
            RootJoinAction,
            PlayerJoinAction,
            ContextAction,
            ControlScheme,
            DeviceRequirement,
            RootJoinComposite,
            PlayerJoinComposite,
            ContextActionComposite,
            RootJoinCompositePart,
            PlayerJoinCompositePart,
            ContextActionCompositePart
        }

        private enum SchemaValueKind : byte
        {
            Scalar = 0,
            Object,
            MappingSequence,
            ScalarSequence
        }

        private readonly struct SchemaKeyDefinition
        {
            internal SchemaKeyDefinition(
                string name,
                SchemaValueKind valueKind = SchemaValueKind.Scalar,
                SchemaMappingType childType = SchemaMappingType.None)
            {
                Name = name;
                ValueKind = valueKind;
                ChildType = childType;
            }

            internal string Name { get; }
            internal SchemaValueKind ValueKind { get; }
            internal SchemaMappingType ChildType { get; }
        }

        private readonly struct SchemaTransition
        {
            internal SchemaTransition(
                SchemaValueKind valueKind,
                SchemaMappingType childType,
                SchemaPath childPath)
            {
                ValueKind = valueKind;
                ChildType = childType;
                ChildPath = childPath;
            }

            internal SchemaValueKind ValueKind { get; }
            internal SchemaMappingType ChildType { get; }
            internal SchemaPath ChildPath { get; }
            internal bool HasMappingChild =>
                ValueKind == SchemaValueKind.Object ||
                ValueKind == SchemaValueKind.MappingSequence;
        }

        private struct MappingScope
        {
            internal SchemaMappingType Type;
            internal SchemaPath Path;
            internal int KeyIndentation;
            internal ulong SeenKeys;
            internal bool IsSequenceElement;
            internal SchemaTransition PendingTransition;
        }

        private static readonly SchemaKeyDefinition[] RootKeys =
        {
            new SchemaKeyDefinition("schemaVersion"),
            new SchemaKeyDefinition("schemaFingerprint"),
            new SchemaKeyDefinition("playerSlots", SchemaValueKind.MappingSequence, SchemaMappingType.PlayerSlot),
            new SchemaKeyDefinition("joinAction", SchemaValueKind.Object, SchemaMappingType.Action)
        };

        private static readonly SchemaKeyDefinition[] PlayerSlotKeys =
        {
            new SchemaKeyDefinition("playerId"),
            new SchemaKeyDefinition("joinAction", SchemaValueKind.Object, SchemaMappingType.Action),
            new SchemaKeyDefinition("contexts", SchemaValueKind.MappingSequence, SchemaMappingType.Context),
            new SchemaKeyDefinition("controlSchemes", SchemaValueKind.MappingSequence, SchemaMappingType.ControlScheme),
            new SchemaKeyDefinition("defaultControlScheme")
        };

        private static readonly SchemaKeyDefinition[] ContextKeys =
        {
            new SchemaKeyDefinition("name"),
            new SchemaKeyDefinition("actionMap"),
            new SchemaKeyDefinition("bindings", SchemaValueKind.MappingSequence, SchemaMappingType.Action),
            new SchemaKeyDefinition("priority"),
            new SchemaKeyDefinition("blocksLowerPriority")
        };

        private static readonly SchemaKeyDefinition[] ActionKeys =
        {
            new SchemaKeyDefinition("type"),
            new SchemaKeyDefinition("action"),
            new SchemaKeyDefinition("deviceBindings", SchemaValueKind.ScalarSequence),
            new SchemaKeyDefinition("compositeBindings", SchemaValueKind.MappingSequence, SchemaMappingType.Composite),
            new SchemaKeyDefinition("expectedControlType"),
            new SchemaKeyDefinition("interactions"),
            new SchemaKeyDefinition("processors"),
            new SchemaKeyDefinition("bindingGroups"),
            new SchemaKeyDefinition("updateMode"),
            new SchemaKeyDefinition("longPressMs"),
            new SchemaKeyDefinition("longPressValueThreshold")
        };

        private static readonly SchemaKeyDefinition[] ControlSchemeKeys =
        {
            new SchemaKeyDefinition("name"),
            new SchemaKeyDefinition("bindingGroup"),
            new SchemaKeyDefinition("deviceRequirements", SchemaValueKind.MappingSequence, SchemaMappingType.DeviceRequirement)
        };

        private static readonly SchemaKeyDefinition[] DeviceRequirementKeys =
        {
            new SchemaKeyDefinition("controlPath"),
            new SchemaKeyDefinition("isOptional"),
            new SchemaKeyDefinition("isOr")
        };

        private static readonly SchemaKeyDefinition[] CompositeKeys =
        {
            new SchemaKeyDefinition("name"),
            new SchemaKeyDefinition("parameters"),
            new SchemaKeyDefinition("bindingGroups"),
            new SchemaKeyDefinition("parts", SchemaValueKind.MappingSequence, SchemaMappingType.CompositePart)
        };

        private static readonly SchemaKeyDefinition[] CompositePartKeys =
        {
            new SchemaKeyDefinition("name"),
            new SchemaKeyDefinition("path"),
            new SchemaKeyDefinition("processors"),
            new SchemaKeyDefinition("interactions")
        };

        public static bool TryValidate(string yaml, out string error)
        {
            error = null;
            if (yaml == null || yaml.Length == 0)
            {
                error = "Configuration content is empty.";
                return false;
            }
            if (yaml.Length > FileInputConfigurationStore.DefaultMaximumBytes)
            {
                error = $"Configuration exceeds the {FileInputConfigurationStore.DefaultMaximumBytes}-byte UTF-8 limit.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(yaml))
            {
                error = "Configuration content is empty.";
                return false;
            }
            try
            {
                if (StrictUtf8.GetByteCount(yaml) > FileInputConfigurationStore.DefaultMaximumBytes)
                {
                    error = $"Configuration exceeds the {FileInputConfigurationStore.DefaultMaximumBytes}-byte UTF-8 limit.";
                    return false;
                }
            }
            catch (EncoderFallbackException)
            {
                error = "Configuration content contains invalid Unicode.";
                return false;
            }

            int lineCount = 1;
            int lineLength = 0;
            int indentation = 0;
            int structuralTokens = 0;
            int flowDepth = 0;
            int compactSequenceDepth = 0;
            bool onlyWhitespaceOnLine = true;
            bool inSingleQuotedScalar = false;
            bool inDoubleQuotedScalar = false;
            bool escaped = false;
            bool inComment = false;

            for (int index = 0; index < yaml.Length; index++)
            {
                char character = yaml[index];
                if (IsForbiddenCharacter(yaml, index))
                {
                    error = "Configuration YAML contains a forbidden control, format, private-use, or separator character.";
                    return false;
                }
                if (character == '\u0085' || character == '\u2028' || character == '\u2029')
                {
                    error = "Configuration YAML contains an unsupported line separator.";
                    return false;
                }
                if (character == '\r' || character == '\n')
                {
                    if (character == '\r' && index + 1 < yaml.Length && yaml[index + 1] == '\n') index++;
                    if (++lineCount > MaxLineCount)
                    {
                        error = $"Configuration YAML exceeds the line limit of {MaxLineCount}.";
                        return false;
                    }

                    lineLength = 0;
                    indentation = 0;
                    compactSequenceDepth = 0;
                    onlyWhitespaceOnLine = true;
                    inComment = false;
                    escaped = false;
                    continue;
                }

                if (++lineLength > MaxLineLength)
                {
                    error = $"Configuration YAML contains a line longer than {MaxLineLength} characters.";
                    return false;
                }

                if (inComment) continue;
                if (inDoubleQuotedScalar)
                {
                    if (escaped) escaped = false;
                    else if (character == '\\') escaped = true;
                    else if (character == '"') inDoubleQuotedScalar = false;
                    continue;
                }

                if (inSingleQuotedScalar)
                {
                    if (character != '\'') continue;
                    if (index + 1 < yaml.Length && yaml[index + 1] == '\'')
                    {
                        index++;
                        if (++lineLength > MaxLineLength)
                        {
                            error = $"Configuration YAML contains a line longer than {MaxLineLength} characters.";
                            return false;
                        }
                    }
                    else
                    {
                        inSingleQuotedScalar = false;
                    }
                    continue;
                }

                if (onlyWhitespaceOnLine && (character == ' ' || character == '\t'))
                {
                    if (character == '\t')
                    {
                        error = "Configuration YAML cannot use tabs for indentation.";
                        return false;
                    }
                    if (++indentation > MaxIndentation)
                    {
                        error = $"Configuration YAML exceeds the indentation limit of {MaxIndentation} spaces.";
                        return false;
                    }
                    continue;
                }

                bool atLineContentStart = onlyWhitespaceOnLine;
                if (!char.IsWhiteSpace(character)) onlyWhitespaceOnLine = false;
                if (character == '#' && (index == 0 || char.IsWhiteSpace(yaml[index - 1])))
                {
                    inComment = true;
                    continue;
                }
                if (character == '\'' && IsTokenStart(yaml, index))
                {
                    inSingleQuotedScalar = true;
                    continue;
                }
                if (character == '"' && IsTokenStart(yaml, index))
                {
                    inDoubleQuotedScalar = true;
                    continue;
                }

                if (atLineContentStart &&
                    (character == '%' || IsDocumentMarker(yaml, index, "---") || IsDocumentMarker(yaml, index, "...")))
                {
                    error = "Configuration YAML directives and document markers are not supported.";
                    return false;
                }

                if ((character == '&' || character == '*' || character == '!') &&
                    IsTokenStart(yaml, index))
                {
                    error = "Configuration YAML anchors, aliases, and explicit tags are not supported.";
                    return false;
                }
                if ((character == '|' || character == '>') && IsTokenStart(yaml, index))
                {
                    error = "Configuration YAML block scalars are not supported.";
                    return false;
                }

                bool isCompactBlockIndicator =
                    (character == '-' || character == '?') &&
                    (index == 0 || char.IsWhiteSpace(yaml[index - 1])) &&
                    (index + 1 == yaml.Length || char.IsWhiteSpace(yaml[index + 1]));
                if (isCompactBlockIndicator)
                {
                    if (++compactSequenceDepth > MaxNestingDepth)
                    {
                        error = $"Configuration YAML exceeds the block nesting limit of {MaxNestingDepth}.";
                        return false;
                    }
                    structuralTokens++;
                }
                else if (character == '[' || character == '{')
                {
                    if (++flowDepth > MaxNestingDepth)
                    {
                        error = $"Configuration YAML exceeds the flow nesting limit of {MaxNestingDepth}.";
                        return false;
                    }
                    structuralTokens++;
                }
                else if (character == ']' || character == '}')
                {
                    if (flowDepth > 0) flowDepth--;
                    structuralTokens++;
                }
                else if (character == ':' || character == ',')
                {
                    structuralTokens++;
                }

                if (structuralTokens > MaxStructuralTokenCount)
                {
                    error = $"Configuration YAML exceeds the structural token limit of {MaxStructuralTokenCount}.";
                    return false;
                }
            }

            if (inSingleQuotedScalar || inDoubleQuotedScalar)
            {
                error = "Configuration YAML contains an unterminated quoted scalar.";
                return false;
            }
            if (flowDepth != 0)
            {
                error = "Configuration YAML contains an unterminated flow collection.";
                return false;
            }

            return TryValidateSchema(yaml, out error);
        }

        private static bool TryValidateSchema(string yaml, out string error)
        {
            error = null;
            var scopes = new MappingScope[MaxNestingDepth + 1];
            int scopeCount = 0;
            int index = 0;

            while (index < yaml.Length)
            {
                int lineStart = index;
                while (index < yaml.Length && yaml[index] == ' ') index++;
                if (IsLineEnd(yaml, index))
                {
                    ConsumeLineEnd(yaml, ref index);
                    continue;
                }
                if (yaml[index] == '#')
                {
                    SkipLine(yaml, ref index);
                    continue;
                }

                bool hasSequenceIndicator = false;
                int sequenceIndicatorCount = 0;
                while (index < yaml.Length && yaml[index] == '-' &&
                       IsBlockIndicator(yaml, index))
                {
                    hasSequenceIndicator = true;
                    if (++sequenceIndicatorCount > 1)
                    {
                        error = "Configuration YAML compact nested sequences are not supported by the input schema.";
                        return false;
                    }

                    index++;
                    while (index < yaml.Length && yaml[index] == ' ') index++;
                }

                if (index < yaml.Length && yaml[index] == '?' && IsBlockIndicator(yaml, index))
                {
                    error = $"Configuration YAML explicit mapping keys are not supported at '{GetNearestPath(scopes, scopeCount)}'.";
                    return false;
                }
                if (IsLineEnd(yaml, index))
                {
                    if (hasSequenceIndicator)
                    {
                        error = $"Configuration YAML mapping sequence items must declare their first key on the indicator line at '{GetNearestPath(scopes, scopeCount)}'.";
                        return false;
                    }

                    ConsumeLineEnd(yaml, ref index);
                    continue;
                }

                int keyStart = index;
                if (yaml[index] == '\'' || yaml[index] == '"')
                {
                    if (!TrySkipQuotedScalar(yaml, ref index))
                    {
                        error = "Configuration YAML contains an unterminated quoted scalar.";
                        return false;
                    }

                    while (index < yaml.Length && yaml[index] == ' ') index++;
                    if (IsMappingValueIndicator(yaml, index))
                    {
                        error = $"Configuration YAML quoted mapping keys are not supported at '{GetNearestPath(scopes, scopeCount)}'.";
                        return false;
                    }

                    SkipLine(yaml, ref index);
                    continue;
                }

                if (yaml[index] == '{' || yaml[index] == '[')
                {
                    error = "Configuration YAML flow-style root and sequence-item collections are not supported.";
                    return false;
                }

                int colonIndex = -1;
                while (index < yaml.Length && !IsLineEnd(yaml, index))
                {
                    char character = yaml[index];
                    if (character == '#' &&
                        (index == lineStart || char.IsWhiteSpace(yaml[index - 1])))
                        break;
                    if (character == ':' && IsMappingValueIndicator(yaml, index))
                    {
                        colonIndex = index;
                        break;
                    }
                    index++;
                }

                if (colonIndex < 0)
                {
                    SkipLine(yaml, ref index);
                    continue;
                }

                int keyEnd = colonIndex;
                while (keyEnd > keyStart && yaml[keyEnd - 1] == ' ') keyEnd--;
                if (keyEnd == keyStart)
                {
                    error = $"Configuration YAML contains an empty mapping key at '{GetNearestPath(scopes, scopeCount)}'.";
                    return false;
                }

                int keyIndentation = keyStart - lineStart;
                if (!TryRegisterSchemaKey(
                        yaml,
                        keyStart,
                        keyEnd - keyStart,
                        keyIndentation,
                        hasSequenceIndicator,
                        scopes,
                        ref scopeCount,
                        out int scopeIndex,
                        out SchemaKeyDefinition definition,
                        out error))
                    return false;

                index = colonIndex + 1;
                if (!TryReadInlineValue(yaml, ref index, out bool hasInlineValue, out error))
                    return false;

                scopes[scopeIndex].PendingTransition =
                    !hasInlineValue && definition.ValueKind != SchemaValueKind.Scalar
                        ? new SchemaTransition(
                            definition.ValueKind,
                            definition.ChildType,
                            ResolveChildPath(scopes[scopeIndex].Path, definition.ChildType))
                        : default;
                ConsumeLineEnd(yaml, ref index);
            }

            if (scopeCount > 0) return true;
            error = "Configuration YAML root must be a block mapping.";
            return false;
        }

        private static bool TryRegisterSchemaKey(
            string yaml,
            int keyStart,
            int keyLength,
            int keyIndentation,
            bool hasSequenceIndicator,
            MappingScope[] scopes,
            ref int scopeCount,
            out int scopeIndex,
            out SchemaKeyDefinition definition,
            out string error)
        {
            scopeIndex = -1;
            definition = default;
            error = null;

            if (scopeCount == 0)
            {
                if (hasSequenceIndicator || keyIndentation != 0)
                {
                    error = "Configuration YAML root must be an unindented block mapping.";
                    return false;
                }

                scopes[0] = new MappingScope
                {
                    Type = SchemaMappingType.Root,
                    Path = SchemaPath.Root,
                    KeyIndentation = 0
                };
                scopeCount = 1;
            }
            else
            {
                while (scopeCount > 0 && keyIndentation < scopes[scopeCount - 1].KeyIndentation)
                    scopeCount--;
                if (scopeCount == 0)
                {
                    error = "Configuration YAML mapping indentation escapes the root scope.";
                    return false;
                }

                ref MappingScope current = ref scopes[scopeCount - 1];
                if (keyIndentation == current.KeyIndentation)
                {
                    if (hasSequenceIndicator)
                    {
                        if (!current.IsSequenceElement)
                        {
                            error = $"Configuration YAML contains an unexpected mapping sequence at '{GetSchemaPath(current.Path)}'.";
                            return false;
                        }

                        current.SeenKeys = 0;
                    }

                    current.PendingTransition = default;
                }
                else
                {
                    SchemaTransition transition = current.PendingTransition;
                    bool expectsSequence = transition.ValueKind == SchemaValueKind.MappingSequence;
                    if (!transition.HasMappingChild || expectsSequence != hasSequenceIndicator)
                    {
                        error = $"Configuration YAML contains an unexpected nested mapping at '{GetSchemaPath(current.Path)}'.";
                        return false;
                    }
                    if (scopeCount >= scopes.Length)
                    {
                        error = $"Configuration YAML exceeds the schema nesting limit of {MaxNestingDepth}.";
                        return false;
                    }

                    current.PendingTransition = default;
                    scopes[scopeCount++] = new MappingScope
                    {
                        Type = transition.ChildType,
                        Path = transition.ChildPath,
                        KeyIndentation = keyIndentation,
                        IsSequenceElement = expectsSequence
                    };
                }
            }

            scopeIndex = scopeCount - 1;
            SchemaKeyDefinition[] definitions = GetSchemaKeys(scopes[scopeIndex].Type);
            int keyIndex = -1;
            for (int candidate = 0; candidate < definitions.Length; candidate++)
            {
                if (!KeyEquals(yaml, keyStart, keyLength, definitions[candidate].Name)) continue;
                keyIndex = candidate;
                definition = definitions[candidate];
                break;
            }

            string diagnosticKey = GetDiagnosticKey(yaml, keyStart, keyLength);
            if (keyIndex < 0)
            {
                error = $"Configuration YAML contains unknown key '{diagnosticKey}' at '{GetSchemaPath(scopes[scopeIndex].Path)}'.";
                return false;
            }

            ulong keyMask = 1UL << keyIndex;
            if ((scopes[scopeIndex].SeenKeys & keyMask) != 0)
            {
                error = $"Configuration YAML contains duplicate key '{diagnosticKey}' at '{GetSchemaPath(scopes[scopeIndex].Path)}'.";
                return false;
            }

            scopes[scopeIndex].SeenKeys |= keyMask;
            return true;
        }

        private static bool TryReadInlineValue(
            string yaml,
            ref int index,
            out bool hasInlineValue,
            out string error)
        {
            hasInlineValue = false;
            error = null;
            while (index < yaml.Length && yaml[index] == ' ') index++;
            if (IsLineEnd(yaml, index) || yaml[index] == '#')
            {
                SkipLine(yaml, ref index);
                return true;
            }

            hasInlineValue = true;
            if (yaml[index] == '{')
            {
                error = "Configuration YAML flow mappings are not supported.";
                return false;
            }
            if (yaml[index] != '[')
            {
                SkipLine(yaml, ref index);
                return true;
            }

            index++;
            while (index < yaml.Length && yaml[index] == ' ') index++;
            if (index >= yaml.Length || yaml[index] != ']')
            {
                error = "Configuration YAML non-empty flow sequences are not supported.";
                return false;
            }

            index++;
            while (index < yaml.Length && yaml[index] == ' ') index++;
            if (!IsLineEnd(yaml, index) && yaml[index] != '#')
            {
                error = "Configuration YAML flow sequences must be the complete mapping value.";
                return false;
            }

            SkipLine(yaml, ref index);
            return true;
        }

        private static bool TrySkipQuotedScalar(string yaml, ref int index)
        {
            char quote = yaml[index++];
            bool escaped = false;
            while (index < yaml.Length && !IsLineEnd(yaml, index))
            {
                char character = yaml[index++];
                if (quote == '"')
                {
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }
                    if (character == '\\')
                    {
                        escaped = true;
                        continue;
                    }
                    if (character == quote) return true;
                }
                else if (character == quote)
                {
                    if (index < yaml.Length && yaml[index] == quote)
                    {
                        index++;
                        continue;
                    }

                    return true;
                }
            }

            return false;
        }

        private static SchemaKeyDefinition[] GetSchemaKeys(SchemaMappingType type)
        {
            return type switch
            {
                SchemaMappingType.Root => RootKeys,
                SchemaMappingType.PlayerSlot => PlayerSlotKeys,
                SchemaMappingType.Context => ContextKeys,
                SchemaMappingType.Action => ActionKeys,
                SchemaMappingType.ControlScheme => ControlSchemeKeys,
                SchemaMappingType.DeviceRequirement => DeviceRequirementKeys,
                SchemaMappingType.Composite => CompositeKeys,
                SchemaMappingType.CompositePart => CompositePartKeys,
                _ => Array.Empty<SchemaKeyDefinition>()
            };
        }

        private static SchemaPath ResolveChildPath(SchemaPath parentPath, SchemaMappingType childType)
        {
            return childType switch
            {
                SchemaMappingType.PlayerSlot => SchemaPath.PlayerSlot,
                SchemaMappingType.Context => SchemaPath.Context,
                SchemaMappingType.ControlScheme => SchemaPath.ControlScheme,
                SchemaMappingType.DeviceRequirement => SchemaPath.DeviceRequirement,
                SchemaMappingType.Action => parentPath switch
                {
                    SchemaPath.Root => SchemaPath.RootJoinAction,
                    SchemaPath.PlayerSlot => SchemaPath.PlayerJoinAction,
                    _ => SchemaPath.ContextAction
                },
                SchemaMappingType.Composite => parentPath switch
                {
                    SchemaPath.RootJoinAction => SchemaPath.RootJoinComposite,
                    SchemaPath.PlayerJoinAction => SchemaPath.PlayerJoinComposite,
                    _ => SchemaPath.ContextActionComposite
                },
                SchemaMappingType.CompositePart => parentPath switch
                {
                    SchemaPath.RootJoinComposite => SchemaPath.RootJoinCompositePart,
                    SchemaPath.PlayerJoinComposite => SchemaPath.PlayerJoinCompositePart,
                    _ => SchemaPath.ContextActionCompositePart
                },
                _ => SchemaPath.Root
            };
        }

        private static string GetSchemaPath(SchemaPath path)
        {
            return path switch
            {
                SchemaPath.Root => "$",
                SchemaPath.PlayerSlot => "$.playerSlots[]",
                SchemaPath.Context => "$.playerSlots[].contexts[]",
                SchemaPath.RootJoinAction => "$.joinAction",
                SchemaPath.PlayerJoinAction => "$.playerSlots[].joinAction",
                SchemaPath.ContextAction => "$.playerSlots[].contexts[].bindings[]",
                SchemaPath.ControlScheme => "$.playerSlots[].controlSchemes[]",
                SchemaPath.DeviceRequirement => "$.playerSlots[].controlSchemes[].deviceRequirements[]",
                SchemaPath.RootJoinComposite => "$.joinAction.compositeBindings[]",
                SchemaPath.PlayerJoinComposite => "$.playerSlots[].joinAction.compositeBindings[]",
                SchemaPath.ContextActionComposite => "$.playerSlots[].contexts[].bindings[].compositeBindings[]",
                SchemaPath.RootJoinCompositePart => "$.joinAction.compositeBindings[].parts[]",
                SchemaPath.PlayerJoinCompositePart => "$.playerSlots[].joinAction.compositeBindings[].parts[]",
                SchemaPath.ContextActionCompositePart => "$.playerSlots[].contexts[].bindings[].compositeBindings[].parts[]",
                _ => "$"
            };
        }

        private static string GetNearestPath(MappingScope[] scopes, int scopeCount)
        {
            return scopeCount == 0 ? "$" : GetSchemaPath(scopes[scopeCount - 1].Path);
        }

        private static bool KeyEquals(string yaml, int start, int length, string expected)
        {
            return length == expected.Length &&
                   string.CompareOrdinal(yaml, start, expected, 0, length) == 0;
        }

        private static string GetDiagnosticKey(string yaml, int start, int length)
        {
            int displayedLength = Math.Min(length, MaximumDiagnosticKeyLength);
            string key = yaml.Substring(start, displayedLength);
            return displayedLength == length ? key : key + "...";
        }

        private static bool IsBlockIndicator(string yaml, int index)
        {
            int after = index + 1;
            return after == yaml.Length || char.IsWhiteSpace(yaml[after]);
        }

        private static bool IsMappingValueIndicator(string yaml, int index)
        {
            if (index >= yaml.Length || yaml[index] != ':') return false;
            int after = index + 1;
            return after == yaml.Length || char.IsWhiteSpace(yaml[after]);
        }

        private static bool IsLineEnd(string yaml, int index)
        {
            return index >= yaml.Length || yaml[index] == '\r' || yaml[index] == '\n';
        }

        private static void SkipLine(string yaml, ref int index)
        {
            while (index < yaml.Length && !IsLineEnd(yaml, index)) index++;
        }

        private static void ConsumeLineEnd(string yaml, ref int index)
        {
            if (index >= yaml.Length) return;
            if (yaml[index] == '\r' && index + 1 < yaml.Length && yaml[index + 1] == '\n') index += 2;
            else if (yaml[index] == '\r' || yaml[index] == '\n') index++;
        }

        private static bool IsTokenStart(string yaml, int index)
        {
            if (index == 0) return true;
            char previous = yaml[index - 1];
            return char.IsWhiteSpace(previous) ||
                   previous == '[' || previous == '{' || previous == ',' || previous == ':';
        }

        private static bool IsDocumentMarker(string yaml, int index, string marker)
        {
            if (index > yaml.Length - marker.Length ||
                string.CompareOrdinal(yaml, index, marker, 0, marker.Length) != 0)
                return false;
            int after = index + marker.Length;
            return after == yaml.Length || char.IsWhiteSpace(yaml[after]);
        }

        private static bool IsForbiddenCharacter(string value, int index)
        {
            char character = value[index];
            if (char.IsLowSurrogate(character)) return false;
            if (char.IsControl(character) && character != '\r' && character != '\n' &&
                character != '\t' && character != '\u0085')
                return true;
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(value, index);
            return category == UnicodeCategory.Format ||
                   category == UnicodeCategory.PrivateUse;
        }
    }

    /// <summary>
    /// Serializes a prepared configuration into the module's bounded canonical YAML form.
    /// </summary>
    public static class InputConfigurationYamlCodec
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static bool TrySerialize(
            InputConfiguration configuration,
            out string yaml,
            out string error)
        {
            yaml = null;
            error = null;
            if (configuration == null)
            {
                error = "Configuration is null.";
                return false;
            }

            try
            {
                byte[] bytes = YamlSerializer.Serialize(configuration).ToArray();
                if (bytes.Length > FileInputConfigurationStore.DefaultMaximumBytes)
                {
                    error = $"Serialized configuration exceeds the {FileInputConfigurationStore.DefaultMaximumBytes}-byte limit.";
                    return false;
                }

                yaml = StrictUtf8.GetString(bytes).Replace("\r\n", "\n").Replace("\r", "\n");
                if (!InputConfigurationYamlPreflight.TryValidate(yaml, out error))
                {
                    yaml = null;
                    return false;
                }

                return true;
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException &&
                exception is not AccessViolationException &&
                exception is not StackOverflowException)
            {
                error = $"Configuration YAML serialization failed ({exception.GetType().Name}).";
                return false;
            }
        }
    }
}
