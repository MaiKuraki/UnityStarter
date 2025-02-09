using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AdvancedSceneManager.Loading;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEngine;

namespace AdvancedSceneManager.Editor
{

    class ProgressListener : ILoadProgressListener
    {

        int? rootProgressId;
        readonly Dictionary<Scene, int> sceneProgressIds = new Dictionary<Scene, int>();
        readonly Dictionary<Scene, DateTime> lastReport = new Dictionary<Scene, DateTime>();

        System.Timers.Timer timer;

        public void OnProgressChanged(ILoadProgressData progress)
        {

            if (progress is not SceneLoadProgressData sceneProgressData || !sceneProgressData.scene)
                return;

            if (!rootProgressId.HasValue)
                rootProgressId = Progress.Start("Advanced Scene Manager", options: Progress.Options.Indefinite | Progress.Options.Sticky);

            if (!sceneProgressIds.TryGetValue(sceneProgressData.scene, out var id))
            {
                id = Progress.Start(sceneProgressData.operationKind.ToString(), sceneProgressData.scene.path, parentId: rootProgressId.Value, options: Progress.Options.Sticky);
                sceneProgressIds.Add(sceneProgressData.scene, id);
            }

            if (!Mathf.Approximately(sceneProgressData.value, 1f))
            {
                lastReport.Set(sceneProgressData.scene, DateTime.Now);
                if (Progress.Exists(id))
                    Progress.Report(id, progress.value);
            }
            else
            {
                if (Progress.Exists(id))
                    Progress.Finish(id);
                RemoveProgress(sceneProgressData.scene);
            }

            if (timer is null)
            {
                timer = new System.Timers.Timer(5);
                timer.Elapsed += OnTimeout;
            }

            timer.Stop();
            timer.Start();

        }

        void OnTimeout(object sender, EventArgs e)
        {

            timer.Stop();

            foreach (var report in lastReport.ToArray())
                if (DateTime.Now - report.Value > TimeSpan.FromSeconds(5))
                    RemoveProgress(report.Key);

        }

        private async void RemoveProgress(Scene scene)
        {

            lastReport.Remove(scene);

            await Task.Delay(3500);

            EditorApplication.delayCall += () =>
            {

                if (sceneProgressIds.Remove(scene, out var id) && Progress.Exists(id))
                    Progress.Remove(id);

                if (sceneProgressIds.Count == 0)
                {
                    if (rootProgressId.HasValue && Progress.Exists(rootProgressId.Value))
                    {
                        Progress.Remove(rootProgressId.Value);
                        rootProgressId = null;
                    }
                }

            };

        }

    }

}
