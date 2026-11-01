using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SimpleTools.Controllers
{
    // Single place to read the controller from. Sources are merged: a button is held if any source holds
    // it, and the most tilted stick wins. The first connected source, in priority order, decides family
    // (button icons) and trackpad. Sources added by the game (for example Steam Input) come before the
    // built-in ones: Linux SDL joystick, then the Input System gamepad.
    //
    // Call Configure once, before the first scene (RuntimeInitializeLoadType.BeforeSceneLoad). State is
    // updated once per frame before other scripts.
    public static class Controllers
    {
        private static readonly List<IControllerSource> Sources = new List<IControllerSource>();
        private static readonly List<IControllerSource> Connected = new List<IControllerSource>();
        private static readonly HashSet<object> OpenMenus = new HashSet<object>();
        private static bool[] _held = new bool[0];
        private static bool[] _heldBefore = new bool[0];
        private static IControllerSource _main;
        private static int _builtInSources;

        public static ControllerSetup Setup { get; private set; }
        public static bool IsConnected => _main != null;
        public static ControllerFamily Family => _main != null ? _main.Family : ControllerFamily.Xbox;
        public static bool HasTrackpad => _main != null && _main.HasTrackpad;
        public static Vector2 LeftStick { get; private set; }
        public static Vector2 RightStick { get; private set; }
        // Right trigger minus left trigger, from -1 to 1
        public static float Triggers { get; private set; }
        // Trackpad movement since the last frame, in pixels with y up, like the mouse
        public static Vector2 LeftTrackpad { get; private set; }
        public static Vector2 RightTrackpad { get; private set; }
        public static IReadOnlyList<IControllerSource> ConnectedSources => Connected;

        // Paused game or menus declared with SetMenuOpen. Sources can switch to menu controls (Steam
        // Input action sets). Menus destroyed without closing themselves are dropped.
        public static bool InMenu
        {
            get
            {
                OpenMenus.RemoveWhere(menu => menu is Object unityObject && unityObject == null);
                return Time.timeScale == 0f || OpenMenus.Count > 0;
            }
        }

        // Sources, connected devices and per-source status, for an on-screen overlay or the log
        public static string Diagnostics
        {
            get
            {
                var names = new List<string>();
                foreach (IControllerSource source in Connected)
                {
                    names.Add(source.Name);
                }

                string text = $"sources: {(names.Count > 0 ? string.Join(", ", names) : "none")} | " +
                              $"Input System: gamepads {Gamepad.all.Count}, joysticks {Joystick.all.Count}";
                foreach (IControllerSource source in Sources)
                {
                    if (source.Status != null)
                    {
                        text += "\n" + source.Status;
                    }
                }

                return text;
            }
        }

        public static void Configure(ControllerSetup setup)
        {
            Setup = setup;
            _held = new bool[setup.ButtonCount];
            _heldBefore = new bool[setup.ButtonCount];
            Sources.Clear();
            Sources.Add(new LinuxJoystickControllerSource(setup));
            Sources.Add(new InputSystemControllerSource(setup));
            _builtInSources = Sources.Count;
            ControllersRunner.Create();
        }

        // Adds a source with priority over the built-in ones (and over sources added before it)
        public static void AddSource(IControllerSource source)
        {
            Sources.Insert(Sources.Count - _builtInSources, source);
        }

        public static bool IsHeld(int button) => button >= 0 && button < _held.Length && _held[button];

        public static bool WasPressed(int button) => IsHeld(button) && !_heldBefore[button];

        public static void SetMenuOpen(object menu, bool open)
        {
            if (open)
            {
                OpenMenus.Add(menu);
            }
            else
            {
                OpenMenus.Remove(menu);
            }
        }

        // Motor speeds from 0 to 1: low = heavy motor (left), high = light motor (right)
        public static void Vibrate(float low, float high)
        {
            foreach (IControllerSource source in Connected)
            {
                source.Vibrate(low, high);
            }
        }

        // Held buttons, sticks, triggers and trackpads, for diagnostics; buttonName turns an id into text
        public static string HeldSummary(System.Func<int, string> buttonName)
        {
            var held = new List<string>();
            for (int index = 0; index < _held.Length; index++)
            {
                if (_held[index])
                {
                    held.Add(buttonName(index));
                }
            }

            return $"buttons: {string.Join(" ", held)} | sticks ({LeftStick.x:0.0},{LeftStick.y:0.0}) ({RightStick.x:0.0},{RightStick.y:0.0}) | " +
                   $"triggers {Triggers:0.0} | trackpads ({LeftTrackpad.x:0},{LeftTrackpad.y:0}) ({RightTrackpad.x:0},{RightTrackpad.y:0})";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Setup = null;
            Sources.Clear();
            Connected.Clear();
            OpenMenus.Clear();
            _held = new bool[0];
            _heldBefore = new bool[0];
            _main = null;
            _builtInSources = 0;
            LeftStick = RightStick = LeftTrackpad = RightTrackpad = Vector2.zero;
            Triggers = 0f;
        }

        internal static void Update()
        {
            bool inMenu = InMenu;
            Connected.Clear();
            foreach (IControllerSource source in Sources)
            {
                source.Update(inMenu);
                if (source.IsConnected)
                {
                    Connected.Add(source);
                }
            }

            _main = Connected.Count > 0 ? Connected[0] : null;
            Vector2 leftStick = Vector2.zero;
            Vector2 rightStick = Vector2.zero;
            float triggers = 0f;
            Vector2 leftTrackpad = Vector2.zero;
            Vector2 rightTrackpad = Vector2.zero;
            for (int index = 0; index < _held.Length; index++)
            {
                _heldBefore[index] = _held[index];
                _held[index] = false;
            }

            foreach (IControllerSource source in Connected)
            {
                for (int index = 0; index < _held.Length; index++)
                {
                    _held[index] |= source.IsHeld(index);
                }

                leftStick = MostTilted(leftStick, source.LeftStick);
                rightStick = MostTilted(rightStick, source.RightStick);
                triggers = Mathf.Abs(source.Triggers) > Mathf.Abs(triggers) ? source.Triggers : triggers;
                leftTrackpad += source.LeftTrackpad;
                rightTrackpad += source.RightTrackpad;
            }

            LeftStick = leftStick;
            RightStick = rightStick;
            Triggers = triggers;
            LeftTrackpad = leftTrackpad;
            RightTrackpad = rightTrackpad;

            bool replacesGamepads = false;
            foreach (IControllerSource source in Connected)
            {
                replacesGamepads |= source.ReplacesInputSystemGamepads;
            }

            ActiveDevice.Update(IsConnected, Family, replacesGamepads);
        }

        internal static bool NeedsUiNavigation()
        {
            foreach (IControllerSource source in Connected)
            {
                if (source.NeedsUiNavigation)
                {
                    return true;
                }
            }

            return false;
        }

        private static Vector2 MostTilted(Vector2 first, Vector2 second)
        {
            return second.sqrMagnitude > first.sqrMagnitude ? second : first;
        }
    }

    [AddComponentMenu("")]
    [DefaultExecutionOrder(-1000)]
    internal sealed class ControllersRunner : MonoBehaviour
    {
        private static ControllersRunner _instance;

        private float _nextRepeat;
        private string _lastDiagnostics;

        internal static void Create()
        {
            if (_instance != null)
            {
                return;
            }

            GameObject host = new GameObject("SimpleTools.Controllers");
            host.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<ControllersRunner>();
        }

        private void Update()
        {
            Controllers.Update();

            // Every change of controller state goes to the log (Player.log), to understand what happens on
            // machines where you cannot debug
            string diagnostics = Controllers.Diagnostics;
            if (diagnostics != _lastDiagnostics)
            {
                _lastDiagnostics = diagnostics;
                Debug.Log("Controllers: " + diagnostics.Replace("\n", " | "));
            }

            // The Input System UI actions only move menus with Gamepads: other sources are handled here
            if (Controllers.InMenu && Controllers.NeedsUiNavigation())
            {
                NavigateUi(Controllers.Setup);
            }
        }

        // Without UI actions, menus are moved by sending the focused element the events the UI actions would
        // send: directions (repeating while held), submit and cancel
        private void NavigateUi(ControllerSetup setup)
        {
            NavigationMoveEvent.Direction direction = NavigationMoveEvent.Direction.None;
            (int button, NavigationMoveEvent.Direction direction)[] directions =
            {
                (setup.NavigateUp, NavigationMoveEvent.Direction.Up),
                (setup.NavigateDown, NavigationMoveEvent.Direction.Down),
                (setup.NavigateLeft, NavigationMoveEvent.Direction.Left),
                (setup.NavigateRight, NavigationMoveEvent.Direction.Right),
            };
            foreach ((int button, NavigationMoveEvent.Direction candidate) in directions)
            {
                if (!Controllers.IsHeld(button))
                {
                    continue;
                }

                if (Controllers.WasPressed(button))
                {
                    _nextRepeat = Time.unscaledTime + setup.RepeatDelay;
                }
                else if (Time.unscaledTime >= _nextRepeat)
                {
                    _nextRepeat = Time.unscaledTime + setup.RepeatInterval;
                }
                else
                {
                    break;
                }

                direction = candidate;
                break;
            }

            bool submit = Controllers.WasPressed(setup.Submit);
            bool cancel = Controllers.WasPressed(setup.Cancel);
            if (direction == NavigationMoveEvent.Direction.None && !submit && !cancel)
            {
                return;
            }

            VisualElement focused = FocusedElement();
            if (focused == null)
            {
                return;
            }

            if (direction != NavigationMoveEvent.Direction.None)
            {
                using (NavigationMoveEvent moveEvent = NavigationMoveEvent.GetPooled(direction))
                {
                    moveEvent.target = focused;
                    focused.SendEvent(moveEvent);
                }
            }

            if (submit)
            {
                using (NavigationSubmitEvent submitEvent = NavigationSubmitEvent.GetPooled())
                {
                    submitEvent.target = focused;
                    focused.SendEvent(submitEvent);
                }
            }

            if (cancel)
            {
                using (NavigationCancelEvent cancelEvent = NavigationCancelEvent.GetPooled())
                {
                    cancelEvent.target = focused;
                    focused.SendEvent(cancelEvent);
                }
            }
        }

        private static VisualElement FocusedElement()
        {
            foreach (UIDocument document in FindObjectsByType<UIDocument>(FindObjectsSortMode.None))
            {
                if (document.rootVisualElement?.panel?.focusController?.focusedElement is VisualElement element &&
                    element.resolvedStyle.display != DisplayStyle.None)
                {
                    return element;
                }
            }

            return null;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                Controllers.Vibrate(0f, 0f);
            }
        }
    }
}
