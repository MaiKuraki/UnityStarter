mergeInto(LibraryManager.library,
{
    VibrateWebgl: function(duration)
    {
        if (typeof navigator.vibrate === "function") {
            navigator.vibrate(duration);
        }
    }
});