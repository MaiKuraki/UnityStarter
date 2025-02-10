using CycloneGames.Logger;
using strange.extensions.context.impl;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Example.StrangeIoC
{
    public class StrangeIoCExampleBootstrapper : ContextView
    {
        [SerializeField] WorldSettings worldSettings;
        void Awake()
        {
            if (!MLogger.Instance.ContainsLoggerOfType<UnityLogger>())
            {
                MLogger.Instance.AddLogger(new UnityLogger());
            }

            context = new StrangeIoCExampleMainContext(this, worldSettings);
        }

        void Start()
        {

        }
    }
}