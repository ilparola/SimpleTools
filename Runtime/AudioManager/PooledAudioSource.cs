using System;
using UnityEngine;
using UnityEngine.Audio;

namespace SimpleTools.Audio
{
    [RequireComponent(typeof(AudioSource))]
    internal sealed class PooledAudioSource : MonoBehaviour
    {
        private AudioSource _audioSource;
        private Action<PooledAudioSource> _releaseCallback;

        public bool IsAvailable => !_audioSource.isPlaying;

        public void Initialize(Action<PooledAudioSource> releaseCallback)
        {
            _audioSource = GetComponent<AudioSource>();
            _releaseCallback = releaseCallback;
            _audioSource.playOnAwake = false;
            _audioSource.loop = false;
        }

        public void Play(AudioClip clip, float volume, float pitch, AudioMixerGroup outputGroup)
        {
            _audioSource.clip = clip;
            _audioSource.volume = volume;
            _audioSource.pitch = pitch;
            _audioSource.outputAudioMixerGroup = outputGroup;
            _audioSource.loop = false;
            _audioSource.Play();
        }

        public void Stop()
        {
            _audioSource.Stop();
            Release();
        }

        private void Update()
        {
            if (!_audioSource.isPlaying && _audioSource.clip != null)
            {
                Release();
            }
        }

        private void Release()
        {
            _audioSource.clip = null;
            _releaseCallback?.Invoke(this);
        }
    }
}
