using CycloneGames.GameplayFramework.Runtime;
using strange.extensions.context.impl;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Sample.StrangeIoC
{
    public class StrangeIoCSampleBootstrapper : ContextView
    {
        [SerializeField] WorldSettings worldSettings;

        // The context launches the World in its constructor, and World Actor discovery requires the Scene
        // to be loaded. Unity marks a Scene loaded after every Awake and OnEnable in it, so the context has
        // to be built from Start; creating it in Awake fails with "must be loaded before World Actor
        // discovery".
        void Start()
        {
            context = new StrangeIoCSampleMainContext(this, worldSettings);
        }
    }
}