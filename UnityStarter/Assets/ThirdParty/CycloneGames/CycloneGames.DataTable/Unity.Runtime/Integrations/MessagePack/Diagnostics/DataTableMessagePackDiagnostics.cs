namespace CycloneGames.DataTable.Unity.Integrations.MessagePack
{
    internal static class DataTableMessagePackDiagnostics
    {
        internal const string Category = "CycloneGames.DataTable.MessagePack";

        internal static readonly DataTableDiagnosticChannel Channel =
            DataTableDiagnosticChannel.Create(Category);
    }
}
