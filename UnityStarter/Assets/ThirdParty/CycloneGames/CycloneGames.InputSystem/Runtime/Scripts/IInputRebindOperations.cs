namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Rebinding and binding-override persistence for one player. Settings screens depend on this interface and
    /// nothing else.
    /// </summary>
    public interface IInputRebindOperations
    {
        /// <summary>Applies an already chosen path to one binding. Returns false when the identity does not resolve.</summary>
        bool RebindAction(string contextName, string actionMapName, string actionName, string oldBinding, string newBinding);

        /// <summary>Restores one action to its configured bindings.</summary>
        bool ResetActionBinding(string contextName, string actionMapName, string actionName);

        /// <summary>Returns the effective binding paths of an action, overrides included.</summary>
        string[] GetActionBindings(string contextName, string actionMapName, string actionName);

        /// <summary>Restores every action of this player to its configured bindings.</summary>
        void ResetAllActionBindings();

        /// <summary>Serializes the overrides of this player as the module-owned JSON format.</summary>
        string ExportBindingOverridesJson();

        /// <summary>Serializes the overrides of this player without throwing on failure.</summary>
        bool TryExportBindingOverridesJson(out string json);

        /// <summary>Applies previously exported overrides. Returns false for an unsupported schema or a selector mismatch.</summary>
        bool ImportBindingOverridesJson(string json, bool removeExisting = true);
    }
}
