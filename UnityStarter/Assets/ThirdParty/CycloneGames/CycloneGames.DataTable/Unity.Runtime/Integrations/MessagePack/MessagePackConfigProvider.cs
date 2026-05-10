using System;
using System.Collections.Generic;
using MessagePack;

namespace CycloneGames.DataTable.Unity.Integrations.MessagePack
{
    /// <summary>
    /// Builds DataTable&lt;TRow&gt; from MessagePack-serialized bytes and registers
    /// them into DataTableRegistry.
    /// <para>
    /// This class does NOT load anything. You are responsible for loading
    /// .bytes files via your own asset pipeline (YooAsset, Addressables, etc.).
    /// Once you have the raw bytes, call Build to create and register the table.
    /// </para>
    /// <para>
    /// Usage:
    /// <code>
    /// var bytes = await YourAssetPipeline.LoadAsync("monster.bytes");
    /// MessagePackConfigProvider.Build&lt;MonsterRow&gt;(bytes);
    /// var slime = DataTableRegistry.Get&lt;DataTable&lt;MonsterRow&gt;&gt;().Get(1);
    /// </code>
    /// </para>
    /// </summary>
    public static class MessagePackConfigProvider
    {
        /// <summary>
        /// Deserialize MessagePack bytes into a DataTable&lt;TRow&gt; and register it.
        /// Returns the registered table instance.
        /// </summary>
        /// <typeparam name="TRow">Row type implementing IDataRow, annotated with [MessagePackObject] and [Key].</typeparam>
        /// <param name="bytes">Raw MessagePack bytes containing a serialized List&lt;TRow&gt;.</param>
        /// <returns>The registered DataTable&lt;TRow&gt; instance.</returns>
        public static DataTable<TRow> Build<TRow>(byte[] bytes)
            where TRow : IDataRow
        {
            if (bytes == null || bytes.Length == 0)
                throw new ArgumentException(
                    $"Bytes for DataTable<{typeof(TRow).Name}> is null or empty.");

            List<TRow> rows;
            try
            {
                rows = MessagePackSerializer.Deserialize<List<TRow>>(bytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to deserialize MessagePack data for {typeof(TRow).Name}. " +
                    "Ensure TRow has [MessagePackObject] and [Key] attributes.", ex);
            }

            if (rows == null)
                rows = new List<TRow>(0);

            if (rows.Count == 0)
                DataTableLogger.LogWarning(
                    $"Deserialized DataTable<{typeof(TRow).Name}> is empty.");

            var table = new DataTable<TRow>(rows);
            DataTableRegistry.Register(table);
            DataTableLogger.LogInfo(
                $"Registered DataTable<{typeof(TRow).Name}> ({table.Count} rows).");

            return table;
        }
    }
}
