using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SimpleTools.Localization
{
    // Editor window to browse and edit a LocalizationTable: one row per key, one column per language, missing
    // translations highlighted. The selected key is edited in the panel below the table (multi-line texts).
    // Edits go through SerializedObject, so they support Undo and mark the asset dirty.
    public sealed class LocalizationTableWindow : EditorWindow
    {
        private const float KeyColumnWidth = 280f;
        private const float DetailHeight = 300f;
        private const string KeysField = "_entries";
        private const string KeyField = "_key";
        private const string TranslationsField = "_translations";
        private const string LanguageField = "_languageCode";
        private const string TextField = "_text";

        private static readonly Color MissingColor = new Color(1f, 0.55f, 0.1f, 0.25f);
        private static readonly Color SelectedColor = new Color(0.24f, 0.48f, 0.9f, 0.35f);
        private static readonly Color StripeColor = new Color(0f, 0f, 0f, 0.08f);

        [SerializeField] private LocalizationTable _table;
        [SerializeField] private string _search = string.Empty;
        [SerializeField] private bool _onlyMissing;
        [SerializeField] private int _selected = -1;
        [SerializeField] private List<string> _extraLanguages = new List<string>();

        private Vector2 _scroll;
        private Vector2 _detailScroll;
        private string _newKey = string.Empty;
        private string _newLanguage = string.Empty;
        private SerializedObject _serialized;
        private readonly List<int> _visible = new List<int>();
        private GUIStyle _cellStyle;
        private GUIStyle _wrapStyle;

        [MenuItem("Tools/Simple Tools/Localization Table Editor")]
        public static void Open()
        {
            Open(null);
        }

        public static void Open(LocalizationTable table)
        {
            LocalizationTableWindow window = GetWindow<LocalizationTableWindow>("Localization");
            window.minSize = new Vector2(700f, 500f);
            if (table != null)
            {
                window.SetTable(table);
            }
            else if (window._table == null)
            {
                window.SetTable(FindFirstTable());
            }

            window.Show();
        }

        private static LocalizationTable FindFirstTable()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(LocalizationTable));
            return guids.Length > 0
                ? AssetDatabase.LoadAssetAtPath<LocalizationTable>(AssetDatabase.GUIDToAssetPath(guids[0]))
                : null;
        }

        private void SetTable(LocalizationTable table)
        {
            _table = table;
            _serialized = null;
            _selected = -1;
            _extraLanguages.Clear();
        }

        private SerializedObject Serialized
        {
            get
            {
                if (_serialized == null || _serialized.targetObject != _table)
                {
                    _serialized = _table != null ? new SerializedObject(_table) : null;
                }

                return _serialized;
            }
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += Repaint;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= Repaint;
        }

        private void OnGUI()
        {
            _cellStyle ??= new GUIStyle(EditorStyles.label) { clipping = TextClipping.Clip };
            _wrapStyle ??= new GUIStyle(EditorStyles.textArea) { wordWrap = true };

            DrawToolbar();
            if (_table == null)
            {
                EditorGUILayout.HelpBox("Choose a LocalizationTable (Assets > Create > Simple Tools > Localization Table).", MessageType.Info);
                return;
            }

            Serialized.Update();
            List<string> languages = Languages();
            Filter(languages);
            DrawSummary(languages);
            DrawTable(languages);
            DrawDetail(languages);
            DrawFooter();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var table = (LocalizationTable)EditorGUILayout.ObjectField(_table, typeof(LocalizationTable), false,
                    GUILayout.Width(260f));
                if (table != _table)
                {
                    SetTable(table);
                }

                GUILayout.Space(8f);
                _search = EditorGUILayout.TextField(_search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(160f));
                _onlyMissing = GUILayout.Toggle(_onlyMissing, "Only missing", EditorStyles.toolbarButton, GUILayout.Width(100f));
                GUILayout.FlexibleSpace();
                if (_table != null && GUILayout.Button("Select asset", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                {
                    Selection.activeObject = _table;
                    EditorGUIUtility.PingObject(_table);
                }
            }
        }

        // Languages in order of first appearance in the table, plus the ones added from this window
        private List<string> Languages()
        {
            var languages = new List<string>();
            foreach (LocalizationEntry entry in _table.Entries)
            {
                if (entry == null)
                {
                    continue;
                }

                foreach (LocalizationTranslation translation in entry.Translations)
                {
                    if (translation != null && !string.IsNullOrEmpty(translation.LanguageCode) &&
                        !languages.Contains(translation.LanguageCode))
                    {
                        languages.Add(translation.LanguageCode);
                    }
                }
            }

            foreach (string language in _extraLanguages)
            {
                if (!languages.Contains(language))
                {
                    languages.Add(language);
                }
            }

            return languages;
        }

        private void Filter(List<string> languages)
        {
            _visible.Clear();
            IReadOnlyList<LocalizationEntry> entries = _table.Entries;
            for (int index = 0; index < entries.Count; index++)
            {
                LocalizationEntry entry = entries[index];
                if (entry == null)
                {
                    continue;
                }

                if (_onlyMissing && !IsMissingAny(entry, languages))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(_search) && !Matches(entry, _search))
                {
                    continue;
                }

                _visible.Add(index);
            }
        }

        private static bool Matches(LocalizationEntry entry, string search)
        {
            if (entry.Key != null && entry.Key.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            foreach (LocalizationTranslation translation in entry.Translations)
            {
                if (translation?.Text != null && translation.Text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsMissingAny(LocalizationEntry entry, List<string> languages)
        {
            foreach (string language in languages)
            {
                if (!entry.TryGetText(language, out _))
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawSummary(List<string> languages)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label($"{_table.Entries.Count} keys, {_visible.Count} shown", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                foreach (string language in languages)
                {
                    int missing = 0;
                    foreach (LocalizationEntry entry in _table.Entries)
                    {
                        if (entry != null && !entry.TryGetText(language, out _))
                        {
                            missing++;
                        }
                    }

                    GUILayout.Label(missing == 0 ? $"{language}: complete" : $"{language}: {missing} missing",
                        EditorStyles.miniLabel);
                }
            }
        }

        private void DrawTable(List<string> languages)
        {
            float rowHeight = EditorGUIUtility.singleLineHeight + 4f;
            Rect header = GUILayoutUtility.GetRect(0f, rowHeight, GUILayout.ExpandWidth(true));
            float columnWidth = Mathf.Max(80f, (header.width - KeyColumnWidth - 16f) / Mathf.Max(1, languages.Count));
            EditorGUI.DrawRect(header, StripeColor);
            GUI.Label(new Rect(header.x + 4f, header.y + 2f, KeyColumnWidth, rowHeight), "Key", EditorStyles.boldLabel);
            for (int column = 0; column < languages.Count; column++)
            {
                GUI.Label(new Rect(header.x + KeyColumnWidth + column * columnWidth + 4f, header.y + 2f, columnWidth, rowHeight),
                    languages[column], EditorStyles.boldLabel);
            }

            // Only the visible rows are drawn: tables have hundreds of keys
            Rect area = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            var content = new Rect(0f, 0f, area.width - 16f, _visible.Count * rowHeight);
            _scroll = GUI.BeginScrollView(area, _scroll, content);
            int first = Mathf.Max(0, Mathf.FloorToInt(_scroll.y / rowHeight));
            int last = Mathf.Min(_visible.Count - 1, Mathf.CeilToInt((_scroll.y + area.height) / rowHeight));
            for (int row = first; row <= last; row++)
            {
                int index = _visible[row];
                LocalizationEntry entry = _table.Entries[index];
                var rect = new Rect(0f, row * rowHeight, content.width, rowHeight);
                if (index == _selected)
                {
                    EditorGUI.DrawRect(rect, SelectedColor);
                }
                else if (row % 2 == 1)
                {
                    EditorGUI.DrawRect(rect, StripeColor);
                }

                GUI.Label(new Rect(4f, rect.y + 2f, KeyColumnWidth - 8f, rowHeight), entry.Key, _cellStyle);
                for (int column = 0; column < languages.Count; column++)
                {
                    var cell = new Rect(KeyColumnWidth + column * columnWidth, rect.y, columnWidth, rowHeight);
                    if (entry.TryGetText(languages[column], out string text))
                    {
                        GUI.Label(new Rect(cell.x + 4f, cell.y + 2f, cell.width - 8f, rowHeight), OneLine(text), _cellStyle);
                    }
                    else
                    {
                        EditorGUI.DrawRect(new Rect(cell.x + 1f, cell.y + 1f, cell.width - 2f, cell.height - 2f), MissingColor);
                    }
                }

                if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
                {
                    _selected = index;
                    GUI.FocusControl(null);
                    Event.current.Use();
                }
            }

            GUI.EndScrollView();
        }

        private static string OneLine(string text)
        {
            return string.IsNullOrEmpty(text) ? string.Empty : text.Replace("\n", " ⏎ ");
        }

        private void DrawDetail(List<string> languages)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Height(DetailHeight)))
            {
                SerializedProperty entries = Serialized.FindProperty(KeysField);
                if (_selected < 0 || _selected >= entries.arraySize)
                {
                    GUILayout.Label("Select a key to edit its translations.", EditorStyles.centeredGreyMiniLabel);
                    GUILayout.FlexibleSpace();
                    return;
                }

                SerializedProperty entry = entries.GetArrayElementAtIndex(_selected);
                SerializedProperty key = entry.FindPropertyRelative(KeyField);
                SerializedProperty translations = entry.FindPropertyRelative(TranslationsField);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    string newKey = EditorGUILayout.DelayedTextField("Key", key.stringValue);
                    if (EditorGUI.EndChangeCheck() && newKey != key.stringValue)
                    {
                        if (string.IsNullOrWhiteSpace(newKey) || IndexOfKey(newKey) >= 0)
                        {
                            Debug.LogWarning($"Localization: the key \"{newKey}\" is empty or already exists.");
                        }
                        else
                        {
                            key.stringValue = newKey;
                        }
                    }

                    if (GUILayout.Button("Delete key", GUILayout.Width(90f)) &&
                        EditorUtility.DisplayDialog("Delete key", $"Delete \"{key.stringValue}\" and its translations?", "Delete", "Cancel"))
                    {
                        entries.DeleteArrayElementAtIndex(_selected);
                        _selected = -1;
                        Serialized.ApplyModifiedProperties();
                        GUIUtility.ExitGUI();
                    }
                }

                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                foreach (string language in languages)
                {
                    SerializedProperty text = FindText(translations, language);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(language, EditorStyles.boldLabel, GUILayout.Width(40f));
                        if (text == null)
                        {
                            GUILayout.Label("missing (the game shows the fallback language)", EditorStyles.miniLabel);
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("Add", GUILayout.Width(60f)))
                            {
                                AddTranslation(translations, language);
                            }
                        }
                        else
                        {
                            EditorGUI.BeginChangeCheck();
                            string value = EditorGUILayout.TextArea(text.stringValue, _wrapStyle, GUILayout.MinHeight(38f));
                            if (EditorGUI.EndChangeCheck())
                            {
                                text.stringValue = value;
                            }
                            if (GUILayout.Button("✕", GUILayout.Width(22f)))
                            {
                                RemoveTranslation(translations, language);
                                GUIUtility.ExitGUI();
                            }
                        }
                    }
                }

                EditorGUILayout.EndScrollView();
                Serialized.ApplyModifiedProperties();
            }
        }

        private void DrawFooter()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _newKey = EditorGUILayout.TextField("New key", _newKey);
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newKey) || IndexOfKey(_newKey.Trim()) >= 0))
                {
                    if (GUILayout.Button("Add key", GUILayout.Width(80f)))
                    {
                        AddKey(_newKey.Trim());
                        _newKey = string.Empty;
                        GUI.FocusControl(null);
                    }
                }

                GUILayout.Space(16f);
                _newLanguage = EditorGUILayout.TextField("New language", _newLanguage, GUILayout.Width(220f));
                using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newLanguage)))
                {
                    if (GUILayout.Button("Add language", GUILayout.Width(100f)))
                    {
                        // Only a column: entries get the language when a translation is written, so the game keeps
                        // using the fallback instead of showing empty texts
                        string language = _newLanguage.Trim();
                        if (!_extraLanguages.Contains(language))
                        {
                            _extraLanguages.Add(language);
                        }

                        _newLanguage = string.Empty;
                        GUI.FocusControl(null);
                    }
                }
            }
        }

        private int IndexOfKey(string key)
        {
            IReadOnlyList<LocalizationEntry> entries = _table.Entries;
            for (int index = 0; index < entries.Count; index++)
            {
                if (entries[index] != null && string.Equals(entries[index].Key, key, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        private void AddKey(string key)
        {
            SerializedProperty entries = Serialized.FindProperty(KeysField);
            int index = entries.arraySize;
            entries.InsertArrayElementAtIndex(index);
            SerializedProperty entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative(KeyField).stringValue = key;
            entry.FindPropertyRelative(TranslationsField).ClearArray();
            Serialized.ApplyModifiedProperties();
            _selected = index;
            _search = string.Empty;
            _onlyMissing = false;
            _scroll.y = float.MaxValue;
        }

        private static SerializedProperty FindText(SerializedProperty translations, string language)
        {
            for (int index = 0; index < translations.arraySize; index++)
            {
                SerializedProperty translation = translations.GetArrayElementAtIndex(index);
                if (string.Equals(translation.FindPropertyRelative(LanguageField).stringValue, language,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return translation.FindPropertyRelative(TextField);
                }
            }

            return null;
        }

        private static void AddTranslation(SerializedProperty translations, string language)
        {
            int index = translations.arraySize;
            translations.InsertArrayElementAtIndex(index);
            SerializedProperty translation = translations.GetArrayElementAtIndex(index);
            translation.FindPropertyRelative(LanguageField).stringValue = language;
            translation.FindPropertyRelative(TextField).stringValue = string.Empty;
        }

        private void RemoveTranslation(SerializedProperty translations, string language)
        {
            for (int index = 0; index < translations.arraySize; index++)
            {
                SerializedProperty translation = translations.GetArrayElementAtIndex(index);
                if (string.Equals(translation.FindPropertyRelative(LanguageField).stringValue, language,
                        StringComparison.OrdinalIgnoreCase))
                {
                    translations.DeleteArrayElementAtIndex(index);
                    Serialized.ApplyModifiedProperties();
                    return;
                }
            }
        }
    }

    // The table's inspector: a summary and the button that opens the editor window
    [CustomEditor(typeof(LocalizationTable))]
    public sealed class LocalizationTableInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var table = (LocalizationTable)target;
            EditorGUILayout.LabelField("Keys", table.Entries.Count.ToString());
            if (GUILayout.Button("Open Localization Table Editor", GUILayout.Height(28f)))
            {
                LocalizationTableWindow.Open(table);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("Hundreds of entries are slow to edit here: use the editor window.", MessageType.None);
        }
    }
}
