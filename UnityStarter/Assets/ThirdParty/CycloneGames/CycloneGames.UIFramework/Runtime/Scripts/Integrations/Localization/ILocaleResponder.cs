using CycloneGames.Localization.Runtime;

namespace CycloneGames.UIFramework.Runtime.Integrations.Localization
{
    /// <summary>
    /// Interface for UI components that need to respond to locale changes.
    /// Implement on MonoBehaviours within a UIWindow hierarchy.
    /// When a <see cref="LocalizationWindowBinder"/> is registered, it automatically
    /// discovers all ILocaleResponders on each window and notifies them on locale change.
    /// </summary>
    public interface ILocaleResponder
    {
        void OnLocaleChanged(LocaleId newLocale);
    }
}
