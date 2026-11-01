# SimpleTools for Unity

Small, independent Unity modules distributed as a UPM package: game state, JSON settings, localization, audio and input. Each module is usable on its own and communicates through plain C# APIs and events.

SimpleTools grows alongside real games: generic pieces written for a game end up here so the next project can reuse them.

## Contents

- **GameStateManager\<TState\>** - Generic state management based on `Enum`.
- **SettingsManager\<TSettings\>** - User settings serialized to JSON.
- **LocalizationManager** - Translations with fallback and TextMeshPro support.
- **AudioManager** - Music, SFX, mixer routing, pooling, crossfade and 3D emitters.
- **InputManager & InputRemapper** - Unity Input System wrapper, abstract events and persistent rebinding.

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
  SimpleTools.Runtime.asmdef
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

## License

Released into the public domain under [The Unlicense](LICENSE): use it, change it and ship it however you like, with no attribution required.
