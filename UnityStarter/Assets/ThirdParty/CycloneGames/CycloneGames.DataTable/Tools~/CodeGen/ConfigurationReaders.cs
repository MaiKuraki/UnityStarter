using System;
using System.IO;
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

    }
}
