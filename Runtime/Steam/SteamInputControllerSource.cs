using System.IO;
using SimpleTools.Controllers;
using Steamworks;
using UnityEngine;

namespace SimpleTools.Steam
{
    // Analog Steam Input actions mapped onto the Controllers sticks, triggers and trackpads (null = unused).
    // Trackpads should be "absolute_mouse" actions.
    public sealed class SteamAnalogActions
    {
        public string LeftStick { get; set; }
        public string RightStick { get; set; }
        public string LeftTrigger { get; set; }
        public string RightTrigger { get; set; }
        public string LeftTrackpad { get; set; }
        public string RightTrackpad { get; set; }
    }

    // The game's Steam Input actions: an action manifest in StreamingAssets (Steam reads it from there until
    // the game publishes its configuration on Steamworks) with two action sets, one for gameplay and one
    // for menus, switched by Controllers.InMenu.
    public sealed class SteamInputSetup
    {
        // Relative to StreamingAssets
        public string ManifestPath { get; set; }
        public string GameActionSet { get; set; } = "Game";
        public string MenuActionSet { get; set; } = "Menu";
        // Digital action for each button id in the two sets (null = not in that set)
        public (int id, string gameAction, string menuAction)[] Buttons { get; set; } = new (int, string, string)[0];
        public SteamAnalogActions GameAnalog { get; set; } = new SteamAnalogActions();
        public SteamAnalogActions MenuAnalog { get; set; } = new SteamAnalogActions();
    }

    // Controller read through Steam Input. Steam applies the game's actions to any controller (Steam Deck,
    // Steam Controller, PlayStation, Xbox...) and exposes the trackpads; the Input System gamepads are the
    // same physical controller, so they get disabled while this source is connected.
    public sealed class SteamInputControllerSource : IControllerSource
    {
        // After switching action set, Steam keeps reporting the old set for a frame or two, so a button
        // held across the switch (Start opening and closing the pause) looks released and then pressed
        // again. Such a button counts again only after it has really been released, which is trusted no
        // earlier than this many seconds after the switch.
        private const float SwitchSettleTime = 0.2f;

        private sealed class AnalogHandles
        {
            public InputAnalogActionHandle_t leftStick;
            public InputAnalogActionHandle_t rightStick;
            public InputAnalogActionHandle_t leftTrigger;
            public InputAnalogActionHandle_t rightTrigger;
            public InputAnalogActionHandle_t leftTrackpad;
            public InputAnalogActionHandle_t rightTrackpad;
        }

        private readonly SteamInputSetup _setup;
        private readonly bool _manifestAccepted;
        private readonly InputDigitalActionHandle_t[] _gameButtons;
        private readonly InputDigitalActionHandle_t[] _menuButtons;
        private readonly bool[] _held;
        private readonly bool[] _latched;
        private readonly InputHandle_t[] _controllers = new InputHandle_t[Constants.STEAM_INPUT_MAX_COUNT];
        private readonly AnalogHandles _gameAnalog = new AnalogHandles();
        private readonly AnalogHandles _menuAnalog = new AnalogHandles();

        private InputActionSetHandle_t _gameSet;
        private InputActionSetHandle_t _menuSet;
        private InputHandle_t _active;
        private bool _handlesLoaded;
        private int _controllerCount;
        private int _actionsLoaded;
        private int _nextAttempt;
        private bool _inMenu;
        private ESteamInputType _type;
        private bool _setChosen;
        private float _switchTime;

        private SteamInputControllerSource(SteamInputSetup setup, bool manifestAccepted)
        {
            _setup = setup;
            _manifestAccepted = manifestAccepted;
            _gameButtons = new InputDigitalActionHandle_t[setup.Buttons.Length];
            _menuButtons = new InputDigitalActionHandle_t[setup.Buttons.Length];
            int count = 0;
            foreach ((int id, _, _) in setup.Buttons)
            {
                count = Mathf.Max(count, id + 1);
            }

            _held = new bool[count];
            _latched = new bool[count];
        }

        public string Name => "Steam Input";
        public bool IsConnected => _active.m_InputHandle != 0;
        public ControllerFamily Family { get; private set; }
        public bool HasTrackpad { get; private set; }
        public Vector2 LeftStick { get; private set; }
        public Vector2 RightStick { get; private set; }
        public float Triggers { get; private set; }
        public Vector2 LeftTrackpad { get; private set; }
        public Vector2 RightTrackpad { get; private set; }
        public bool ReplacesInputSystemGamepads => IsConnected;
        public bool NeedsUiNavigation => true;

        public string Status =>
            $"Steam Input: manifest {(_manifestAccepted ? "ok" : "rejected")}, controllers {_controllerCount}" +
            (IsConnected
                ? $" ({_type}), sets {(_gameSet.m_InputActionSetHandle != 0 ? "ok" : "no")}/" +
                  $"{(_menuSet.m_InputActionSetHandle != 0 ? "ok" : "no")}, actions {_actionsLoaded}, " +
                  $"active {(_inMenu ? _setup.MenuActionSet : _setup.GameActionSet)}"
                : "");

        // Starts Steam Input and loads the manifest; returns null when Steam is not running. Add the result
        // with Controllers.AddSource.
        public static SteamInputControllerSource Create(SteamInputSetup setup)
        {
            if (!SteamService.IsActive || !SteamInput.Init(false))
            {
                Debug.LogWarning("SteamInputControllerSource: Steam Input not available.");
                return null;
            }

            string path = Path.Combine(Application.streamingAssetsPath, setup.ManifestPath);
            bool accepted = SteamInput.SetInputActionManifestFilePath(path);
            if (!accepted)
            {
                Debug.LogWarning("SteamInputControllerSource: Steam rejected the action manifest " + path);
            }

            var source = new SteamInputControllerSource(setup, accepted);
            SteamService.ShuttingDown += source.Shutdown;
            return source;
        }

        public void Update(bool inMenu)
        {
            _controllerCount = SteamInput.GetConnectedControllers(_controllers);
            _active = _controllerCount > 0 ? _controllers[0] : default;
            if (_setChosen && inMenu != _inMenu)
            {
                _switchTime = Time.unscaledTime;
                for (int id = 0; id < _held.Length; id++)
                {
                    _latched[id] |= _held[id];
                }
            }

            _setChosen = true;
            _inMenu = inMenu;
            if (!IsConnected)
            {
                return;
            }

            // Handles stay 0 until Steam has loaded the manifest: ask again (about every half second)
            // until they are all there
            if (!_handlesLoaded && Time.frameCount >= _nextAttempt)
            {
                _nextAttempt = Time.frameCount + 30;
                LoadHandles();
            }

            _type = SteamInput.GetInputTypeForHandle(_active);
            Family = _type == ESteamInputType.k_ESteamInputType_SteamDeckController ? ControllerFamily.SteamDeck
                : _type == ESteamInputType.k_ESteamInputType_PS3Controller || _type == ESteamInputType.k_ESteamInputType_PS4Controller ||
                  _type == ESteamInputType.k_ESteamInputType_PS5Controller ? ControllerFamily.PlayStation
                : ControllerFamily.Xbox;
            HasTrackpad = _type == ESteamInputType.k_ESteamInputType_SteamDeckController ||
                          _type == ESteamInputType.k_ESteamInputType_SteamController;

            SteamInput.ActivateActionSet(_active, inMenu ? _menuSet : _gameSet);
            InputDigitalActionHandle_t[] buttons = inMenu ? _menuButtons : _gameButtons;
            System.Array.Clear(_held, 0, _held.Length);
            for (int index = 0; index < buttons.Length; index++)
            {
                _held[_setup.Buttons[index].id] |= buttons[index].m_InputDigitalActionHandle != 0 &&
                                                   SteamInput.GetDigitalActionData(_active, buttons[index]).bState != 0;
            }

            bool settled = Time.unscaledTime - _switchTime >= SwitchSettleTime;
            for (int id = 0; id < _held.Length; id++)
            {
                if (_latched[id])
                {
                    _latched[id] = _held[id] || !settled;
                    _held[id] = false;
                }
            }

            AnalogHandles analog = inMenu ? _menuAnalog : _gameAnalog;
            LeftStick = Analog(analog.leftStick);
            RightStick = Analog(analog.rightStick);
            Triggers = Analog(analog.rightTrigger).x - Analog(analog.leftTrigger).x;
            // Steam reports trackpad movement with y down, like the screen: flip it to match the mouse in
            // Unity (y up)
            LeftTrackpad = FlipY(Analog(analog.leftTrackpad));
            RightTrackpad = FlipY(Analog(analog.rightTrackpad));
        }

        public bool IsHeld(int button) => IsConnected && button >= 0 && button < _held.Length && _held[button];

        public void Vibrate(float low, float high)
        {
            if (IsConnected)
            {
                SteamInput.TriggerVibration(_active, (ushort)(Mathf.Clamp01(low) * ushort.MaxValue),
                    (ushort)(Mathf.Clamp01(high) * ushort.MaxValue));
            }
        }

        private void Shutdown()
        {
            Vibrate(0f, 0f);
            SteamInput.Shutdown();
        }

        private static Vector2 FlipY(Vector2 movement) => new Vector2(movement.x, -movement.y);

        private Vector2 Analog(InputAnalogActionHandle_t action)
        {
            if (action.m_InputAnalogActionHandle == 0)
            {
                return Vector2.zero;
            }

            InputAnalogActionData_t data = SteamInput.GetAnalogActionData(_active, action);
            return data.bActive != 0 ? new Vector2(data.x, data.y) : Vector2.zero;
        }

        private void LoadHandles()
        {
            _gameSet = SteamInput.GetActionSetHandle(_setup.GameActionSet);
            _menuSet = SteamInput.GetActionSetHandle(_setup.MenuActionSet);
            _actionsLoaded = 0;
            int expected = 0;
            for (int index = 0; index < _setup.Buttons.Length; index++)
            {
                _gameButtons[index] = Digital(_setup.Buttons[index].gameAction, ref expected);
                _menuButtons[index] = Digital(_setup.Buttons[index].menuAction, ref expected);
            }

            LoadAnalog(_setup.GameAnalog, _gameAnalog, ref expected);
            LoadAnalog(_setup.MenuAnalog, _menuAnalog, ref expected);
            _handlesLoaded = _gameSet.m_InputActionSetHandle != 0 && _menuSet.m_InputActionSetHandle != 0 &&
                             _actionsLoaded == expected;
        }

        private void LoadAnalog(SteamAnalogActions names, AnalogHandles handles, ref int expected)
        {
            handles.leftStick = Analog(names.LeftStick, ref expected);
            handles.rightStick = Analog(names.RightStick, ref expected);
            handles.leftTrigger = Analog(names.LeftTrigger, ref expected);
            handles.rightTrigger = Analog(names.RightTrigger, ref expected);
            handles.leftTrackpad = Analog(names.LeftTrackpad, ref expected);
            handles.rightTrackpad = Analog(names.RightTrackpad, ref expected);
        }

        private InputDigitalActionHandle_t Digital(string name, ref int expected)
        {
            if (string.IsNullOrEmpty(name))
            {
                return default;
            }

            expected++;
            InputDigitalActionHandle_t handle = SteamInput.GetDigitalActionHandle(name);
            if (handle.m_InputDigitalActionHandle != 0)
            {
                _actionsLoaded++;
            }

            return handle;
        }

        private InputAnalogActionHandle_t Analog(string name, ref int expected)
        {
            if (string.IsNullOrEmpty(name))
            {
                return default;
            }

            expected++;
            InputAnalogActionHandle_t handle = SteamInput.GetAnalogActionHandle(name);
            if (handle.m_InputAnalogActionHandle != 0)
            {
                _actionsLoaded++;
            }

            return handle;
        }
    }
}
