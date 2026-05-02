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
                   $"\"{ActionA}\"({TypeA}) ↔ \"{ActionB}\"({TypeB})";
        }
    }

    public static class InputBindingValidator
    {
        public static List<BindingConflict> DetectConflicts(PlayerSlotConfig config)
        {
            var conflicts = new List<BindingConflict>();
            if (config?.Contexts == null) return conflicts;

            foreach (var ctxConfig in config.Contexts)
            {
                DetectContextConflicts(ctxConfig, conflicts);
            }

            return conflicts;
        }

        public static List<BindingConflict> DetectConflicts(PlayerSlotConfig config, string contextNameFilter)
        {
            var conflicts = new List<BindingConflict>();
            if (config?.Contexts == null || string.IsNullOrEmpty(contextNameFilter)) return conflicts;

            foreach (var ctxConfig in config.Contexts)
            {
                if (ctxConfig.Name == contextNameFilter)
                {
                    DetectContextConflicts(ctxConfig, conflicts);
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
                    sb.AppendLine($"    └─ \"{c.ActionA}\"({c.TypeA}) vs \"{c.ActionB}\"({c.TypeB})");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static void DetectContextConflicts(ContextDefinitionConfig ctxConfig, List<BindingConflict> conflicts)
        {
            if (ctxConfig?.Bindings == null) return;

            var bindingToAction = new Dictionary<string, (string actionName, ActionValueType type, int longPressMs)>();

            foreach (var binding in ctxConfig.Bindings)
            {
                if (binding.DeviceBindings == null) continue;

                foreach (var rawBinding in binding.DeviceBindings)
                {
                    var individualPaths = ExpandBindingPaths(rawBinding);

                    foreach (var path in individualPaths)
                    {
                        var normalizedPath = NormalizeBindingPath(path);

                        if (bindingToAction.TryGetValue(normalizedPath, out var existing))
                        {
                            bool isLongPressDifferentiated =
                                (existing.longPressMs > 0 && binding.LongPressMs == 0) ||
                                (existing.longPressMs == 0 && binding.LongPressMs > 0) ||
                                (existing.longPressMs > 0 && binding.LongPressMs > 0 && existing.longPressMs != binding.LongPressMs);

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
                                ctxConfig.Name ?? "unnamed",
                                ctxConfig.ActionMap ?? "unknown",
                                normalizedPath,
                                existing.actionName, existing.type,
                                binding.ActionName, binding.Type
                            ));
                        }
                        else
                        {
                            bindingToAction[normalizedPath] = (binding.ActionName, binding.Type, binding.LongPressMs);
                        }
                    }
                }
            }
        }

        private static List<string> ExpandBindingPaths(string rawBinding)
        {
            var paths = new List<string>(4);

            if (string.IsNullOrEmpty(rawBinding))
            {
                paths.Add(string.Empty);
                return paths;
            }

            if (IsCompositeBinding(rawBinding))
            {
                var compositePaths = ExtractCompositePaths(rawBinding);
                if (compositePaths.Length > 0)
                {
                    foreach (var p in compositePaths)
                    {
                        if (!string.IsNullOrEmpty(p))
                            paths.Add(p);
                    }
                    return paths;
                }
            }

            paths.Add(rawBinding);
            return paths;
        }

        private static bool IsCompositeBinding(string binding)
        {
            return !string.IsNullOrEmpty(binding) &&
                   binding.StartsWith("2DVector(", System.StringComparison.OrdinalIgnoreCase) &&
                   binding.EndsWith(")");
        }

        private static string[] ExtractCompositePaths(string composite)
        {
            const string prefix = "2DVector(";
            var inner = composite.Substring(prefix.Length, composite.Length - prefix.Length - 1);
            var segments = inner.Split(',');
            var paths = new string[4];
            int pathCount = 0;

            for (int i = 0; i < segments.Length; i++)
            {
                var seg = segments[i].Trim();
                int eq = seg.IndexOf('=');
                if (eq <= 0 || eq >= seg.Length - 1) continue;

                var key = seg.Substring(0, eq).Trim();
                var val = seg.Substring(eq + 1).Trim();

                if (key.Equals("up") || key.Equals("down") || key.Equals("left") || key.Equals("right"))
                {
                    if (!string.IsNullOrEmpty(val))
                    {
                        paths[pathCount] = val;
                        pathCount++;
                    }
                }
            }

            var result = new string[pathCount];
            for (int i = 0; i < pathCount; i++)
                result[i] = paths[i];
            return result;
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
