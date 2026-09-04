using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayTags.DataTable.Tests.Editor
{
    /// <summary>
    /// Editor test host for the DataTable integration fixtures.
    /// </summary>
    /// <remarks>
    /// Extends <see cref="GameplayTagHostPlatformBase"/>, so it accepts the project tag sources under
    /// test through <see cref="GameplayTagHost.RegisterProjectTagSource"/> while supplying no build data
    /// and reporting not-playing - exactly the host facts a designer-authored table flow has before the
    /// baked manifest exists.
    /// </remarks>
    public sealed class GameplayTagDataTableTestPlatform : GameplayTagHostPlatformBase
    {
        public override string Name => "DataTable.Test";
    }
}
