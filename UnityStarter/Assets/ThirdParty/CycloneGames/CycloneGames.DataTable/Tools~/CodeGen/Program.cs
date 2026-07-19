using System;
using System.Linq;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private const string TABLES_SCHEMA_FILE = "__tables__.xlsx";
        private const string FULL_NAME_COLUMN = "full_name";
        private const string INPUT_COLUMN = "input";
        private const string DEFAULT_VALUE_COLUMN = "name";
        private const string DEFAULT_COMMENT_COLUMN = "comment";
        private const string DEFAULT_ENABLED_COLUMN = "enabled";
        private const string DEFAULT_GENERATED_COMMENT_LANGUAGE = "en";
        private const int MAX_CONFIG_FILE_BYTES = 1024 * 1024;
        private const int MAX_CONFIG_LINES = 16384;
        private const int MAX_CONFIG_LINE_CHARACTERS = 16384;
        private const int MAX_CONFIGURED_TABLES = 1024;
        private const int MAX_GENERATED_FILE_CHARACTERS = 16 * 1024 * 1024;
        private const long MAX_TOTAL_GENERATED_CHARACTERS = 64L * 1024 * 1024;
        private const string OWNED_OUTPUT_MANIFEST_FILE = ".cyclonegames-datatable-codegen-manifest.json";
        private const string OWNED_OUTPUT_MANIFEST_SCHEMA = "CycloneGames.DataTable.CodeGen.OwnedOutputs";
        private const int OWNED_OUTPUT_MANIFEST_VERSION = 1;
        private const int MAX_OWNED_OUTPUT_FILES = 8192;
        private const int MAX_OWNED_RELATIVE_PATH_CHARACTERS = 1024;
        private const int MAX_OWNED_OUTPUT_MANIFEST_BYTES = 1024 * 1024;

        private static int Main(string[] args)
        {
            try
            {
                if (args.Any(static argument =>
                        string.Equals(argument, "--self-test", StringComparison.OrdinalIgnoreCase)))
                {
                    if (args.Length != 1)
                    {
                        throw new ArgumentException("--self-test cannot be combined with generation arguments.");
                    }

                    StringConstantGenerator.RunSelfTests();
                    Console.WriteLine("[DataTable.CodeGen] Self-tests passed.");
                    return 0;
                }

                if (args.Any(static argument =>
                        string.Equals(argument, "--help", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(argument, "-h", StringComparison.OrdinalIgnoreCase)))
                {
                    if (args.Length != 1)
                    {
                        throw new ArgumentException("--help cannot be combined with other arguments.");
                    }

                    PrintUsage();
                    return 0;
                }

                ToolArguments arguments = ToolArguments.Parse(args);
                StringConstantGenerator.Run(arguments);
                return 0;
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                Console.Error.WriteLine("[DataTable.CodeGen] " + exception.Message);
                return 1;
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("CycloneGames.DataTable.CodeGen");
            Console.WriteLine("Required: --config <file> --luban-conf <file> --data-dir <dir> --target <name> --code-output <dir>");
            Console.WriteLine("Optional: --line-ending <crlf|lf> --validate-only");
            Console.WriteLine("Focused safety checks: --self-test");
        }

        private static bool IsRecoverableException(Exception exception)
        {
            return exception is not OutOfMemoryException and
                   not AccessViolationException and
                   not AppDomainUnloadedException and
                   not BadImageFormatException and
                   not CannotUnloadAppDomainException and
                   not StackOverflowException;
        }
    }
}
