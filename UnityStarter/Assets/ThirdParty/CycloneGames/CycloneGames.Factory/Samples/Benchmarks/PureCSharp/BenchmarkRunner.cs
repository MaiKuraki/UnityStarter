using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CycloneGames.Factory.Samples.Benchmarks.PureCSharp
{
    /// <summary>
    /// Utility class for running and measuring performance benchmarks.
    /// Provides timing, memory usage tracking, statistical analysis, and detailed reporting.
    /// </summary>
    public class BenchmarkRunner
    {
        private readonly List<BenchmarkResult> _results = new List<BenchmarkResult>();
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private readonly string _sessionStartTime;

        public BenchmarkRunner()
        {
            _sessionStartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// Runs a benchmark with warm-up, measurement, and statistical analysis.
        /// </summary>
        /// <param name="name">Name of the benchmark</param>
        /// <param name="iterations">Number of iterations to run</param>
        /// <param name="action">The action to benchmark</param>
        /// <param name="warmupIterations">Number of warm-up iterations (default: 10% of total)</param>
        public void RunBenchmark(string name, int iterations, Action action, int warmupIterations = -1)
        {
            if (warmupIterations < 0)
                warmupIterations = Math.Max(1, iterations / 10);

            Console.WriteLine($"Running benchmark: {name}");
            Console.WriteLine($"  Warm-up: {warmupIterations} iterations");
            Console.WriteLine($"  Measurement: {iterations} iterations");

            // Warm-up phase
            for (int i = 0; i < warmupIterations; i++)
            {
                action();
            }

            // Force garbage collection before measurement
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var initialMemory = GC.GetTotalMemory(false);

            // Measurement phase
            _stopwatch.Restart();
            
            for (int i = 0; i < iterations; i++)
            {
                action();
            }
            
            _stopwatch.Stop();
            
            var finalMemory = GC.GetTotalMemory(false);
            var memoryUsed = finalMemory - initialMemory;

            var result = new BenchmarkResult
            {
                Name = name,
                Iterations = iterations,
                TotalTime = _stopwatch.Elapsed,
                AverageTime = TimeSpan.FromTicks(_stopwatch.Elapsed.Ticks / iterations),
                MemoryUsed = memoryUsed,
                MemoryPerIteration = memoryUsed / iterations
            };

            _results.Add(result);

            Console.WriteLine($"  Total time: {result.TotalTime.TotalMilliseconds:F2} ms");
            Console.WriteLine($"  Average per iteration: {result.AverageTime.TotalMilliseconds * 1000:F2} μs");
            Console.WriteLine($"  Operations per second: {iterations / result.TotalTime.TotalSeconds:F0}");
            Console.WriteLine($"  Memory used: {FormatBytes(result.MemoryUsed)}");
            Console.WriteLine($"  Memory per iteration: {FormatBytes(result.MemoryPerIteration)}");
            Console.WriteLine();
        }

        /// <summary>
        /// Runs a benchmark with multiple trials for statistical analysis
        /// </summary>
        public void RunBenchmarkWithTrials(string name, int iterations, Action action, int trials = 5)
        {
            Console.WriteLine($"Running benchmark with trials: {name}");
            Console.WriteLine($"  Trials: {trials}");
            Console.WriteLine($"  Iterations per trial: {iterations}");

            var trialResults = new List<BenchmarkResult>();

            for (int trial = 0; trial < trials; trial++)
            {
                Console.WriteLine($"  Trial {trial + 1}/{trials}...");
                
                // Run single trial
                RunSingleTrial(name, iterations, action, out var result);
                trialResults.Add(result);
            }

            // Calculate statistics
            var avgTotalTime = TimeSpan.FromTicks((long)trialResults.Average(r => r.TotalTime.Ticks));
            var avgIterationTime = TimeSpan.FromTicks((long)trialResults.Average(r => r.AverageTime.Ticks));
            var avgMemoryUsed = (long)trialResults.Average(r => r.MemoryUsed);
            var avgMemoryPerIteration = (long)trialResults.Average(r => r.MemoryPerIteration);

            var stdDevTotalTime = CalculateStandardDeviation(trialResults.Select(r => r.TotalTime.TotalMilliseconds));
            var stdDevIterationTime = CalculateStandardDeviation(trialResults.Select(r => r.AverageTime.TotalMilliseconds * 1000));

            var finalResult = new BenchmarkResult
            {
                Name = $"{name} (Avg of {trials} trials)",
                Iterations = iterations,
                TotalTime = avgTotalTime,
                AverageTime = avgIterationTime,
                MemoryUsed = avgMemoryUsed,
                MemoryPerIteration = avgMemoryPerIteration,
                StandardDeviationMs = stdDevTotalTime,
                StandardDeviationUs = stdDevIterationTime
            };

            _results.Add(finalResult);

            Console.WriteLine($"  === Statistical Summary ===");
            Console.WriteLine($"  Average total time: {avgTotalTime.TotalMilliseconds:F2} ms (±{stdDevTotalTime:F2})");
            Console.WriteLine($"  Average per iteration: {avgIterationTime.TotalMilliseconds * 1000:F2} μs (±{stdDevIterationTime:F2})");
            Console.WriteLine($"  Average operations per second: {iterations / avgTotalTime.TotalSeconds:F0}");
            Console.WriteLine($"  Average memory used: {FormatBytes(avgMemoryUsed)}");
            Console.WriteLine($"  Average memory per iteration: {FormatBytes(avgMemoryPerIteration)}");
            Console.WriteLine();
        }

        private void RunSingleTrial(string name, int iterations, Action action, out BenchmarkResult result)
        {
            // Warm-up
            for (int i = 0; i < Math.Max(1, iterations / 20); i++)
            {
                action();
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            var initialMemory = GC.GetTotalMemory(false);

            _stopwatch.Restart();
            for (int i = 0; i < iterations; i++)
            {
                action();
            }
            _stopwatch.Stop();

            var finalMemory = GC.GetTotalMemory(false);
            var memoryUsed = finalMemory - initialMemory;

            result = new BenchmarkResult
            {
                Name = name,
                Iterations = iterations,
                TotalTime = _stopwatch.Elapsed,
                AverageTime = TimeSpan.FromTicks(_stopwatch.Elapsed.Ticks / iterations),
                MemoryUsed = memoryUsed,
                MemoryPerIteration = memoryUsed / iterations
            };
        }

        /// <summary>
        /// Prints a summary of all benchmark results
        /// </summary>
        public void PrintSummary()
        {
            if (_results.Count == 0)
            {
                Console.WriteLine("No benchmark results to display.");
                return;
            }

            Console.WriteLine("=== BENCHMARK SUMMARY ===");
            Console.WriteLine();

            // Sort by average time (fastest first)
            var sortedResults = _results.OrderBy(r => r.AverageTime.Ticks).ToList();

            Console.WriteLine($"{"Benchmark",-40} {"Avg Time (μs)",-15} {"Ops/sec",-12} {"Memory/Op",-12}");
            Console.WriteLine(new string('-', 85));

            foreach (var result in sortedResults)
            {
                var opsPerSec = result.Iterations / result.TotalTime.TotalSeconds;
                Console.WriteLine($"{result.Name,-40} {result.AverageTime.TotalMilliseconds * 1000,-15:F2} {opsPerSec,-12:F0} {FormatBytes(result.MemoryPerIteration),-12}");
            }

            Console.WriteLine();

            // Performance ranking
            Console.WriteLine("=== PERFORMANCE RANKING ===");
            var baseline = sortedResults.First();
            for (int i = 0; i < sortedResults.Count; i++)
            {
                var result = sortedResults[i];
                var relativePerformance = (result.AverageTime.TotalMilliseconds * 1000) / (baseline.AverageTime.TotalMilliseconds * 1000);
                var status = i == 0 ? "FASTEST" : $"{relativePerformance:F2}x slower";
                Console.WriteLine($"{i + 1}. {result.Name} - {status}");
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Generates and saves a comprehensive benchmark report
        /// </summary>
        public void GenerateReport(string customName = "")
        {
            if (_results.Count == 0)
            {
                Console.WriteLine("No benchmark results to generate report for.");
                return;
            }

            var report = GenerateFormattedReport(customName);
            var markdown = GenerateMarkdownReport(customName);
            var markdownZh = GenerateMarkdownReportZhCN(customName);
            
            // Save to file
            SaveReportToFile(report, customName);
            SaveMarkdownReportToFile(markdown, customName);
            SaveMarkdownSchReportToFile(markdownZh, customName);
            
            // Output summary to console
            Console.WriteLine();
            Console.WriteLine("📄 DETAILED REPORT GENERATED");
            Console.WriteLine($"Report saved to: {GetReportFilePath(customName)}");
            Console.WriteLine($"Markdown saved to: {GetMarkdownReportFilePath(customName)}");
            Console.WriteLine($"Markdown (SCH) saved to: {GetMarkdownSchReportFilePath(customName)}");
            Console.WriteLine();
        }

        /// <summary>
        /// Generates a detailed formatted report
        /// </summary>
        private string GenerateFormattedReport(string customName)
        {
            var sb = new StringBuilder();
            
            // Header
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("                   PURE C# FACTORY BENCHMARK REPORT");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine($"Session Start: {_sessionStartTime}");
            sb.AppendLine($"Report Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($".NET Version: {Environment.Version}");
            sb.AppendLine($"Platform: {Environment.OSVersion}");
            sb.AppendLine($"Processor Count: {Environment.ProcessorCount}");
            sb.AppendLine($"Total Benchmarks: {_results.Count}");
            if (!string.IsNullOrEmpty(customName))
            {
                sb.AppendLine($"Custom Label: {customName}");
            }
            sb.AppendLine();

            // Performance Summary
            GeneratePerformanceSummary(sb);
            
            // Memory Analysis
            GenerateMemoryAnalysis(sb);
            
            // Detailed Results
            GenerateDetailedResults(sb);
            
            // GC Analysis
            GenerateGCAnalysis(sb);
            
            // Statistical Analysis
            GenerateStatisticalAnalysis(sb);
            
            // Recommendations
            GenerateRecommendations(sb);

            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("                         END OF REPORT");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            return sb.ToString();
        }

        /// <summary>
        /// Saves the report to a file
        /// </summary>
        private void SaveReportToFile(string report, string customName)
        {
            try
            {
                var filePath = GetReportFilePath(customName);
                var directory = Path.GetDirectoryName(filePath);
                
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                
                File.WriteAllText(filePath, report, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save benchmark report: {ex.Message}");
            }
        }

        private void SaveMarkdownReportToFile(string report, string customName)
        {
            try
            {
                var filePath = GetMarkdownReportFilePath(customName);
                var directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(filePath, report, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save markdown report: {ex.Message}");
            }
        }

        private void SaveMarkdownSchReportToFile(string report, string customName)
        {
            try
            {
                var filePath = GetMarkdownSchReportFilePath(customName);
                var directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(filePath, report, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to save SCH markdown report: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the file path for the benchmark report
        /// </summary>
        private string GetReportFilePath(string customName)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = string.IsNullOrEmpty(customName) 
                ? $"PureCSharpFactoryBenchmark_{timestamp}.txt"
                : $"PureCSharpFactoryBenchmark_{customName}_{timestamp}.txt";
            
            // Save to BenchmarkReports folder relative to current directory
            var currentDir = Directory.GetCurrentDirectory();
            return Path.Combine(currentDir, "BenchmarkReports", filename);
        }

        private string GetMarkdownReportFilePath(string customName)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = string.IsNullOrEmpty(customName)
                ? $"PureCSharpFactoryBenchmark_{timestamp}.md"
                : $"PureCSharpFactoryBenchmark_{customName}_{timestamp}.md";

            var currentDir = Directory.GetCurrentDirectory();
            return Path.Combine(currentDir, "BenchmarkReports", filename);
        }

        private string GetMarkdownSchReportFilePath(string customName)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = string.IsNullOrEmpty(customName)
                ? $"PureCSharpFactoryBenchmark_{timestamp}.SCH.md"
                : $"PureCSharpFactoryBenchmark_{customName}_{timestamp}.SCH.md";

            var currentDir = Directory.GetCurrentDirectory();
            return Path.Combine(currentDir, "BenchmarkReports", filename);
        }

        private void GeneratePerformanceSummary(StringBuilder sb)
        {
            sb.AppendLine("─── PERFORMANCE SUMMARY ───");
            
            var sortedResults = _results.OrderBy(r => r.AverageTime.Ticks).ToList();
            if (!sortedResults.Any())
            {
                sb.AppendLine("No performance data available.");
                sb.AppendLine();
                return;
            }

            sb.AppendLine($"{"Benchmark",-45} {"Avg Time (μs)",-15} {"Ops/Sec",-12} {"Relative",-10}");
            sb.AppendLine(new string('─', 85));

            var fastest = sortedResults.First();
            foreach (var result in sortedResults)
            {
                var opsPerSec = result.Iterations / result.TotalTime.TotalSeconds;
                var avgTimeUs = result.AverageTime.TotalMilliseconds * 1000;
                var relative = result.AverageTime.TotalMilliseconds / fastest.AverageTime.TotalMilliseconds;
                var relativeStr = relative == 1.0 ? "FASTEST" : $"{relative:F1}x";
                
                sb.AppendLine($"{TruncateString(result.Name, 44),-45} {avgTimeUs,-15:F2} {opsPerSec,-12:F0} {relativeStr,-10}");
            }
            sb.AppendLine();
        }

        private void GenerateMemoryAnalysis(StringBuilder sb)
        {
            sb.AppendLine("─── MEMORY ANALYSIS ───");
            
            var memoryResults = _results.Where(r => r.MemoryUsed != 0).ToList();
            if (!memoryResults.Any())
            {
                sb.AppendLine("No memory allocation data available.");
                sb.AppendLine();
                return;
            }

            sb.AppendLine($"{"Benchmark",-45} {"Total Alloc",-12} {"Per Op",-12} {"GC Count",-10}");
            sb.AppendLine(new string('─', 85));

            foreach (var result in memoryResults.OrderBy(r => r.MemoryPerIteration))
            {
                // Note: For pure C# we don't have actual GC count tracking, so we'll estimate
                var estimatedGC = result.MemoryUsed > 1024 * 1024 ? "High" : result.MemoryUsed > 1024 ? "Medium" : "Low";
                sb.AppendLine($"{TruncateString(result.Name, 44),-45} {FormatBytes(result.MemoryUsed),-12} {FormatBytes(result.MemoryPerIteration),-12} {estimatedGC,-10}");
            }
            sb.AppendLine();
        }

        private void GenerateDetailedResults(StringBuilder sb)
        {
            sb.AppendLine("─── DETAILED RESULTS ───");
            
            foreach (var result in _results)
            {
                sb.AppendLine($"📊 {result.Name}");
                sb.AppendLine($"   Iterations: {result.Iterations:N0}");
                sb.AppendLine($"   Total Time: {result.TotalTime.TotalMilliseconds:F3} ms");
                sb.AppendLine($"   Average Time: {result.AverageTime.TotalMilliseconds * 1000:F3} μs/operation");
                sb.AppendLine($"   Operations/Sec: {result.Iterations / result.TotalTime.TotalSeconds:F0}");
                
                if (result.MemoryUsed != 0)
                {
                    sb.AppendLine($"   Memory Used: {FormatBytes(result.MemoryUsed)}");
                    sb.AppendLine($"   Memory/Operation: {FormatBytes(result.MemoryPerIteration)}");
                }
                
                if (result.StandardDeviationMs > 0)
                {
                    sb.AppendLine($"   Std Deviation: ±{result.StandardDeviationMs:F3} ms");
                }
                
                sb.AppendLine();
            }
        }

        private void GenerateGCAnalysis(StringBuilder sb)
        {
            sb.AppendLine("─── GARBAGE COLLECTION ANALYSIS ───");
            sb.AppendLine("Note: GC analysis for pure C# is estimated based on memory allocation patterns.");
            sb.AppendLine();
            
            var highMemoryResults = _results.Where(r => r.MemoryPerIteration > 1024).ToList();
            if (highMemoryResults.Any())
            {
                sb.AppendLine("High Memory Allocation Operations (likely to trigger GC):");
                foreach (var result in highMemoryResults.OrderByDescending(r => r.MemoryPerIteration))
                {
                    var allocationRate = result.MemoryPerIteration * (result.Iterations / result.TotalTime.TotalSeconds);
                    sb.AppendLine($"  • {result.Name}: {FormatBytes(result.MemoryPerIteration)}/op ({FormatBytes((long)allocationRate)}/sec)");
                }
            }
            else
            {
                sb.AppendLine("✅ All operations have low memory allocation profiles.");
            }
            sb.AppendLine();
        }

        private void GenerateStatisticalAnalysis(StringBuilder sb)
        {
            if (!_results.Any(r => r.StandardDeviationMs > 0)) return;

            sb.AppendLine("─── STATISTICAL ANALYSIS ───");
            
            var resultsWithStats = _results.Where(r => r.StandardDeviationMs > 0).ToList();
            foreach (var result in resultsWithStats)
            {
                var cv = (result.StandardDeviationMs / result.AverageTime.TotalMilliseconds) * 100; // Coefficient of variation
                var consistency = cv < 5 ? "Excellent" : cv < 10 ? "Good" : cv < 20 ? "Fair" : "Poor";
                
                sb.AppendLine($"{result.Name}:");
                sb.AppendLine($"  Coefficient of Variation: {cv:F1}% ({consistency})");
                sb.AppendLine($"  Standard Deviation: ±{result.StandardDeviationMs:F3} ms");
                sb.AppendLine();
            }
        }

        private void GenerateRecommendations(StringBuilder sb)
        {
            sb.AppendLine("─── PERFORMANCE RECOMMENDATIONS ───");
            
            var highMemoryResults = _results.Where(r => r.MemoryPerIteration > 1024).ToList();
            var slowResults = _results.Where(r => r.AverageTime.TotalMilliseconds > 1.0).ToList();
            
            if (highMemoryResults.Any())
            {
                sb.AppendLine("🔴 HIGH MEMORY ALLOCATION DETECTED:");
                foreach (var result in highMemoryResults)
                {
                    sb.AppendLine($"  • {result.Name}: {FormatBytes(result.MemoryPerIteration)}/op - Consider object pooling");
                }
                sb.AppendLine();
            }
            
            if (slowResults.Any())
            {
                sb.AppendLine("🟡 SLOW OPERATIONS:");
                foreach (var result in slowResults)
                {
                    sb.AppendLine($"  • {result.Name}: {result.AverageTime.TotalMilliseconds:F3}ms/op - Optimize algorithm or use pooling");
                }
                sb.AppendLine();
            }
            
            // Find pooling effectiveness
            var directAllocation = _results.FirstOrDefault(r => r.Name.ToLower().Contains("direct"));
            var poolAllocation = _results.FirstOrDefault(r => r.Name.ToLower().Contains("pool"));
            
            if (directAllocation != null && poolAllocation != null)
            {
                var speedup = directAllocation.AverageTime.TotalMilliseconds / poolAllocation.AverageTime.TotalMilliseconds;
                var memoryReduction = (double)(directAllocation.MemoryPerIteration - poolAllocation.MemoryPerIteration) / directAllocation.MemoryPerIteration * 100;
                
                sb.AppendLine($"✅ POOLING EFFECTIVENESS:");
                sb.AppendLine($"  • Performance: Object pooling is {speedup:F1}x faster");
                if (memoryReduction > 0)
                {
                    sb.AppendLine($"  • Memory: {memoryReduction:F1}% reduction in allocations");
                }
                sb.AppendLine();
            }
            
            sb.AppendLine("💡 GENERAL RECOMMENDATIONS:");
            sb.AppendLine("  • Use object pooling for frequently allocated objects");
            sb.AppendLine("  • Monitor memory allocation patterns in production");
            sb.AppendLine("  • Consider struct types for small, value-based objects");
            sb.AppendLine("  • Batch operations when possible to reduce per-operation overhead");
            sb.AppendLine("  • Profile with release builds for accurate performance data");
            sb.AppendLine();
        }

        private static string TruncateString(string str, int maxLength)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return str.Length <= maxLength ? str : str.Substring(0, maxLength - 3) + "...";
        }

        /// <summary>
        /// Clears all benchmark results
        /// </summary>
        public void ClearResults()
        {
            _results.Clear();
        }

        private static double CalculateStandardDeviation(IEnumerable<double> values)
        {
            var enumerable = values.ToList();
            var avg = enumerable.Average();
            var sumOfSquares = enumerable.Sum(val => (val - avg) * (val - avg));
            return Math.Sqrt(sumOfSquares / enumerable.Count);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
        }

		private static int ParseBurstFromName(string name)
		{
			if (string.IsNullOrEmpty(name)) return 1;
			int start = name.IndexOf("(Burst ", StringComparison.OrdinalIgnoreCase);
			if (start < 0) return 1;
			int end = name.IndexOf(')', start);
			if (end < 0) return 1;
			var inside = name.Substring(start + 7, end - (start + 7)).Trim();
			if (int.TryParse(inside, out int burst) && burst > 0) return burst;
			return 1;
		}

		private static bool IsPooling(string name)
		{
			if (string.IsNullOrEmpty(name)) return false;
			var lower = name.ToLowerInvariant();
			return lower.Contains("pool");
		}

		private static bool IsDirectOrFactory(string name)
		{
			if (string.IsNullOrEmpty(name)) return false;
			var lower = name.ToLowerInvariant();
			return lower.Contains("direct") || lower.Contains("factory") || lower.Contains("instantiat");
		}

        private string GenerateMarkdownReport(string customName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Pure C# Factory Benchmark Report");
            sb.AppendLine();
            sb.AppendLine($"- **Session Start**: {_sessionStartTime}");
            sb.AppendLine($"- **Generated**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- **.NET**: {Environment.Version}");
            sb.AppendLine($"- **Platform**: {Environment.OSVersion}");
            sb.AppendLine($"- **Total Benchmarks**: {_results.Count}");
            if (!string.IsNullOrEmpty(customName)) sb.AppendLine($"- **Label**: {customName}");
            sb.AppendLine();
            sb.AppendLine("> Legend: Avg Time (lower is better), Per-Item = AvgTime/Burst, Ops/Sec (higher is better), Memory/Op (lower is better)");
            sb.AppendLine();

            var perf = _results
                .Where(r => r.TotalTime.TotalMilliseconds > 0 && r.Iterations > 0)
                .Select(r => new { r.Name, r.Iterations, AvgMs = r.AverageTime.TotalMilliseconds, Ops = r.Iterations / r.TotalTime.TotalSeconds })
                .OrderBy(x => x.AvgMs)
                .ToList();
            if (perf.Any())
            {
                var fastest = perf.First().AvgMs;
                sb.AppendLine("### Performance (Aggregated)");
                sb.AppendLine("| Benchmark | Avg Time (ms) | Per-Item (µs) | Ops/Sec | Relative |");
                sb.AppendLine("|---|---:|---:|---:|---:|");
                foreach (var p in perf)
                {
                    int burst = ParseBurstFromName(p.Name);
                    double perUs = (p.AvgMs * 1000.0) / Math.Max(1, burst);
                    string rel = Math.Abs(p.AvgMs - fastest) < 1e-9 ? "FASTEST" : $"{p.AvgMs / fastest:F1}x";
                    sb.AppendLine($"| {p.Name} | {p.AvgMs:F3} | {perUs:F2} | {p.Ops:F0} | {rel} |");
                }
                sb.AppendLine();
            }

            var mem = _results.Where(r => r.MemoryUsed != 0)
                .Select(r => new { r.Name, r.MemoryUsed, r.MemoryPerIteration })
                .OrderBy(x => x.MemoryPerIteration)
                .ToList();
            if (mem.Any())
            {
                sb.AppendLine("### Memory (Aggregated)");
                sb.AppendLine("| Benchmark | Total Alloc | Memory/Op |");
                sb.AppendLine("|---|---:|---:|");
                foreach (var m in mem)
                {
                    sb.AppendLine($"| {m.Name} | {FormatBytes(m.MemoryUsed)} | {FormatBytes(m.MemoryPerIteration)} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("### Detailed Results");
            sb.AppendLine("| Name | Iterations | Total Time (s) | Avg Time (ms) | Per-Item (µs) | Ops/Sec | Total Memory | Memory/Op |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var r in _results)
            {
                int burst = ParseBurstFromName(r.Name);
                double perUs = (r.AverageTime.TotalMilliseconds * 1000.0) / Math.Max(1, burst);
                double ops = r.TotalTime.TotalSeconds > 0 ? (r.Iterations / r.TotalTime.TotalSeconds) : 0.0;
                sb.AppendLine($"| {r.Name} | {r.Iterations} | {r.TotalTime.TotalSeconds:F3} | {r.AverageTime.TotalMilliseconds:F3} | {perUs:F2} | {ops:F0} | {FormatBytes(r.MemoryUsed)} | {FormatBytes(r.MemoryPerIteration)} |");
            }

            return sb.ToString();
        }

        private string GenerateMarkdownReportZhCN(string customName)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## 纯 C# 工厂基准测试报告");
            sb.AppendLine();
            sb.AppendLine($"- **会话开始**: {_sessionStartTime}");
            sb.AppendLine($"- **生成时间**: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"- **.NET**: {Environment.Version}");
            sb.AppendLine($"- **平台**: {Environment.OSVersion}");
            sb.AppendLine($"- **基准条目数**: {_results.Count}");
            if (!string.IsNullOrEmpty(customName)) sb.AppendLine($"- **标签**: {customName}");
            sb.AppendLine();
            sb.AppendLine("> 说明：Avg Time（越小越好）、单个对象 (µs)=AvgTime/Burst、Ops/Sec（越大越好）、Memory/Op（越小越好）");
            sb.AppendLine();

            var perf = _results
                .Where(r => r.TotalTime.TotalMilliseconds > 0 && r.Iterations > 0)
                .Select(r => new { r.Name, r.Iterations, AvgMs = r.AverageTime.TotalMilliseconds, Ops = r.Iterations / r.TotalTime.TotalSeconds })
                .OrderBy(x => x.AvgMs)
                .ToList();
            if (perf.Any())
            {
                var fastest = perf.First().AvgMs;
                sb.AppendLine("### 性能（聚合）");
                sb.AppendLine("| 基准 | 平均时间 (ms) | 单个对象 (µs) | 次/秒 | 相对值 |");
                sb.AppendLine("|---|---:|---:|---:|---:|");
                foreach (var p in perf)
                {
                    int burst = ParseBurstFromName(p.Name);
                    double perUs = (p.AvgMs * 1000.0) / Math.Max(1, burst);
                    string rel = Math.Abs(p.AvgMs - fastest) < 1e-9 ? "最快" : $"{p.AvgMs / fastest:F1}x";
                    sb.AppendLine($"| {p.Name} | {p.AvgMs:F3} | {perUs:F2} | {p.Ops:F0} | {rel} |");
                }
                sb.AppendLine();
            }

            var mem = _results.Where(r => r.MemoryUsed != 0)
                .Select(r => new { r.Name, r.MemoryUsed, r.MemoryPerIteration })
                .OrderBy(x => x.MemoryPerIteration)
                .ToList();
            if (mem.Any())
            {
                sb.AppendLine("### 内存（聚合）");
                sb.AppendLine("| 基准 | 总分配 | 每次操作内存 |");
                sb.AppendLine("|---|---:|---:|");
                foreach (var m in mem)
                {
                    sb.AppendLine($"| {m.Name} | {FormatBytes(m.MemoryUsed)} | {FormatBytes(m.MemoryPerIteration)} |");
                }
                sb.AppendLine();
            }

            sb.AppendLine("### 运行明细");
            sb.AppendLine("| 名称 | 次数 | 总时间 (s) | 平均时间 (ms) | 单个对象 (µs) | 次/秒 | 总内存 | 每次操作内存 |");
            sb.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|");
            foreach (var r in _results)
            {
                int burst = ParseBurstFromName(r.Name);
                double perUs = (r.AverageTime.TotalMilliseconds * 1000.0) / Math.Max(1, burst);
                double ops = r.TotalTime.TotalSeconds > 0 ? (r.Iterations / r.TotalTime.TotalSeconds) : 0.0;
                sb.AppendLine($"| {r.Name} | {r.Iterations} | {r.TotalTime.TotalSeconds:F3} | {r.AverageTime.TotalMilliseconds:F3} | {perUs:F2} | {ops:F0} | {FormatBytes(r.MemoryUsed)} | {FormatBytes(r.MemoryPerIteration)} |");
            }

            return sb.ToString();
        }
    }

    /// <summary>
    /// Contains the results of a single benchmark run
    /// </summary>
    public class BenchmarkResult
    {
        public string Name { get; set; }
        public int Iterations { get; set; }
        public TimeSpan TotalTime { get; set; }
        public TimeSpan AverageTime { get; set; }
        public long MemoryUsed { get; set; }
        public long MemoryPerIteration { get; set; }
        public double StandardDeviationMs { get; set; }
        public double StandardDeviationUs { get; set; }
    }
}
