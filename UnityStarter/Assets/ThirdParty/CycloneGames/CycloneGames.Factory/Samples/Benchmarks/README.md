# CycloneGames.Factory Benchmarks

This directory contains comprehensive benchmark suites for testing the performance characteristics of the CycloneGames.Factory package. The benchmarks are designed to help you understand the performance benefits of using object pooling and factory patterns in both pure C# and Unity environments.

## Overview

The benchmark suite includes:

### Pure C# Benchmarks (`PureCSharp/`)
- **Environment**: Standalone C# application
- **Purpose**: Test core factory and pooling algorithms without Unity overhead
- **Metrics**: CPU performance, memory allocation, GC pressure
- **Use Case**: Server applications, batch processing, pure computation

### Unity Benchmarks (`Unity/`)
- **Environment**: Unity Editor/Runtime
- **Purpose**: Test GameObject pooling, MonoBehaviour lifecycle, Unity-specific features
- **Metrics**: Frame time, Unity memory management, Profiler integration
- **Use Case**: Game development, real-time applications, Unity projects

## Quick Start

### Running Pure C# Benchmarks

1. **Standalone Execution**: The `PureCSharp/Program.cs` can be run independently
   ```bash
   # Compile and run (if extracting to standalone project)
   dotnet run
   ```

2. **Within Unity**: The benchmarks can also be called from Unity scripts
   ```csharp
   var benchmark = new FactoryBenchmark();
   benchmark.RunAllBenchmarks();
   ```

### Running Unity Benchmarks

1. **Add Component**: Add `GameObjectPoolBenchmark` to a GameObject in your scene
2. **Configure**: Set the bullet prefab and spawn parent in the inspector
3. **Run**: Either enable "Run On Start" or call methods manually
4. **Monitor**: View results in Console and Unity Profiler

## Benchmark Categories

### 1. Allocation Performance
- **Direct Allocation**: `new Object()` baseline performance
- **Factory Allocation**: `IFactory<T>.Create()` overhead measurement
- **Comparison**: Direct vs Factory allocation patterns

### 2. Object Pool Performance
- **Spawn/Despawn**: Pool operation timing
- **Memory Efficiency**: Allocation reduction measurement
- **Scaling Behavior**: Performance under varying load

### 3. Concurrency Testing
- **Thread Safety**: Multi-threaded pool access
- **Lock Contention**: Performance under concurrent load
- **Scalability**: Performance with multiple CPU cores

### 4. Unity-Specific Features
- **GameObject Instantiation**: `Instantiate()` vs pooling
- **MonoBehaviour Lifecycle**: Component initialization overhead
- **Memory Profiling**: Unity-specific memory patterns
- **Frame Time Analysis**: Real-time performance impact

## Configuration Options

### Pure C# Benchmarks

```csharp
// Customize iteration counts
const int iterations = 100000;  // Measurement iterations
const int warmupIterations = 1000;  // Warm-up iterations

// Adjust pool settings
var pool = new ObjectPool<Data, Object>(factory, initialCapacity: 100);
```

### Unity Benchmarks

```csharp
[SerializeField] private int measurementIterations = 1000;
[SerializeField] private int maxConcurrentObjects = 5000;
[SerializeField] private float stressTestDuration = 10f;
```

## Interpreting Results

### Performance Metrics

1. **Operations per Second**: Higher is better
2. **Average Time per Operation**: Lower is better (measured in microseconds)
3. **Memory per Operation**: Lower is better (measured in bytes)
4. **GC Collections**: Fewer is better

### Typical Performance Expectations

| Operation | Expected Performance | Notes |
|-----------|---------------------|-------|
| Direct Allocation | 1-10 million ops/sec | Baseline performance |
| Factory Creation | 0.8-8 million ops/sec | Small overhead from abstraction |
| Pool Spawn/Despawn | 2-20 million ops/sec | Significantly faster than allocation |
| GameObject Pool | 50k-200k ops/sec | Unity overhead, but much faster than Instantiate |

### Memory Usage Patterns

- **Direct Allocation**: High GC pressure, frequent collections
- **Object Pooling**: Low GC pressure, stable memory usage
- **Initial Pool Creation**: One-time allocation cost, then stable

## Performance Tips

### For Best Results
1. **Warm-up**: Always include warm-up iterations to account for JIT compilation
2. **GC Control**: Force garbage collection between measurements
3. **Consistent Environment**: Run benchmarks on dedicated hardware when possible
4. **Multiple Runs**: Average results across multiple benchmark runs

### Optimizing Your Code
1. **Pool Sizing**: Start with reasonable initial capacity to avoid early expansions
2. **Lifetime Management**: Use appropriate object lifetimes to balance memory and performance
3. **Batch Operations**: Process multiple objects per frame when possible

## Advanced Usage

### Custom Benchmarks

Extend the benchmark framework for your specific use cases:

```csharp
// Pure C# custom benchmark
public class MyCustomBenchmark
{
    private readonly BenchmarkRunner _runner = new BenchmarkRunner();
    
    public void RunMyBenchmark()
    {
        _runner.RunBenchmark("My Test", 10000, () => {
            // Your test code here
        });
    }
}

// Unity custom benchmark
public class MyUnityBenchmark : MonoBehaviour
{
    private UnityBenchmarkRunner _runner = new UnityBenchmarkRunner();
    
    IEnumerator RunMyBenchmark()
    {
        yield return StartCoroutine(_runner.RunBenchmark(
            "My Unity Test", 1000, () => {
                // Your test code here
            }));
    }
}
```

### Profiler Integration

The Unity benchmarks integrate with Unity's Profiler:

1. **Open Profiler**: Window → Analysis → Profiler
2. **Run Benchmarks**: Execute benchmark scripts
3. **Analyze**: Look for `Benchmark_*` samples in the Profiler timeline
4. **Memory Analysis**: Monitor Memory and GC Alloc tracks

### Data Export

Export benchmark results for analysis:

```csharp
// Get CSV data for external analysis
string csvData = _runner.ExportToCSV();
System.IO.File.WriteAllText("benchmark_results.csv", csvData);
```