using CycloneGames.Logger;
using strange.extensions.context.impl;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Sample.StrangeIoC
{
    public class StrangeIoCSampleBootstrapper : ContextView
    {
        [SerializeField] WorldSettings worldSettings;
        void Awake()
        {
            if (!MLogger.Instance.ContainsLoggerOfType<UnityLogger>())
            {
                MLogger.Instance.AddLogger(new UnityLogger());
            }

            context = new StrangeIoCSampleMainContext(this, worldSettings);
        }

        void Start()
        {

        }
    }
}