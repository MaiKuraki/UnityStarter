using AdvancedSceneManager.Core;
using AdvancedSceneManager.Utility;
using UnityEngine;

namespace AdvancedSceneManager.Models.Utility
{

    /// <summary>Represents a <see cref="SceneCollection"/> that changes depending on active <see cref="Profile"/>.</summary>
    [CreateAssetMenu(menuName = "Advanced Scene Manager/Profile dependent collection")]
    public class ProfileDependentCollection : ProfileDependent<SceneCollection>, ISceneCollection.IOpenable,
        SceneCollection.IMethods, SceneCollection.IMethods.IEvent
    {

        public static implicit operator SceneCollection(ProfileDependentCollection instance) =>
            instance.GetModel(out var scene) ? scene : null;

        #region IMethods

        public SceneOperation Open(bool openAll = false) => DoAction(c => c.Open(openAll));
        public SceneOperation Reopen(bool openAll = false) => DoAction(c => c.Reopen(openAll));
        public SceneOperation OpenAdditive(bool openAll = false) => DoAction(c => c.OpenAdditive(openAll));

        public SceneOperation Preload(bool openAll = false) => DoAction(c => c.Preload(openAll));
        public SceneOperation PreloadAdditive(bool openAll = false) => DoAction(c => c.PreloadAdditive(openAll));

        public SceneOperation ToggleOpen(bool openAll = false) => DoAction(c => c.ToggleOpen(openAll));

        public SceneOperation Close() => DoAction(c => c.Close());

        #endregion
        #region IMethods.IEvent

        public void _Open() => SpamCheck.EventMethods.Execute(() => Open());

        public void _OpenAdditive() => SpamCheck.EventMethods.Execute(() => OpenAdditive());

        public void _Preload() => SpamCheck.EventMethods.Execute(() => Preload());
        public void _PreloadAdditive() => SpamCheck.EventMethods.Execute(() => PreloadAdditive());

        public void _ToggleOpen() => SpamCheck.EventMethods.Execute(() => ToggleOpen());

        public void _Close() => SpamCheck.EventMethods.Execute(() => Close());

        #endregion

    }

}
