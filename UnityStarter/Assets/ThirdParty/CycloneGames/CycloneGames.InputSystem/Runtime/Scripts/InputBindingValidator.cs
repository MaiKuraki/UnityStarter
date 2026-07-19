using System;
using System.Collections.Generic;
using System.Text;

namespace CycloneGames.InputSystem.Runtime
{
    public enum BindingConflictSeverity
    {
        Critical,
        Warning,
        Info
    }

    public sealed class BindingConflict
    {
        public BindingConflictSeverity Severity { get; }
        public string ContextName { get; }
        public string ActionMapName { get; }
        public string BindingPath { get; }
        public string ActionA { get; }
        public string ActionB { get; }
        public ActionValueType TypeA { get; }
        public ActionValueType TypeB { get; }

        internal BindingConflict(
            BindingConflictSeverity severity,
            string contextName,
            string actionMapName,
            string bindingPath,
            string actionA, ActionValueType typeA,
            string actionB, ActionValueType typeB)
        {
            Severity = severity;
            ContextName = contextName;
            ActionMapName = actionMapName;
            BindingPath = bindingPath;
            ActionA = actionA;
            ActionB = actionB;
            TypeA = typeA;
            TypeB = typeB;
        }

        public override string ToString()
        {
            return $"[{Severity}] Context=\"{ContextName}\", Map=\"{ActionMapName}\", Binding=\"{BindingPath}\": " +
                   $"\"{ActionA}\"({TypeA}) <-> \"{ActionB}\"({TypeB})";
        }
    }

    public static class InputBindingValidator
    {
        private const int MaxReportedConflicts = 256;

        public static List<BindingConflict> DetectConflicts(PlayerSlotConfig config)
        {
            var conflicts = new List<BindingConflict>();
            if (config?.Contexts == null) return conflicts;

            InputConfigurationLimits limits = InputConfigurationLimits.Default;
            int remainingActions = limits.MaxTotalActionsPerPlayer;
            int contextCount = Math.Min(config.Contexts.Count, limits.MaxContextsPerPlayer);
            for (int contextIndex = 0;
                 contextIndex < contextCount && remainingActions > 0 && conflicts.Count < MaxReportedConflicts;
                 contextIndex++)
            {
                DetectContextConflicts(
                    config.Contexts[contextIndex],
                    conflicts,
                    limits,
                    ref remainingActions);
            }

            return conflicts;
        }

        public static List<BindingConflict> DetectConflicts(PlayerSlotConfig config, string contextNameFilter)
        {
            var conflicts = new List<BindingConflict>();
            if (config?.Contexts == null || string.IsNullOrEmpty(contextNameFilter)) return conflicts;

            InputConfigurationLimits limits = InputConfigurationLimits.Default;
            int contextCount = Math.Min(config.Contexts.Count, limits.MaxContextsPerPlayer);
            for (int contextIndex = 0; contextIndex < contextCount; contextIndex++)
            {
                ContextDefinitionConfig context = config.Contexts[contextIndex];
                if (context != null &&
                    string.Equals(context.Name, contextNameFilter, StringComparison.Ordinal))
                {
                    int remainingActions = limits.MaxTotalActionsPerPlayer;
                    DetectContextConflicts(
                        context,
                        conflicts,
                        limits,
                        ref remainingActions);
                    break;
                }
            }

            return conflicts;
        }

        public static string FormatConflictsReport(List<BindingConflict> conflicts)
        {
            if (conflicts == null || conflicts.Count == 0)
                return "No binding conflicts detected.";

            var sb = new StringBuilder();
            int critical = 0, warning = 0, info = 0;

            foreach (var c in conflicts)
            {
                switch (c.Severity)
                {
                    case BindingConflictSeverity.Critical: critical++; break;
                    case BindingConflictSeverity.Warning: warning++; break;
                    case BindingConflictSeverity.Info: info++; break;
                }
            }

            sb.AppendLine($"=== Binding Conflict Report ({conflicts.Count} total: {critical} critical, {warning} warning, {info} info) ===");
            sb.AppendLine();

            var byContext = new Dictionary<string, List<BindingConflict>>();
            foreach (var c in conflicts)
            {
                if (!byContext.TryGetValue(c.ContextName, out var list))
                {
                    list = new List<BindingConflict>();
                    byContext[c.ContextName] = list;
                }
                list.Add(c);
            }

            foreach (var (ctxName, ctxConflicts) in byContext)
            {
                sb.AppendLine($"Context: \"{ctxName}\" ({ctxConflicts.Count} conflicts)");
                foreach (var c in ctxConflicts)
                {
                    sb.AppendLine($"  {c.Severity.ToString().PadRight(9)} | {c.BindingPath}");
                    sb.AppendLine($"    - \"{c.ActionA}\"({c.TypeA}) vs \"{c.ActionB}\"({c.TypeB})");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static void DetectContextConflicts(
            ContextDefinitionConfig ctxConfig,
            List<BindingConflict> conflicts,
            InputConfigurationLimits limits,
            ref int remainingActions)
        {
            if (ctxConfig?.Bindings == null || remainingActions <= 0) return;

            var bindingToAction = new Dictionary<string, (string actionName, ActionValueType type, int longPressMs)>(
                StringComparer.OrdinalIgnoreCase);
            int actionCount = Math.Min(
                Math.Min(ctxConfig.Bindings.Count, limits.MaxActionsPerContext),
                remainingActions);
            remainingActions -= actionCount;

            for (int actionIndex = 0;
                 actionIndex < actionCount && conflicts.Count < MaxReportedConflicts;
                 actionIndex++)
            {
                ActionBindingConfig binding = ctxConfig.Bindings[actionIndex];
                if (binding == null) continue;

                int remainingBindingEntries = limits.MaxBindingsPerAction;
                int directCount = Math.Min(
                    binding.DeviceBindings?.Count ?? 0,
                    remainingBindingEntries);
                for (int directIndex = 0;
                     directIndex < directCount && conflicts.Count < MaxReportedConflicts;
                     directIndex++)
                {
                    RegisterBindingPath(
                        ctxConfig,
                        binding,
                        binding.DeviceBindings[directIndex],
                        bindingToAction,
                        conflicts,
                        limits);
                }

                remainingBindingEntries -= directCount;
                int compositeCount = Math.Min(
                    binding.CompositeBindings?.Count ?? 0,
                    limits.MaxCompositesPerAction);
                for (int compositeIndex = 0;
                     compositeIndex < compositeCount &&
                     remainingBindingEntries > 0 &&
                     conflicts.Count < MaxReportedConflicts;
                     compositeIndex++)
                {
                    CompositeBindingConfig composite = binding.CompositeBindings[compositeIndex];
                    if (composite?.Parts == null) continue;
                    int partCount = Math.Min(
                        Math.Min(composite.Parts.Count, limits.MaxPartsPerComposite),
                        remainingBindingEntries);
                    for (int partIndex = 0;
                         partIndex < partCount && conflicts.Count < MaxReportedConflicts;
                         partIndex++)
                    {
                        CompositePartBindingConfig part = composite.Parts[partIndex];
                        if (part == null) continue;
                        RegisterBindingPath(
                            ctxConfig,
                            binding,
                            part.Path,
                            bindingToAction,
                            conflicts,
                            limits);
                    }

                    remainingBindingEntries -= partCount;
                }
            }
        }

        private static void RegisterBindingPath(
            ContextDefinitionConfig context,
            ActionBindingConfig binding,
            string path,
            Dictionary<string, (string actionName, ActionValueType type, int longPressMs)> bindingToAction,
            List<BindingConflict> conflicts,
            InputConfigurationLimits limits)
        {
            if (string.IsNullOrWhiteSpace(path) ||
                path.Length > limits.MaxStringLength ||
                InputConfigurationValidator.ContainsForbiddenTechnicalCharacter(path))
            {
                return;
            }

            string normalizedPath = NormalizeBindingPath(path);
            if (bindingToAction.TryGetValue(normalizedPath, out var existing))
            {
                bool isLongPressDifferentiated =
                    (existing.longPressMs > 0 && binding.LongPressMs == 0) ||
                    (existing.longPressMs == 0 && binding.LongPressMs > 0) ||
                    (existing.longPressMs > 0 &&
                     binding.LongPressMs > 0 &&
                     existing.longPressMs != binding.LongPressMs);

                BindingConflictSeverity severity;
                if (isLongPressDifferentiated)
                {
                    severity = BindingConflictSeverity.Info;
                }
                else if (existing.type == binding.Type)
                {
                    severity = BindingConflictSeverity.Critical;
                }
                else
                {
                    severity = BindingConflictSeverity.Warning;
                }

                conflicts.Add(new BindingConflict(
                    severity,
                    context.Name ?? "unnamed",
                    context.ActionMap ?? "unknown",
                    normalizedPath,
                    existing.actionName,
                    existing.type,
                    binding.ActionName,
                    binding.Type));
            }
            else
            {
                bindingToAction[normalizedPath] =
                    (binding.ActionName, binding.Type, binding.LongPressMs);
            }
        }

        private static string NormalizeBindingPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            string trimmed = path.Trim();

            if (trimmed.StartsWith("<") && trimmed.Contains("/") && trimmed.EndsWith(">"))
            {
                int slashIndex = trimmed.IndexOf('/');
                if (slashIndex > 1)
                {
                    string control = trimmed.Substring(1, slashIndex - 1);
                    string usage = trimmed.Substring(slashIndex + 1).TrimEnd('>');
                    return $"<{control}/{usage}>";
                }
            }

            return trimmed;
        }
    }
}
