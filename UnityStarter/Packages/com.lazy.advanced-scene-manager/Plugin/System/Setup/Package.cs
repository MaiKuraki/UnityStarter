#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace AdvancedSceneManager
{

    /// <summary>Contains info about the ASM package.</summary>
    /// <remarks>Only available in editor.</remarks>
    public class Package : DependencyInjection.Editor.IPackage
    {

        /// <summary>The id of this package.</summary>
        public string id => ReadProperty(p => p.packageId);

        /// <summary>The folder that ASM is contained within.</summary>
        public string folder => ReadProperty(p => p.assetPath);

        /// <summary>The version of ASM.</summary>
        public string version
        {
            get => ReadProperty(p => p.version);
            internal set => SetProperty("version", value);
        }

        static string ReadProperty(Func<PackageInfo, string> predicate)
        {
            if (GetPackageInfo(out var package))
                return predicate(package);
            else
                return string.Empty;
        }

        static bool GetPackageInfo(out PackageInfo package)
        {
            package = PackageInfo.FindForAssembly(typeof(Package).Assembly);
            return package != null;
        }

        static void SetProperty(string name, string value)
        {

            if (!GetPackageInfo(out var package))
                throw new InvalidOperationException("Could not find package.json");

            var file = package.assetPath + "/package.json";

            var json = File.ReadAllText(file);

            json = json.Replace(PropertyString(package.version), PropertyString(value));
            File.WriteAllText(file, json);
            AssetDatabase.Refresh();

            string PropertyString(string value) =>
                $"\"{name}\": \"{value}\"";

        }

    }

}
#endif
