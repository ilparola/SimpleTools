using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace SimpleTools.Audio
{
    public class AudioManager : MonoBehaviour
    {
        [Header("Audio Mixer")]
        [SerializeField] private AudioMixerGroup _musicOutputGroup;
        [SerializeField] private AudioMixerGroup _sfxOutputGroup;

        [Header("Music")]
        [SerializeField] private AudioClip _initialMusic;
        [SerializeField, Range(0f, 1f)] private float _musicVolume = 1f;
        [SerializeField] private bool _playInitialMusic = true;

        [Header("SFX Pool")]
        [SerializeField, Min(1)] private int _initialPoolSize = 10;
        [SerializeField, Min(1)] private int _maximumPoolSize = 30;
        [SerializeField, Range(0f, 1f)] private float _sfxVolume = 1f;

        private readonly List<PooledAudioSource> _sfxPool = new List<PooledAudioSource>();
        private AudioSource _musicSourceA;
        private AudioSource _musicSourceB;
        private AudioSource _activeMusicSource;
        private Coroutine _crossfadeCoroutine;

        public static AudioManager Instance { get; private set; }
        public AudioClip CurrentMusic { get; private set; }
        public float MusicVolume => _musicVolume;
        public float SfxVolume => _sfxVolume;

        public event Action<AudioClip> OnMusicChanged;
        public event Action<AudioClip> OnSfxPlayed;

        private void Awake()
        {
            if (Instance != null && !ReferenceEquals(Instance, this))
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            _initialPoolSize = Mathf.Max(1, _initialPoolSize);
            _maximumPoolSize = Mathf.Max(_initialPoolSize, _maximumPoolSize);
            CreateMusicSources();
            PrewarmPool();
            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            if (_playInitialMusic && _initialMusic != null)
            {
                PlayMusic(_initialMusic, 0f);
            }
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        public void PlayMusic(AudioClip clip, float fadeDuration = 1f, bool loop = true)
        {
            if (clip == null)
            {
                return;
            }

            if (_activeMusicSource != null && CurrentMusic == clip)
            {
                _activeMusicSource.loop = loop;
                return;
            }

            AudioSource nextSource = GetInactiveMusicSource();
            nextSource.clip = clip;
            nextSource.loop = loop;
            nextSource.outputAudioMixerGroup = _musicOutputGroup;
            nextSource.volume = 0f;
            nextSource.Play();

            if (_crossfadeCoroutine != null)
            {
                StopCoroutine(_crossfadeCoroutine);
            }

            _crossfadeCoroutine = StartCoroutine(CrossfadeMusic(nextSource, Mathf.Max(0f, fadeDuration)));
            CurrentMusic = clip;
            OnMusicChanged?.Invoke(clip);
        }

        public void StopMusic(float fadeDuration = 1f)
        {
            if (_activeMusicSource == null)
            {
                return;
            }

            if (_crossfadeCoroutine != null)
            {
                StopCoroutine(_crossfadeCoroutine);
            }

            _crossfadeCoroutine = StartCoroutine(FadeOutMusic(_activeMusicSource, Mathf.Max(0f, fadeDuration)));
            CurrentMusic = null;
            OnMusicChanged?.Invoke(null);
        }

        public void PlaySfx(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            PlaySfxAtPosition(clip, Vector3.zero, volume, pitch, false);
        }

        public void PlaySfxAtPosition(AudioClip clip, Vector3 position, float volume = 1f, float pitch = 1f, bool spatial = true)
        {
            if (clip == null)
            {
                return;
            }

            PooledAudioSource pooledSource = GetAvailableSfxSource();

            if (pooledSource == null)
            {
                return;
            }

            pooledSource.transform.position = position;
            AudioSource audioSource = pooledSource.GetComponent<AudioSource>();
            audioSource.spatialBlend = spatial ? 1f : 0f;
            pooledSource.Play(clip, Mathf.Clamp01(volume) * _sfxVolume, Mathf.Max(0.01f, pitch), _sfxOutputGroup);
            OnSfxPlayed?.Invoke(clip);
        }

        public void StopAllSfx()
        {
            for (int index = 0; index < _sfxPool.Count; index++)
            {
                if (!_sfxPool[index].IsAvailable)
                {
                    _sfxPool[index].Stop();
                }
            }
        }

        public void SetMusicVolume(float volume)
        {
            _musicVolume = Mathf.Clamp01(volume);

            if (_activeMusicSource != null)
            {
                _activeMusicSource.volume = _musicVolume;
            }
        }

        public void SetSfxVolume(float volume)
        {
            _sfxVolume = Mathf.Clamp01(volume);
        }

        private void CreateMusicSources()
        {
            _musicSourceA = CreateAudioSource("Music A");
            _musicSourceB = CreateAudioSource("Music B");
        }

        private AudioSource CreateAudioSource(string sourceName)
        {
            GameObject sourceObject = new GameObject(sourceName);
            sourceObject.transform.SetParent(transform);
            AudioSource source = sourceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.volume = 0f;
            return source;
        }

        private void PrewarmPool()
        {
            for (int index = 0; index < _initialPoolSize; index++)
            {
                CreatePooledSource(index);
            }
        }

        private PooledAudioSource CreatePooledSource(int index)
        {
            GameObject sourceObject = new GameObject($"SFX Pool {index + 1}");
            sourceObject.transform.SetParent(transform);
            AudioSource audioSource = sourceObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            PooledAudioSource pooledSource = sourceObject.AddComponent<PooledAudioSource>();
            pooledSource.Initialize(HandleSourceReleased);
            _sfxPool.Add(pooledSource);
            return pooledSource;
        }

        private PooledAudioSource GetAvailableSfxSource()
        {
            for (int index = 0; index < _sfxPool.Count; index++)
            {
                if (_sfxPool[index].IsAvailable)
                {
                    return _sfxPool[index];
                }
            }

            return _sfxPool.Count < _maximumPoolSize
                ? CreatePooledSource(_sfxPool.Count)
                : null;
        }

        private void HandleSourceReleased(PooledAudioSource source)
        {
            source.transform.localPosition = Vector3.zero;
        }

        private AudioSource GetInactiveMusicSource()
        {
            return ReferenceEquals(_activeMusicSource, _musicSourceA) ? _musicSourceB : _musicSourceA;
        }

        private IEnumerator CrossfadeMusic(AudioSource nextSource, float duration)
        {
            AudioSource previousSource = _activeMusicSource;
            _activeMusicSource = nextSource;

            if (duration <= 0f)
            {
                if (previousSource != null)
                {
                    previousSource.Stop();
                    previousSource.volume = 0f;
                }

                nextSource.volume = _musicVolume;
                _crossfadeCoroutine = null;
                yield break;
            }

            float elapsed = 0f;
            float previousVolume = previousSource == null ? 0f : previousSource.volume;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);

                if (previousSource != null)
                {
                    previousSource.volume = Mathf.Lerp(previousVolume, 0f, progress);
                }

                nextSource.volume = Mathf.Lerp(0f, _musicVolume, progress);
                yield return null;
            }

            if (previousSource != null)
            {
                previousSource.Stop();
                previousSource.volume = 0f;
            }

            nextSource.volume = _musicVolume;
            _crossfadeCoroutine = null;
        }

        private IEnumerator FadeOutMusic(AudioSource source, float duration)
        {
            float initialVolume = source.volume;

            if (duration <= 0f)
            {
                source.Stop();
                source.volume = 0f;
                _activeMusicSource = null;
                _crossfadeCoroutine = null;
                yield break;
            }

            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                source.volume = Mathf.Lerp(initialVolume, 0f, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            source.Stop();
            source.volume = 0f;
            _activeMusicSource = null;
            _crossfadeCoroutine = null;
        }
    }
}
