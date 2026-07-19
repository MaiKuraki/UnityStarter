using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CycloneGames.InputSystem.Runtime;

namespace CycloneGames.InputSystem.Editor
{
    internal static class InputConstantsCodeGenerator
    {
        private const string GlobalActionMap = "GlobalActions";
        private const string PlayerJoinContext = "PlayerJoin";

        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
            "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
            "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
            "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
            "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
            "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
            "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
            "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
            "void", "volatile", "while", "add", "alias", "ascending", "async", "await", "by", "descending",
            "dynamic", "equals", "from", "get", "global", "group", "init", "into", "join", "let", "managed",
            "nameof", "nint", "not", "notnull", "nuint", "on", "or", "orderby", "partial", "record", "remove",
            "required", "scoped", "select", "set", "unmanaged", "value", "var", "when", "where", "with", "yield"
        };

        internal static bool TryGenerate(
            InputConfiguration configuration,
            string requestedNamespace,
            out string source,
            out string error)
        {
            source = null;
            error = null;

            InputEditorValidationResult validation = InputEditorConfigurationValidator.Validate(configuration);
            if (!validation.IsValid)
            {
                error = validation.Error;
                return false;
            }

            if (!TryFormatNamespace(requestedNamespace, out string formattedNamespace, out error))
            {
                return false;
            }

            var contexts = new HashSet<string>(StringComparer.Ordinal);
            var actionMaps = new HashSet<string>(StringComparer.Ordinal) { GlobalActionMap };
            var actions = new List<GeneratedAction>();
            var logicalActions = new HashSet<string>(StringComparer.Ordinal);
            var collisionMap = new Dictionary<int, string>();

            if (configuration.JoinAction != null)
            {
                contexts.Add(PlayerJoinContext);
                if (!TryAddAction(
                        actions,
                        logicalActions,
                        collisionMap,
                        "PlayerJoin_Global_" + configuration.JoinAction.ActionName,
                        PlayerJoinContext,
                        GlobalActionMap,
                        configuration.JoinAction.ActionName,
                        false,
                        out error))
                {
                    return false;
                }
            }

            for (int slotIndex = 0; slotIndex < configuration.PlayerSlots.Count; slotIndex++)
            {
                PlayerSlotConfig slot = configuration.PlayerSlots[slotIndex];
                if (slot.JoinAction != null)
                {
                    contexts.Add(PlayerJoinContext);
                }
                if (slot.JoinAction != null &&
                    !TryAddAction(
                        actions,
                        logicalActions,
                        collisionMap,
                        string.Concat("PlayerJoin_P", slot.PlayerId.ToString(CultureInfo.InvariantCulture), "_", slot.JoinAction.ActionName),
                        PlayerJoinContext,
                        GlobalActionMap,
                        slot.JoinAction.ActionName,
                        true,
                        out error))
                {
                    return false;
                }

                for (int contextIndex = 0; contextIndex < slot.Contexts.Count; contextIndex++)
                {
                    ContextDefinitionConfig context = slot.Contexts[contextIndex];
                    if (!ValidateGeneratedText(context.Name, "Context name", out error) ||
                        !ValidateGeneratedText(context.ActionMap, "ActionMap name", out error))
                    {
                        return false;
                    }

                    contexts.Add(context.Name);
                    actionMaps.Add(context.ActionMap);

                    for (int bindingIndex = 0; bindingIndex < context.Bindings.Count; bindingIndex++)
                    {
                        ActionBindingConfig binding = context.Bindings[bindingIndex];
                        if (!TryAddAction(
                                actions,
                                logicalActions,
                                collisionMap,
                                string.Concat(context.Name, "_", binding.ActionName),
                                context.Name,
                                context.ActionMap,
                                binding.ActionName,
                                false,
                                out error))
                        {
                            return false;
                        }
                    }
                }
            }

            source = BuildSource(formattedNamespace, contexts, actionMaps, actions);
            return true;
        }

        private static bool TryAddAction(
            List<GeneratedAction> actions,
            HashSet<string> logicalActions,
            Dictionary<int, string> collisionMap,
            string identifierBase,
            string context,
            string actionMap,
            string action,
            bool preserveDuplicateIdentifier,
            out string error)
        {
            error = null;
            if (!ValidateGeneratedText(context, "Context name", out error) ||
                !ValidateGeneratedText(actionMap, "ActionMap name", out error) ||
                !ValidateGeneratedText(action, "Action name", out error))
            {
                return false;
            }

            string logicalKey = string.Concat(context, "\u001f", actionMap, "\u001f", action);
            int actionId = InputHashUtility.GetActionId(context, actionMap, action);
            if (actionId == 0)
            {
                error = $"Action \"{context}/{actionMap}/{action}\" produced the reserved action ID 0.";
                return false;
            }

            if (collisionMap.TryGetValue(actionId, out string existingLogicalKey) &&
                !existingLogicalKey.Equals(logicalKey, StringComparison.Ordinal))
            {
                error = $"Action ID collision detected for ID {actionId}. Rename one of the colliding actions.";
                return false;
            }

            collisionMap[actionId] = logicalKey;
            if (!logicalActions.Add(logicalKey) && !preserveDuplicateIdentifier)
            {
                return true;
            }

            actions.Add(new GeneratedAction(identifierBase, context, actionMap, action, actionId));
            return true;
        }

        private static string BuildSource(
            string formattedNamespace,
            HashSet<string> contexts,
            HashSet<string> actionMaps,
            List<GeneratedAction> actions)
        {
            var builder = new StringBuilder(4096);
            builder.AppendLine("// -- AUTO-GENERATED FILE --");
            builder.AppendLine("// Generated by CycloneGames.InputSystem. Do not edit manually.");
            builder.AppendLine();
            builder.Append("namespace ").Append(formattedNamespace).AppendLine();
            builder.AppendLine("{");
            builder.AppendLine("    public static class InputActions");
            builder.AppendLine("    {");

            AppendStringConstants(builder, "Contexts", contexts);
            builder.AppendLine();
            AppendActionMapConstants(builder, actionMaps);
            builder.AppendLine();
            AppendActionConstants(builder, actions);

            builder.AppendLine("    }");
            builder.AppendLine("}");
            return builder.ToString();
        }

        private static void AppendStringConstants(
            StringBuilder builder,
            string className,
            HashSet<string> values)
        {
            var sorted = new List<string>(values);
            sorted.Sort(StringComparer.Ordinal);
            var identifiers = new HashSet<string>(StringComparer.Ordinal);

            builder.Append("        public static class ").Append(className).AppendLine();
            builder.AppendLine("        {");
            for (int i = 0; i < sorted.Count; i++)
            {
                string identifier = GetUniqueIdentifier(SanitizeIdentifier(sorted[i]), identifiers);
                builder.Append("            public const string ")
                    .Append(EscapeKeyword(identifier))
                    .Append(" = \"")
                    .Append(EscapeStringLiteral(sorted[i]))
                    .AppendLine("\";");
            }
            builder.AppendLine("        }");
        }

        private static void AppendActionMapConstants(
            StringBuilder builder,
            HashSet<string> actionMaps)
        {
            var sorted = new List<string>(actionMaps);
            sorted.Sort(StringComparer.Ordinal);
            var identifiers = new HashSet<string>(StringComparer.Ordinal);

            builder.AppendLine("        public static class ActionMaps");
            builder.AppendLine("        {");
            for (int i = 0; i < sorted.Count; i++)
            {
                string identifier = GetUniqueIdentifier(SanitizeIdentifier(sorted[i]), identifiers);
                builder.Append("            public const string ")
                    .Append(EscapeKeyword(identifier))
                    .Append(" = \"")
                    .Append(EscapeStringLiteral(sorted[i]))
                    .AppendLine("\";");
            }
            builder.AppendLine("        }");
        }

        private static void AppendActionConstants(
            StringBuilder builder,
            List<GeneratedAction> actions)
        {
            actions.Sort(GeneratedActionComparer.Instance);
            var identifiers = new HashSet<string>(StringComparer.Ordinal);

            builder.AppendLine("        public static class Actions");
            builder.AppendLine("        {");
            for (int i = 0; i < actions.Count; i++)
            {
                GeneratedAction action = actions[i];
                string identifier = GetUniqueIdentifier(SanitizeIdentifier(action.IdentifierBase), identifiers);
                builder.Append("            public const int ")
                    .Append(EscapeKeyword(identifier))
                    .Append(" = ")
                    .Append(action.ActionId.ToString(CultureInfo.InvariantCulture))
                    .Append("; // FNV-1a32: ")
                    .Append(action.Context)
                    .Append('/')
                    .Append(action.ActionMap)
                    .Append('/')
                    .AppendLine(action.Action);
            }
            builder.AppendLine("        }");
        }

        private static bool TryFormatNamespace(
            string requestedNamespace,
            out string formattedNamespace,
            out string error)
        {
            formattedNamespace = null;
            error = null;

            if (string.IsNullOrWhiteSpace(requestedNamespace) || requestedNamespace.Length > 512)
            {
                error = "Enter a valid namespace with at most 512 characters.";
                return false;
            }

            if (!ValidateGeneratedText(requestedNamespace, "Namespace", out error))
            {
                return false;
            }

            string[] segments = requestedNamespace.Split('.');
            var builder = new StringBuilder(requestedNamespace.Length + segments.Length);
            for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                string segment = segments[segmentIndex];
                if (!IsIdentifier(segment))
                {
                    error = $"Namespace segment \"{segment}\" is not a valid C# identifier.";
                    return false;
                }

                if (segmentIndex > 0)
                {
                    builder.Append('.');
                }
                builder.Append(EscapeKeyword(segment));
            }

            formattedNamespace = builder.ToString();
            return true;
        }

        private static bool ValidateGeneratedText(string value, string label, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(value) || value.Length > 512)
            {
                error = $"{label} must contain 1..512 visible characters.";
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (char.IsHighSurrogate(character))
                {
                    if (i + 1 >= value.Length || !char.IsLowSurrogate(value[i + 1]))
                    {
                        error = $"{label} contains an unpaired surrogate at index {i}.";
                        return false;
                    }

                    UnicodeCategory pairCategory = CharUnicodeInfo.GetUnicodeCategory(value, i);
                    if (pairCategory == UnicodeCategory.Format ||
                        pairCategory == UnicodeCategory.PrivateUse ||
                        pairCategory == UnicodeCategory.LineSeparator ||
                        pairCategory == UnicodeCategory.ParagraphSeparator)
                    {
                        error = $"{label} contains an unsupported character at index {i}.";
                        return false;
                    }

                    i++;
                    continue;
                }

                UnicodeCategory category = char.GetUnicodeCategory(character);
                if (char.IsLowSurrogate(character) ||
                    char.IsControl(character) ||
                    category == UnicodeCategory.Format ||
                    category == UnicodeCategory.LineSeparator ||
                    category == UnicodeCategory.ParagraphSeparator ||
                    category == UnicodeCategory.PrivateUse)
                {
                    error = $"{label} contains an unsupported character at index {i}.";
                    return false;
                }
            }

            return true;
        }

        private static bool IsIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 128 || !IsIdentifierStart(value[0]))
            {
                return false;
            }

            for (int i = 1; i < value.Length; i++)
            {
                if (!IsIdentifierPart(value[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static string SanitizeIdentifier(string value)
        {
            var builder = new StringBuilder(value.Length + 1);
            if (!IsIdentifierStart(value[0]))
            {
                builder.Append('_');
            }

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                builder.Append(IsIdentifierPart(character) ? character : '_');
            }

            return builder.Length == 0 ? "_" : builder.ToString();
        }

        private static string EscapeStringLiteral(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string EscapeKeyword(string identifier)
        {
            return Keywords.Contains(identifier) ? "@" + identifier : identifier;
        }

        private static string GetUniqueIdentifier(string baseIdentifier, HashSet<string> used)
        {
            string candidate = baseIdentifier;
            int suffix = 1;
            while (!used.Add(candidate))
            {
                candidate = string.Concat(
                    baseIdentifier,
                    "_",
                    suffix.ToString(CultureInfo.InvariantCulture));
                suffix++;
            }

            return candidate;
        }

        private static bool IsIdentifierStart(char character)
        {
            return character == '_' || char.IsLetter(character);
        }

        private static bool IsIdentifierPart(char character)
        {
            return character == '_' || char.IsLetterOrDigit(character);
        }

        private sealed class GeneratedAction
        {
            internal GeneratedAction(
                string identifierBase,
                string context,
                string actionMap,
                string action,
                int actionId)
            {
                IdentifierBase = identifierBase;
                Context = context;
                ActionMap = actionMap;
                Action = action;
                ActionId = actionId;
            }

            internal string IdentifierBase { get; }
            internal string Context { get; }
            internal string ActionMap { get; }
            internal string Action { get; }
            internal int ActionId { get; }
        }

        private sealed class GeneratedActionComparer : IComparer<GeneratedAction>
        {
            internal static readonly GeneratedActionComparer Instance = new GeneratedActionComparer();

            public int Compare(GeneratedAction left, GeneratedAction right)
            {
                if (ReferenceEquals(left, right))
                {
                    return 0;
                }
                if (left == null)
                {
                    return -1;
                }
                if (right == null)
                {
                    return 1;
                }

                int contextComparison = string.Compare(left.Context, right.Context, StringComparison.Ordinal);
                if (contextComparison != 0)
                {
                    return contextComparison;
                }

                int mapComparison = string.Compare(left.ActionMap, right.ActionMap, StringComparison.Ordinal);
                if (mapComparison != 0)
                {
                    return mapComparison;
                }

                int actionComparison = string.Compare(left.Action, right.Action, StringComparison.Ordinal);
                return actionComparison != 0
                    ? actionComparison
                    : string.Compare(left.IdentifierBase, right.IdentifierBase, StringComparison.Ordinal);
            }
        }
    }
}
