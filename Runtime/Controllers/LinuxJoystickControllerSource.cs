using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace SimpleTools.Controllers
{
    // A controller that the Input System on Linux creates as an SDL joystick instead of a Gamepad. This is
    // the Steam Deck when Steam presents it as an emulated Xbox gamepad. Controls are named after their
    // Linux usage: A, B, X, Y (Xbox labels), TriggerLeft/Right (shoulders), Select, Start, ThumbLeft/Right,
    // Stick (left stick), RotateX/RotateY (right stick), Z/RotateZ (triggers), Hat (d-pad).
    public sealed class LinuxJoystickControllerSource : IControllerSource
    {
        // Above this travel an analog trigger counts as pressed when the game binds it as a button
        private const float TriggerPressPoint = 0.5f;

        private static readonly Dictionary<GamepadButton, string> ButtonNames = new Dictionary<GamepadButton, string>
        {
            { GamepadButton.South, "A" },
            { GamepadButton.East, "B" },
            { GamepadButton.West, "X" },
            { GamepadButton.North, "Y" },
            { GamepadButton.LeftShoulder, "TriggerLeft" },
            { GamepadButton.RightShoulder, "TriggerRight" },
            { GamepadButton.LeftStick, "ThumbLeft" },
            { GamepadButton.RightStick, "ThumbRight" },
            { GamepadButton.Start, "Start" },
            { GamepadButton.Select, "Select" },
        };

        private readonly ControllerSetup _setup;
        private readonly List<(int id, Func<bool> held)> _buttons = new List<(int, Func<bool>)>();
        private Joystick _joystick;
        private StickControl _leftStick;
        private AxisControl _rightX;
        private AxisControl _rightY;
        private AxisControl _leftTrigger;
        private AxisControl _rightTrigger;
        // Trigger value at rest: SDL reports them from -1 to 1 or from 0 to 1 depending on the driver
        private float _leftTriggerRest;
        private float _rightTriggerRest;

        public LinuxJoystickControllerSource(ControllerSetup setup)
        {
            _setup = setup;
        }

        public string Name => "Linux joystick" + (_joystick != null ? $" ({_joystick.displayName})" : "");
        public bool IsConnected => _joystick != null;
        public ControllerFamily Family => Environment.GetEnvironmentVariable("SteamDeck") == "1"
            ? ControllerFamily.SteamDeck
            : ControllerFamily.Xbox;
        public bool HasTrackpad => false;
        public Vector2 LeftStick => _leftStick != null ? _leftStick.ReadValue() : Vector2.zero;
        // On Linux the right vertical axis grows downwards
        public Vector2 RightStick => new Vector2(Read(_rightX), -Read(_rightY));
        public float Triggers => Trigger(_rightTrigger, _rightTriggerRest) - Trigger(_leftTrigger, _leftTriggerRest);
        public Vector2 LeftTrackpad => Vector2.zero;
        public Vector2 RightTrackpad => Vector2.zero;
        public bool ReplacesInputSystemGamepads => false;
        public bool NeedsUiNavigation => true;
        public string Status => null;

        public void Update(bool inMenu)
        {
            if (_joystick != null && _joystick.added)
            {
                return;
            }

            _joystick = null;
            _buttons.Clear();
            foreach (Joystick joystick in Joystick.all)
            {
                if (joystick.TryGetChildControl<ButtonControl>("A") != null &&
                    joystick.TryGetChildControl<ButtonControl>("Start") != null)
                {
                    Connect(joystick);
                    return;
                }
            }
        }

        public bool IsHeld(int button)
        {
            foreach ((int id, Func<bool> held) in _buttons)
            {
                if (id == button && held())
                {
                    return true;
                }
            }

            return false;
        }

        // Input System joysticks have no rumble
        public void Vibrate(float low, float high)
        {
        }

        private void Connect(Joystick joystick)
        {
            _joystick = joystick;
            _leftStick = joystick.TryGetChildControl<StickControl>("Stick");
            _rightX = joystick.TryGetChildControl<AxisControl>("RotateX");
            _rightY = joystick.TryGetChildControl<AxisControl>("RotateY");
            _leftTrigger = joystick.TryGetChildControl<AxisControl>("Z");
            _rightTrigger = joystick.TryGetChildControl<AxisControl>("RotateZ");
            // Triggers are assumed to be released when the controller connects
            _leftTriggerRest = Read(_leftTrigger);
            _rightTriggerRest = Read(_rightTrigger);

            DpadControl hat = joystick.TryGetChildControl<DpadControl>("Hat");
            foreach ((int id, GamepadButton physical) in _setup.Buttons)
            {
                Func<bool> held = Button(joystick, hat, physical);
                if (held != null)
                {
                    _buttons.Add((id, held));
                }
            }

            Debug.Log($"LinuxJoystickControllerSource: connected {joystick.displayName} ({joystick.layout}), " +
                      $"controls: {string.Join(" ", Array.ConvertAll(joystick.allControls.ToArray(), control => control.name))}");
        }

        private Func<bool> Button(Joystick joystick, DpadControl hat, GamepadButton physical)
        {
            switch (physical)
            {
                case GamepadButton.DpadUp: return hat != null ? () => hat.up.isPressed : null;
                case GamepadButton.DpadDown: return hat != null ? () => hat.down.isPressed : null;
                case GamepadButton.DpadLeft: return hat != null ? () => hat.left.isPressed : null;
                case GamepadButton.DpadRight: return hat != null ? () => hat.right.isPressed : null;
                case GamepadButton.LeftTrigger: return () => Trigger(_leftTrigger, _leftTriggerRest) >= TriggerPressPoint;
                case GamepadButton.RightTrigger: return () => Trigger(_rightTrigger, _rightTriggerRest) >= TriggerPressPoint;
            }

            ButtonControl control = ButtonNames.TryGetValue(physical, out string name)
                ? joystick.TryGetChildControl<ButtonControl>(name)
                : null;
            return control != null ? () => control.isPressed : null;
        }

        private static float Read(AxisControl axis) => axis != null ? axis.ReadValue() : 0f;

        private static float Trigger(AxisControl axis, float rest)
        {
            float travel = 1f - rest;
            return axis == null || travel <= 0.01f ? 0f : Mathf.Clamp01((axis.ReadValue() - rest) / travel);
        }
    }
}
