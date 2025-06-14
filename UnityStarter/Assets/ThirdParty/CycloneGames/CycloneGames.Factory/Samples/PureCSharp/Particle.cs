using System;
using CycloneGames.Factory.Runtime;

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
    public class Particle : IPoolable<ParticleData, IMemoryPool>, ITickable, IDisposable
    {
        private IMemoryPool _pool;
        private ParticleData _data;
        private int _ticksRemaining;
        private System.Numerics.Vector2 _currentPosition;

        // OnSpawned configures the particle with its new state
        public void OnSpawned(ParticleData data, IMemoryPool pool)
        {
            _data = data;
            _pool = pool;

            _currentPosition = _data.StartPosition;
            _ticksRemaining = _data.LifetimeTicks;

            Console.WriteLine($"---> Particle SPAWNED at {_currentPosition}. Will live for {_ticksRemaining} ticks.");
        }

        // OnDespawned resets the object for reuse
        public void OnDespawned()
        {
            Console.WriteLine($"<--- Particle DESPAWNED at {_currentPosition}.");
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
            Console.WriteLine("Particle instance permanently destroyed.");
        }
    }
}

