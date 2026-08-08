using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Build.Data;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public sealed partial class BuildDataEditor
    {
        private bool DrawWorkspaceHealth()
        {
            RefreshWorkspaceSnapshotIfRequired();
            BuildInspectorStatus status = GetWorkspaceInspectorStatus();
            showWorkspaceDetails = BuildInspectorUi.DrawFoldoutHeader(
                "Workspace Safety",
                showWorkspaceDetails,
                BuildInspectorUi.SafetyColor,
                status,
                "Durable transaction evidence and project-wide build lease state.");
            bool isReady = workspaceSnapshot != null
                && workspaceSnapshot.Status == BuildWorkspaceHealthStatus.Clean;
            if (!showWorkspaceDetails)
            {
                return isReady;
            }

            BuildInspectorUi.BeginPanel();
            if (workspaceSnapshot == null)
            {
                BuildInspectorUi.DrawNotice(
                    string.IsNullOrWhiteSpace(workspaceInspectionError)
                        ? "Build workspace health is unavailable."
                        : workspaceInspectionError,
                    BuildInspectorTone.Error);
            }
            else if (workspaceSnapshot.Status == BuildWorkspaceHealthStatus.Clean)
            {
                BuildInspectorUi.DrawStatusRow(
                    "Transaction Evidence",
                    "Clean",
                    BuildInspectorTone.Ready);
                BuildInspectorUi.DrawMutedText(workspaceSnapshot.Summary);
            }
            else
            {
                BuildInspectorUi.DrawNotice(
                    workspaceSnapshot.Summary,
                    workspaceSnapshot.Status == BuildWorkspaceHealthStatus.Blocked
                        ? BuildInspectorTone.Error
                        : workspaceSnapshot.Status == BuildWorkspaceHealthStatus.Busy
                            ? BuildInspectorTone.Busy
                            : BuildInspectorTone.Warning);
            }

            var commands = new[]
            {
                new BuildInspectorCommand(
                    0,
                    new GUIContent(
                        "Refresh Status",
                        "Inspect durable transaction evidence again without changing the workspace.")),
                new BuildInspectorCommand(
                    1,
                    new GUIContent(
                        "Open Workspace Health",
                        "Open detailed recovery evidence and explicit recovery actions."))
            };
            int clicked = BuildInspectorUi.DrawCommandGrid(commands, maximumColumns: 2);
            if (clicked == 0)
            {
                RefreshWorkspaceSnapshot();
            }
            else if (clicked == 1)
            {
                BuildWorkspaceHealthWindow.ShowWindow();
            }

            BuildInspectorUi.EndPanel();
            return isReady;
        }

        private void RefreshWorkspaceSnapshotIfRequired()
        {
            if (Event.current.type == EventType.Layout
                && EditorApplication.timeSinceStartup >= nextWorkspaceInspectionTime)
            {
                RefreshWorkspaceSnapshot();
            }
        }

        private BuildInspectorStatus GetWorkspaceInspectorStatus()
        {
            if (workspaceSnapshot == null)
            {
                return new BuildInspectorStatus(
                    BuildInspectorTone.Error,
                    "UNAVAILABLE",
                    workspaceInspectionError);
            }

            switch (workspaceSnapshot.Status)
            {
                case BuildWorkspaceHealthStatus.Clean:
                    return new BuildInspectorStatus(BuildInspectorTone.Ready, "CLEAN");
                case BuildWorkspaceHealthStatus.RecoveryRequired:
                    return new BuildInspectorStatus(BuildInspectorTone.Warning, "RECOVERY");
                case BuildWorkspaceHealthStatus.Busy:
                    return new BuildInspectorStatus(BuildInspectorTone.Busy, "BUSY");
                default:
                    return new BuildInspectorStatus(BuildInspectorTone.Error, "BLOCKED");
            }
        }

        private void RefreshWorkspaceSnapshot()
        {
            nextWorkspaceInspectionTime = EditorApplication.timeSinceStartup + 2d;
            workspaceInspectionError = null;
            try
            {
                workspaceSnapshot = BuildWorkspaceService.Inspect();
            }
            catch (Exception exception)
            {
                workspaceSnapshot = null;
                workspaceInspectionError =
                    "Build workspace inspection failed: " + exception.Message;
            }
        }
    }
}
