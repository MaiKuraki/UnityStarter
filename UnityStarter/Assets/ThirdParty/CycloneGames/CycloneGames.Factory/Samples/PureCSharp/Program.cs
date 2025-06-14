using System;
using System.Threading;

namespace CycloneGames.Factory.Samples.PureCSharp
{
    class Program
    {
        static void Main(string[] args)
        {
            var simulator = new ParticleSystemSimulator();

            // Run the simulation for 20 "ticks"
            for (int i = 0; i < 20; i++)
            {
                simulator.Update();
                Thread.Sleep(200); // Pause to make the output readable
            }

            simulator.Shutdown();
        }
    }
}
