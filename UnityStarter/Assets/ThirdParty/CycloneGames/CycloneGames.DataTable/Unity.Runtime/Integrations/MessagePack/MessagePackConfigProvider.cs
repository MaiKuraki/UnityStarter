using System;
using System.Collections.Generic;
using MessagePack;

namespace CycloneGames.DataTable.Unity.Integrations.MessagePack
{
    /// <summary>
    /// Builds DataTable<TRow> from MessagePack-serialized bytes and registers
    /// it into DataTableRegistry.
    /// <para>
    /// This class does NOT load anything. You are responsible for loading
    /// .bytes files via your own asset pipeline (YooAsset, Addressables, etc.).
    /// Once you have the raw bytes, call Build to create and register the table.
    /// </para>
    /// <para>
    /// Usage:
    /// <code>
    /// var bytes = await YourAssetPipeline.LoadAsync("monster.bytes");
    /// MessagePackConfigProvider.Build<MonsterRow>(bytes, GeneratedResolverOptions);
    /// var slime = DataTableRegistry.Get<DataTable<MonsterRow>>().Get(1);
    /// </code>
    /// </para>
    /// </summary>
    public static class MessagePackConfigProvider
    {
        /// <summary>
        /// Deserialize MessagePack bytes containing a TRow[] payload into a DataTable<TRow> and register it.
        /// Returns the registered table instance.
        /// </summary>
        /// <typeparam name="TRow">Row type implementing IDataRow, annotated for the project's MessagePack resolver.</typeparam>
        /// <param name="bytes">Raw MessagePack bytes containing a serialized TRow[].</param>
        /// <param name="options">Explicit MessagePack options. Pass the generated resolver options for IL2CPP/AOT builds.</param>
        /// <returns>The registered DataTable<TRow> instance.</returns>
        public static DataTable<TRow> Build<TRow>(
            byte[] bytes,
            MessagePackSerializerOptions options = null)
            where TRow : IDataRow
        {
            ValidateBytes<TRow>(bytes);

            TRow[] rows;
            try
            {
                rows = MessagePackSerializer.Deserialize<TRow[]>(
                    bytes,
                    ResolveOptions(options));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to deserialize MessagePack data for {typeof(TRow).Name}. " +
                    "Ensure the payload is TRow[] and the MessagePack options include the generated resolver.", ex);
            }

            return RegisterRows(rows ?? Array.Empty<TRow>());
        }

        /// <summary>
        /// Deserialize legacy MessagePack bytes containing a List<TRow> payload.
        /// Prefer Build for new generated data because TRow[] can be registered without an extra copy.
        /// </summary>
        public static DataTable<TRow> BuildList<TRow>(
            byte[] bytes,
            MessagePackSerializerOptions options = null)
            where TRow : IDataRow
        {
            ValidateBytes<TRow>(bytes);

            List<TRow> rows;
            try
            {
                rows = MessagePackSerializer.Deserialize<List<TRow>>(
                    bytes,
                    ResolveOptions(options));
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to deserialize legacy MessagePack list data for {typeof(TRow).Name}. " +
                    "Ensure the payload is List<TRow> and the MessagePack options include the generated resolver.", ex);
            }

            if (rows == null || rows.Count == 0)
            {
                return RegisterRows(Array.Empty<TRow>());
            }

            return RegisterRows(rows.ToArray());
        }

        /// <summary>
        /// Register already materialized rows. This is the lowest-allocation path after an external loader has decoded data.
        /// </summary>
        public static DataTable<TRow> RegisterRows<TRow>(TRow[] rows)
            where TRow : IDataRow
        {
            if (rows == null) throw new ArgumentNullException(nameof(rows));

            if (rows.Length == 0)
            {
                DataTableLogger.LogWarning(
                    $"Deserialized DataTable<{typeof(TRow).Name}> is empty.");
            }

            var table = new DataTable<TRow>(rows);
            DataTableRegistry.Register(table);
            DataTableLogger.LogInfo(
                $"Registered DataTable<{typeof(TRow).Name}> ({table.Count} rows).");

            return table;
        }

        private static void ValidateBytes<TRow>(byte[] bytes)
            where TRow : IDataRow
        {
            if (bytes == null || bytes.Length == 0)
            {
                throw new ArgumentException(
                    $"Bytes for DataTable<{typeof(TRow).Name}> is null or empty.",
                    nameof(bytes));
            }
        }

        private static MessagePackSerializerOptions ResolveOptions(MessagePackSerializerOptions options)
        {
            return options ?? MessagePackSerializer.DefaultOptions ?? MessagePackSerializerOptions.Standard;
        }
    }
}
