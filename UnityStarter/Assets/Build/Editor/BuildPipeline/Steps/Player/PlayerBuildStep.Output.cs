using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Build.Pipeline.Editor
{
    public sealed partial class PlayerBuildStep
    {
        private static void DeleteDesktopDebugDirectories(
            BuildRequest request,
            string outputPath)
        {
            if (request.Target != BuildTarget.StandaloneWindows64
                && request.Target != BuildTarget.StandaloneOSX
                && request.Target != BuildTarget.StandaloneLinux64)
            {
                return;
            }

            string parent = Path.GetDirectoryName(outputPath);
            string productName = request.ProductName;
            string[] names =
            {
                productName + "_BackUpThisFolder_ButDontShipItWithYourGame",
                productName + "_BurstDebugInformation_DoNotShip"
            };

            foreach (string name in names)
            {
                string path = Path.Combine(parent, name);
                if (!Directory.Exists(path))
                {
                    continue;
                }

                BuildPathPolicy.EnsureSafeDeleteDirectoryTree(
                    request.ProjectRoot,
                    path,
                    request.BuildRoot,
                    request.AllowExternalOutput);
                Directory.Delete(path, true);
            }
        }
    }
}

