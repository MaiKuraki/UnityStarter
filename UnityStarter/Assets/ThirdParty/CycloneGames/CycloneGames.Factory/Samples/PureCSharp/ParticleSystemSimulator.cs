using System;
using CycloneGames.Factory.Runtime;
using CycloneGames.Logging;

namespace CycloneGames.Factory.Samples.PureCSharp
{
    public class ParticleSystemSimulator
    {
        private static readonly LogChannel Log = FactorySamplesLog.Channel;

        private readonly ObjectPool<ParticleData, Particle> _particlePool;
        private int _ticksElapsed = 0;

        // Cached delegate: ForEachActive receives a reused Action instead of allocating one per call.
        private static readonly Action<Particle> TickParticle = static particle => particle.Tick();

        public ParticleSystemSimulator()
        {
            // 1. Create the factory for our Particle class
            var particleFactory = new DefaultFactory<Particle>();

            // 2. Create the pool using the factory
            _particlePool = new ObjectPool<ParticleData, Particle>(particleFactory, 10);
            
            Log.Info($"Particle System Initialized. Pool contains {_particlePool.CountInactive} inactive particles.");
        }

        // Simulates one frame of the game. This is a console-hosted demonstration driven at a
        // readable pace (see Program.cs), not a Unity per-frame hot path.
        public void SimulateFrame()
        {
            _ticksElapsed++;
            Log.Debug($"Tick {_ticksElapsed}");

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
            _particlePool.ForEachActive(TickParticle);

            Log.Info($"Pool Status - Active: {_particlePool.CountActive}, Inactive: {_particlePool.CountInactive}");
        }

        public void Shutdown()
        {
            Log.Info("Shutting down particle system.");
            _particlePool.Dispose();
            Log.Info($"Pool disposed. Active: {_particlePool.CountActive}, Inactive: {_particlePool.CountInactive}");
        }
    }
}
