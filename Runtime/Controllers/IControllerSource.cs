using UnityEngine;

namespace SimpleTools.Controllers
{
    public enum ControllerFamily
    {
        Xbox,
        PlayStation,
        SteamDeck
    }

    // One way of reading a controller (Input System, Linux SDL joystick, Steam Input...). Buttons are
    // the game's own ids, as listed in ControllerSetup. Controllers merges all connected sources.
    public interface IControllerSource
    {
        // Shown in the diagnostics
        string Name { get; }
        bool IsConnected { get; }
        ControllerFamily Family { get; }
        bool HasTrackpad { get; }
        Vector2 LeftStick { get; }
        Vector2 RightStick { get; }
        // Right trigger minus left trigger, from -1 to 1
        float Triggers { get; }
        // Trackpad movement since the last frame, in pixels with y up, like the mouse
        Vector2 LeftTrackpad { get; }
        Vector2 RightTrackpad { get; }
        // True when the source reads the same physical controller as the Input System gamepads, which
        // then get disabled so that UI actions do not see it twice (Steam Input does this)
        bool ReplacesInputSystemGamepads { get; }
        // False only for sources whose controller already moves the UI through the Input System UI actions
        bool NeedsUiNavigation { get; }
        // Extra diagnostics line, or null
        string Status { get; }

        void Update(bool inMenu);
        bool IsHeld(int button);
        // Motor speeds from 0 to 1: low = heavy motor (left), high = light motor (right)
        void Vibrate(float low, float high);
    }
}
