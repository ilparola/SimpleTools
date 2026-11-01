using UnityEngine;

namespace SimpleTools.Audio
{
    public class AudioEmitter : MonoBehaviour
    {
        [SerializeField] private AudioClip _clip;
        [SerializeField, Range(0f, 1f)] private float _volume = 1f;
        [SerializeField, Range(0.01f, 3f)] private float _pitch = 1f;
        [SerializeField] private bool _playOnCollision;

        public AudioClip Clip => _clip;
        public float Volume => _volume;
        public float Pitch => _pitch;

        private void OnCollisionEnter(Collision collision)
        {
            if (_playOnCollision)
            {
                PlayCollision(collision);
            }
        }

        public void Play()
        {
            if (_clip == null || AudioManager.Instance == null)
            {
                return;
            }

            AudioManager.Instance.PlaySfxAtPosition(_clip, transform.position, _volume, _pitch);
        }

        public void Play(AudioClip clip, float volume = 1f, float pitch = 1f)
        {
            if (clip == null || AudioManager.Instance == null)
            {
                return;
            }

            AudioManager.Instance.PlaySfxAtPosition(clip, transform.position, volume, pitch);
        }

        public void PlayCollision(Collision collision)
        {
            if (_clip == null || AudioManager.Instance == null)
            {
                return;
            }

            Vector3 position = collision != null && collision.contactCount > 0
                ? collision.GetContact(0).point
                : transform.position;

            AudioManager.Instance.PlaySfxAtPosition(_clip, position, _volume, _pitch);
        }
    }
}
