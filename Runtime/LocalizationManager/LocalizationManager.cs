using System;
using UnityEngine;

namespace SimpleTools.Localization
{
    public class LocalizationManager : MonoBehaviour
    {
        [SerializeField] private LocalizationTable _table;
        [SerializeField] private string _initialLanguageCode = "en";
        [SerializeField] private string _fallbackLanguageCode = "en";
        [SerializeField] private bool _dontDestroyOnLoad = true;

        public static LocalizationManager Instance { get; private set; }

        public string CurrentLanguageCode { get; private set; }
        public string FallbackLanguageCode => _fallbackLanguageCode;
        public LocalizationTable Table => _table;

        public event Action<string> OnLanguageChanged;

        private void Awake()
        {
            if (Instance != null && !ReferenceEquals(Instance, this))
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            CurrentLanguageCode = NormalizeLanguageCode(_initialLanguageCode);

            if (_dontDestroyOnLoad)
            {
                transform.SetParent(null);
                DontDestroyOnLoad(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this))
            {
                Instance = null;
            }
        }

        public bool SetLanguage(string languageCode)
        {
            string normalizedLanguageCode = NormalizeLanguageCode(languageCode);

            if (string.IsNullOrEmpty(normalizedLanguageCode))
            {
                return false;
            }

            if (string.Equals(CurrentLanguageCode, normalizedLanguageCode, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            CurrentLanguageCode = normalizedLanguageCode;
            OnLanguageChanged?.Invoke(CurrentLanguageCode);
            return true;
        }

        public string GetText(string key, params object[] arguments)
        {
            if (string.IsNullOrEmpty(key) || _table == null)
            {
                return key;
            }

            string text;

            if (!_table.TryGetText(key, CurrentLanguageCode, out text) &&
                !_table.TryGetText(key, _fallbackLanguageCode, out text))
            {
                return key;
            }

            if (arguments == null || arguments.Length == 0)
            {
                return text;
            }

            try
            {
                return string.Format(text, arguments);
            }
            catch (FormatException)
            {
                return text;
            }
        }

        public bool HasTranslation(string key, string languageCode = null)
        {
            if (_table == null || string.IsNullOrEmpty(key))
            {
                return false;
            }

            string requestedLanguageCode = string.IsNullOrEmpty(languageCode)
                ? CurrentLanguageCode
                : NormalizeLanguageCode(languageCode);

            return _table.TryGetText(key, requestedLanguageCode, out _);
        }

        private static string NormalizeLanguageCode(string languageCode)
        {
            return string.IsNullOrWhiteSpace(languageCode)
                ? string.Empty
                : languageCode.Trim().Replace('_', '-');
        }
    }
}
