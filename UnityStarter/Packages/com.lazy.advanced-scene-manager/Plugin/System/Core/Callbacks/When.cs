namespace AdvancedSceneManager.Core.Callbacks
{

    /// <summary>Specifies if a scene operation callback is before or after the awaited action.</summary>
    public enum When
    {

        /// <summary>Specifies that this enum is not applicable for action that the event callback represents.</summary>
        NotApplicable,

        /// <summary>Specifies that the event callback was invoked before the action it represents.</summary>
        Before,

        /// <summary>Specifies that the event callback was invoked after the action it represents.</summary>
        After

    }

}
