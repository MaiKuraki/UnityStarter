namespace CycloneGames.Analyzers.Verifier
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            VerifierOptions options;
            try
            {
                options = VerifierOptions.Parse(args);
            }
            catch (ArgumentException exception)
            {
                Console.Error.WriteLine("Usage error: " + exception.Message);
                Console.Error.WriteLine();
                Console.Error.WriteLine(VerifierOptions.Usage);
                return 2;
            }

            if (options.ShowHelp)
            {
                Console.WriteLine(VerifierOptions.Usage);
                return 0;
            }

            VerifierReport report = new ActivationVerifier(options).Run();
            report.Print(Console.Out);
            return report.Succeeded ? 0 : 1;
        }
    }

    internal sealed class VerifierOptions
    {
        internal const int DefaultTimeoutSeconds = 600;

        internal static string Usage =>
            "Usage: dotnet run --project CycloneGames.Analyzers.Verifier.csproj -- [options]" + Environment.NewLine +
            "Options:" + Environment.NewLine +
            "  --unity-editor-path <path>   Required. Path to the Unity Editor executable." + Environment.NewLine +
            "  --unity-project-root <path>  Override the Unity project root discovered from" + Environment.NewLine +
            "                               ProjectSettings/ProjectVersion.txt." + Environment.NewLine +
            "  --timeout-seconds <n>        End-to-end deadline in seconds (1..3600, default 600)." + Environment.NewLine +
            "  --skip-build                 Skip the Release build of the Unity-compatible analyzer." + Environment.NewLine +
            "  --keep-temporary-project     Keep the temporary Unity project for diagnosis." + Environment.NewLine +
            "  --help                       Show this usage.";

        internal string? UnityEditorPath { get; private set; }
        internal string? UnityProjectRoot { get; private set; }
        internal int TimeoutSeconds { get; private set; } = DefaultTimeoutSeconds;
        internal bool SkipBuild { get; private set; }
        internal bool KeepTemporaryProject { get; private set; }
        internal bool ShowHelp { get; private set; }

        internal static VerifierOptions Parse(string[] args)
        {
            var options = new VerifierOptions();
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--unity-editor-path":
                        options.UnityEditorPath = RequireValue(args, ref i, "--unity-editor-path");
                        break;
                    case "--unity-project-root":
                        options.UnityProjectRoot = RequireValue(args, ref i, "--unity-project-root");
                        break;
                    case "--timeout-seconds":
                        string value = RequireValue(args, ref i, "--timeout-seconds");
                        if (!int.TryParse(value, out int seconds) || seconds < 1 || seconds > 3600)
                        {
                            throw new ArgumentException("--timeout-seconds must be an integer between 1 and 3600.");
                        }
                        options.TimeoutSeconds = seconds;
                        break;
                    case "--skip-build":
                        options.SkipBuild = true;
                        break;
                    case "--keep-temporary-project":
                        options.KeepTemporaryProject = true;
                        break;
                    case "--help":
                        options.ShowHelp = true;
                        break;
                    default:
                        throw new ArgumentException("Unknown option: " + args[i]);
                }
            }

            if (!options.ShowHelp && string.IsNullOrWhiteSpace(options.UnityEditorPath))
            {
                throw new ArgumentException("--unity-editor-path is required.");
            }

            return options;
        }

        private static string RequireValue(string[] args, ref int index, string optionName)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException(optionName + " requires a value.");
            }
            index++;
            return args[index];
        }
    }
}
