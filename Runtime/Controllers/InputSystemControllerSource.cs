using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.DualShock;

namespace SimpleTools.Controllers
{
    // Gamepad.current from the Input System (Windows and macOS, or Linux with a controller that SDL
    // reports as a gamepad). The UI is moved by the Input System UI actions.
    public sealed class InputSystemControllerSource : IControllerSource
    {
        private readonly ControllerSetup _setup;
        private Gamepad _pad;

        public InputSystemControllerSource(ControllerSetup setup)
        {
            _setup = setup;
        }

        public string Name => "Input System";
        public bool IsConnected => _pad != null;
        public ControllerFamily Family => _pad is DualShockGamepad ? ControllerFamily.PlayStation : ControllerFamily.Xbox;
        public bool HasTrackpad => false;
        public Vector2 LeftStick => _pad != null ? _pad.leftStick.ReadValue() : Vector2.zero;
        public Vector2 RightStick => _pad != null ? _pad.rightStick.ReadValue() : Vector2.zero;
        public float Triggers => _pad != null ? _pad.rightTrigger.ReadValue() - _pad.leftTrigger.ReadValue() : 0f;
        public Vector2 LeftTrackpad => Vector2.zero;
        public Vector2 RightTrackpad => Vector2.zero;
        public bool ReplacesInputSystemGamepads => false;
        public bool NeedsUiNavigation => false;
        public string Status => null;

        public void Update(bool inMenu)
        {
            _pad = Gamepad.current;
        }

        public bool IsHeld(int button)
        {
            if (_pad == null)
            {
                return false;
            }

            foreach ((int id, var physical) in _setup.Buttons)
            {
                if (id == button && _pad[physical] is ButtonControl control && control.isPressed)
                {
                    return true;
                }
            }

            return false;
        }

        public void Vibrate(float low, float high)
        {
            _pad?.SetMotorSpeeds(low, high);
        }
    }
}
