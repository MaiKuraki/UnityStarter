using System;

namespace CycloneGames.Factory.Samples.Benchmarks.PureCSharp
{
    /// <summary>
    /// Entry point for running CycloneGames.Factory benchmarks in a pure C# environment.
    /// This program can be run independently of Unity to test factory and pooling performance.
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("CycloneGames.Factory Benchmark Suite");
            Console.WriteLine("=====================================");
            Console.WriteLine();

            if (args.Length > 0 && args[0] == "--help")
            {
                PrintHelp();
                return;
            }

            try
            {
                var benchmark = new FactoryBenchmark();
                
                if (args.Length > 0)
                {
                    RunSpecificBenchmark(benchmark, args[0]);
                }
                else
                {
                    benchmark.RunAllBenchmarks();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error running benchmarks: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }

        private static void RunSpecificBenchmark(FactoryBenchmark benchmark, string benchmarkName)
        {
            switch (benchmarkName.ToLowerInvariant())
            {
                case "all":
                    benchmark.RunAllBenchmarks();
                    break;
                case "allocation":
                    RunAllocationBenchmarks(benchmark);
                    break;
                case "pooling":
                    RunPoolingBenchmarks(benchmark);
                    break;
                case "scaling":
                    RunScalingBenchmarks(benchmark);
                    break;
                default:
                    Console.WriteLine($"Unknown benchmark: {benchmarkName}");
                    Console.WriteLine("Available benchmarks: all, allocation, pooling, scaling");
                    break;
            }
        }

        private static void RunAllocationBenchmarks(FactoryBenchmark benchmark)
        {
            Console.WriteLine("=== Allocation Benchmarks ===\n");
            // These would call specific methods if FactoryBenchmark was refactored to expose them
            benchmark.RunAllBenchmarks(); // For now, run all
        }

        private static void RunPoolingBenchmarks(FactoryBenchmark benchmark)
        {
            Console.WriteLine("=== Pooling Benchmarks ===\n");
            benchmark.RunAllBenchmarks(); // For now, run all
        }

        private static void RunScalingBenchmarks(FactoryBenchmark benchmark)
        {
            Console.WriteLine("=== Scaling Benchmarks ===\n");
            benchmark.RunAllBenchmarks(); // For now, run all
        }

        private static void PrintHelp()
        {
            Console.WriteLine("Usage: Program [benchmark_type]");
            Console.WriteLine();
            Console.WriteLine("Benchmark types:");
            Console.WriteLine("  all        - Run all benchmarks (default)");
            Console.WriteLine("  allocation - Test direct vs factory allocation performance");
            Console.WriteLine("  pooling    - Test object pool spawn/despawn performance");
            Console.WriteLine("  scaling    - Test auto-scaling behavior of object pools");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  Program                 # Run all benchmarks");
            Console.WriteLine("  Program allocation      # Run only allocation benchmarks");
            Console.WriteLine("  Program --help          # Show this help");
        }
    }
}
