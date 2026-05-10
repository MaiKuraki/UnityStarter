using System;

namespace CycloneGames.DataTable.Unity.Integrations.Luban
{
    /// <summary>
    /// Registers Luban-generated table instances into DataTableRegistry.
    /// <para>
    /// This class does NOT load anything. You are responsible for loading
    /// .bytes files and constructing Luban's generated Tables object:
    /// <code>
    /// var bytes = await YourAssetPipeline.LoadAsync("item.bytes");
    /// var tables = new Tables(fileName => new ByteBuf(YourAssetPipeline.LoadSync(fileName)));
    /// LubanConfigProvider.RegisterAll(tables);
    /// </code>
    /// </para>
    /// <para>
    /// After registration, query via DataTableRegistry.Get&lt;TbItem&gt;().
    /// </para>
    /// </summary>
    public static class LubanConfigProvider
    {
        /// <summary>
        /// Register a single Luban-generated table. Thin wrapper over DataTableRegistry.Register.
        /// </summary>
        public static void RegisterLubanTable<TTable>(TTable table) where TTable : class
        {
            DataTableRegistry.Register(table);
        }

        /// <summary>
        /// Register multiple Luban-generated tables at once via reflection.
        /// Prefer calling RegisterLubanTable individually when possible (AOT-safe, no reflection).
        /// <para>
        /// Usage: <c>LubanConfigProvider.RegisterLubanTables((typeof(TbItem), tables.TbItem), (typeof(TbSkill), tables.TbSkill))</c>
        /// </para>
        /// </summary>
        public static void RegisterLubanTables(params (Type type, object table)[] tables)
        {
            foreach (var (type, table) in tables)
            {
                var method = typeof(DataTableRegistry).GetMethod(nameof(DataTableRegistry.Register));
                var generic = method?.MakeGenericMethod(type);
                generic?.Invoke(null, new[] { table });
            }
        }
    }
}
