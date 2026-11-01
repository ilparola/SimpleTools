using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SimpleTools.Controllers
{
    // Controller rumble made of short pulses. Each pulse fades out linearly; overlapping pulses add up to
    // the motors' maximum. Plays only while the player is on a controller and IsAllowed agrees (hook it to
    // a "Vibration" option). Motors stop while the game is paused (Time.timeScale = 0).
    public static class Haptics
    {
        private const int MaxPulses = 8;

        // For example () => !settings.disableVibration
        public static Func<bool> IsAllowed { get; set; }

        private static HapticsRunner _runner;

        // low = heavy motor (low frequency), high = light motor (high frequency), both from 0 to 1
        public static void Pulse(float low, float high, float duration)
        {
            if (!ActiveDevice.IsController || (IsAllowed != null && !IsAllowed()))
            {
                return;
            }

            if (_runner == null)
            {
                GameObject host = new GameObject("SimpleTools.Haptics");
                host.hideFlags = HideFlags.HideInHierarchy;
                UnityEngine.Object.DontDestroyOnLoad(host);
                _runner = host.AddComponent<HapticsRunner>();
            }

            _runner.Add(low, high, duration);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            IsAllowed = null;
            _runner = null;
        }

        [AddComponentMenu("")]
        private sealed class HapticsRunner : MonoBehaviour
        {
            private struct Entry
            {
                public float low;
                public float high;
                public float start;
                public float duration;
            }

            private readonly Entry[] _pulses = new Entry[MaxPulses];
            private int _next;
            private float _sentLow;
            private float _sentHigh;

            public void Add(float low, float high, float duration)
            {
                _pulses[_next] = new Entry { low = low, high = high, start = Time.unscaledTime, duration = duration };
                _next = (_next + 1) % MaxPulses;
            }

            private void Update()
            {
                float low = 0f;
                float high = 0f;
                if (Time.timeScale > 0f)
                {
                    float now = Time.unscaledTime;
                    foreach (Entry pulse in _pulses)
                    {
                        float left = 1f - (now - pulse.start) / Mathf.Max(pulse.duration, 0.001f);
                        if (pulse.duration > 0f && left > 0f)
                        {
                            low += pulse.low * left;
                            high += pulse.high * left;
                        }
                    }
                }

                Send(Mathf.Clamp01(low), Mathf.Clamp01(high));
            }

            private void Send(float low, float high)
            {
                if (Mathf.Approximately(low, _sentLow) && Mathf.Approximately(high, _sentHigh))
                {
                    return;
                }

                _sentLow = low;
                _sentHigh = high;
                Controllers.Vibrate(low, high);
            }

            private void OnApplicationFocus(bool hasFocus)
            {
                if (!hasFocus)
                {
                    Send(0f, 0f);
                }
            }

            private void OnDestroy()
            {
                InputSystem.ResetHaptics();
            }
        }
    }
}
