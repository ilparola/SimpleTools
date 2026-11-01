using System;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SimpleTools.Controllers
{
    // The device the player is using. With a controller connected (any Controllers source) the controller
    // wins outright: keyboard and mouse are disabled and the pointer is hidden. On the Steam Deck the right
    // trackpad is also a mouse, and alternating it with the controller confused the game. Unplugging the
    // controller brings them back, and so does quitting (also leaving Play mode in the Editor).
    public static class ActiveDevice
    {
        // Set to false to keep keyboard and mouse working alongside the controller
        public static bool ControllerTakesPriority { get; set; } = true;

        public static bool IsController { get; private set; }
        public static ControllerFamily Family { get; private set; }

        // Controller connected or disconnected, or a different family (for icons and texts)
        public static event Action Changed;

        // Input System gamepads disabled because another source reads the same controller
        private static bool _gamepadsDisabled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Initialize()
        {
            IsController = false;
            _gamepadsDisabled = false;
            Family = ControllerFamily.Xbox;
            Changed = null;
            InputSystem.onDeviceChange -= OnDeviceChange;
            InputSystem.onDeviceChange += OnDeviceChange;
            Application.quitting -= EnableAll;
            Application.quitting += EnableAll;
        }

        private static void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            // A mouse or keyboard plugged in while the controller is active stays off as well
            if (change == InputDeviceChange.Added &&
                ((IsController && ControllerTakesPriority && IsKeyboardOrMouse(device)) || (_gamepadsDisabled && device is Gamepad)))
            {
                InputSystem.DisableDevice(device);
            }
        }

        // Called by Controllers every frame: acts only when something changes
        internal static void Update(bool controller, ControllerFamily family, bool replacesGamepads)
        {
            if (replacesGamepads != _gamepadsDisabled)
            {
                _gamepadsDisabled = replacesGamepads;
                foreach (Gamepad gamepad in Gamepad.all.ToArray())
                {
                    if (replacesGamepads)
                    {
                        InputSystem.DisableDevice(gamepad);
                    }
                    else
                    {
                        InputSystem.EnableDevice(gamepad);
                    }
                }
            }

            if (controller == IsController)
            {
                if (controller && family != Family)
                {
                    Family = family;
                    Changed?.Invoke();
                }

                return;
            }

            IsController = controller;
            Family = family;
            if (ControllerTakesPriority)
            {
                foreach (InputDevice device in InputSystem.devices)
                {
                    if (!IsKeyboardOrMouse(device))
                    {
                        continue;
                    }

                    if (controller)
                    {
                        InputSystem.DisableDevice(device);
                    }
                    else
                    {
                        InputSystem.EnableDevice(device);
                    }
                }

                Cursor.visible = !controller;
            }

            Changed?.Invoke();
        }

        private static void EnableAll()
        {
            foreach (InputDevice device in InputSystem.devices)
            {
                if ((IsKeyboardOrMouse(device) || device is Gamepad) && !device.enabled)
                {
                    InputSystem.EnableDevice(device);
                }
            }

            Cursor.visible = true;
        }

        private static bool IsKeyboardOrMouse(InputDevice device)
        {
            return device is Keyboard || device is Mouse;
        }
    }
}
