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
                if (args.Length > 0)
                {
                    RunSpecificBenchmark(args[0]);
                }
                else
                {
                    new FactoryBenchmark().RunAllBenchmarks();
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

        private static void RunSpecificBenchmark(string benchmarkName)
        {
            switch (benchmarkName.ToLowerInvariant())
            {
                case "all":
                    new FactoryBenchmark().RunAllBenchmarks();
                    break;
                case "allocation":
                    RunAllocationBenchmarks();
                    break;
                case "pooling":
                    RunPoolingBenchmarks();
                    break;
                case "scaling":
                    RunScalingBenchmarks();
                    break;
                case "dense":
                    RunDenseBenchmarks();
                    break;
                default:
                    Console.WriteLine($"Unknown benchmark: {benchmarkName}");
                    Console.WriteLine("Available benchmarks: all, allocation, pooling, scaling, dense");
                    break;
            }
        }

        private static void RunAllocationBenchmarks()
        {
            Console.WriteLine("=== Allocation Benchmarks ===\n");
            new FactoryBenchmark().RunAllBenchmarks();
        }

        private static void RunPoolingBenchmarks()
        {
            Console.WriteLine("=== Pooling Benchmarks ===\n");
            new FactoryBenchmark().RunAllBenchmarks();
        }

        private static void RunScalingBenchmarks()
        {
            Console.WriteLine("=== Scaling Benchmarks ===\n");
            new FactoryBenchmark().RunAllBenchmarks();
        }

        private static void RunDenseBenchmarks()
        {
            Console.WriteLine("=== Dense Pool Benchmarks ===\n");
#if PRESENT_COLLECTIONS
            new DensePoolBenchmark().RunAllBenchmarks();
#else
            Console.WriteLine("Dense benchmarks are unavailable because Unity.Collections is not present.");
#endif
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
            Console.WriteLine("  dense      - Test high-density handle/dense pool behavior");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  Program                 # Run all benchmarks");
            Console.WriteLine("  Program allocation      # Run only allocation benchmarks");
            Console.WriteLine("  Program --help          # Show this help");
        }
    }
}
