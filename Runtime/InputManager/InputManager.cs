using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SimpleTools.Input
{
    [AddComponentMenu("Simple Tools/Input Manager")]
    public class InputManager : MonoBehaviour
    {
        [SerializeField] private InputActionAsset _inputActions;
        [SerializeField] private List<InputActionReference> _actions = new List<InputActionReference>();
        [SerializeField] private bool _enableOnStart = true;
        [SerializeField] private bool _dontDestroyOnLoad = true;
        [SerializeField] private string _bindingsFileName = "input-bindings.json";
        [SerializeField] private bool _loadBindingOverridesOnAwake = true;
        [SerializeField] private bool _saveBindingOverridesAutomatically = true;

        private readonly HashSet<InputAction> _subscribedActions = new HashSet<InputAction>();
        private string _bindingsFilePath;

        public static InputManager Instance { get; private set; }
        public InputActionAsset InputActions => _inputActions;
        public string BindingsFilePath => _bindingsFilePath;

        public event Action<InputAction.CallbackContext> OnActionStarted;
        public event Action<InputAction.CallbackContext> OnActionPerformed;
        public event Action<InputAction.CallbackContext> OnActionCanceled;
        public event Action OnBindingsLoaded;
        public event Action OnBindingsSaved;
        public event Action<Exception> OnBindingsPersistenceError;

        private void Awake()
        {
            if (Instance != null && !ReferenceEquals(Instance, this))
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _bindingsFilePath = Path.Combine(Application.persistentDataPath, _bindingsFileName);

            if (_loadBindingOverridesOnAwake)
            {
                LoadBindingOverrides();
            }

            SubscribeToActions();

            if (_dontDestroyOnLoad)
            {
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
        }

        private void Start()
        {
            if (_enableOnStart)
            {
                EnableInput();
            }
        }

        private void OnDestroy()
        {
            UnsubscribeFromActions();

            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        public void EnableInput()
        {
            if (_inputActions != null)
            {
                _inputActions.Enable();
                return;
            }

            for (int index = 0; index < _actions.Count; index++)
            {
                _actions[index]?.action?.Enable();
            }
        }

        public void DisableInput()
        {
            if (_inputActions != null)
            {
                _inputActions.Disable();
                return;
            }

            for (int index = 0; index < _actions.Count; index++)
            {
                _actions[index]?.action?.Disable();
            }
        }

        public InputAction FindAction(string actionName)
        {
            if (_inputActions == null || string.IsNullOrWhiteSpace(actionName))
            {
                return null;
            }

            return _inputActions.FindAction(actionName, throwIfNotFound: false);
        }

        public void SaveBindingOverrides()
        {
            if (_inputActions == null)
            {
                return;
            }

            try
            {
                string directoryPath = Path.GetDirectoryName(_bindingsFilePath);
                Directory.CreateDirectory(directoryPath);
                File.WriteAllText(_bindingsFilePath, _inputActions.SaveBindingOverridesAsJson());
                OnBindingsSaved?.Invoke();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                NotifyPersistenceError(exception);
            }
        }

        public void LoadBindingOverrides()
        {
            if (_inputActions == null || !File.Exists(_bindingsFilePath))
            {
                return;
            }

            try
            {
                _inputActions.LoadBindingOverridesFromJson(File.ReadAllText(_bindingsFilePath));
                OnBindingsLoaded?.Invoke();
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                NotifyPersistenceError(exception);
            }
        }

        public void ResetBindingOverrides()
        {
            if (_inputActions == null)
            {
                return;
            }

            _inputActions.RemoveAllBindingOverrides();
            SaveBindingOverrides();
        }

        private void SubscribeToActions()
        {
            if (_inputActions != null)
            {
                for (int mapIndex = 0; mapIndex < _inputActions.actionMaps.Count; mapIndex++)
                {
                    InputActionMap actionMap = _inputActions.actionMaps[mapIndex];

                    for (int actionIndex = 0; actionIndex < actionMap.actions.Count; actionIndex++)
                    {
                        SubscribeToAction(actionMap.actions[actionIndex]);
                    }
                }
            }

            for (int index = 0; index < _actions.Count; index++)
            {
                SubscribeToAction(_actions[index]?.action);
            }
        }

        private void SubscribeToAction(InputAction action)
        {
            if (action == null || !_subscribedActions.Add(action))
            {
                return;
            }

            action.started += HandleActionStarted;
            action.performed += HandleActionPerformed;
            action.canceled += HandleActionCanceled;
        }

        private void UnsubscribeFromActions()
        {
            foreach (InputAction action in _subscribedActions)
            {
                action.started -= HandleActionStarted;
                action.performed -= HandleActionPerformed;
                action.canceled -= HandleActionCanceled;
            }

            _subscribedActions.Clear();
        }

        private void HandleActionStarted(InputAction.CallbackContext context)
        {
            OnActionStarted?.Invoke(context);
        }

        private void HandleActionPerformed(InputAction.CallbackContext context)
        {
            OnActionPerformed?.Invoke(context);
        }

        private void HandleActionCanceled(InputAction.CallbackContext context)
        {
            OnActionCanceled?.Invoke(context);
        }

        private void NotifyPersistenceError(Exception exception)
        {
            Debug.LogException(exception);
            OnBindingsPersistenceError?.Invoke(exception);
        }
    }
}
