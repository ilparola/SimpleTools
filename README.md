# SimpleTools for Unity

Small, independent Unity modules distributed as a UPM package: game state, JSON settings, localization, audio and input. Each module is usable on its own and communicates through plain C# APIs and events.

SimpleTools grows alongside real games: generic pieces written for a game end up here so the next project can reuse them.

## Contents

- **GameStateManager\<TState\>** - Generic state management based on `Enum`.
- **SettingsManager\<TSettings\>** - User settings serialized to JSON.
- **LocalizationManager** - Translations with fallback and TextMeshPro support.
- **AudioManager** - Music, SFX, mixer routing, pooling, crossfade and 3D emitters.
- **InputManager & InputRemapper** - Unity Input System wrapper, abstract events and persistent rebinding.
- **Controllers** - One controller API over several sources (Input System gamepads, Linux SDL joysticks, Steam Input), controller-first device priority, UI Toolkit menu navigation, rumble pulses and button prompt icons.
- **Steam** (optional) - Steamworks.NET startup and a Steam Input controller source with trackpads. Compiled only when Steamworks.NET is installed.
- **UI** - UI Toolkit elements: `RadarChart` (spider chart for stats).

## Installation

### From Git

Open `Window > Package Manager`, click `+`, choose `Add package from git URL...` and enter:

```text
https://github.com/ilparola/simpletools-unity.git
```

To pin a version, append a tag, for example `https://github.com/ilparola/simpletools-unity.git#v0.1.0`. You can also add it directly to `Packages/manifest.json`:

```json
"com.ilparola.simpletools": "https://github.com/ilparola/simpletools-unity.git#v0.1.0"
```

### From a local copy

Clone the repository, then choose `Add package from disk...` and select `package.json`. This is the way to go if you want to change the library while working on a game.

The package declares `com.unity.inputsystem` automatically. TextMeshPro is required by the localization module.

## Structure

```text
Runtime/
  AudioManager/
  GameStateManager/
  InputManager/
  LocalizationManager/
  SettingsManager/
  Controllers/
  Steam/                          (SimpleTools.Steam.asmdef, needs Steamworks.NET)
  UI/
  SimpleTools.Runtime.asmdef
Editor/
  Steam/                          (copies steam_appid.txt next to builds)
```

## GameStateManager\<TState\>

See [GameStateManager.cs](Runtime/GameStateManager/GameStateManager.cs).

Unity does not serialize a generic `MonoBehaviour` directly, so create a concrete subclass in your project:

```csharp
using SimpleTools.GameState;

public enum GameState
{
	MainMenu,
	Playing,
	Paused,
	GameOver
}

public class GameStateManager : GameStateManager<GameState>
{
}
```

Add `GameStateManager` to a GameObject and change state from other systems:

```csharp
using SimpleTools.GameState;
using UnityEngine;

public class PauseController : MonoBehaviour
{
	private void OnEnable()
	{
		GameStateManager.Instance.OnStateChanged += HandleStateChanged;
	}

	private void OnDisable()
	{
		if (GameStateManager.Instance != null)
		{
			GameStateManager.Instance.OnStateChanged -= HandleStateChanged;
		}
	}

	public void PauseGame()
	{
		GameStateManager.Instance.ChangeState(GameState.Paused);
	}

	private void HandleStateChanged(GameState previousState, GameState currentState)
	{
		Debug.Log($"State: {previousState} -> {currentState}");
	}
}
```

`IsInState`, `RevertToPreviousState`, `RegisterOnEnter` and `RegisterOnExit` are also available.

## SettingsManager\<TSettings\>

See [SettingsManager.cs](Runtime/SettingsManager/SettingsManager.cs). The type must be a `[Serializable]` class compatible with `JsonUtility`:

```csharp
using System;
using SimpleTools.Settings;
using UnityEngine;

[Serializable]
public class PlayerSettings
{
	public float MasterVolume = 1f;
	public string Language = "it";
}

public class SettingsBootstrap : MonoBehaviour
{
	private SettingsManager<PlayerSettings> _settingsManager;

	private void Awake()
	{
		_settingsManager = new SettingsManager<PlayerSettings>("player-settings.json");
		_settingsManager.LoadOrCreate();
		_settingsManager.OnSettingsSaved += HandleSettingsSaved;
	}

	public void SetMasterVolume(float volume)
	{
		_settingsManager.CurrentSettings.MasterVolume = volume;
		_settingsManager.Save();
	}

	private void HandleSettingsSaved(PlayerSettings settings)
	{
		Debug.Log($"Settings saved: {settings.Language}");
	}
}
```

The file is saved under `Application.persistentDataPath`. Available operations are `Load`, `LoadOrCreate`, `Save` and `Reset`, together with load, save, reset and error events.

## LocalizationManager

See [LocalizationTable.cs](Runtime/LocalizationManager/LocalizationTable.cs), [LocalizationManager.cs](Runtime/LocalizationManager/LocalizationManager.cs) and [LocalizedText.cs](Runtime/LocalizationManager/LocalizedText.cs).

1. Create `Assets > Create > Simple Tools > Localization Table`.
2. Add a key and translations using codes such as `en`, `it` or `fr`.
3. Add `LocalizationManager` to a GameObject and assign the table.
4. Add `LocalizedText` to a `TMP_Text` component and set its key.

```csharp
using SimpleTools.Localization;
using UnityEngine;

public class LanguageSelector : MonoBehaviour
{
	public void SelectItalian()
	{
		LocalizationManager.Instance.SetLanguage("it");
	}

	public void SelectEnglish()
	{
		LocalizationManager.Instance.SetLanguage("en");
	}

	private void ShowScore(int score)
	{
		string text = LocalizationManager.Instance.GetText("score", score);
		Debug.Log(text);
	}
}
```

For a `score` key, use texts such as `Score: {0}` and `Points: {0}`. If the current language is missing, the manager tries the fallback language; if that is missing too, it returns the key.

## AudioManager

See [AudioManager.cs](Runtime/AudioManager/AudioManager.cs), [PooledAudioSource.cs](Runtime/AudioManager/PooledAudioSource.cs) and [AudioEmitter.cs](Runtime/AudioManager/AudioEmitter.cs).

Add `AudioManager` to a GameObject, optionally assign the `AudioMixerGroup` fields, and configure `Initial Pool Size` and `Maximum Pool Size`.

```csharp
using SimpleTools.Audio;
using UnityEngine;

public class AudioExample : MonoBehaviour
{
	[SerializeField] private AudioClip _menuMusic;
	[SerializeField] private AudioClip _buttonSfx;

	private void Start()
	{
		AudioManager.Instance.PlayMusic(_menuMusic, fadeDuration: 1f);
	}

	public void PlayButtonSound()
	{
		AudioManager.Instance.PlaySfx(_buttonSfx, volume: 0.8f);
	}

	public void SetMusicVolume(float volume)
	{
		AudioManager.Instance.SetMusicVolume(volume);
	}
}
```

Use `PlaySfxAtPosition` for a 3D positional effect. To associate a sound with a billiard ball, add `AudioEmitter` to the ball and enable `Play On Collision`:

```csharp
using SimpleTools.Audio;
using UnityEngine;

public class BilliardBallAudio : MonoBehaviour
{
	[SerializeField] private AudioEmitter _emitter;

	private void OnCollisionEnter(Collision collision)
	{
		_emitter.PlayCollision(collision);
	}
}
```

The sound is played by a pooled source at the contact point, while `PlaySfx` remains suitable for UI and non-spatial sounds.

## InputManager & InputRemapper

See [InputManager.cs](Runtime/InputManager/InputManager.cs) and [InputRemapper.cs](Runtime/InputManager/InputRemapper.cs). Assign an `InputActionAsset` to `InputManager`; all its actions are forwarded through events. Binding overrides are saved under `Application.persistentDataPath`.

```csharp
using SimpleTools.Input;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputExample : MonoBehaviour
{
	[SerializeField] private InputManager _inputManager;
	[SerializeField] private InputRemapper _inputRemapper;
	[SerializeField] private InputActionReference _jumpAction;

	private void OnEnable()
	{
		_inputManager.OnActionPerformed += HandleActionPerformed;
	}

	private void OnDisable()
	{
		_inputManager.OnActionPerformed -= HandleActionPerformed;
	}

	private void HandleActionPerformed(InputAction.CallbackContext context)
	{
		if (context.action == _jumpAction.action)
		{
			Debug.Log("Jump");
		}
	}

	public void RemapJump()
	{
		_inputRemapper.StartRebinding(_jumpAction, 0, success =>
		{
			Debug.Log(success ? "Binding updated" : "Rebinding canceled");
		});
	}
}
```

Rebinding supports timeouts, cancellation with `Escape`, mouse and pointer exclusion, and resetting an individual binding. For a composite binding such as `WASD`, use the index of the individual part (`Up`, `Down`, `Left` or `Right`).

## Controllers

See [Controllers.cs](Runtime/Controllers/Controllers.cs). The game names its buttons with its own ids (usually an enum) and ties them to physical gamepad buttons; every source is read and merged once per frame, before other scripts.

```csharp
using SimpleTools.Controllers;
using UnityEngine;
using UnityEngine.InputSystem.LowLevel;

public enum PadCommand { Jump, Back, Pause }

public static class GameControllers
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Configure()
    {
        Controllers.Configure(new ControllerSetup(3,
            ((int)PadCommand.Jump, GamepadButton.South),
            ((int)PadCommand.Back, GamepadButton.East),
            ((int)PadCommand.Pause, GamepadButton.Start))
        {
            Submit = (int)PadCommand.Jump,
            Cancel = (int)PadCommand.Back
        });
        Haptics.IsAllowed = () => true; // hook it to a "Vibration" option
    }
}

public class Player : MonoBehaviour
{
    private void Update()
    {
        if (Controllers.WasPressed((int)PadCommand.Jump))
        {
            Haptics.Pulse(0.3f, 0.6f, 0.08f);
        }

        Vector2 move = Controllers.LeftStick;
    }
}
```

- **Sources**: the Input System gamepad (Windows, macOS); the Linux SDL joystick, which is how the Input System sees many controllers on Linux, including the Steam Deck when Steam emulates an Xbox pad; and any source the game adds with `Controllers.AddSource`, which takes priority (for example Steam Input). Implement `IControllerSource` for new ones.
- **Device priority** (`ActiveDevice`): while a controller is connected, keyboard and mouse are disabled and the pointer is hidden; they come back when it is unplugged or the game quits. Set `ControllerTakesPriority = false` to keep both. `ActiveDevice.Changed` fires on connect, disconnect and family change, to swap prompts.
- **Menus**: declare open menus with `Controllers.SetMenuOpen(owner, true)` (a paused game counts too). For sources the Input System UI actions do not see, the focused UI Toolkit element receives navigation, submit and cancel events.
- **Rumble** (`Haptics.Pulse`): pulses fade out and add up; they play only on a controller and stop while paused.
- **Icons** (`ControllerIcons.Get("south")`): loaded from `Resources/<Root>/<family folder>/`, for example with the free Kenney Input Prompts.
- **Diagnostics**: `Controllers.Diagnostics` and `Controllers.HeldSummary`; every change of controller state is also logged.

## Steam

See [SteamService.cs](Runtime/Steam/SteamService.cs) and [SteamInputControllerSource.cs](Runtime/Steam/SteamInputControllerSource.cs). Install [Steamworks.NET](https://github.com/rlabrecque/Steamworks.NET) and the `SimpleTools.Steam` assembly compiles on its own. Put `steam_appid.txt` in the project root while the game has no Steam release; it is copied next to every build.

```csharp
if (SteamService.Initialize())
{
    SteamInputControllerSource steam = SteamInputControllerSource.Create(new SteamInputSetup
    {
        ManifestPath = "SteamInput/game_actions.vdf", // in StreamingAssets
        GameActionSet = "Game",
        MenuActionSet = "Menu",
        Buttons = new[]
        {
            ((int)PadCommand.Jump, "jump", "confirm"),
            ((int)PadCommand.Back, (string)null, "back"),
            ((int)PadCommand.Pause, "pause", "close_pause"),
        },
        GameAnalog = new SteamAnalogActions { LeftStick = "move", RightTrackpad = "aim" },
        MenuAnalog = new SteamAnalogActions { RightStick = "scroll" }
    });
    if (steam != null)
    {
        Controllers.AddSource(steam);
    }
}
```

Steam Input switches between the two action sets with `Controllers.InMenu`, reports the controller type (Steam Deck, PlayStation, Xbox) and exposes trackpads as mouse-like movement.

## UI

`RadarChart` is a UI Toolkit element (`SimpleTools.UI`) that draws a radar ("spider") chart: one axis per value, the first at the top and the others clockwise, values from 0 to 1, grid rings and a filled polygon. Axis labels are child `Label`s with the class `radar-chart__label`, placed just outside the chart.

```xml
<ui:UXML xmlns:ui="UnityEngine.UIElements" xmlns:simple="SimpleTools.UI">
    <simple:RadarChart name="stats" class="stats-chart"/>
</ui:UXML>
```

```csharp
var chart = root.Q<RadarChart>("stats");
chart.SetLabels(new[] { "Speed", "Power", "Skill", "Defense", "Luck" });
chart.SetValues(new[] { 0.8f, 0.6f, 0.9f, 0.4f, 0.5f });
```

Style it with USS custom properties: `--radar-grid-color`, `--radar-fill-color`, `--radar-line-color`, `--radar-rings` (int), `--radar-line-width` and `--radar-label-space` (pixels reserved around the chart for the labels).

## Event-Driven Integration

The modules do not depend on one another. A system can react to events without knowing the implementation of the module that raised them:

```csharp
using SimpleTools.Audio;
using SimpleTools.GameState;
using UnityEngine;

public class GameAudioHooks : MonoBehaviour
{
	[SerializeField] private AudioClip _pauseMusic;

	private void OnEnable()
	{
		GameStateManager.Instance.OnStateEntered += HandleStateEntered;
	}

	private void OnDisable()
	{
		if (GameStateManager.Instance != null)
		{
			GameStateManager.Instance.OnStateEntered -= HandleStateEntered;
		}
	}

	private void HandleStateEntered(GameState state)
	{
		if (state == GameState.Paused)
		{
			AudioManager.Instance.PlayMusic(_pauseMusic);
		}
	}
}
```

## Requirements

- Unity `2021.3` or later.
- Input System (installed automatically) for `InputManager` and `InputRemapper`.
- TextMeshPro for `LocalizedText`.
- Steamworks.NET for the optional `Steam` module.

## License

Released into the public domain under [The Unlicense](LICENSE): use it, change it and ship it however you like, with no attribution required.

## Replay and Media

- `SimpleTools.Replay.TransformTrack`: recorded poses of an object, with unchanged samples dropped and interpolated sampling.
- `SimpleTools.Media.GifEncoder` and `GifPalette`: streaming animated GIF writer in plain C#, no dependencies.
- `SimpleTools.Media.AudioMixdown`: offline mix of timed sounds into a stereo track, and a WAV writer.
- `SimpleTools.Media.FfmpegVideoEncoder`: pipes raw frames to a separate ffmpeg executable and picks the first H.264 encoder that works on the machine. Ship an LGPL build only. `FfmpegBuildCopy` copies `Strumenti/ffmpeg/<mac|windows|linux>` into each build.
