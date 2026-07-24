using System;
using Unity.Collections;
using Unity.Mathematics;

namespace CycloneGames.AIPerception.Runtime
{
    public enum SensorType : byte
    {
        Sight = 0,
        Hearing = 1,
        Proximity = 2,
        Custom = 255
    }

    public interface ISensor : IDisposable
    {
        int SensorId { get; }
        SensorType Type { get; }
        bool IsEnabled { get; set; }
        float UpdateInterval { get; }
        double LastUpdateTime { get; }
        float3 Position { get; }
        SensorUpdateStatus LastUpdateStatus { get; }

        void Initialize();
        void UpdateSensor(float deltaTime);
        
        /// <summary>
        /// Process job results after completion. Called in LateUpdate when using deferred mode.
        /// </summary>
        void ProcessJobResults();
        
        bool HasDetection { get; }
        int DetectedCount { get; }

        bool TryGetResult(int index, out DetectionResult result);
        void GetDetectionResults(ref NativeList<DetectionResult> results);

        // 0GC: Write detected handles to pre-allocated list
        void GetDetectedHandles(ref NativeList<PerceptibleHandle> results);
    }

    internal interface ISensorManagerOwned
    {
        SensorManager Owner { get; }
        bool IsDisposed { get; }
    }
}

