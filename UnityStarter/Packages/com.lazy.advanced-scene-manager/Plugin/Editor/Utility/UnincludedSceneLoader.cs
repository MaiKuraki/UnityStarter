using System.Collections;
using AdvancedSceneManager.Core;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEngine;

namespace AdvancedSceneManager.Editor
{

    class UnincludedSceneLoader : SceneLoader
    {

        public override bool isGlobal => true;

        public override bool CanHandleScene(Scene scene) =>
            !scene.isIncludedInBuilds;

        public override IEnumerator LoadScene(Scene scene, SceneLoadArgs e)
        {

            if (!Profile.current)
                yield break;

            Profile.current.standaloneScenes.Add(scene);
            BuildUtility.UpdateSceneList(true);
            Debug.LogError($"The scene '{scene.path}' could not be opened, as was not included in build. It has been added to standalone, but play mode must be restarted to update build scene list.");
            e.SetCompletedWithoutScene();

        }

        public override IEnumerator UnloadScene(Scene scene, SceneUnloadArgs e)
        {
            yield break;
        }

    }

}
