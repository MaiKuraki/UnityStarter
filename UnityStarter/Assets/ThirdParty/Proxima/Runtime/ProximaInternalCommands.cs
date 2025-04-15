using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Proxima
{
    internal class ProximaInternalCommands
    {
        [Serializable]
        private struct CommandsDump
        {
            public List<CommandDump> Commands;
        }

        [Serializable]
        private struct CommandDump
        {
            public string Name;
            public string Category;
            public string Alias;
            public string Description;
            public string ExampleInput;
            public string ExampleOutput;
            public List<ParameterDump> Parameters;
        }

        [Serializable]
        private struct ParameterDump
        {
            public string Name;
            public string Type;
            public string Default;
        }

        private static bool _paused;
        private static float _timeScale;

        private static string HelpCommand(string command)
        {
            if (!ProximaInspector.Commands.TryGetValue(command, out var method))
            {
                return "Unknown command: " + command;
            }

            var attr = method.GetCustomAttribute<ProximaCommandAttribute>();
            var sb = new StringBuilder();
            sb.AppendLine(method.Name);
            sb.AppendLine("Category: " + attr.Category);
            if (!string.IsNullOrWhiteSpace(attr.Alias))
            {
                sb.AppendLine("Alias: " + attr.Alias);
            }

            if (!string.IsNullOrWhiteSpace(attr.Description))
            {
                sb.AppendLine("Description: " + attr.Description);
            }

            var parameters = method.GetParameters();
            if (parameters.Length > 0)
            {
                sb.AppendLine("Parameters:");
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];

                sb.Append("  ");

                var type = parameter.ParameterType;
                var def = parameter.HasDefaultValue ? parameter.DefaultValue : null;
                if (typeof(IPropertyOrValue).IsAssignableFrom(parameter.ParameterType))
                {
                    type = parameter.ParameterType.GetGenericArguments()[0];
                    def = type.IsValueType ? Activator.CreateInstance(type) : "null";
                }

                sb.Append(parameter.Name);
                sb.Append(": ");
                sb.Append(type.Name);

                if (parameter.HasDefaultValue)
                {
                    sb.Append(" (default: ");
                    sb.Append(def);
                    sb.Append(")");
                }

                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(attr.ExampleInput))
            {
                sb.AppendLine();
                sb.AppendLine("Example:");
                sb.AppendLine("  > " + attr.ExampleInput);

                if (!string.IsNullOrWhiteSpace(attr.ExampleOutput))
                {
                    sb.AppendLine("  " + attr.ExampleOutput.Replace("\n", "\n  "));
                }
            }

            return sb.ToString();
        }

        private static string HelpAll()
        {
            var sb = new StringBuilder();
            var categorizedCommands = new Dictionary<string, List<(string, MethodInfo)>>();
            foreach (var kv in ProximaInspector.Commands)
            {
                var command = kv.Key;
                var method = kv.Value;
                var attr = method.GetCustomAttribute<ProximaCommandAttribute>();
                if (attr.Alias == command || attr.Category == "Internal")
                {
                    continue;
                }

                if (!categorizedCommands.TryGetValue(attr.Category, out var commands))
                {
                    commands = new List<(string, MethodInfo)>();
                    categorizedCommands.Add(attr.Category, commands);
                }

                commands.Add((command, method));
            }

            var sortedCategories = new List<string>(categorizedCommands.Keys);
            sortedCategories.Sort();
            foreach (var category in sortedCategories)
            {
                var commands = categorizedCommands[category];
                commands.Sort((a, b) => a.Item1.CompareTo(b.Item1));

                sb.AppendLine(category + " Commands:");
                foreach (var (command, method) in commands)
                {
                    var attr = method.GetCustomAttribute<ProximaCommandAttribute>();

                    var parameters = method.GetParameters();
                    sb.Append("  ");
                    sb.Append(command);
                    sb.Append(" ");

                    if (!string.IsNullOrEmpty(attr.Alias))
                    {
                        sb.Append("(");
                        sb.Append(attr.Alias);
                        sb.Append(") ");
                    }

                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var parameter = parameters[i];

                        if (parameter.HasDefaultValue)
                        {
                            sb.Append("<");
                            sb.Append(parameter.Name);
                            sb.Append(">");
                        }
                        else
                        {
                            sb.Append(parameter.Name);
                        }

                        if (i < parameters.Length - 1)
                        {
                            sb.Append(" ");
                        }
                    }

                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        [ProximaCommand("Internal", "?")]
        public static string Help(string command = null)
        {
            if (!string.IsNullOrWhiteSpace(command))
            {
                return HelpCommand(command);
            }
            else
            {
                return HelpAll();
            }
        }

        [ProximaCommand("Internal")]
        public static string Ping()
        {
            return "Pong";
        }

        [ProximaCommand("Internal")]
        public static bool IsPaused()
        {
            return _paused;
        }

        [ProximaCommand("Internal")]
        public static string Pause()
        {
            if (!_paused)
            {
                _timeScale = Time.timeScale;
                Time.timeScale = 0;
                _paused = true;
                return "Paused.";
            }

            return "Already paused.";
        }

        [ProximaCommand("Internal")]
        public static string Resume()
        {
            if (_paused)
            {
                Time.timeScale = _timeScale;
                _paused = false;
                return "Resumed.";
            }

            return "Already resume.";
        }

        [ProximaCommand("Internal")]
        public static object DumpCommands()
        {
            var dump = new CommandsDump {
                Commands = new List<CommandDump>()
            };

            foreach (var kv in ProximaInspector.Commands)
            {
                var name = kv.Key;
                var method = kv.Value;
                var attr = method.GetCustomAttribute<ProximaCommandAttribute>();
                if (attr.Category == "Internal") continue;
                if (attr.Alias == name) continue;

                var cd = new CommandDump {
                    Name = method.Name,
                    Category = attr.Category,
                    Alias = attr.Alias,
                    Description = attr.Description,
                    ExampleInput = attr.ExampleInput,
                    ExampleOutput = attr.ExampleOutput,
                    Parameters = new List<ParameterDump>()
                };

                var parameters = method.GetParameters();
                for (int i = 0; i < parameters.Length; i++)
                {
                    var parameter = parameters[i];
                    var type = parameter.ParameterType;
                    var def = parameter.HasDefaultValue ? parameter.DefaultValue : null;
                    if (typeof(IPropertyOrValue).IsAssignableFrom(parameter.ParameterType))
                    {
                        type = parameter.ParameterType.GetGenericArguments()[0];
                        def = type.IsValueType ? Activator.CreateInstance(type) : "null";
                    }

                    cd.Parameters.Add(new ParameterDump {
                        Name = parameter.Name,
                        Type = type.Name,
                        Default = def != null ? ProximaSerialization.Serialize(def) : null
                    });
                }

                dump.Commands.Add(cd);
            }

            return dump;
        }
    }
}