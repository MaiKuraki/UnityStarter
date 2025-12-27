namespace CycloneGames.Service.Runtime
{
    public interface ISettingsService<T> where T : struct
    {
        T Settings { get; }
        void SaveSettings();
        void LoadSettings();
        bool IsInitialized { get; }
    }
}