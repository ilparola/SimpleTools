using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimpleTools.GameState
{
    /// <summary>
    /// Gestore generico di stati di gioco basato su Enum.
    /// Crea una sottoclasse concreta non generica per usarlo come componente in scena,
    /// es: <c>public class GameStateManager : GameStateManager&lt;MyGameState&gt; { }</c>.
    /// </summary>
    /// <typeparam name="TState">Enum che rappresenta gli stati di gioco.</typeparam>
    public abstract class GameStateManager<TState> : MonoBehaviour where TState : struct, Enum
    {
        [SerializeField] private TState _initialState;
        [SerializeField] private bool _dontDestroyOnLoad = true;

        private readonly Dictionary<TState, Action> _onStateEnterCallbacks = new Dictionary<TState, Action>();
        private readonly Dictionary<TState, Action> _onStateExitCallbacks = new Dictionary<TState, Action>();

        private bool _isInitialized;

        /// <summary>Istanza singleton attiva di questo manager.</summary>
        public static GameStateManager<TState> Instance { get; private set; }

        /// <summary>Stato di gioco corrente.</summary>
        public TState CurrentState { get; private set; }

        /// <summary>Stato di gioco precedente rispetto a quello corrente.</summary>
        public TState PreviousState { get; private set; }

        /// <summary>Invocato dopo un cambio stato, con (statoPrecedente, statoNuovo).</summary>
        public event Action<TState, TState> OnStateChanged;

        /// <summary>Invocato quando si entra in uno stato.</summary>
        public event Action<TState> OnStateEntered;

        /// <summary>Invocato quando si esce da uno stato.</summary>
        public event Action<TState> OnStateExited;

        protected virtual void Awake()
        {
            if (Instance != null && !ReferenceEquals(Instance, this))
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            if (_dontDestroyOnLoad)
            {
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
        }

        protected virtual void Start()
        {
            ChangeState(_initialState, forceReenter: true);
        }

        protected virtual void OnDestroy()
        {
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        /// <summary>
        /// Cambia lo stato corrente. Se lo stato richiesto coincide con quello attuale
        /// e <paramref name="forceReenter"/> è false, non succede nulla.
        /// </summary>
        public void ChangeState(TState newState, bool forceReenter = false)
        {
            if (_isInitialized && !forceReenter && EqualityComparer<TState>.Default.Equals(CurrentState, newState))
            {
                return;
            }

            TState previous = CurrentState;

            if (_isInitialized)
            {
                RaiseExit(previous);
            }

            PreviousState = previous;
            CurrentState = newState;
            _isInitialized = true;

            RaiseEnter(newState);
            OnStateChanged?.Invoke(previous, newState);
        }

        /// <summary>Torna allo stato precedente al corrente.</summary>
        public void RevertToPreviousState()
        {
            ChangeState(PreviousState);
        }

        /// <summary>Verifica se lo stato corrente è quello indicato.</summary>
        public bool IsInState(TState state)
        {
            return EqualityComparer<TState>.Default.Equals(CurrentState, state);
        }

        /// <summary>Registra un callback invocato ogni volta che si entra nello stato indicato.</summary>
        public void RegisterOnEnter(TState state, Action callback)
        {
            if (_onStateEnterCallbacks.TryGetValue(state, out var existing))
            {
                _onStateEnterCallbacks[state] = existing + callback;
            }
            else
            {
                _onStateEnterCallbacks[state] = callback;
            }
        }

        /// <summary>Rimuove un callback precedentemente registrato con <see cref="RegisterOnEnter"/>.</summary>
        public void UnregisterOnEnter(TState state, Action callback)
        {
            if (_onStateEnterCallbacks.TryGetValue(state, out var existing))
            {
                _onStateEnterCallbacks[state] = existing - callback;
            }
        }

        /// <summary>Registra un callback invocato ogni volta che si esce dallo stato indicato.</summary>
        public void RegisterOnExit(TState state, Action callback)
        {
            if (_onStateExitCallbacks.TryGetValue(state, out var existing))
            {
                _onStateExitCallbacks[state] = existing + callback;
            }
            else
            {
                _onStateExitCallbacks[state] = callback;
            }
        }

        /// <summary>Rimuove un callback precedentemente registrato con <see cref="RegisterOnExit"/>.</summary>
        public void UnregisterOnExit(TState state, Action callback)
        {
            if (_onStateExitCallbacks.TryGetValue(state, out var existing))
            {
                _onStateExitCallbacks[state] = existing - callback;
            }
        }

        private void RaiseEnter(TState state)
        {
            if (_onStateEnterCallbacks.TryGetValue(state, out var callback))
            {
                callback?.Invoke();
            }

            OnStateEntered?.Invoke(state);
        }

        private void RaiseExit(TState state)
        {
            if (_onStateExitCallbacks.TryGetValue(state, out var callback))
            {
                callback?.Invoke();
            }

            OnStateExited?.Invoke(state);
        }
    }
}
