using System;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Samples.PureCSharp
{
    public class ParticleSystemSimulator
    {
        private readonly ObjectPool<ParticleData, Particle> _particlePool;
        private int _ticksElapsed = 0;

        public ParticleSystemSimulator()
        {
            // 1. Create the factory for our Particle class
            var particleFactory = new DefaultFactory<Particle>();

            // 2. Create the pool using the factory
            _particlePool = new ObjectPool<ParticleData, Particle>(particleFactory, 10);
            
            Console.WriteLine($"Particle System Initialized. Pool contains {_particlePool.NumInactive} inactive particles.\n");
        }

        // This simulates one frame of the game
        public void Update()
        {
            _ticksElapsed++;
            Console.WriteLine($"\n----- Tick {_ticksElapsed} -----");

            // Every 3 ticks, spawn a new particle
            if (_ticksElapsed % 3 == 0)
            {
                var data = new ParticleData
                {
                    StartPosition = System.Numerics.Vector2.Zero,
                    Velocity = new System.Numerics.Vector2(_ticksElapsed, -_ticksElapsed),
                    LifetimeTicks = 5
                };
                _particlePool.Spawn(data);
            }

            // Update all currently active particles
            _particlePool.Tick();

            Console.WriteLine($"Pool Status - Active: {_particlePool.NumActive}, Inactive: {_particlePool.NumInactive}");
        }

        public void Shutdown()
        {
            Console.WriteLine("\n----- SHUTTING DOWN -----");
            _particlePool.Dispose();
            Console.WriteLine($"Pool disposed. Active: {_particlePool.NumActive}, Inactive: {_particlePool.NumInactive}");
        }
    }
}
