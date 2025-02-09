using System.ComponentModel;

namespace AdvancedSceneManager.Models
{

    /// <summary>Specifies a object that can be locked, using <see cref="LockUtility"/>.</summary>
    /// <remarks>Available, but no effect in build.</remarks>
    public interface ILockable : INotifyPropertyChanged
    {
        public bool isLocked { get; set; }
        public string lockMessage { get; set; }
        public void Save();
    }

}
