using System;
using System.Linq;
using CycloneGames.InputSystem.Runtime;
using R3;
using System.Collections.Generic;
using System.Text;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

namespace CycloneGames.InputSystem.Tests.Editor
{
    internal enum DeviceScenario
    {
        KeyboardMouse,
        Gamepad,
        Mixed
    }

    /// <summary>
    /// Declarative description of one measured configuration. Every axis is expressed in absolute counts so that
    /// the generated action graph is reproducible from the row alone.
    /// </summary>
    internal readonly struct PerfScenario
    {
        internal PerfScenario(
            string name,
            int players,
            int contexts,
            int actionsPerContext,
            int pollingPercent,
            DeviceScenario device,
            bool blocksLowerPriority = false)
        {
            Name = name;
            Players = players;
            Contexts = contexts;
            ActionsPerContext = actionsPerContext;
            PollingPercent = pollingPercent;
            Device = device;
            BlocksLowerPriority = blocksLowerPriority;
        }

        internal string Name { get; }
        internal int Players { get; }
        internal int Contexts { get; }
        internal int ActionsPerContext { get; }
        internal int PollingPercent { get; }
        internal DeviceScenario Device { get; }
        internal bool BlocksLowerPriority { get; }

        internal int ActionsPerPlayer => Contexts * ActionsPerContext;
        internal int TotalActions => Players * ActionsPerPlayer;

        internal int PollingActionsPerContext =>
            (int)Math.Round(ActionsPerContext * (PollingPercent / 100d), MidpointRounding.AwayFromZero);

        internal string Describe()
        {
            return Name +
                   " [players=" + Players +
                   ", contexts=" + Contexts +
                   ", actionsPerContext=" + ActionsPerContext +
                   ", pollingPct=" + PollingPercent +
                   ", device=" + Device +
                   "]";
        }
    }

    /// <summary>
    /// Builds player slot configurations, context stacks and input events for a scenario. Bindings are real
    /// control paths so that the generated action graph has the same shape and resolution cost as production
    /// configurations.
    /// </summary>
    internal static class PerfScenarioBuilder
    {
        private const int LongPressEveryNthAction = 8;

        internal static PlayerSlotConfig BuildPlayerSlot(PerfScenario scenario, int playerId)
        {
            var contexts = new List<ContextDefinitionConfig>(scenario.Contexts);
            for (int contextIndex = 0; contextIndex < scenario.Contexts; contextIndex++)
            {
                contexts.Add(new ContextDefinitionConfig
                {
                    Name = "Ctx" + contextIndex,
                    ActionMap = "Map" + contextIndex,
                    Priority = contextIndex,
                    BlocksLowerPriority = scenario.BlocksLowerPriority,
                    Bindings = BuildBindings(scenario, contextIndex)
                });
            }

            return new PlayerSlotConfig
            {
                PlayerId = playerId,
                Contexts = contexts
            };
        }

        private static List<ActionBindingConfig> BuildBindings(PerfScenario scenario, int contextIndex)
        {
            int pollingCount = scenario.PollingActionsPerContext;
            var bindings = new List<ActionBindingConfig>(scenario.ActionsPerContext);
            for (int actionIndex = 0; actionIndex < scenario.ActionsPerContext; actionIndex++)
            {
                bool polling = actionIndex < pollingCount;
                // A three-way rotation keeps every value type present in every scenario so that subject
                // creation, delta detection and hold-state wiring are all exercised.
                int kind = actionIndex % 3;
                string name = "A" + actionIndex;
                if (kind == 1)
                {
                    bindings.Add(new ActionBindingConfig
                    {
                        Type = ActionValueType.Vector2,
                        ActionName = name,
                        ExpectedControlType = "Vector2",
                        DeviceBindings = new List<string> { Vector2Path(scenario.Device, contextIndex) },
                        UpdateMode = polling ? InputUpdateMode.Polling : InputUpdateMode.EventDriven
                    });
                }
                else if (kind == 2)
                {
                    bindings.Add(new ActionBindingConfig
                    {
                        Type = ActionValueType.Float,
                        ActionName = name,
                        ExpectedControlType = "Axis",
                        DeviceBindings = new List<string> { FloatPath(scenario.Device, contextIndex) },
                        UpdateMode = polling ? InputUpdateMode.Polling : InputUpdateMode.EventDriven,
                        LongPressMs = actionIndex % LongPressEveryNthAction == 2 ? 350 : 0
                    });
                }
                else
                {
                    bindings.Add(new ActionBindingConfig
                    {
                        Type = ActionValueType.Button,
                        ActionName = name,
                        DeviceBindings = new List<string> { ButtonPath(scenario.Device, contextIndex, actionIndex) },
                        UpdateMode = polling ? InputUpdateMode.Polling : InputUpdateMode.EventDriven,
                        LongPressMs = actionIndex % LongPressEveryNthAction == 0 ? 250 : 0
                    });
                }
            }

            return bindings;
        }

        private static string Vector2Path(DeviceScenario device, int contextIndex)
        {
            return device switch
            {
                DeviceScenario.KeyboardMouse => Wrapped("<Mouse>/delta", contextIndex),
                DeviceScenario.Gamepad => Wrapped("<Gamepad>/leftStick", contextIndex),
                _ => Wrapped(contextIndex % 2 == 0 ? "<Gamepad>/leftStick" : "<Mouse>/delta", contextIndex)
            };
        }

        private static string FloatPath(DeviceScenario device, int contextIndex)
        {
            return device switch
            {
                DeviceScenario.KeyboardMouse => Wrapped("<Mouse>/scroll/y", contextIndex),
                DeviceScenario.Gamepad => Wrapped("<Gamepad>/rightTrigger", contextIndex),
                _ => Wrapped(contextIndex % 2 == 0 ? "<Gamepad>/rightTrigger" : "<Mouse>/scroll/y", contextIndex)
            };
        }

        private static string ButtonPath(DeviceScenario device, int contextIndex, int actionIndex)
        {
            // Button bindings cycle over a small key set so that distinct bindings do not require thousands of
            // distinct control names while still producing distinct action identities.
            int slot = (actionIndex + contextIndex) % 8;
            if (device == DeviceScenario.Gamepad)
            {
                string[] gamepadButtons =
                {
                    "<Gamepad>/buttonSouth", "<Gamepad>/buttonEast", "<Gamepad>/buttonNorth",
                    "<Gamepad>/buttonWest", "<Gamepad>/leftShoulder", "<Gamepad>/rightShoulder",
                    "<Gamepad>/leftStickPress", "<Gamepad>/rightStickPress"
                };
                return gamepadButtons[slot];
            }

            string[] keyboardKeys =
            {
                "<Keyboard>/a", "<Keyboard>/b", "<Keyboard>/c", "<Keyboard>/d",
                "<Keyboard>/e", "<Keyboard>/f", "<Keyboard>/g", "<Keyboard>/h"
            };
            return keyboardKeys[slot];
        }

        private static string Wrapped(string path, int contextIndex)
        {
            // Contexts intentionally share the same control paths; isolation comes from the per-context action
            // map, which is what the module builds at runtime.
            return path;
        }

        internal static InputConfigurationLimits BuildLimits()
        {
            return new InputConfigurationLimits(
                maxPlayers: 8,
                maxContextsPerPlayer: 32,
                maxActionsPerContext: 1024,
                maxBindingsPerAction: 16,
                maxTotalActionsPerPlayer: 8192,
                maxStringLength: 256,
                maxLongPressMilliseconds: 600000,
                minContextPriority: -100000,
                maxContextPriority: 100000,
                maxControlSchemesPerPlayer: 16,
                maxDeviceRequirementsPerScheme: 16,
                maxCompositesPerAction: 16,
                maxPartsPerComposite: 16);
        }

        internal static string SerializeConfiguration(IReadOnlyList<PlayerSlotConfig> slots)
        {
            var configuration = new InputConfiguration
            {
                SchemaVersion = InputConfiguration.CurrentSchemaVersion,
                PlayerSlots = new List<PlayerSlotConfig>(slots)
            };

            return Encoding.UTF8.GetString(VYaml.Serialization.YamlSerializer.Serialize(configuration).ToArray());
        }

        /// <summary>
        /// Pushes the whole configured context stack for a player so that every context is active.
        /// </summary>
        internal static List<InputContext> PushAllContexts(IInputPlayer player, PerfScenario scenario)
        {
            var contexts = new List<InputContext>(scenario.Contexts);
            for (int i = 0; i < scenario.Contexts; i++)
            {
                var context = new InputContext("Map" + i, "Ctx" + i, i, scenario.BlocksLowerPriority);
                contexts.Add(context);
                player.PushContext(context);
            }

            return contexts;
        }

        /// <summary>
        /// Creates one subscriber per action of the given value shapes so that the measured frame includes the
        /// cost of dispatching into product callbacks.
        /// </summary>
        internal static IDisposable SubscribeAll(IInputPlayer player, PerfScenario scenario, PerfSink sink)
        {
            var subscriptions = new List<IDisposable>(scenario.ActionsPerPlayer * 3);
            for (int contextIndex = 0; contextIndex < scenario.Contexts; contextIndex++)
            {
                string contextName = "Ctx" + contextIndex;
                string mapName = "Map" + contextIndex;
                for (int actionIndex = 0; actionIndex < scenario.ActionsPerContext; actionIndex++)
                {
                    string actionName = "A" + actionIndex;
                    int kind = actionIndex % 3;
                    if (kind == 1)
                    {
                        subscriptions.Add(player.GetVector2Observable(contextName, mapName, actionName).Subscribe((UnityEngine.Vector2 value) => sink.OnVector2(value)));
                    }
                    else if (kind == 2)
                    {
                        subscriptions.Add(player.GetScalarObservable(contextName, mapName, actionName).Subscribe((float value) => sink.OnScalar(value)));
                    }
                    else
                    {
                        subscriptions.Add(player.GetButtonObservable(contextName, mapName, actionName).Subscribe((R3.Unit value) => sink.OnUnit(value)));
                        subscriptions.Add(player.GetPressStateObservable(contextName, mapName, actionName).Subscribe((bool value) => sink.OnBool(value)));
                    }
                }
            }

            return new CompositeSubscription(subscriptions);
        }

        private sealed class CompositeSubscription : IDisposable
        {
            private readonly List<IDisposable> _items;

            internal CompositeSubscription(List<IDisposable> items)
            {
                _items = items;
            }

            public void Dispose()
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    try
                    {
                        _items[i].Dispose();
                    }
                    catch (Exception)
                    {
                        // Teardown of a measurement fixture must not mask the measurement result.
                    }
                }

                _items.Clear();
            }
        }

        internal static InputUser PairNewUser(InputDevice device)
        {
            return InputUser.PerformPairingWithDevice(device);
        }
    }

    /// <summary>
    /// Sink that consumes every dispatched value. Fields are written so that dispatch work is not optimized away.
    /// </summary>
    internal sealed class PerfSink
    {
        internal float UnitCount;
        internal float Vector2Sum;
        internal float ScalarSum;
        internal int BoolToggles;

        internal void OnUnit(R3.Unit _)
        {
            UnitCount += 1f;
        }

        internal void OnVector2(UnityEngine.Vector2 value)
        {
            Vector2Sum += value.x + value.y;
        }

        internal void OnScalar(float value)
        {
            ScalarSum += value;
        }

        internal void OnBool(bool value)
        {
            if (value) BoolToggles++;
        }
    }
}
