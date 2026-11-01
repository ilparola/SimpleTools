using TMPro;
using UnityEngine;

namespace SimpleTools.Localization
{
    [RequireComponent(typeof(TMP_Text))]
    public class LocalizedText : MonoBehaviour
    {
        [SerializeField] private string _key;
        [SerializeField] private object[] _formatArguments;

        private TMP_Text _textComponent;
        private LocalizationManager _localizationManager;

        private void Awake()
        {
            _textComponent = GetComponent<TMP_Text>();
        }

        private void OnEnable()
        {
            SubscribeToManager();
            Refresh();
        }

        private void OnDisable()
        {
            if (_localizationManager != null)
            {
                _localizationManager.OnLanguageChanged -= HandleLanguageChanged;
                _localizationManager = null;
            }
        }

        public void SetKey(string key)
        {
            _key = key;
            Refresh();
        }

        public void SetFormatArguments(params object[] formatArguments)
        {
            _formatArguments = formatArguments;
            Refresh();
        }

        public void Refresh()
        {
            if (_textComponent == null)
            {
                return;
            }

            if (_localizationManager == null)
            {
                SubscribeToManager();
            }

            if (_localizationManager != null)
            {
                _textComponent.text = _localizationManager.GetText(_key, _formatArguments);
            }
        }

        private void SubscribeToManager()
        {
            if (_localizationManager == LocalizationManager.Instance)
            {
                return;
            }

            if (_localizationManager != null)
            {
                _localizationManager.OnLanguageChanged -= HandleLanguageChanged;
            }

            _localizationManager = LocalizationManager.Instance;

            if (_localizationManager != null)
            {
                _localizationManager.OnLanguageChanged += HandleLanguageChanged;
            }
        }

        private void HandleLanguageChanged(string languageCode)
        {
            Refresh();
        }
    }
}
