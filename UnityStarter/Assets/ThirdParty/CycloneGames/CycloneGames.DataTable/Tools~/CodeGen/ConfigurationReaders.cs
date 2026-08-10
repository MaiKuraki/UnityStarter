using System;
using System.Collections.Generic;
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
            private const int MaximumSchemaSources = 1024;

            public static LubanTarget ReadTarget(string path, string targetName)
            {
                using JsonDocument document = ReadDocument(path);

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

            public static string[] ReadSchemaSources(string path)
            {
                using JsonDocument document = ReadDocument(path);
                LubanSchemaSource[] declarations = ReadSchemaSourceDeclarations(document);
                var results = new string[declarations.Length];
                for (int index = 0; index < declarations.Length; index++)
                {
                    results[index] = declarations[index].FileName;
                }

                return results;
            }

            public static string[] ReadTableSchemaSources(string path)
            {
                using JsonDocument document = ReadDocument(path);
                LubanSchemaSource[] declarations = ReadSchemaSourceDeclarations(document);
                var results = new List<string>();
                foreach (LubanSchemaSource declaration in declarations)
                {
                    if (declaration.Type.Length == 0)
                    {
                        throw new InvalidOperationException(
                            "The DataTable pipeline safety profile requires every Luban schemaFiles item " +
                            "to declare a non-empty type. Pinned Luban XML sources can declare tables " +
                            "outside the bounded XLSX table-input manifest.");
                    }

                    if (string.Equals(declaration.Type, "table", StringComparison.Ordinal))
                    {
                        results.Add(declaration.FileName);
                        continue;
                    }

                    if (!string.Equals(declaration.Type, "bean", StringComparison.Ordinal) &&
                        !string.Equals(declaration.Type, "enum", StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "The DataTable pipeline safety profile supports only explicit table, bean, " +
                            "and enum schemaFiles types: " + declaration.Type);
                    }
                }

                return results.ToArray();
            }

            private static LubanSchemaSource[] ReadSchemaSourceDeclarations(JsonDocument document)
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("schemaFiles", out JsonElement schemaFiles) ||
                    schemaFiles.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException(
                        "Luban configuration must contain a schemaFiles array.");
                }

                var results = new List<LubanSchemaSource>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (JsonElement schemaFile in schemaFiles.EnumerateArray())
                {
                    if (results.Count >= MaximumSchemaSources)
                    {
                        throw new InvalidOperationException(
                            $"Luban configuration exceeds the {MaximumSchemaSources} schema-source limit.");
                    }

                    if (schemaFile.ValueKind != JsonValueKind.Object ||
                        !schemaFile.TryGetProperty("fileName", out JsonElement fileNameElement) ||
                        fileNameElement.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidOperationException(
                            "Every Luban schemaFiles item must contain a string fileName.");
                    }

                    string fileName = fileNameElement.GetString() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(fileName) ||
                        !string.Equals(fileName, fileName.Trim(), StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Luban schemaFiles fileName values must be non-empty and have no surrounding whitespace.");
                    }

                    if (!seen.Add(fileName))
                    {
                        throw new InvalidOperationException(
                            "Luban schemaFiles contains a duplicate or case-colliding fileName: " + fileName);
                    }

                    string type = string.Empty;
                    if (schemaFile.TryGetProperty("type", out JsonElement typeElement))
                    {
                        if (typeElement.ValueKind != JsonValueKind.String)
                        {
                            throw new InvalidOperationException(
                                "Luban schemaFiles type values must be strings.");
                        }

                        type = typeElement.GetString() ?? string.Empty;
                        if (!string.Equals(type, type.Trim(), StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                "Luban schemaFiles type values must have no surrounding whitespace.");
                        }
                    }

                    results.Add(new LubanSchemaSource(fileName, type));
                }

                return results.ToArray();
            }

            public static string ReadDataDirectory(string path)
            {
                using JsonDocument document = ReadDocument(path);
                if (document.RootElement.ValueKind != JsonValueKind.Object ||
                    !document.RootElement.TryGetProperty("dataDir", out JsonElement dataDirectoryElement) ||
                    dataDirectoryElement.ValueKind != JsonValueKind.String)
                {
                    throw new InvalidOperationException(
                        "Luban configuration must contain a string dataDir.");
                }

                string dataDirectory = dataDirectoryElement.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(dataDirectory) ||
                    !string.Equals(dataDirectory, dataDirectory.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "Luban dataDir must be non-empty and have no surrounding whitespace.");
                }

                return dataDirectory;
            }

            private static JsonDocument ReadDocument(string path)
            {
                ValidateFileSize(path, MAX_CONFIG_FILE_BYTES, "Luban configuration");
                using FileStream stream = File.OpenRead(path);
                JsonDocument document = JsonDocument.Parse(
                    stream,
                    new JsonDocumentOptions
                    {
                        AllowTrailingCommas = true,
                        CommentHandling = JsonCommentHandling.Skip,
                        MaxDepth = 64,
                    });
                try
                {
                    RejectDuplicateProperties(document.RootElement);
                    return document;
                }
                catch
                {
                    document.Dispose();
                    throw;
                }
            }

            private static void RejectDuplicateProperties(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (JsonProperty property in element.EnumerateObject())
                    {
                        if (!properties.Add(property.Name))
                        {
                            throw new InvalidOperationException(
                                "Luban configuration contains a duplicate or case-colliding JSON property: " +
                                property.Name);
                        }

                        RejectDuplicateProperties(property.Value);
                    }

                    return;
                }

                if (element.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                foreach (JsonElement item in element.EnumerateArray())
                {
                    RejectDuplicateProperties(item);
                }
            }

            private readonly struct LubanSchemaSource
            {
                public LubanSchemaSource(string fileName, string type)
                {
                    FileName = fileName;
                    Type = type;
                }

                public string FileName { get; }
                public string Type { get; }
            }
        }

    }
}
