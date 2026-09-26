using System.Runtime.CompilerServices;

// The TMP editor slice is a separate conditionally compiled assembly, so it cannot reach the
// internal log facade owned by this assembly without an explicit friend declaration.
[assembly: InternalsVisibleTo("CycloneGames.UIFramework.Editor.Integrations.Localization.TextMeshPro")]
