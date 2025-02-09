namespace AdvancedSceneManager.Callbacks.Events
{

    /// <summary>Callback for scene operations.</summary>
    public delegate void EventCallback<in TEventType>(TEventType evt);

}
