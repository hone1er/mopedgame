using System;
using UnityEngine;

namespace MopedSim.App
{
    /// <summary>
    /// Top-level gameplay session modes.
    /// </summary>
    public enum GameSessionState
    {
        Garage = 0,
        Ride = 1,
        RoadsideRepair = 2,
        Tow = 3
    }

    /// <summary>
    /// Coordinates valid transitions between Garage, Ride, RoadsideRepair, and Tow states.
    /// </summary>
    public sealed class GameSessionStateMachine : MonoBehaviour
    {
        [SerializeField] private GameSessionState _initialState = GameSessionState.Garage;

        private GameSessionState _currentState;

        public event Action<GameSessionState, GameSessionState> StateChanged;

        public GameSessionState CurrentState
        {
            get { return _currentState; }
        }

        private void Awake()
        {
            _currentState = _initialState;
        }

        public bool CanTransitionTo(GameSessionState targetState)
        {
            if (targetState == _currentState)
            {
                return true;
            }

            switch (_currentState)
            {
                case GameSessionState.Garage:
                    return targetState == GameSessionState.Ride;
                case GameSessionState.Ride:
                    return targetState == GameSessionState.Garage ||
                           targetState == GameSessionState.RoadsideRepair ||
                           targetState == GameSessionState.Tow;
                case GameSessionState.RoadsideRepair:
                    return targetState == GameSessionState.Ride ||
                           targetState == GameSessionState.Tow ||
                           targetState == GameSessionState.Garage;
                case GameSessionState.Tow:
                    return targetState == GameSessionState.Garage;
                default:
                    return false;
            }
        }

        public bool TryTransitionTo(GameSessionState targetState)
        {
            if (!CanTransitionTo(targetState))
            {
                return false;
            }

            if (targetState == _currentState)
            {
                return true;
            }

            GameSessionState previous = _currentState;
            _currentState = targetState;

            if (StateChanged != null)
            {
                StateChanged.Invoke(previous, _currentState);
            }

            return true;
        }

        public void ForceState(GameSessionState targetState)
        {
            if (targetState == _currentState)
            {
                return;
            }

            GameSessionState previous = _currentState;
            _currentState = targetState;
            if (StateChanged != null)
            {
                StateChanged.Invoke(previous, _currentState);
            }
        }
    }
}
