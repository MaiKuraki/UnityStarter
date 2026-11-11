namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// The measured configuration matrix. Each group varies a single axis so that the cost contribution of that
    /// axis can be read directly from the table instead of being inferred from a full grid.
    /// </summary>
    internal static class PerfMatrix
    {
        /// <summary>
        /// Player-count scaling at a fixed per-player action load.
        /// </summary>
        internal static readonly PerfScenario[] PlayerScaling =
        {
            new("P1", players: 1, contexts: 3, actionsPerContext: 64, pollingPercent: 50, DeviceScenario.Gamepad),
            new("P2", players: 2, contexts: 3, actionsPerContext: 64, pollingPercent: 50, DeviceScenario.Gamepad),
            new("P4", players: 4, contexts: 3, actionsPerContext: 64, pollingPercent: 50, DeviceScenario.Gamepad),
            new("P8", players: 8, contexts: 3, actionsPerContext: 64, pollingPercent: 50, DeviceScenario.Gamepad)
        };

        /// <summary>
        /// Action-count scaling at a fixed player count.
        /// </summary>
        internal static readonly PerfScenario[] ActionScaling =
        {
            new("A10", players: 2, contexts: 3, actionsPerContext: 10, pollingPercent: 50, DeviceScenario.Gamepad),
            new("A64", players: 2, contexts: 3, actionsPerContext: 64, pollingPercent: 50, DeviceScenario.Gamepad),
            new("A256", players: 2, contexts: 3, actionsPerContext: 256, pollingPercent: 50, DeviceScenario.Gamepad)
        };

        /// <summary>
        /// Context-depth scaling, with lower-priority blocking enabled so that active-context projection is
        /// exercised in both directions.
        /// </summary>
        internal static readonly PerfScenario[] ContextScaling =
        {
            new("C1", players: 2, contexts: 1, actionsPerContext: 64, pollingPercent: 50, DeviceScenario.Gamepad),
            new("C3", players: 2, contexts: 3, actionsPerContext: 64, pollingPercent: 50, DeviceScenario.Gamepad),
            new("C6", players: 2, contexts: 6, actionsPerContext: 64, pollingPercent: 50, DeviceScenario.Gamepad),
            new("C6-blocks", players: 2, contexts: 6, actionsPerContext: 64, pollingPercent: 50, DeviceScenario.Gamepad, blocksLowerPriority: true)
        };

        /// <summary>
        /// Polling-ratio scaling. This is the axis that drives the module's own per-frame pump.
        /// </summary>
        internal static readonly PerfScenario[] PollingScaling =
        {
            new("Q0", players: 2, contexts: 3, actionsPerContext: 64, pollingPercent: 0, DeviceScenario.Gamepad),
            new("Q50", players: 2, contexts: 3, actionsPerContext: 64, pollingPercent: 50, DeviceScenario.Gamepad),
            new("Q100", players: 2, contexts: 3, actionsPerContext: 64, pollingPercent: 100, DeviceScenario.Gamepad)
        };

        /// <summary>
        /// Device-kind scaling. Keyboard/mouse introduces many more distinct controls per action map than a
        /// gamepad does, so this also exercises control-path resolution.
        /// </summary>
        internal static readonly PerfScenario[] DeviceScaling =
        {
            new("D-kbm", players: 2, contexts: 3, actionsPerContext: 64, pollingPercent: 50, DeviceScenario.KeyboardMouse),
            new("D-pad", players: 2, contexts: 3, actionsPerContext: 64, pollingPercent: 50, DeviceScenario.Gamepad),
            new("D-mixed", players: 2, contexts: 3, actionsPerContext: 64, pollingPercent: 50, DeviceScenario.Mixed)
        };

        internal static readonly PerfScenario[] All = BuildAll();

        private static PerfScenario[] BuildAll()
        {
            var all = new System.Collections.Generic.List<PerfScenario>();
            all.AddRange(PlayerScaling);
            all.AddRange(ActionScaling);
            all.AddRange(ContextScaling);
            all.AddRange(PollingScaling);
            all.AddRange(DeviceScaling);
            return all.ToArray();
        }
    }
}
