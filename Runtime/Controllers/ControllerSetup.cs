using System;
using UnityEngine.InputSystem.LowLevel;

namespace SimpleTools.Controllers
{
    // What the game calls its buttons. Ids are the game's own (usually an enum cast to int); each one can
    // be tied to a physical gamepad button, read by the Input System and Linux joystick sources. Ids with
    // no physical button can still come from other sources, for example a Steam Input action bound to a
    // trackpad touch.
    public sealed class ControllerSetup
    {
        public ControllerSetup(int buttonCount, params (int id, GamepadButton button)[] buttons)
        {
            ButtonCount = buttonCount;
            Buttons = buttons ?? Array.Empty<(int, GamepadButton)>();
        }

        public int ButtonCount { get; }
        public (int id, GamepadButton button)[] Buttons { get; }

        // Buttons that move the focused UI Toolkit element when a menu is open (-1 = none). Used only for
        // sources that the Input System UI actions do not see.
        public int NavigateUp { get; set; } = -1;
        public int NavigateDown { get; set; } = -1;
        public int NavigateLeft { get; set; } = -1;
        public int NavigateRight { get; set; } = -1;
        public int Submit { get; set; } = -1;
        public int Cancel { get; set; } = -1;

        // Holding a direction repeats it after the delay, then every interval (seconds)
        public float RepeatDelay { get; set; } = 0.4f;
        public float RepeatInterval { get; set; } = 0.12f;
    }
}
