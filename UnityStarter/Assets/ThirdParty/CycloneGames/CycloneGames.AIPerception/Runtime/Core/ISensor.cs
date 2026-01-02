using Unity.Collections;

namespace CycloneGames.AIPerception.Runtime
{
    public enum SensorType : byte
    {
        Sight = 0,
        Hearing = 1,
        Proximity = 2,
        Custom = 255
    }

    public interface ISensor
    {
        int SensorId { get; }
        SensorType Type { get; }
        bool IsEnabled { get; set; }
        float UpdateInterval { get; }
        float LastUpdateTime { get; }

        void Initialize();
        void UpdateSensor(float deltaTime);
        
        /// <summary>
        /// Process job results after completion. Called in LateUpdate when using deferred mode.
        /// </summary>
        void ProcessJobResults();
        
        void Dispose();

        bool HasDetection { get; }
        int DetectedCount { get; }

        // 0GC: Write detected handles to pre-allocated list
        void GetDetectedHandles(ref NativeList<PerceptibleHandle> results);
    }
}

