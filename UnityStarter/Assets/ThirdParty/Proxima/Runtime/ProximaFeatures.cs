using System.Collections.Generic;
using System.Reflection;

namespace Proxima
{
    internal class ProximaFeatures
    {
        [ProximaCommand("Internal")]
        public static List<string> GetInstalledFeatures()
        {
            var features = new List<string>();
            var assembly = Assembly.GetAssembly(typeof(ProximaInspector));

            if (assembly.GetType("Proxima.ProximaConsoleCommands") != null)
            {
                features.Add("console");
            }

            if (assembly.GetType("Proxima.ProximaProComponentCommands") != null)
            {
                features.Add("inspectorEdit");
            }

            if (assembly.GetType("Proxima.ProximaProfilerCommands") != null)
            {
                features.Add("profiler");
            }

            return features;
        }

        public static void RegisterProFeatures()
        {
            ProximaInspector.RegisterCommands<ProximaFeatures>();

            var assembly = Assembly.GetAssembly(typeof(ProximaInspector));

            var consoleCommands = assembly.GetType("Proxima.ProximaConsoleCommands");
            if (consoleCommands != null)
            {
                ProximaInspector.RegisterCommands(consoleCommands);
            }

            var proComponentCommands = assembly.GetType("Proxima.ProximaProComponentCommands");
            if (proComponentCommands != null)
            {
                ProximaInspector.RegisterCommands(proComponentCommands);
            }

            var profilerCommands = assembly.GetType("Proxima.ProximaProfilerCommands");
            if (profilerCommands != null)
            {
                ProximaInspector.RegisterCommands(profilerCommands);
            }
        }

        public static bool AllFeaturesInstalled()
        {
            return GetInstalledFeatures().Count == 3;
        }
    }
}