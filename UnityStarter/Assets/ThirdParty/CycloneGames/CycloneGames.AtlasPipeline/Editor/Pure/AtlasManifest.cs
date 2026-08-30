using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CycloneGames.AtlasPipeline.Pure
{
    /// <summary>
    /// One atlas as recorded after generation: what it should contain and the fingerprint of that
    /// content. The manifest is the contract between developer machines and CI.
    /// </summary>
    public sealed class AtlasManifestEntry
    {
        public AtlasManifestEntry(
            string atlasKey,
            string outputPath,
            int spriteCount,
            long contentHash,
            int pageCount,
            int ruleId)
        {
            AtlasKey = atlasKey ?? string.Empty;
            OutputPath = outputPath ?? string.Empty;
            SpriteCount = spriteCount < 0 ? 0 : spriteCount;
            ContentHash = contentHash;
            PageCount = pageCount < 1 ? 1 : pageCount;
            RuleId = ruleId;
        }

        public string AtlasKey { get; }
        public string OutputPath { get; }
        public int SpriteCount { get; }
        public long ContentHash { get; }
        public int PageCount { get; }
        public int RuleId { get; }
    }

    /// <summary>
    /// Snapshot of every atlas a given ruleset should produce. Written next to the generated atlases
    /// so a CI job can answer "are the committed atlases up to date" without regenerating them,
    /// which keeps CI fast and keeps the build workspace clean.
    /// </summary>
    public sealed class AtlasManifest
    {
        public const int CurrentSchemaVersion = 1;

        public AtlasManifest(
            int schemaVersion,
            string generatorVersion,
            long settingsFingerprint,
            IList<AtlasManifestEntry> entries)
        {
            SchemaVersion = schemaVersion;
            GeneratorVersion = generatorVersion ?? string.Empty;
            SettingsFingerprint = settingsFingerprint;
            Entries = entries ?? new List<AtlasManifestEntry>();
        }

        public int SchemaVersion { get; }
        public string GeneratorVersion { get; }
        public long SettingsFingerprint { get; }
        public IList<AtlasManifestEntry> Entries { get; }
    }

    /// <summary>
    /// Difference between a recorded manifest and the state a machine currently computes.
    /// </summary>
    public sealed class AtlasManifestDelta
    {
        public AtlasManifestDelta(
            IReadOnlyList<string> added,
            IReadOnlyList<string> removed,
            IReadOnlyList<string> changed)
        {
            Added = added ?? new List<string>();
            Removed = removed ?? new List<string>();
            Changed = changed ?? new List<string>();
        }

        /// <summary>Atlases the current state needs but the recorded manifest does not have.</summary>
        public IReadOnlyList<string> Added { get; }

        /// <summary>Atlases in the recorded manifest that no longer exist.</summary>
        public IReadOnlyList<string> Removed { get; }

        /// <summary>Atlases whose content fingerprint changed: members or governing configuration.</summary>
        public IReadOnlyList<string> Changed { get; }

        public bool IsUpToDate => Added.Count == 0 && Removed.Count == 0 && Changed.Count == 0;

        public int DifferenceCount => Added.Count + Removed.Count + Changed.Count;
    }

    public static class AtlasManifestComparer
    {
        /// <summary>
        /// Compares the recorded manifest against the freshly computed one. Comparison is by atlas
        /// key and content fingerprint only; file existence is deliberately not checked here so the
        /// same call can serve both "is the manifest stale" (CI) and "did generation drift" (local).
        /// </summary>
        public static AtlasManifestDelta Compare(AtlasManifest recorded, AtlasManifest current)
        {
            var added = new List<string>();
            var removed = new List<string>();
            var changed = new List<string>();

            if (recorded == null || current == null)
            {
                return new AtlasManifestDelta(added, removed, changed);
            }

            var recordedHashes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            IList<AtlasManifestEntry> recordedEntries = recorded.Entries;
            for (int i = 0; i < recordedEntries.Count; i++)
            {
                AtlasManifestEntry entry = recordedEntries[i];
                recordedHashes[entry.AtlasKey] = entry.ContentHash;
            }

            var currentHashes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            IList<AtlasManifestEntry> currentEntries = current.Entries;
            for (int i = 0; i < currentEntries.Count; i++)
            {
                AtlasManifestEntry entry = currentEntries[i];
                currentHashes[entry.AtlasKey] = entry.ContentHash;

                if (!recordedHashes.TryGetValue(entry.AtlasKey, out long previous))
                {
                    continue;
                }

                if (previous != entry.ContentHash)
                {
                    changed.Add(entry.AtlasKey);
                }
            }

            foreach (KeyValuePair<string, long> entry in recordedHashes)
            {
                if (!currentHashes.ContainsKey(entry.Key))
                {
                    removed.Add(entry.Key);
                }
            }

            foreach (KeyValuePair<string, long> entry in currentHashes)
            {
                if (!recordedHashes.ContainsKey(entry.Key))
                {
                    added.Add(entry.Key);
                }
            }

            added.Sort(StringComparer.Ordinal);
            removed.Sort(StringComparer.Ordinal);
            changed.Sort(StringComparer.Ordinal);
            return new AtlasManifestDelta(added, removed, changed);
        }
    }

    /// <summary>
    /// Line-oriented manifest serialization. A custom format is used instead of JSON on purpose:
    /// the file is committed and reviewed, so it must produce stable, minimal, line-granular diffs
    /// when one atlas changes. A serialized JSON array rewrites and re-indents on every change and
    /// turns every merge into a conflict.
    /// Newlines are always LF, never <see cref="Environment.NewLine"/>, so a manifest written on
    /// Windows and one written on macOS or a Linux CI agent are byte-identical.
    /// </summary>
    public static class AtlasManifestSerializer
    {
        private const char FieldSeparator = '\t';
        private const string SchemaKey = "schema";
        private const string GeneratorKey = "generator";
        private const string SettingsKey = "settings";
        private const string AtlasKey = "atlas";

        public static string Write(AtlasManifest manifest)
        {
            if (manifest == null)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(256 + (manifest.Entries.Count * 96));
            builder.Append("# CycloneGames atlas manifest. Generated file - do not edit by hand.");
            builder.Append('\n');
            builder.Append(SchemaKey).Append('=')
                .Append(manifest.SchemaVersion.ToString(CultureInfo.InvariantCulture))
                .Append('\n');
            builder.Append(GeneratorKey).Append('=').Append(manifest.GeneratorVersion)
                .Append('\n');
            builder.Append(SettingsKey).Append('=')
                .Append(AtlasHash.ToHex(manifest.SettingsFingerprint)).Append('\n');

            var ordered = new List<AtlasManifestEntry>(manifest.Entries);
            ordered.Sort(
                (left, right) => string.CompareOrdinal(left.AtlasKey, right.AtlasKey));

            for (int i = 0; i < ordered.Count; i++)
            {
                AtlasManifestEntry entry = ordered[i];
                builder.Append(AtlasKey).Append('=')
                    .Append(entry.AtlasKey).Append(FieldSeparator)
                    .Append(entry.OutputPath).Append(FieldSeparator)
                    .Append(entry.SpriteCount.ToString(CultureInfo.InvariantCulture))
                    .Append(FieldSeparator)
                    .Append(AtlasHash.ToHex(entry.ContentHash)).Append(FieldSeparator)
                    .Append(entry.PageCount.ToString(CultureInfo.InvariantCulture))
                    .Append(FieldSeparator)
                    .Append(entry.RuleId.ToString(CultureInfo.InvariantCulture))
                    .Append('\n');
            }

            return builder.ToString();
        }

        /// <summary>
        /// Tolerant reader: unknown keys, blank lines and comments are ignored, and a malformed atlas
        /// line is reported through <paramref name="errors"/> rather than aborting the read. A
        /// partially corrupted manifest must still let CI report the atlases it could parse.
        /// </summary>
        public static AtlasManifest Read(string text, ICollection<string> errors = null)
        {
            if (string.IsNullOrEmpty(text))
            {
                return new AtlasManifest(
                    AtlasManifest.CurrentSchemaVersion,
                    string.Empty,
                    AtlasHash.NullHash,
                    new List<AtlasManifestEntry>());
            }

            int schema = AtlasManifest.CurrentSchemaVersion;
            string generator = string.Empty;
            long settings = AtlasHash.NullHash;
            var entries = new List<AtlasManifestEntry>();

            int position = 0;
            int length = text.Length;
            while (position < length)
            {
                int end = text.IndexOf('\n', position);
                if (end < 0)
                {
                    end = length;
                }

                int lineEnd = end;
                if (lineEnd > position && text[lineEnd - 1] == '\r')
                {
                    lineEnd--;
                }

                string line = text.Substring(position, lineEnd - position);
                position = end + 1;

                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    continue;
                }

                string key = line.Substring(0, separator);
                string value = line.Substring(separator + 1);

                if (string.Equals(key, SchemaKey, StringComparison.Ordinal))
                {
                    if (int.TryParse(
                            value,
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int parsed))
                    {
                        schema = parsed;
                    }

                    continue;
                }

                if (string.Equals(key, GeneratorKey, StringComparison.Ordinal))
                {
                    generator = value;
                    continue;
                }

                if (string.Equals(key, SettingsKey, StringComparison.Ordinal))
                {
                    settings = TryParseHex(value);
                    continue;
                }

                if (!string.Equals(key, AtlasKey, StringComparison.Ordinal))
                {
                    continue;
                }

                string[] fields = value.Split(FieldSeparator);
                if (fields.Length < 6)
                {
                    errors?.Add($"Manifest atlas line has {fields.Length} field(s), expected 6: '{line}'.");
                    continue;
                }

                if (!int.TryParse(
                        fields[2],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int spriteCount)
                    || !int.TryParse(
                        fields[4],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int pageCount)
                    || !int.TryParse(
                        fields[5],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out int ruleId))
                {
                    errors?.Add($"Manifest atlas line has non-numeric fields: '{line}'.");
                    continue;
                }

                entries.Add(new AtlasManifestEntry(
                    fields[0],
                    fields[1],
                    spriteCount,
                    TryParseHex(fields[3]),
                    pageCount,
                    ruleId));
            }

            return new AtlasManifest(schema, generator, settings, entries);
        }

        private static long TryParseHex(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return AtlasHash.NullHash;
            }

#if NET6_0_OR_GREATER
            return long.Parse(value.AsSpan(), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
#else
            return long.TryParse(
                value,
                NumberStyles.HexNumber,
                CultureInfo.InvariantCulture,
                out long parsed)
                ? parsed
                : AtlasHash.NullHash;
#endif
        }
    }
}
