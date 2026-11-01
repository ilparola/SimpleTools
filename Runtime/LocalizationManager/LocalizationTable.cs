using System;
using System.Collections.Generic;
using UnityEngine;

namespace SimpleTools.Localization
{
    [Serializable]
    public class LocalizationTranslation
    {
        [SerializeField] private string _languageCode;
        [TextArea(1, 5)]
        [SerializeField] private string _text;

        public string LanguageCode => _languageCode;
        public string Text => _text;
    }

    [Serializable]
    public class LocalizationEntry
    {
        [SerializeField] private string _key;
        [SerializeField] private List<LocalizationTranslation> _translations = new List<LocalizationTranslation>();

        public string Key => _key;
        public IReadOnlyList<LocalizationTranslation> Translations => _translations;

        public bool TryGetText(string languageCode, out string text)
        {
            for (int index = 0; index < _translations.Count; index++)
            {
                LocalizationTranslation translation = _translations[index];

                if (translation != null && string.Equals(translation.LanguageCode, languageCode, StringComparison.OrdinalIgnoreCase))
                {
                    text = translation.Text;
                    return true;
                }
            }

            text = null;
            return false;
        }
    }

    [CreateAssetMenu(fileName = "LocalizationTable", menuName = "Simple Tools/Localization Table")]
    public class LocalizationTable : ScriptableObject
    {
        [SerializeField] private List<LocalizationEntry> _entries = new List<LocalizationEntry>();

        public IReadOnlyList<LocalizationEntry> Entries => _entries;

        public bool TryGetText(string key, string languageCode, out string text)
        {
            for (int index = 0; index < _entries.Count; index++)
            {
                LocalizationEntry entry = _entries[index];

                if (entry != null && string.Equals(entry.Key, key, StringComparison.Ordinal))
                {
                    return entry.TryGetText(languageCode, out text);
                }
            }

            text = null;
            return false;
        }
    }
}
