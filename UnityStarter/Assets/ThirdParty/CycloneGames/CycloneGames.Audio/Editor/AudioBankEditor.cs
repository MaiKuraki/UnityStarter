using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using CycloneGames.Audio.Runtime;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;

namespace CycloneGames.Audio.Editor
{
    /// <summary>
    /// Override inspector for quick editing in the graph
    /// </summary>
    [CustomEditor(typeof(AudioBank))]
    public class AudioBankEditor : UnityEditor.Editor
    {
        /// <summary>
        /// AudioBank to edit in the graph
        /// </summary>
        private AudioBank myTarget;
        private readonly AudioBankValidationReport validationReport = new AudioBankValidationReport();
        private Vector2 validationScrollPosition;

        /// <summary>
        /// Set reference for AudioBank to pass to graph window
        /// </summary>
        private void OnEnable()
        {
            this.myTarget = (AudioBank)target;
        }

        /// <summary>
        /// Display a button to open the bank in the graph
        /// </summary>
        public override void OnInspectorGUI()
        {
            Rect openRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            if (GUI.Button(openRect, "Open in Graph"))
            {
                AudioGraph.OpenAudioGraph(this.myTarget);
            }

            EditorGUILayout.Space(6f);

            Rect validateRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            if (GUI.Button(validateRect, "Validate Bank"))
            {
                AudioBankValidator.Validate(this.myTarget, this.validationReport);
                AudioBankValidator.LogReport(this.validationReport);
            }

            DrawValidationReport();
        }

        private void DrawValidationReport()
        {
            if (this.validationReport.Issues.Count == 0) return;

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(
                $"Validation: {this.validationReport.ErrorCount} errors, {this.validationReport.WarningCount} warnings",
                EditorStyles.boldLabel);

            float maxHeight = Mathf.Min(260f, 28f + this.validationReport.Issues.Count * 34f);
            Rect scrollRect = EditorGUILayout.GetControlRect(false, maxHeight);

            List<AudioBankValidationIssue> issues = this.validationReport.Issues;
            float contentHeight = issues.Count * 38f;
            Rect contentRect = new Rect(0f, 0f, Mathf.Max(1f, scrollRect.width - 16f), contentHeight);
            this.validationScrollPosition = GUI.BeginScrollView(scrollRect, this.validationScrollPosition, contentRect);
            for (int i = 0; i < issues.Count; i++)
            {
                AudioBankValidationIssue issue = issues[i];
                MessageType messageType = issue.Severity == AudioBankValidationSeverity.Error
                    ? MessageType.Error
                    : issue.Severity == AudioBankValidationSeverity.Warning
                        ? MessageType.Warning
                        : MessageType.Info;

                Rect rowRect = new Rect(0f, i * 38f, contentRect.width, 36f);
                Rect messageRect = new Rect(rowRect.x, rowRect.y, Mathf.Max(1f, rowRect.width - 54f), rowRect.height);
                Rect pingRect = new Rect(rowRect.xMax - 48f, rowRect.y + 4f, 48f, EditorGUIUtility.singleLineHeight);

                EditorGUI.HelpBox(messageRect, issue.Message, messageType);
                using (new EditorGUI.DisabledScope(issue.Context == null))
                {
                    if (GUI.Button(pingRect, "Ping"))
                    {
                        EditorGUIUtility.PingObject(issue.Context);
                        Selection.activeObject = issue.Context;
                    }
                }
            }

            GUI.EndScrollView();
        }

        [OnOpenAsset]
        public static bool OnOpen(int instanceID, int line)
        {
            var asset = EditorUtility.InstanceIDToObject(instanceID) as AudioBank;
            if (asset != null)
            {
                AudioGraph.OpenAudioGraph(asset);
                return true;
            }
            return false;
        }

        [MenuItem("Tools/CycloneGames/Audio/Validate All Audio Banks")]
        private static void ValidateAllAudioBanksMenu()
        {
            AudioBankValidationSummary summary = AudioBankValidator.ValidateAllAudioBanks(true);
            ShowValidationSummaryDialog(summary);
        }

        [MenuItem("Tools/CycloneGames/Audio/Run Runtime Diagnostics")]
        private static void RunRuntimeDiagnosticsMenu()
        {
            AudioRuntimeDiagnostics.Report report = AudioRuntimeDiagnostics.Run();
            EditorUtility.DisplayDialog("Audio Runtime Diagnostics", report.DialogMessage, "OK");
        }

        [MenuItem("Tools/CycloneGames/Audio/Run External Reference Diagnostics")]
        private static void RunExternalReferenceDiagnosticsMenu()
        {
            AudioRuntimeDiagnostics.Report report = AudioRuntimeDiagnostics.RunExternalReferenceDiagnostics();
            EditorUtility.DisplayDialog("Audio External Reference Diagnostics", report.DialogMessage, "OK");
        }

        [MenuItem("Tools/CycloneGames/Audio/Run Playback Smoke Test")]
        private static void RunPlaybackSmokeTestMenu()
        {
            AudioRuntimeDiagnostics.Report report = AudioRuntimeDiagnostics.RunPlaybackSmokeTest();
            EditorUtility.DisplayDialog("Audio Playback Smoke Test", report.DialogMessage, "OK");
        }

        [MenuItem("Tools/CycloneGames/Audio/Run Playback Smoke Test", true)]
        private static bool RunPlaybackSmokeTestMenuValidate()
        {
            return EditorApplication.isPlaying;
        }

        [MenuItem("Assets/CycloneGames/Audio/Validate Selected Audio Banks")]
        private static void ValidateSelectedAudioBanksMenu()
        {
            AudioBank[] banks = Selection.GetFiltered<AudioBank>(SelectionMode.Assets);
            AudioBankValidationSummary summary = AudioBankValidator.ValidateBanks(banks, true);
            ShowValidationSummaryDialog(summary);
        }

        [MenuItem("Assets/CycloneGames/Audio/Validate Selected Audio Banks", true)]
        private static bool ValidateSelectedAudioBanksMenuValidate()
        {
            AudioBank[] banks = Selection.GetFiltered<AudioBank>(SelectionMode.Assets);
            return banks != null && banks.Length > 0;
        }

        private static void ShowValidationSummaryDialog(AudioBankValidationSummary summary)
        {
            if (summary == null) return;

            string message = $"Banks: {summary.BankCount}\nPassed: {summary.PassedCount}\nWarnings: {summary.WarningCount}\nErrors: {summary.ErrorCount}";
            EditorUtility.DisplayDialog("Audio Bank Validation", message, "OK");
        }
    }

    internal sealed class AudioBankBuildValidator : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            AudioBankValidationSummary summary = AudioBankValidator.ValidateAllAudioBanks(true);
            if (summary.ErrorCount > 0)
            {
                throw new BuildFailedException($"AudioBank validation failed. Banks: {summary.BankCount}, Errors: {summary.ErrorCount}, Warnings: {summary.WarningCount}.");
            }
        }
    }

    internal static class AudioRuntimeDiagnostics
    {
        private static readonly StringBuilder ReportBuilder = new StringBuilder(1024);
        private static readonly List<AudioClipReference> ExternalReferences = new List<AudioClipReference>(128);
        private static readonly HashSet<int> ExternalReferenceIds = new HashSet<int>();
        private static readonly List<IAudioClipProvider> Providers = new List<IAudioClipProvider>(8);

        public readonly struct Report
        {
            public readonly int ErrorCount;
            public readonly int WarningCount;
            public readonly string DialogMessage;

            public Report(int errorCount, int warningCount, string dialogMessage)
            {
                ErrorCount = errorCount;
                WarningCount = warningCount;
                DialogMessage = dialogMessage;
            }
        }

        public static Report Run()
        {
            AudioBankValidationSummary bankSummary = AudioBankValidator.ValidateAllAudioBanks(false);
            AudioRuntimeStats stats = AudioManager.GetRuntimeStats();

            int errors = bankSummary.ErrorCount;
            int warnings = bankSummary.WarningCount;

            ReportBuilder.Length = 0;
            ReportBuilder.AppendLine("Audio Runtime Diagnostics");
            ReportBuilder.AppendLine();
            ReportBuilder.Append("Banks: ").Append(bankSummary.BankCount)
                .Append(", Passed: ").Append(bankSummary.PassedCount)
                .Append(", Warnings: ").Append(bankSummary.WarningCount)
                .Append(", Errors: ").Append(bankSummary.ErrorCount)
                .AppendLine();
            ReportBuilder.AppendLine();
            ReportBuilder.Append("Pool: ").Append(stats.PoolInUse).Append(" in use / ")
                .Append(stats.PoolAvailable).Append(" available / ")
                .Append(stats.PoolCurrentSize).Append(" current / ")
                .Append(stats.PoolMaxSize).Append(" max")
                .AppendLine();
            ReportBuilder.Append("Runtime: active events ").Append(stats.ActiveEvents)
                .Append(", pending removals ").Append(stats.PendingRemovals)
                .Append(", registered parameters ").Append(stats.RegisteredParameters)
                .Append(", registered states ").Append(stats.RegisteredStateGroups)
                .Append(", state mix profiles ").Append(stats.RegisteredStateMixProfiles)
                .Append(", scoped parameters ").Append(stats.ScopedParameterOverrides)
                .AppendLine();
            ReportBuilder.Append("External cache: entries ").Append(stats.ExternalCacheEntries)
                .Append(", refs ").Append(stats.ExternalTotalRefCount)
                .Append(", loading ").Append(stats.ExternalLoadingCount)
                .Append(", failed ").Append(stats.ExternalFailedCount)
                .AppendLine();

            CheckRuntimeInvariant(stats.PoolCurrentSize >= 0, "Pool current size is negative.", ref errors);
            CheckRuntimeInvariant(stats.PoolInUse >= 0, "Pool in-use count is negative.", ref errors);
            CheckRuntimeInvariant(stats.PoolAvailable >= 0, "Pool available count is negative.", ref errors);
            CheckRuntimeInvariant(stats.PoolInUse + stats.PoolAvailable == stats.PoolCurrentSize, "Pool counts do not balance.", ref errors);
            CheckRuntimeInvariant(stats.PoolCurrentSize <= stats.PoolMaxSize, "Pool current size exceeds max size.", ref errors);
            CheckRuntimeInvariant(stats.PendingRemovals <= stats.ActiveEvents + 8, "Pending removals are unexpectedly high.", ref warnings);
            CheckRuntimeInvariant(stats.ExternalTotalRefCount >= 0, "External cache ref count is negative.", ref errors);

            string message = ReportBuilder.ToString();
            if (errors > 0)
            {
                Debug.LogError(message);
            }
            else if (warnings > 0)
            {
                Debug.LogWarning(message);
            }
            else
            {
                Debug.Log(message);
            }

            return new Report(errors, warnings, message);
        }

        public static Report RunExternalReferenceDiagnostics()
        {
            int errors = 0;
            int warnings = 0;
            int bankCount = 0;
            int eventCount = 0;
            int externalNodeCount = 0;
            int missingReferenceCount = 0;
            int missingLocationCount = 0;
            int brokenEditorAssetLinkCount = 0;
            int noProviderCount = 0;
            int matchedProviderCount = 0;
            int loggedIssueCount = 0;

            ExternalReferences.Clear();
            ExternalReferenceIds.Clear();
            Providers.Clear();

            ExternalAudioClipCacheStats beforeCache = AudioClipResolver.GetExternalCacheStats();
            AudioClipResolver.GetProviders(Providers);

            string[] guids = AssetDatabase.FindAssets("t:AudioBank");
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    AudioBank bank = AssetDatabase.LoadAssetAtPath<AudioBank>(path);
                    if (bank == null || bank.AudioEvents == null) continue;

                    bankCount++;
                    if (guids.Length > 8)
                    {
                        EditorUtility.DisplayProgressBar("Audio External Reference Diagnostics", path, (float)i / guids.Length);
                    }

                    List<AudioEvent> events = bank.AudioEvents;
                    for (int eventIndex = 0; eventIndex < events.Count; eventIndex++)
                    {
                        AudioEvent audioEvent = events[eventIndex];
                        if (audioEvent == null) continue;

                        eventCount++;
                        CollectExternalReferences(audioEvent, ref externalNodeCount, ref missingReferenceCount);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            ReportBuilder.Length = 0;
            ReportBuilder.AppendLine("Audio External Reference Diagnostics");
            ReportBuilder.AppendLine();
            ReportBuilder.Append("Banks scanned: ").Append(bankCount)
                .Append(", events scanned: ").Append(eventCount)
                .AppendLine();
            ReportBuilder.Append("External nodes: ").Append(externalNodeCount)
                .Append(", unique references: ").Append(ExternalReferences.Count)
                .Append(", missing references: ").Append(missingReferenceCount)
                .AppendLine();
            ReportBuilder.Append("Providers registered: ").Append(Providers.Count)
                .AppendLine();
            if (bankCount == 0)
            {
                ReportBuilder.AppendLine("Result: no AudioBank assets were found in the project.");
            }
            else if (externalNodeCount == 0)
            {
                ReportBuilder.AppendLine("Result: no external AudioClipReference nodes were found; all scanned events use embedded clips or non-external nodes.");
            }

            for (int i = 0; i < ExternalReferences.Count; i++)
            {
                AudioClipReference reference = ExternalReferences[i];
                if (reference == null)
                    continue;

                if (string.IsNullOrWhiteSpace(reference.Location))
                {
                    errors++;
                    missingLocationCount++;
                    AppendReferenceIssue("AudioClipReference has an empty location.", ref loggedIssueCount);
                    continue;
                }

                if (reference.HasEditorAssetLink && string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(reference.GUID)))
                {
                    errors++;
                    brokenEditorAssetLinkCount++;
                    AppendReferenceIssue($"AudioClipReference '{reference.name}' has a broken editor asset GUID.", ref loggedIssueCount);
                }

                if (TryFindProvider(reference, out IAudioClipProvider provider))
                {
                    matchedProviderCount++;
                }
                else
                {
                    warnings++;
                    noProviderCount++;
                    AppendReferenceIssue($"No AudioClipProvider can load '{reference.name}' ({reference.LocationKind}: {reference.GetDisplayLocation()}).", ref loggedIssueCount);
                }
            }

            if (missingReferenceCount > 0)
            {
                errors += missingReferenceCount;
                ReportBuilder.Append("[Issue] Missing AudioClipReference assets were found on external nodes. Count: ")
                    .Append(missingReferenceCount)
                    .AppendLine();
            }

            ExternalAudioClipCacheStats afterCache = AudioClipResolver.GetExternalCacheStats();
            if (!CacheStatsEqual(beforeCache, afterCache))
            {
                warnings++;
                ReportBuilder.Append("[Issue] External cache stats changed during a non-loading diagnostics pass.")
                    .AppendLine();
            }

            ReportBuilder.AppendLine();
            ReportBuilder.Append("Provider match: matched ").Append(matchedProviderCount)
                .Append(", no provider ").Append(noProviderCount)
                .Append(", empty location ").Append(missingLocationCount)
                .Append(", broken asset link ").Append(brokenEditorAssetLinkCount)
                .AppendLine();
            ReportBuilder.Append("Cache before: entries ").Append(beforeCache.EntryCount)
                .Append(", refs ").Append(beforeCache.TotalRefCount)
                .Append(", loading ").Append(beforeCache.LoadingCount)
                .Append(", requests ").Append(beforeCache.TotalLoadRequests)
                .AppendLine();
            ReportBuilder.Append("Cache after: entries ").Append(afterCache.EntryCount)
                .Append(", refs ").Append(afterCache.TotalRefCount)
                .Append(", loading ").Append(afterCache.LoadingCount)
                .Append(", requests ").Append(afterCache.TotalLoadRequests)
                .AppendLine();

            string message = ReportBuilder.ToString();
            if (errors > 0)
            {
                Debug.LogError(message);
            }
            else if (warnings > 0)
            {
                Debug.LogWarning(message);
            }
            else
            {
                Debug.Log(message);
            }

            ExternalReferences.Clear();
            ExternalReferenceIds.Clear();
            Providers.Clear();

            return new Report(errors, warnings, message);
        }

        public static Report RunPlaybackSmokeTest()
        {
            if (!EditorApplication.isPlaying)
            {
                const string playModeMessage = "Audio playback smoke test requires Play Mode.";
                Debug.LogWarning(playModeMessage);
                return new Report(0, 1, playModeMessage);
            }

            AudioRuntimeStats beforeStats = AudioManager.GetRuntimeStats();
            int errors = 0;
            int warnings = 0;
            int bankCount = 0;
            int loadedBankCount = 0;
            int attemptedEvents = 0;
            int playedEvents = 0;
            int skippedExternalEvents = 0;
            int skippedInvalidEvents = 0;

            string[] guids = AssetDatabase.FindAssets("t:AudioBank");
            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    AudioBank bank = AssetDatabase.LoadAssetAtPath<AudioBank>(path);
                    if (bank == null || bank.AudioEvents == null) continue;

                    bankCount++;
                    AudioManager.LoadBank(bank, true);
                    loadedBankCount++;

                    List<AudioEvent> events = bank.AudioEvents;
                    int perBankAttempts = 0;
                    for (int eventIndex = 0; eventIndex < events.Count && perBankAttempts < 4 && attemptedEvents < 32; eventIndex++)
                    {
                        AudioEvent audioEvent = events[eventIndex];
                        if (audioEvent == null)
                        {
                            skippedInvalidEvents++;
                            continue;
                        }

                        if (UsesExternalClipReference(audioEvent))
                        {
                            skippedExternalEvents++;
                            continue;
                        }

                        attemptedEvents++;
                        perBankAttempts++;

                        ActiveEvent activeEvent = AudioManager.PlayEvent(audioEvent, Vector3.zero);
                        if (activeEvent == null)
                        {
                            skippedInvalidEvents++;
                            continue;
                        }

                        playedEvents++;
                        activeEvent.StopImmediate();
                        AudioManager.RemoveActiveEvent(activeEvent);
                    }

                    AudioManager.UnloadBank(bank);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AudioRuntimeStats afterStats = AudioManager.GetRuntimeStats();

            ReportBuilder.Length = 0;
            ReportBuilder.AppendLine("Audio Playback Smoke Test");
            ReportBuilder.AppendLine();
            ReportBuilder.Append("Banks scanned: ").Append(bankCount)
                .Append(", loaded: ").Append(loadedBankCount)
                .AppendLine();
            ReportBuilder.Append("Events attempted: ").Append(attemptedEvents)
                .Append(", played: ").Append(playedEvents)
                .Append(", skipped external: ").Append(skippedExternalEvents)
                .Append(", skipped invalid: ").Append(skippedInvalidEvents)
                .AppendLine();
            ReportBuilder.AppendLine();
            ReportBuilder.Append("Before: active ").Append(beforeStats.ActiveEvents)
                .Append(", in use ").Append(beforeStats.PoolInUse)
                .Append(", available ").Append(beforeStats.PoolAvailable)
                .Append(", pending ").Append(beforeStats.PendingRemovals)
                .AppendLine();
            ReportBuilder.Append("After: active ").Append(afterStats.ActiveEvents)
                .Append(", in use ").Append(afterStats.PoolInUse)
                .Append(", available ").Append(afterStats.PoolAvailable)
                .Append(", pending ").Append(afterStats.PendingRemovals)
                .AppendLine();

            CheckRuntimeInvariant(afterStats.PoolInUse + afterStats.PoolAvailable == afterStats.PoolCurrentSize, "Pool counts do not balance after smoke test.", ref errors);
            CheckRuntimeInvariant(afterStats.ActiveEvents == beforeStats.ActiveEvents, "Active event count changed after smoke test.", ref errors);
            CheckRuntimeInvariant(afterStats.PoolInUse == beforeStats.PoolInUse, "Pool in-use count changed after smoke test.", ref errors);
            CheckRuntimeInvariant(afterStats.PendingRemovals <= beforeStats.PendingRemovals + 1, "Pending removals grew after smoke test.", ref warnings);
            CheckRuntimeInvariant(attemptedEvents > 0, "No embedded AudioEvents were available for playback smoke test.", ref warnings);

            string message = ReportBuilder.ToString();
            if (errors > 0)
            {
                Debug.LogError(message);
            }
            else if (warnings > 0)
            {
                Debug.LogWarning(message);
            }
            else
            {
                Debug.Log(message);
            }

            return new Report(errors, warnings, message);
        }

        private static void CheckRuntimeInvariant(bool condition, string message, ref int counter)
        {
            if (condition) return;

            counter++;
            ReportBuilder.Append("[Issue] ").AppendLine(message);
        }

        private static void CollectExternalReferences(AudioEvent audioEvent, ref int externalNodeCount, ref int missingReferenceCount)
        {
            List<AudioNode> nodes = audioEvent.Nodes;
            if (nodes == null) return;

            for (int i = 0; i < nodes.Count; i++)
            {
                AudioNode node = nodes[i];
                if (node is AudioFile audioFile && audioFile.SourceMode == AudioFile.AudioFileSourceMode.ExternalReference)
                {
                    externalNodeCount++;
                    AddExternalReference(audioFile.ExternalReference, ref missingReferenceCount);
                }
                else if (node is AudioVoiceFile voiceFile && voiceFile.SourceMode == AudioFile.AudioFileSourceMode.ExternalReference)
                {
                    externalNodeCount++;
                    AddExternalReference(voiceFile.ExternalReference, ref missingReferenceCount);
                }
                else if (node is AudioBlendFile blendFile && blendFile.SourceMode == AudioFile.AudioFileSourceMode.ExternalReference)
                {
                    externalNodeCount++;
                    AddExternalReference(blendFile.ExternalReference, ref missingReferenceCount);
                }
            }
        }

        private static void AddExternalReference(AudioClipReference reference, ref int missingReferenceCount)
        {
            if (reference == null)
            {
                missingReferenceCount++;
                return;
            }

            int id = reference.GetInstanceID();
            if (!ExternalReferenceIds.Add(id)) return;

            ExternalReferences.Add(reference);
        }

        private static bool TryFindProvider(AudioClipReference reference, out IAudioClipProvider provider)
        {
            for (int i = 0; i < Providers.Count; i++)
            {
                provider = Providers[i];
                if (provider != null && provider.CanLoad(reference))
                    return true;
            }

            provider = null;
            return false;
        }

        private static void AppendReferenceIssue(string message, ref int loggedIssueCount)
        {
            if (loggedIssueCount >= 12)
                return;

            loggedIssueCount++;
            ReportBuilder.Append("[Issue] ").AppendLine(message);
        }

        private static bool CacheStatsEqual(ExternalAudioClipCacheStats left, ExternalAudioClipCacheStats right)
        {
            return left.EntryCount == right.EntryCount
                && left.LoadingCount == right.LoadingCount
                && left.LoadedCount == right.LoadedCount
                && left.FailedCount == right.FailedCount
                && left.TotalRefCount == right.TotalRefCount
                && left.TotalLoadRequests == right.TotalLoadRequests
                && left.CacheHitCount == right.CacheHitCount
                && left.CacheMissCount == right.CacheMissCount
                && left.TotalFailureCount == right.TotalFailureCount;
        }

        private static bool UsesExternalClipReference(AudioEvent audioEvent)
        {
            List<AudioNode> nodes = audioEvent.Nodes;
            if (nodes == null) return false;

            for (int i = 0; i < nodes.Count; i++)
            {
                AudioNode node = nodes[i];
                if (node is AudioFile audioFile && audioFile.SourceMode == AudioFile.AudioFileSourceMode.ExternalReference)
                    return true;
                if (node is AudioVoiceFile voiceFile && voiceFile.SourceMode == AudioFile.AudioFileSourceMode.ExternalReference)
                    return true;
                if (node is AudioBlendFile blendFile && blendFile.SourceMode == AudioFile.AudioFileSourceMode.ExternalReference)
                    return true;
            }

            return false;
        }
    }

    internal enum AudioBankValidationSeverity
    {
        Info,
        Warning,
        Error
    }

    internal readonly struct AudioBankValidationIssue
    {
        public readonly AudioBankValidationSeverity Severity;
        public readonly string Message;
        public readonly Object Context;

        public AudioBankValidationIssue(AudioBankValidationSeverity severity, string message, Object context)
        {
            Severity = severity;
            Message = message;
            Context = context;
        }
    }

    internal sealed class AudioBankValidationReport
    {
        public readonly List<AudioBankValidationIssue> Issues = new List<AudioBankValidationIssue>(64);
        public int ErrorCount { get; private set; }
        public int WarningCount { get; private set; }

        public void Clear()
        {
            Issues.Clear();
            ErrorCount = 0;
            WarningCount = 0;
        }

        public void Add(AudioBankValidationSeverity severity, string message, Object context)
        {
            Issues.Add(new AudioBankValidationIssue(severity, message, context));
            if (severity == AudioBankValidationSeverity.Error) ErrorCount++;
            else if (severity == AudioBankValidationSeverity.Warning) WarningCount++;
        }
    }

    internal sealed class AudioBankValidationSummary
    {
        public int BankCount { get; private set; }
        public int PassedCount { get; private set; }
        public int WarningCount { get; private set; }
        public int ErrorCount { get; private set; }

        public void Clear()
        {
            BankCount = 0;
            PassedCount = 0;
            WarningCount = 0;
            ErrorCount = 0;
        }

        public void Add(AudioBankValidationReport report)
        {
            BankCount++;

            if (report == null)
            {
                ErrorCount++;
                return;
            }

            WarningCount += report.WarningCount;
            ErrorCount += report.ErrorCount;
            if (report.ErrorCount == 0 && report.WarningCount == 0)
            {
                PassedCount++;
            }
        }
    }

    internal static class AudioBankValidator
    {
        private static readonly Dictionary<string, int> EventNameCounts = new Dictionary<string, int>(128);
        private static readonly Dictionary<string, int> AssetNameCounts = new Dictionary<string, int>(64);
        private static readonly Dictionary<string, int> StateNameCounts = new Dictionary<string, int>(16);
        private static readonly HashSet<AudioNode> VisitedNodes = new HashSet<AudioNode>();
        private static readonly HashSet<AudioNode> StackNodes = new HashSet<AudioNode>();
        private static readonly AudioBankValidationReport BatchReport = new AudioBankValidationReport();
        private static readonly AudioBankValidationSummary BatchSummary = new AudioBankValidationSummary();

        public static AudioBankValidationSummary ValidateAllAudioBanks(bool logIssues)
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioBank");
            return ValidateAudioBankGuids(guids, logIssues);
        }

        public static AudioBankValidationSummary ValidateBanks(AudioBank[] banks, bool logIssues)
        {
            BatchSummary.Clear();
            if (banks == null) return BatchSummary;

            try
            {
                for (int i = 0; i < banks.Length; i++)
                {
                    AudioBank bank = banks[i];
                    if (bank == null) continue;

                    if (banks.Length > 8)
                    {
                        EditorUtility.DisplayProgressBar("Audio Bank Validation", bank.name, (float)i / banks.Length);
                    }

                    Validate(bank, BatchReport);
                    BatchSummary.Add(BatchReport);
                    if (logIssues)
                    {
                        LogReport(BatchReport);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            LogSummary(BatchSummary);
            return BatchSummary;
        }

        private static AudioBankValidationSummary ValidateAudioBankGuids(string[] guids, bool logIssues)
        {
            BatchSummary.Clear();
            if (guids == null) return BatchSummary;

            try
            {
                for (int i = 0; i < guids.Length; i++)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    AudioBank bank = AssetDatabase.LoadAssetAtPath<AudioBank>(path);
                    if (bank == null) continue;

                    if (guids.Length > 8)
                    {
                        EditorUtility.DisplayProgressBar("Audio Bank Validation", path, (float)i / guids.Length);
                    }

                    Validate(bank, BatchReport);
                    BatchSummary.Add(BatchReport);
                    if (logIssues)
                    {
                        LogReport(BatchReport);
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            LogSummary(BatchSummary);
            return BatchSummary;
        }

        public static void Validate(AudioBank bank, AudioBankValidationReport report)
        {
            if (report == null) return;

            report.Clear();
            EventNameCounts.Clear();
            AssetNameCounts.Clear();
            StateNameCounts.Clear();
            VisitedNodes.Clear();
            StackNodes.Clear();

            if (bank == null)
            {
                report.Add(AudioBankValidationSeverity.Error, "AudioBank is missing.", null);
                return;
            }

            List<AudioEvent> events = bank.AudioEvents;
            if (events == null || events.Count == 0)
            {
                report.Add(AudioBankValidationSeverity.Warning, $"Bank '{bank.name}' has no events.", bank);
            }
            else
            {
                BuildEventNameCounts(events);

                for (int i = 0; i < events.Count; i++)
                {
                    ValidateEvent(events[i], report);
                }
            }

            ValidateParameters(bank, report);
            ValidateSwitches(bank, report);
            ValidateStateGroups(bank, report);
            ValidateStateMixProfiles(bank, report);
            ValidateReferencedActionEvents(bank, report);

            if (report.Issues.Count == 0)
            {
                report.Add(AudioBankValidationSeverity.Info, $"Bank '{bank.name}' passed validation.", bank);
            }
        }

        public static void LogReport(AudioBankValidationReport report)
        {
            if (report == null || report.Issues.Count == 0) return;

            List<AudioBankValidationIssue> issues = report.Issues;
            for (int i = 0; i < issues.Count; i++)
            {
                AudioBankValidationIssue issue = issues[i];
                if (issue.Severity == AudioBankValidationSeverity.Error)
                {
                    Debug.LogError(issue.Message, issue.Context);
                }
                else if (issue.Severity == AudioBankValidationSeverity.Warning)
                {
                    Debug.LogWarning(issue.Message, issue.Context);
                }
            }
        }

        private static void LogSummary(AudioBankValidationSummary summary)
        {
            if (summary == null) return;

            string message = $"AudioBank validation finished. Banks: {summary.BankCount}, Passed: {summary.PassedCount}, Warnings: {summary.WarningCount}, Errors: {summary.ErrorCount}.";
            if (summary.ErrorCount > 0)
            {
                Debug.LogError(message);
            }
            else if (summary.WarningCount > 0)
            {
                Debug.LogWarning(message);
            }
            else
            {
                Debug.Log(message);
            }
        }

        private static void BuildEventNameCounts(List<AudioEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                AudioEvent audioEvent = events[i];
                if (audioEvent == null || string.IsNullOrEmpty(audioEvent.name)) continue;

                string eventName = audioEvent.name;
                if (EventNameCounts.TryGetValue(eventName, out int count))
                {
                    EventNameCounts[eventName] = count + 1;
                }
                else
                {
                    EventNameCounts.Add(eventName, 1);
                }
            }
        }

        private static void ValidateEvent(AudioEvent audioEvent, AudioBankValidationReport report)
        {
            if (audioEvent == null)
            {
                report.Add(AudioBankValidationSeverity.Error, "Bank contains a missing AudioEvent reference.", null);
                return;
            }

            if (string.IsNullOrEmpty(audioEvent.name))
            {
                report.Add(AudioBankValidationSeverity.Error, "AudioEvent has an empty name.", audioEvent);
            }
            else if (EventNameCounts.TryGetValue(audioEvent.name, out int count) && count > 1)
            {
                report.Add(AudioBankValidationSeverity.Error, $"AudioEvent name '{audioEvent.name}' is duplicated in this bank.", audioEvent);
            }

            if (audioEvent.Output == null)
            {
                report.Add(AudioBankValidationSeverity.Error, $"AudioEvent '{audioEvent.name}' is missing an AudioOutput node.", audioEvent);
                return;
            }

            ValidateAudioOutputNode(audioEvent, audioEvent.Output, report);

            List<AudioNode> nodes = audioEvent.Nodes;
            if (nodes == null || nodes.Count == 0)
            {
                report.Add(AudioBankValidationSeverity.Warning, $"AudioEvent '{audioEvent.name}' has no nodes.", audioEvent);
                return;
            }

            bool hasPlayableNode = false;
            for (int i = 0; i < nodes.Count; i++)
            {
                AudioNode node = nodes[i];
                if (node is AudioFile || node is AudioVoiceFile || node is AudioBlendFile) hasPlayableNode = true;
                ValidateNode(audioEvent, node, report);
            }

            if (!hasPlayableNode)
            {
                report.Add(AudioBankValidationSeverity.Warning, $"AudioEvent '{audioEvent.name}' has no playable audio file node.", audioEvent);
            }

            VisitedNodes.Clear();
            StackNodes.Clear();
            DetectCycles(audioEvent.Output, report);

            for (int i = 0; i < nodes.Count; i++)
            {
                AudioNode node = nodes[i];
                if (node != null && !VisitedNodes.Contains(node))
                {
                    report.Add(AudioBankValidationSeverity.Warning, $"AudioEvent '{audioEvent.name}' node '{node.name}' is not connected to the output graph.", node);
                }
            }
        }

        private static void ValidateNode(AudioEvent audioEvent, AudioNode node, AudioBankValidationReport report)
        {
            if (node == null)
            {
                report.Add(AudioBankValidationSeverity.Error, $"AudioEvent '{audioEvent.name}' contains a missing node reference.", audioEvent);
                return;
            }

            if (node is AudioFile audioFile)
            {
                ValidateAudioFileNode(audioEvent, audioFile, report);
            }
            else if (node is AudioVoiceFile voiceFile)
            {
                ValidateVoiceFileNode(audioEvent, voiceFile, report);
            }
            else if (node is AudioBlendFile blendFile)
            {
                ValidateBlendFileNode(audioEvent, blendFile, report);
            }

            if (node.Input == null) return;

            AudioNodeOutput[] connectedNodes = node.Input.ConnectedNodes;
            if (connectedNodes == null) return;

            for (int i = 0; i < connectedNodes.Length; i++)
            {
                AudioNodeOutput output = connectedNodes[i];
                if (output == null || output.ParentNode == null)
                {
                    report.Add(AudioBankValidationSeverity.Error, $"AudioEvent '{audioEvent.name}' node '{node.name}' has a broken input connection.", node);
                }
            }
        }

        private static void ValidateAudioFileNode(AudioEvent audioEvent, AudioFile node, AudioBankValidationReport report)
        {
            if (node.SourceMode == AudioFile.AudioFileSourceMode.EmbeddedClip)
            {
                if (node.File == null)
                {
                    report.Add(AudioBankValidationSeverity.Error, $"AudioEvent '{audioEvent.name}' audio file node '{node.name}' is missing an embedded AudioClip.", node);
                }

                return;
            }

            AudioClipReference reference = node.ExternalReference;
            if (reference == null)
            {
                report.Add(AudioBankValidationSeverity.Error, $"AudioEvent '{audioEvent.name}' audio file node '{node.name}' is missing an AudioClipReference.", node);
                return;
            }

            ValidateAudioClipReference(reference, report);
        }

        private static void ValidateVoiceFileNode(AudioEvent audioEvent, AudioVoiceFile node, AudioBankValidationReport report)
        {
            if (node.SourceMode == AudioFile.AudioFileSourceMode.EmbeddedClip)
            {
                if (node.File == null)
                {
                    report.Add(AudioBankValidationSeverity.Error, $"AudioEvent '{audioEvent.name}' voice file node '{node.name}' is missing an embedded AudioClip.", node);
                }

                return;
            }

            AudioClipReference reference = node.ExternalReference;
            if (reference == null)
            {
                report.Add(AudioBankValidationSeverity.Error, $"AudioEvent '{audioEvent.name}' voice file node '{node.name}' is missing an AudioClipReference.", node);
                return;
            }

            ValidateAudioClipReference(reference, report);
        }

        private static void ValidateBlendFileNode(AudioEvent audioEvent, AudioBlendFile node, AudioBankValidationReport report)
        {
            if (node.SourceMode == AudioFile.AudioFileSourceMode.EmbeddedClip)
            {
                if (node.File == null)
                {
                    report.Add(AudioBankValidationSeverity.Error, $"AudioEvent '{audioEvent.name}' blend file node '{node.name}' is missing an embedded AudioClip.", node);
                }

                return;
            }

            AudioClipReference reference = node.ExternalReference;
            if (reference == null)
            {
                report.Add(AudioBankValidationSeverity.Error, $"AudioEvent '{audioEvent.name}' blend file node '{node.name}' is missing an AudioClipReference.", node);
                return;
            }

            ValidateAudioClipReference(reference, report);
        }

        private static void ValidateAudioClipReference(AudioClipReference reference, AudioBankValidationReport report)
        {
            if (string.IsNullOrWhiteSpace(reference.Location))
            {
                report.Add(AudioBankValidationSeverity.Error, $"AudioClipReference '{reference.name}' has an empty location.", reference);
            }

            if (reference.LocationKind == AudioLocationKind.AssetAddress && !reference.HasEditorAssetLink)
            {
                report.Add(AudioBankValidationSeverity.Warning, $"AudioClipReference '{reference.name}' uses AssetAddress without an editor asset link.", reference);
            }
        }

        private static void ValidateAudioOutputNode(AudioEvent audioEvent, AudioOutput output, AudioBankValidationReport report)
        {
            if (output.EffectiveSpatialBlend <= 0f) return;

            if (output.EffectiveMaxDistance < output.EffectiveMinDistance)
            {
                report.Add(AudioBankValidationSeverity.Error, $"AudioEvent '{audioEvent.name}' output has MaxDistance below MinDistance.", output);
            }

            AnimationCurve attenuationCurve = output.EffectiveAttenuationCurve;
            if (attenuationCurve == null || attenuationCurve.length == 0)
            {
                report.Add(AudioBankValidationSeverity.Warning, $"AudioEvent '{audioEvent.name}' output has no attenuation curve; runtime falls back to logarithmic rolloff.", output);
            }

            if (output.EffectiveUseDistanceLowPass)
            {
                AnimationCurve lowPassCurve = output.EffectiveDistanceLowPassCurve;
                if (lowPassCurve == null || lowPassCurve.length == 0)
                    report.Add(AudioBankValidationSeverity.Warning, $"AudioEvent '{audioEvent.name}' output enables distance low-pass with an empty curve.", output);
            }

            if (output.EffectiveUseSpreadCurve)
            {
                AnimationCurve spreadCurve = output.EffectiveSpreadCurve;
                if (spreadCurve == null || spreadCurve.length == 0)
                    report.Add(AudioBankValidationSeverity.Warning, $"AudioEvent '{audioEvent.name}' output enables spread curve with an empty curve.", output);
            }
        }

        private static void ValidateParameters(AudioBank bank, AudioBankValidationReport report)
        {
            IReadOnlyList<AudioParameter> parameters = bank.Parameters;
            if (parameters == null) return;

            AssetNameCounts.Clear();
            for (int i = 0; i < parameters.Count; i++)
            {
                AudioParameter parameter = parameters[i];
                if (parameter == null)
                {
                    report.Add(AudioBankValidationSeverity.Error, $"Bank '{bank.name}' contains a missing AudioParameter reference.", bank);
                    continue;
                }

                CountAssetName(parameter.name);
            }

            for (int i = 0; i < parameters.Count; i++)
            {
                AudioParameter parameter = parameters[i];
                if (parameter == null) continue;

                if (string.IsNullOrEmpty(parameter.name))
                {
                    report.Add(AudioBankValidationSeverity.Error, "AudioParameter has an empty name.", parameter);
                }
                else if (AssetNameCounts.TryGetValue(parameter.name, out int count) && count > 1)
                {
                    report.Add(AudioBankValidationSeverity.Error, $"AudioParameter name '{parameter.name}' is duplicated in this bank.", parameter);
                }
            }

            AssetNameCounts.Clear();
        }

        private static void ValidateSwitches(AudioBank bank, AudioBankValidationReport report)
        {
            IReadOnlyList<AudioSwitch> switches = bank.Switches;
            if (switches == null) return;

            AssetNameCounts.Clear();
            for (int i = 0; i < switches.Count; i++)
            {
                AudioSwitch audioSwitch = switches[i];
                if (audioSwitch == null)
                {
                    report.Add(AudioBankValidationSeverity.Error, $"Bank '{bank.name}' contains a missing AudioSwitch reference.", bank);
                    continue;
                }

                CountAssetName(audioSwitch.name);
            }

            for (int i = 0; i < switches.Count; i++)
            {
                AudioSwitch audioSwitch = switches[i];
                if (audioSwitch == null) continue;

                if (string.IsNullOrEmpty(audioSwitch.name))
                {
                    report.Add(AudioBankValidationSeverity.Error, "AudioSwitch has an empty name.", audioSwitch);
                }
                else if (AssetNameCounts.TryGetValue(audioSwitch.name, out int count) && count > 1)
                {
                    report.Add(AudioBankValidationSeverity.Error, $"AudioSwitch name '{audioSwitch.name}' is duplicated in this bank.", audioSwitch);
                }

                ValidateStateNameArray("AudioSwitch", audioSwitch.name, audioSwitch.StateNames, audioSwitch.DefaultValue, audioSwitch, report);
            }

            AssetNameCounts.Clear();
        }

        private static void ValidateStateGroups(AudioBank bank, AudioBankValidationReport report)
        {
            IReadOnlyList<AudioStateGroup> stateGroups = bank.StateGroups;
            if (stateGroups == null) return;

            AssetNameCounts.Clear();
            for (int i = 0; i < stateGroups.Count; i++)
            {
                AudioStateGroup stateGroup = stateGroups[i];
                if (stateGroup == null)
                {
                    report.Add(AudioBankValidationSeverity.Error, $"Bank '{bank.name}' contains a missing AudioStateGroup reference.", bank);
                    continue;
                }

                CountAssetName(stateGroup.name);
            }

            for (int i = 0; i < stateGroups.Count; i++)
            {
                AudioStateGroup stateGroup = stateGroups[i];
                if (stateGroup == null) continue;

                if (string.IsNullOrEmpty(stateGroup.name))
                {
                    report.Add(AudioBankValidationSeverity.Error, "AudioStateGroup has an empty name.", stateGroup);
                }
                else if (AssetNameCounts.TryGetValue(stateGroup.name, out int count) && count > 1)
                {
                    report.Add(AudioBankValidationSeverity.Error, $"AudioStateGroup name '{stateGroup.name}' is duplicated in this bank.", stateGroup);
                }

                ValidateStateNameArray("AudioStateGroup", stateGroup.name, stateGroup.StateNames, stateGroup.DefaultValue, stateGroup, report);
            }

            AssetNameCounts.Clear();
        }

        private static void ValidateStateMixProfiles(AudioBank bank, AudioBankValidationReport report)
        {
            IReadOnlyList<AudioStateMixProfile> profiles = bank.StateMixProfiles;
            if (profiles == null) return;

            for (int i = 0; i < profiles.Count; i++)
            {
                AudioStateMixProfile profile = profiles[i];
                if (profile == null)
                {
                    report.Add(AudioBankValidationSeverity.Error, $"Bank '{bank.name}' contains a missing AudioStateMixProfile reference.", bank);
                    continue;
                }

                if (string.IsNullOrEmpty(profile.name))
                {
                    report.Add(AudioBankValidationSeverity.Error, "AudioStateMixProfile has an empty name.", profile);
                }

                IReadOnlyList<AudioStateMixRule> rules = profile.Rules;
                if (rules == null || rules.Count == 0)
                {
                    report.Add(AudioBankValidationSeverity.Warning, $"AudioStateMixProfile '{profile.name}' has no rules.", profile);
                    continue;
                }

                for (int ruleIndex = 0; ruleIndex < rules.Count; ruleIndex++)
                {
                    ValidateStateMixRule(bank, profile, rules[ruleIndex], ruleIndex, report);
                }
            }
        }

        private static void ValidateStateMixRule(AudioBank bank, AudioStateMixProfile profile, AudioStateMixRule rule, int ruleIndex, AudioBankValidationReport report)
        {
            if (rule == null)
            {
                report.Add(AudioBankValidationSeverity.Error, $"AudioStateMixProfile '{profile.name}' has a missing rule at index {ruleIndex}.", profile);
                return;
            }

            AudioStateGroup stateGroup = rule.StateGroup;
            if (stateGroup == null)
            {
                report.Add(AudioBankValidationSeverity.Error, $"AudioStateMixProfile '{profile.name}' rule {ruleIndex} has no state group.", profile);
                return;
            }

            if (!BankContainsStateGroup(bank, stateGroup))
            {
                report.Add(AudioBankValidationSeverity.Warning, $"AudioStateMixProfile '{profile.name}' rule {ruleIndex} references state group '{stateGroup.name}' outside this bank.", profile);
            }

            if (string.IsNullOrEmpty(rule.StateName))
            {
                report.Add(AudioBankValidationSeverity.Error, $"AudioStateMixProfile '{profile.name}' rule {ruleIndex} has an empty state name.", profile);
            }
            else if (stateGroup.GetStateIndex(rule.StateName) < 0)
            {
                report.Add(AudioBankValidationSeverity.Error, $"AudioStateMixProfile '{profile.name}' rule {ruleIndex} references missing state '{rule.StateName}'.", profile);
            }

            switch (rule.TargetType)
            {
                case AudioStateMixTargetType.Parameter:
                    if (rule.Parameter == null)
                    {
                        report.Add(AudioBankValidationSeverity.Error, $"AudioStateMixProfile '{profile.name}' rule {ruleIndex} has no AudioParameter target.", profile);
                    }
                    else if (!BankContainsParameter(bank, rule.Parameter))
                    {
                        report.Add(AudioBankValidationSeverity.Warning, $"AudioStateMixProfile '{profile.name}' rule {ruleIndex} references parameter '{rule.Parameter.name}' outside this bank.", profile);
                    }
                    break;
                case AudioStateMixTargetType.MixerParameter:
                    if (string.IsNullOrEmpty(rule.MixerParameterName))
                    {
                        report.Add(AudioBankValidationSeverity.Error, $"AudioStateMixProfile '{profile.name}' rule {ruleIndex} has an empty mixer parameter name.", profile);
                    }
                    break;
                case AudioStateMixTargetType.Snapshot:
                    if (rule.Snapshot == null)
                    {
                        report.Add(AudioBankValidationSeverity.Error, $"AudioStateMixProfile '{profile.name}' rule {ruleIndex} has no AudioMixerSnapshot target.", profile);
                    }
                    else if (rule.SnapshotTransitionTime < 0f)
                    {
                        report.Add(AudioBankValidationSeverity.Error, $"AudioStateMixProfile '{profile.name}' rule {ruleIndex} has a negative snapshot transition time.", profile);
                    }
                    break;
            }
        }

        private static bool BankContainsStateGroup(AudioBank bank, AudioStateGroup stateGroup)
        {
            IReadOnlyList<AudioStateGroup> stateGroups = bank.StateGroups;
            if (stateGroups == null) return false;

            for (int i = 0; i < stateGroups.Count; i++)
            {
                if (stateGroups[i] == stateGroup)
                    return true;
            }

            return false;
        }

        private static bool BankContainsParameter(AudioBank bank, AudioParameter parameter)
        {
            IReadOnlyList<AudioParameter> parameters = bank.Parameters;
            if (parameters == null) return false;

            for (int i = 0; i < parameters.Count; i++)
            {
                if (parameters[i] == parameter)
                    return true;
            }

            return false;
        }

        private static void ValidateReferencedActionEvents(AudioBank bank, AudioBankValidationReport report)
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioActionEvent");
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                AudioActionEvent actionEvent = AssetDatabase.LoadAssetAtPath<AudioActionEvent>(path);
                if (actionEvent == null) continue;
                if (!ActionEventTouchesBank(bank, actionEvent)) continue;

                ValidateActionEvent(bank, actionEvent, report);
            }
        }

        private static bool ActionEventTouchesBank(AudioBank bank, AudioActionEvent actionEvent)
        {
            int actionCount = actionEvent.ActionCount;
            for (int i = 0; i < actionCount; i++)
            {
                AudioEventAction action = actionEvent.GetAction(i);
                if (action == null) continue;

                if (action.AudioEvent != null && BankContainsEvent(bank, action.AudioEvent))
                    return true;
                if (action.Parameter != null && BankContainsParameter(bank, action.Parameter))
                    return true;
                if (action.StateGroup != null && BankContainsStateGroup(bank, action.StateGroup))
                    return true;
            }

            return false;
        }

        private static void ValidateActionEvent(AudioBank bank, AudioActionEvent actionEvent, AudioBankValidationReport report)
        {
            int actionCount = actionEvent.ActionCount;
            if (actionCount == 0)
            {
                report.Add(AudioBankValidationSeverity.Warning, $"AudioActionEvent '{actionEvent.name}' has no actions.", actionEvent);
                return;
            }

            for (int i = 0; i < actionCount; i++)
            {
                ValidateAction(bank, actionEvent, actionEvent.GetAction(i), i, report);
            }
        }

        private static void ValidateAction(AudioBank bank, AudioActionEvent actionEvent, AudioEventAction action, int actionIndex, AudioBankValidationReport report)
        {
            if (action == null)
            {
                report.Add(AudioBankValidationSeverity.Error, $"AudioActionEvent '{actionEvent.name}' has a missing action at index {actionIndex}.", actionEvent);
                return;
            }

            switch (action.ActionType)
            {
                case AudioActionType.PlayEvent:
                case AudioActionType.StopEvent:
                    if (action.AudioEvent == null)
                    {
                        report.Add(AudioBankValidationSeverity.Error, $"AudioActionEvent '{actionEvent.name}' action {actionIndex} has no AudioEvent target.", actionEvent);
                    }
                    else if (!BankContainsEvent(bank, action.AudioEvent))
                    {
                        report.Add(AudioBankValidationSeverity.Warning, $"AudioActionEvent '{actionEvent.name}' action {actionIndex} references event '{action.AudioEvent.name}' outside bank '{bank.name}'.", actionEvent);
                    }
                    break;
                case AudioActionType.StopEventByName:
                    if (string.IsNullOrEmpty(action.EventName))
                    {
                        report.Add(AudioBankValidationSeverity.Error, $"AudioActionEvent '{actionEvent.name}' action {actionIndex} has an empty event name.", actionEvent);
                    }
                    break;
                case AudioActionType.SetParameter:
                    if (action.Parameter == null)
                    {
                        report.Add(AudioBankValidationSeverity.Error, $"AudioActionEvent '{actionEvent.name}' action {actionIndex} has no AudioParameter target.", actionEvent);
                    }
                    else if (!BankContainsParameter(bank, action.Parameter))
                    {
                        report.Add(AudioBankValidationSeverity.Warning, $"AudioActionEvent '{actionEvent.name}' action {actionIndex} references parameter '{action.Parameter.name}' outside bank '{bank.name}'.", actionEvent);
                    }
                    break;
                case AudioActionType.SetParameterByName:
                    if (string.IsNullOrEmpty(action.ParameterName))
                    {
                        report.Add(AudioBankValidationSeverity.Error, $"AudioActionEvent '{actionEvent.name}' action {actionIndex} has an empty parameter name.", actionEvent);
                    }
                    break;
                case AudioActionType.SetState:
                    if (action.StateGroup == null)
                    {
                        report.Add(AudioBankValidationSeverity.Error, $"AudioActionEvent '{actionEvent.name}' action {actionIndex} has no state group target.", actionEvent);
                    }
                    else if (!BankContainsStateGroup(bank, action.StateGroup))
                    {
                        report.Add(AudioBankValidationSeverity.Warning, $"AudioActionEvent '{actionEvent.name}' action {actionIndex} references state group '{action.StateGroup.name}' outside bank '{bank.name}'.", actionEvent);
                    }
                    break;
                case AudioActionType.SetStateByName:
                    if (string.IsNullOrEmpty(action.StateGroupName) || string.IsNullOrEmpty(action.StateName))
                    {
                        report.Add(AudioBankValidationSeverity.Error, $"AudioActionEvent '{actionEvent.name}' action {actionIndex} has an empty state group or state name.", actionEvent);
                    }
                    break;
                case AudioActionType.SetMixerParameter:
                    if (string.IsNullOrEmpty(action.MixerParameterName))
                    {
                        report.Add(AudioBankValidationSeverity.Error, $"AudioActionEvent '{actionEvent.name}' action {actionIndex} has an empty mixer parameter name.", actionEvent);
                    }
                    break;
                case AudioActionType.TransitionSnapshot:
                    if (action.Snapshot == null)
                    {
                        report.Add(AudioBankValidationSeverity.Error, $"AudioActionEvent '{actionEvent.name}' action {actionIndex} has no AudioMixerSnapshot target.", actionEvent);
                    }
                    break;
            }
        }

        private static bool BankContainsEvent(AudioBank bank, AudioEvent audioEvent)
        {
            List<AudioEvent> events = bank.AudioEvents;
            if (events == null) return false;

            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] == audioEvent)
                    return true;
            }

            return false;
        }

        private static void CountAssetName(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return;

            if (AssetNameCounts.TryGetValue(assetName, out int count))
            {
                AssetNameCounts[assetName] = count + 1;
            }
            else
            {
                AssetNameCounts.Add(assetName, 1);
            }
        }

        private static void ValidateStateNameArray(string assetType, string assetName, string[] states, int defaultValue, Object context, AudioBankValidationReport report)
        {
            if (states == null || states.Length == 0)
            {
                report.Add(AudioBankValidationSeverity.Error, $"{assetType} '{assetName}' has no states.", context);
                return;
            }

            if (defaultValue < 0 || defaultValue >= states.Length)
            {
                report.Add(AudioBankValidationSeverity.Error, $"{assetType} '{assetName}' default state index is out of range.", context);
            }

            StateNameCounts.Clear();
            for (int i = 0; i < states.Length; i++)
            {
                string state = states[i];
                if (string.IsNullOrEmpty(state))
                {
                    report.Add(AudioBankValidationSeverity.Error, $"{assetType} '{assetName}' has an empty state at index {i}.", context);
                    continue;
                }

                if (StateNameCounts.TryGetValue(state, out int stateCount))
                {
                    StateNameCounts[state] = stateCount + 1;
                }
                else
                {
                    StateNameCounts.Add(state, 1);
                }
            }

            for (int i = 0; i < states.Length; i++)
            {
                string state = states[i];
                if (string.IsNullOrEmpty(state)) continue;

                if (StateNameCounts.TryGetValue(state, out int count) && count > 1)
                {
                    report.Add(AudioBankValidationSeverity.Error, $"{assetType} '{assetName}' has duplicate state '{state}'.", context);
                }
            }

            StateNameCounts.Clear();
        }

        private static void DetectCycles(AudioNode node, AudioBankValidationReport report)
        {
            if (node == null) return;
            if (StackNodes.Contains(node))
            {
                report.Add(AudioBankValidationSeverity.Error, $"Audio graph contains a cycle at node '{node.name}'.", node);
                return;
            }

            if (VisitedNodes.Contains(node)) return;

            VisitedNodes.Add(node);
            StackNodes.Add(node);

            AudioNodeInput input = node.Input;
            AudioNodeOutput[] connectedNodes = input != null ? input.ConnectedNodes : null;
            if (connectedNodes != null)
            {
                for (int i = 0; i < connectedNodes.Length; i++)
                {
                    AudioNodeOutput output = connectedNodes[i];
                    if (output == null) continue;
                    DetectCycles(output.ParentNode, report);
                }
            }

            StackNodes.Remove(node);
        }
    }

    [CustomEditor(typeof(AudioActionEvent))]
    public sealed class AudioActionEventEditor : UnityEditor.Editor
    {
        private SerializedProperty actionsProp;

        private void OnEnable()
        {
            actionsProp = serializedObject.FindProperty("actions");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Add Action", EditorStyles.toolbarButton))
            {
                int index = actionsProp.arraySize;
                actionsProp.InsertArrayElementAtIndex(index);
                SerializedProperty action = actionsProp.GetArrayElementAtIndex(index);
                ResetAction(action);
                action.isExpanded = true;
            }
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Execute", EditorStyles.toolbarButton))
                {
                    ((AudioActionEvent)target).Execute();
                }
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(actionsProp.arraySize + " actions", EditorStyles.miniLabel, GUILayout.Width(72f));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4f);

            if (actionsProp.arraySize == 0)
            {
                EditorGUILayout.HelpBox("Add actions to build a reusable audio event command list.", MessageType.Info);
            }

            for (int i = 0; i < actionsProp.arraySize; i++)
            {
                DrawAction(i);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawAction(int index)
        {
            SerializedProperty action = actionsProp.GetArrayElementAtIndex(index);
            SerializedProperty typeProp = action.FindPropertyRelative("actionType");
            AudioActionType actionType = (AudioActionType)typeProp.enumValueIndex;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            action.isExpanded = EditorGUILayout.Foldout(action.isExpanded, index + ". " + actionType, true);
            if (GUILayout.Button("Up", EditorStyles.miniButtonLeft, GUILayout.Width(34f)) && index > 0)
            {
                actionsProp.MoveArrayElement(index, index - 1);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            if (GUILayout.Button("Down", EditorStyles.miniButtonMid, GUILayout.Width(48f)) && index < actionsProp.arraySize - 1)
            {
                actionsProp.MoveArrayElement(index, index + 1);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            if (GUILayout.Button("Delete", EditorStyles.miniButtonRight, GUILayout.Width(54f)))
            {
                actionsProp.DeleteArrayElementAtIndex(index);
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                return;
            }
            EditorGUILayout.EndHorizontal();

            if (action.isExpanded)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(typeProp, new GUIContent("Action"));
                EditorGUILayout.PropertyField(action.FindPropertyRelative("delaySeconds"), new GUIContent("Delay"));
                DrawActionFields(action, actionType);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndVertical();
        }

        private static void ResetAction(SerializedProperty action)
        {
            action.FindPropertyRelative("actionType").enumValueIndex = (int)AudioActionType.PlayEvent;
            action.FindPropertyRelative("delaySeconds").floatValue = 0f;
            action.FindPropertyRelative("audioEvent").objectReferenceValue = null;
            action.FindPropertyRelative("eventName").stringValue = string.Empty;
            action.FindPropertyRelative("group").intValue = 0;
            action.FindPropertyRelative("useExplicitPosition").boolValue = false;
            action.FindPropertyRelative("position").vector3Value = Vector3.zero;
            action.FindPropertyRelative("parameter").objectReferenceValue = null;
            action.FindPropertyRelative("parameterName").stringValue = string.Empty;
            action.FindPropertyRelative("parameterValue").floatValue = 0f;
            action.FindPropertyRelative("stateGroup").objectReferenceValue = null;
            action.FindPropertyRelative("stateValue").intValue = 0;
            action.FindPropertyRelative("stateGroupName").stringValue = string.Empty;
            action.FindPropertyRelative("stateName").stringValue = string.Empty;
            action.FindPropertyRelative("mixerParameterName").stringValue = string.Empty;
            action.FindPropertyRelative("mixerParameterValue").floatValue = 0f;
            action.FindPropertyRelative("snapshot").objectReferenceValue = null;
            action.FindPropertyRelative("snapshotTransitionTime").floatValue = 0.1f;
        }

        private static void DrawActionFields(SerializedProperty action, AudioActionType actionType)
        {
            switch (actionType)
            {
                case AudioActionType.PlayEvent:
                    EditorGUILayout.PropertyField(action.FindPropertyRelative("audioEvent"), new GUIContent("Event"));
                    SerializedProperty usePosition = action.FindPropertyRelative("useExplicitPosition");
                    EditorGUILayout.PropertyField(usePosition, new GUIContent("Use Position"));
                    if (usePosition.boolValue)
                    {
                        EditorGUILayout.PropertyField(action.FindPropertyRelative("position"), new GUIContent("Position"));
                    }
                    break;
                case AudioActionType.StopEvent:
                    EditorGUILayout.PropertyField(action.FindPropertyRelative("audioEvent"), new GUIContent("Event"));
                    break;
                case AudioActionType.StopEventByName:
                    EditorGUILayout.PropertyField(action.FindPropertyRelative("eventName"), new GUIContent("Event Name"));
                    break;
                case AudioActionType.StopGroup:
                    EditorGUILayout.PropertyField(action.FindPropertyRelative("group"), new GUIContent("Group"));
                    break;
                case AudioActionType.SetParameter:
                    EditorGUILayout.PropertyField(action.FindPropertyRelative("parameter"), new GUIContent("Parameter"));
                    EditorGUILayout.PropertyField(action.FindPropertyRelative("parameterValue"), new GUIContent("Value"));
                    break;
                case AudioActionType.SetParameterByName:
                    EditorGUILayout.PropertyField(action.FindPropertyRelative("parameterName"), new GUIContent("Parameter Name"));
                    EditorGUILayout.PropertyField(action.FindPropertyRelative("parameterValue"), new GUIContent("Value"));
                    break;
                case AudioActionType.SetState:
                    SerializedProperty stateGroupProp = action.FindPropertyRelative("stateGroup");
                    SerializedProperty stateValueProp = action.FindPropertyRelative("stateValue");
                    EditorGUILayout.PropertyField(stateGroupProp, new GUIContent("State Group"));
                    DrawStateValueField(stateGroupProp.objectReferenceValue as AudioStateGroup, stateValueProp);
                    break;
                case AudioActionType.SetStateByName:
                    EditorGUILayout.PropertyField(action.FindPropertyRelative("stateGroupName"), new GUIContent("State Group Name"));
                    EditorGUILayout.PropertyField(action.FindPropertyRelative("stateName"), new GUIContent("State Name"));
                    break;
                case AudioActionType.SetMixerParameter:
                    EditorGUILayout.PropertyField(action.FindPropertyRelative("mixerParameterName"), new GUIContent("Mixer Parameter"));
                    EditorGUILayout.PropertyField(action.FindPropertyRelative("mixerParameterValue"), new GUIContent("Value"));
                    break;
                case AudioActionType.TransitionSnapshot:
                    EditorGUILayout.PropertyField(action.FindPropertyRelative("snapshot"), new GUIContent("Snapshot"));
                    EditorGUILayout.PropertyField(action.FindPropertyRelative("snapshotTransitionTime"), new GUIContent("Transition Time"));
                    break;
            }
        }

        private static void DrawStateValueField(AudioStateGroup stateGroup, SerializedProperty stateValueProp)
        {
            string[] stateNames = stateGroup != null ? stateGroup.StateNames : null;
            if (stateNames != null && stateNames.Length > 0)
            {
                int value = Mathf.Clamp(stateValueProp.intValue, 0, stateNames.Length - 1);
                stateValueProp.intValue = EditorGUILayout.Popup("State", value, stateNames);
            }
            else
            {
                EditorGUILayout.PropertyField(stateValueProp, new GUIContent("State"));
            }
        }
    }
}
