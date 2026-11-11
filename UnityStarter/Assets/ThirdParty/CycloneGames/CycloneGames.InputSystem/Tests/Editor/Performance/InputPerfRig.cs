using System;
using CycloneGames.InputSystem.Runtime;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Users;
using UIS = UnityEngine.InputSystem.InputSystem;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Owns the devices, users, players, contexts and subscriptions of one measured configuration.
    /// All members are main-thread confined, matching the module's threading contract.
    /// </summary>
    internal sealed class PerfRig : IDisposable
    {
        private readonly List<InputPlayer> _players = new List<InputPlayer>();
        private readonly List<InputDevice> _devices = new List<InputDevice>();
        private readonly List<InputContext> _contexts = new List<InputContext>();
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        private InputUser[] _users = Array.Empty<InputUser>();

        internal PerfScenario Scenario { get; private set; }
        internal IReadOnlyList<InputPlayer> Players => _players;
        internal IReadOnlyList<InputDevice> Devices => _devices;
        internal IReadOnlyList<InputContext> Contexts => _contexts;
        internal PerfSink Sink { get; } = new PerfSink();

        internal InputPlayer First => _players.Count > 0 ? _players[0] : null;

        internal static PerfRig Create(
            PerfScenario scenario,
            bool pushContexts = true,
            bool subscribe = true,
            int maxPlayers = -1)
        {
            var rig = new PerfRig { Scenario = scenario };
            int playerCount = maxPlayers >= 0 ? Math.Min(maxPlayers, scenario.Players) : scenario.Players;
            rig._users = new InputUser[playerCount];
            InputConfigurationLimits limits = PerfScenarioBuilder.BuildLimits();

            for (int playerIndex = 0; playerIndex < playerCount; playerIndex++)
            {
                PlayerSlotConfig slot = PerfScenarioBuilder.BuildPlayerSlot(scenario, playerIndex);
                InputDevice primary = rig.AddPlayerDevices(scenario.Device, playerIndex, out InputDevice companion);

                InputUser user = InputUser.PerformPairingWithDevice(primary);
                if (companion != null) InputUser.PerformPairingWithDevice(companion, user);
                rig._users[playerIndex] = user;

                var player = new InputPlayer(playerIndex, user, slot, limits, primary);
                rig._players.Add(player);

                if (pushContexts) rig._contexts.AddRange(PerfScenarioBuilder.PushAllContexts(player, scenario));
                if (subscribe) rig._subscriptions.Add(PerfScenarioBuilder.SubscribeAll(player, scenario, rig.Sink));
            }

            return rig;
        }

        private InputDevice AddPlayerDevices(DeviceScenario device, int playerIndex, out InputDevice companion)
        {
            companion = null;
            bool wantGamepad = device == DeviceScenario.Gamepad ||
                               (device == DeviceScenario.Mixed && playerIndex % 2 == 0);
            if (wantGamepad)
            {
                var gamepad = UIS.AddDevice<Gamepad>("PerfPad" + playerIndex);
                _devices.Add(gamepad);
                return gamepad;
            }

            var keyboard = UIS.AddDevice<Keyboard>("PerfKeyboard" + playerIndex);
            var mouse = UIS.AddDevice<Mouse>("PerfMouse" + playerIndex);
            _devices.Add(keyboard);
            _devices.Add(mouse);
            companion = mouse;
            return keyboard;
        }

        /// <summary>
        /// Queues one state event per player and advances the Input System by one frame so that event-driven
        /// actions, composite resolution and reactive forwarding all run.
        /// </summary>
        internal void QueueFrameInput(float phase)
        {
            for (int i = 0; i < _devices.Count; i++)
            {
                InputDevice device = _devices[i];
                if (device is Gamepad gamepad)
                {
                    UIS.QueueStateEvent(gamepad, new GamepadState
                    {
                        leftStick = new Vector2(phase, phase),
                        rightTrigger = phase > 0f ? phase : 0f,
                        buttons = phase > 0.5f ? 1u : 0u
                    });
                }
                else if (device is Keyboard keyboard)
                {
                    UIS.QueueStateEvent(keyboard, new KeyboardState(phase > 0.5f ? Key.A : Key.None, Key.B));
                }
                else if (device is Mouse mouse)
                {
                    UIS.QueueStateEvent(mouse, new MouseState
                    {
                        position = new Vector2(phase * 100f, phase * 50f),
                        delta = new Vector2(phase, phase),
                        scroll = new Vector2(0f, phase)
                    });
                }
            }
        }

        /// <summary>
        /// One synthetic frame: advance the Input System, then run the module's own per-frame pump.
        /// </summary>
        internal void Frame(float phase, double timestamp)
        {
            QueueFrameInput(phase);
            UIS.Update();
            for (int i = 0; i < _players.Count; i++) _players[i].UpdateRuntimeState(timestamp);
        }

        /// <summary>
        /// Runs only the module's per-frame pump, with no Input System update. This isolates the code that the
        /// module itself executes each frame: polling reads and hold-progress evaluation.
        /// </summary>
        internal void PumpOnly(double timestamp)
        {
            for (int i = 0; i < _players.Count; i++) _players[i].UpdateRuntimeState(timestamp);
        }

        /// <summary>
        /// Advances only the Unity Input System, which provides the baseline cost that the module is added on
        /// top of.
        /// </summary>
        internal void UnityInputUpdateOnly(float phase)
        {
            QueueFrameInput(phase);
            UIS.Update();
        }

        public void Dispose()
        {
            for (int i = 0; i < _subscriptions.Count; i++)
            {
                try
                {
                    _subscriptions[i].Dispose();
                }
                catch (Exception)
                {
                }
            }

            _subscriptions.Clear();

            for (int i = 0; i < _contexts.Count; i++)
            {
                try
                {
                    _contexts[i].Dispose();
                }
                catch (Exception)
                {
                }
            }

            _contexts.Clear();

            for (int i = 0; i < _players.Count; i++)
            {
                try
                {
                    _players[i].Dispose();
                }
                catch (Exception)
                {
                }
            }

            _players.Clear();

            for (int i = 0; i < _devices.Count; i++)
            {
                try
                {
                    if (_devices[i] != null && _devices[i].added) UIS.RemoveDevice(_devices[i]);
                }
                catch (Exception)
                {
                }
            }

            _devices.Clear();
            _users = Array.Empty<InputUser>();
        }
    }
}
