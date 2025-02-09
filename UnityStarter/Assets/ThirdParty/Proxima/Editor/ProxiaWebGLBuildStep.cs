using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Proxima
{
    public class ProxiaWebGLBuildStep : IPostprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform == BuildTarget.WebGL)
            {
                var staticFiles = Resources.Load<ProximaStatic>("Proxima/web");
                string destinationPath = Path.Combine(report.summary.outputPath, "StreamingAssets", "proxima");

                if (Directory.Exists(destinationPath))
                {
                    Directory.Delete(destinationPath, true);
                }

                foreach (var file in staticFiles.Files)
                {
                    if (file.Path.EndsWith("/")) continue;
                    var filePath = Path.Combine(destinationPath, file.Path);
                    var dirPath = Path.GetDirectoryName(filePath);
                    Directory.CreateDirectory(dirPath);
                    File.WriteAllBytes(filePath, file.Bytes);
                }
            }
        }
    }
}