using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private readonly struct LubanTarget
        {
            public LubanTarget(string name, string topModule)
            {
                Name = name;
                TopModule = topModule;
            }

            public string Name { get; }
            public string TopModule { get; }
        }

        private static class LubanConf
        {
            public static LubanTarget ReadTarget(string path, string targetName)
            {
                ValidateFileSize(path, MAX_CONFIG_FILE_BYTES, "Luban configuration");
                using FileStream stream = File.OpenRead(path);
                using JsonDocument document = JsonDocument.Parse(
                    stream,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip,
                        MaxDepth = 64,
                    });

                JsonElement targets = document.RootElement.GetProperty("targets");
                foreach (JsonElement target in targets.EnumerateArray())
                {
                    string? name = target.GetProperty("name").GetString();
                    if (!string.Equals(name, targetName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string topModule = target.TryGetProperty("topModule", out JsonElement topModuleValue)
                        ? topModuleValue.GetString() ?? string.Empty
                        : string.Empty;
                    return new LubanTarget(name ?? targetName, topModule);
                }

                throw new InvalidOperationException($"Luban target not found: {targetName}");
            }
        }

        private static class IniFile
        {
            public static Dictionary<string, string> Read(string path)
            {
                ValidateFileSize(path, MAX_CONFIG_FILE_BYTES, "build configuration");
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using var reader = new StreamReader(
                    path,
                    new UTF8Encoding(false, true),
                    true,
                    4096);
                int lineNumber = 0;
                string? rawLine;
                while ((rawLine = reader.ReadLine()) != null)
                {
                    lineNumber++;
                    if (lineNumber > MAX_CONFIG_LINES)
                    {
                        throw new InvalidOperationException(
                            $"Build configuration exceeds the {MAX_CONFIG_LINES}-line limit: {path}");
                    }

                    if (rawLine.Length > MAX_CONFIG_LINE_CHARACTERS)
                    {
                        throw new InvalidOperationException(
                            $"Build configuration line {lineNumber} exceeds the {MAX_CONFIG_LINE_CHARACTERS}-character limit.");
                    }

                    string line = rawLine.Trim();
                    if (line.Length == 0 ||
                        line[0] == '#' ||
                        line[0] == ';' ||
                        line[0] == '[')
                    {
                        continue;
                    }

                    int separatorIndex = line.IndexOf('=');
                    if (separatorIndex <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separatorIndex).Trim();
                    string value = line.Substring(separatorIndex + 1).Trim();
                    values[key] = value;
                }

                return values;
            }
        }
    }
}
