using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;

namespace SimpleTools.Input
{
    [AddComponentMenu("Simple Tools/Input Remapper")]
    public class InputRemapper : MonoBehaviour
    {
        [SerializeField] private InputManager _inputManager;
        [SerializeField] private float _timeout = 5f;
        [SerializeField] private bool _excludePointerAndMouse = false;

        private InputActionRebindingExtensions.RebindingOperation _rebindingOperation;

        public bool IsRebinding => _rebindingOperation != null;

        public event Action<InputAction, int> OnRebindingStarted;
        public event Action<InputAction, int, string> OnRebindingCompleted;
        public event Action OnRebindingCanceled;
        public event Action<InputAction, int, string> OnRebindingFailed;

        private void Awake()
        {
            if (_inputManager == null)
            {
                _inputManager = GetComponent<InputManager>();
            }
        }

        private void OnDestroy()
        {
            CancelRebinding();
        }

        public bool StartRebinding(string actionName, int bindingIndex, Action<bool> completed = null)
        {
            InputAction action = _inputManager == null ? null : _inputManager.FindAction(actionName);
            return StartRebinding(action, bindingIndex, completed);
        }

        public bool StartRebinding(InputActionReference actionReference, int bindingIndex, Action<bool> completed = null)
        {
            return StartRebinding(actionReference == null ? null : actionReference.action, bindingIndex, completed);
        }

        public bool StartRebinding(InputAction action, int bindingIndex, Action<bool> completed = null)
        {
            if (IsRebinding || action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
            {
                completed?.Invoke(false);
                return false;
            }

            if (action.bindings[bindingIndex].isComposite)
            {
                completed?.Invoke(false);
                return false;
            }

            CancelRebinding();
            bool wasEnabled = action.enabled;
            action.Disable();

            _rebindingOperation = action.PerformInteractiveRebinding(bindingIndex)
                .WithCancelingThrough("<Keyboard>/escape")
                .WithTimeout(Mathf.Max(0f, _timeout));

            if (_excludePointerAndMouse)
            {
                _rebindingOperation = _rebindingOperation.WithControlsExcluding("<Mouse>");
                _rebindingOperation = _rebindingOperation.WithControlsExcluding("<Pointer>");
            }

            _rebindingOperation
                .OnCancel(operation =>
                {
                    operation.Dispose();
                    _rebindingOperation = null;
                    RestoreActionState(action, wasEnabled);
                    OnRebindingCanceled?.Invoke();
                    completed?.Invoke(false);
                })
                .OnComplete(operation =>
                {
                    string bindingDisplayName = action.GetBindingDisplayString(bindingIndex);
                    operation.Dispose();
                    _rebindingOperation = null;
                    RestoreActionState(action, wasEnabled);

                    if (_inputManager != null)
                    {
                        _inputManager.SaveBindingOverrides();
                    }

                    OnRebindingCompleted?.Invoke(action, bindingIndex, bindingDisplayName);
                    completed?.Invoke(true);
                })
                .OnPotentialMatch(operation =>
                {
                    if (operation.selectedControl == null)
                    {
                        return;
                    }

                    string path = operation.selectedControl.path;
                    if (string.Equals(path, "<Keyboard>/escape", StringComparison.OrdinalIgnoreCase))
                    {
                        operation.Cancel();
                    }
                });

            OnRebindingStarted?.Invoke(action, bindingIndex);
            _rebindingOperation.Start();
            return true;
        }

        public void CancelRebinding()
        {
            if (_rebindingOperation != null)
            {
                _rebindingOperation.Cancel();
            }
        }

        public void ResetBinding(InputActionReference actionReference, int bindingIndex)
        {
            if (actionReference == null)
            {
                return;
            }

            ResetBinding(actionReference.action, bindingIndex);
        }

        public void ResetBinding(InputAction action, int bindingIndex)
        {
            if (action == null || bindingIndex < 0 || bindingIndex >= action.bindings.Count)
            {
                return;
            }

            action.RemoveBindingOverride(bindingIndex);
            _inputManager?.SaveBindingOverrides();
        }

        public string GetBindingDisplayString(InputActionReference actionReference, int bindingIndex)
        {
            if (actionReference == null || actionReference.action == null || bindingIndex < 0 || bindingIndex >= actionReference.action.bindings.Count)
            {
                return string.Empty;
            }

            return actionReference.action.GetBindingDisplayString(bindingIndex);
        }

        private static void RestoreActionState(InputAction action, bool wasEnabled)
        {
            if (wasEnabled)
            {
                action.Enable();
            }
        }
    }
}
