using System;
using CycloneGames.Factory.Runtime;
using CycloneGames.Logging;

namespace CycloneGames.Factory.Samples.PureCSharp
{
    // Data to initialize a new particle
    public struct ParticleData
    {
        public System.Numerics.Vector2 StartPosition;
        public System.Numerics.Vector2 Velocity;
        public int LifetimeTicks; // How many "frames" the particle will live
    }

    // The Particle class itself
    public class Particle : IPoolable<ParticleData, Particle>, ITickable, IDisposable
    {
        private static readonly LogChannel Log = FactorySamplesLog.Channel;

        private IDespawnableMemoryPool<Particle> _pool;
        private ParticleData _data;
        private int _ticksRemaining;
        private System.Numerics.Vector2 _currentPosition;

        // OnSpawned configures the particle with its new state
        public void OnSpawned(ParticleData data, IDespawnableMemoryPool<Particle> pool)
        {
            _data = data;
            _pool = pool;

            _currentPosition = _data.StartPosition;
            _ticksRemaining = _data.LifetimeTicks;

            Log.Info($"Particle spawned at {_currentPosition}. It will live for {_ticksRemaining} ticks.");
        }

        // OnDespawned resets the object for reuse
        public void OnDespawned()
        {
            Log.Info($"Particle despawned at {_currentPosition}.");
        }

        // Tick is called each "frame" for active particles
        public void Tick()
        {
            _ticksRemaining--;
            _currentPosition += _data.Velocity;

            if (_ticksRemaining <= 0)
            {
                // Lifetime is over, tell the pool to despawn this instance
                _pool.Despawn(this);
            }
        }

        // Dispose is called when the pool is cleared permanently
        public void Dispose()
        {
            // No unmanaged resources, so we just log a message
            Log.Info("Particle instance permanently destroyed.");
        }
    }
}

