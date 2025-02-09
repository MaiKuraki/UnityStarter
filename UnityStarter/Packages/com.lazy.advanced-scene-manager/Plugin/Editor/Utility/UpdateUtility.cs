using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEngine;

namespace AdvancedSceneManager.Editor.Utility
{

    static class UpdateUtility
    {

        public const string GithubReleases = "https://github.com/Lazy-Solutions/AdvancedSceneManager/releases/latest";
        public const string GithubPatchFile = "https://gist.githubusercontent.com/Zumwani/195afd3053cf1cb951013e30908903c0/raw";
        public static event Action updateChecked;

        #region Auto

        [InitializeOnLoadMethod]
        static void StartUpdateCheckTimer()
        {
            CoroutineUtility.Timer(CheckUpdateIfCooldownOver, TimeSpan.FromHours(1));
        }

        static void CheckUpdateIfCooldownOver()
        {

            if (SceneManager.settings.user.updateInterval == Models.UpdateInterval.Never)
                return;

            if (SceneManager.settings.user.updateInterval == Models.UpdateInterval.Auto && !SceneManager.settings.project.allowUpdateCheck)
                return;

            if (IsCooldownOver())
                _ = CheckUpdate();

        }

        static bool IsCooldownOver()
        {
            if (!DateTime.TryParse(lastUpdateCheck, out var lastCheckDate))
                return true;

            TimeSpan? cooldownPeriod = SceneManager.settings.user.updateInterval switch
            {
                Models.UpdateInterval.Auto => TimeSpan.FromHours(3),
                Models.UpdateInterval.EveryHour => TimeSpan.FromHours(1),
                Models.UpdateInterval.Every3Hours => TimeSpan.FromHours(3),
                Models.UpdateInterval.Every6Hours => TimeSpan.FromHours(6),
                Models.UpdateInterval.Every12Hours => TimeSpan.FromHours(12),
                Models.UpdateInterval.Every24Hours => TimeSpan.FromDays(1),
                Models.UpdateInterval.Every48Hours => TimeSpan.FromDays(2),
                Models.UpdateInterval.EveryWeek => TimeSpan.FromDays(7),
                _ => null
            };

            if (!cooldownPeriod.HasValue)
                return false;

            return DateTime.Now - lastCheckDate >= cooldownPeriod;

        }

        #endregion
        #region Properties

        public static string lastNotifyVersion
        {
            get => SceneManager.settings.user.lastPatchWhenNotified;
            set => SceneManager.settings.user.lastPatchWhenNotified = value;
        }

        public static string lastUpdateCheck
        {
            get => SceneManager.settings.user.lastUpdateCheck;
            set => SceneManager.settings.user.lastUpdateCheck = value;
        }

        public static string availableVersionStr
        {
            get => SceneManager.settings.user.cachedLatestVersion;
            set => SceneManager.settings.user.cachedLatestVersion = value;
        }

        public static string availablePatchNotes
        {
            get => SceneManager.settings.user.cachedPatchNotes;
            set => SceneManager.settings.user.cachedPatchNotes = value;
        }

        private static readonly HttpClient client = new();

        static Version _availableVersion;
        public static Version availableVersion => _availableVersion ??= (Version.TryParse(availableVersionStr, out var v) ? v : null);

        static Version _installedVersion;
        public static Version installedVersion => _installedVersion ??= Version.Parse(SceneManager.package.version);

        public static bool isAssetStoreUpdateRequired =>
            availableVersion > installedVersion && availableVersion.Minor > installedVersion.Minor;

        public static bool isUpdateAvailable =>
            availableVersion > installedVersion;

        public static bool hasNotifiedAboutVersion =>
             Version.TryParse(lastNotifyVersion, out var lastNotifyPatch) &&
             lastNotifyPatch >= availableVersion;

        #endregion
        #region Check

        public async static Task CheckUpdate(bool logError = false, CancellationToken? token = null)
        {

            lastUpdateCheck = DateTime.Now.ToString();
#if ASM_DEV
            Debug.Log("Checking for update...");
#endif

            try
            {

                var message = await client.GetAsync(GithubPatchFile, token ?? CancellationToken.None);
                if (token?.IsCancellationRequested ?? false) throw new TaskCanceledException();

                var text = await message.Content.ReadAsStringAsync();
                if (token?.IsCancellationRequested ?? false) throw new TaskCanceledException();

                if (!text.Contains("\n"))
                    throw new Exception("Could not parse version file.");

                var versionStr = text[..text.IndexOf("\n")];
                var patchNotes = text[text.IndexOf("\n")..];

#if ASM_DEV
                //versionStr += ".1";
                //versionStr = "2.3.0";
                //versionStr = "2.2.26";
                //versionStr = "2.3.11";
#endif

                if (!Version.TryParse(versionStr, out var patchVersion))
                    throw new Exception("Could not parse version file.");


                if (!Version.TryParse(SceneManager.package.version, out var currentVersion))
                    throw new Exception("Could not retrieve current version.");

                availableVersionStr = versionStr;
                availablePatchNotes = patchNotes;
                _availableVersion = null;
                updateChecked?.Invoke();

#if ASM_DEV
                Debug.Log("Latest update found: " + availableVersion);
#endif
            }
            catch (TaskCanceledException)
            { }
            catch (Exception e)
            {
                if (logError)
                {
                    Debug.LogError("An error occured when checking for updates, please try again. Exception will be logged below.");
                    Debug.LogException(e);
                }
            }

        }

        #endregion
        #region Update

        public static async void Update()
        {

            try
            {

                if (isAssetStoreUpdateRequired)
                {
                    UnityEditor.PackageManager.UI.Window.Open(SceneManager.package.id);
                    return;
                }

                var path = await FindAndDownloadPackage();

#if !ASM_DEV
                AssetDatabase.ImportPackage(path, interactive: true);
#else
                Debug.Log($"AssetDatabase.ImportPackage({path}, interactive: true);");
#endif

            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }

        }

        static async Task<string> FindAndDownloadPackage()
        {

            var owner = "Lazy-Solutions";
            var repo = "AdvancedSceneManager";
            var url = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";

            client.DefaultRequestHeaders.Add("User-Agent", "request");

            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var responseBody = await response.Content.ReadAsStringAsync();
            var latestRelease = JsonUtility.FromJson<Release>(responseBody);

            var unityPackageUrl = latestRelease.assets.FirstOrDefault(a => a.name.EndsWith(".unitypackage")).browser_download_url;

            if (string.IsNullOrEmpty(unityPackageUrl))
                throw new InvalidOperationException("No .unitypackage file found in the latest release.");

            var stream = await client.GetStreamAsync(unityPackageUrl);

            var path = Path.Combine("Temp", "ASM", "Updates");
            if (!Directory.Exists(path))
                _ = Directory.CreateDirectory(path);

            path = Path.Combine(path, $"ASM.{availableVersionStr}.partial.unitypackage");
            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            await stream.CopyToAsync(fs);

            return path;

        }

        [Serializable]
        struct Release
        {
            public Asset[] assets;
        }

        [Serializable]
        struct Asset
        {
            public string name;
            public string browser_download_url;
        }

        #endregion
        #region Remove old packages

        [InitializeOnLoadMethod]
        static void DeleteOldUpdatePackages()
        {

            var path = Path.Combine("Temp", "ASM", "Updates");
            if (Directory.Exists(path))
                Directory.Delete(path, true);

        }

        #endregion

    }

}
