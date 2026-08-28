using System.Runtime.CompilerServices;

// Grants the test assembly access to internal members (e.g. RefreshResolvedFolder,
// HealSourceFolderGuid). The name must match the CycloneGames.AtlasPipeline.Tests.Editor.asmdef
// "name" field exactly.
[assembly: InternalsVisibleTo("CycloneGames.AtlasPipeline.Tests.Editor")]
