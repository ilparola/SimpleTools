using System;
using System.IO;
using UnityEngine;

namespace SimpleTools.Settings
{
    /// <summary>Gestisce la persistenza JSON di un tipo di configurazione.</summary>
    /// <typeparam name="TSettings">Tipo serializzabile con <see cref="SerializableAttribute"/>.</typeparam>
    public class SettingsManager<TSettings> where TSettings : class, new()
    {
        private const string TemporaryFileSuffix = ".tmp";

        private readonly string _filePath;

        /// <summary>Crea un manager che salva nella cartella persistente dell'applicazione.</summary>
        /// <param name="fileName">Nome del file JSON, senza percorso.</param>
        public SettingsManager(string fileName)
            : this(Application.persistentDataPath, fileName)
        {
        }

        /// <summary>Crea un manager con una directory di persistenza esplicita.</summary>
        /// <param name="directoryPath">Directory in cui salvare il file.</param>
        /// <param name="fileName">Nome del file JSON, senza percorso.</param>
        public SettingsManager(string directoryPath, string fileName)
        {
            if (string.IsNullOrWhiteSpace(directoryPath))
            {
                throw new ArgumentException("La directory di persistenza non può essere vuota.", nameof(directoryPath));
            }

            if (string.IsNullOrWhiteSpace(fileName) || fileName != Path.GetFileName(fileName))
            {
                throw new ArgumentException("Il nome del file deve essere un nome semplice senza percorso.", nameof(fileName));
            }

            _filePath = Path.Combine(directoryPath, fileName);
        }

        /// <summary>Percorso completo del file JSON.</summary>
        public string FilePath => _filePath;

        /// <summary>Configurazione attualmente caricata, oppure null prima del primo caricamento.</summary>
        public TSettings CurrentSettings { get; private set; }

        /// <summary>Indica se il file di configurazione esiste.</summary>
        public bool HasSavedSettings => File.Exists(_filePath);

        /// <summary>Invocato dopo un caricamento riuscito.</summary>
        public event Action<TSettings> OnSettingsLoaded;

        /// <summary>Invocato dopo un salvataggio riuscito.</summary>
        public event Action<TSettings> OnSettingsSaved;

        /// <summary>Invocato dopo un reset ai valori di default.</summary>
        public event Action<TSettings> OnSettingsReset;

        /// <summary>Invocato quando un'operazione di persistenza fallisce.</summary>
        public event Action<Exception> OnPersistenceError;

        /// <summary>Carica il file oppure crea e salva una nuova configurazione.</summary>
        public TSettings LoadOrCreate()
        {
            if (!HasSavedSettings)
            {
                return Reset();
            }

            return Load();
        }

        /// <summary>Carica la configurazione dal file JSON esistente.</summary>
        public TSettings Load()
        {
            try
            {
                string json = File.ReadAllText(_filePath);
                TSettings settings = JsonUtility.FromJson<TSettings>(json);

                if (settings == null)
                {
                    throw new InvalidDataException("Il file JSON non contiene una configurazione valida.");
                }

                CurrentSettings = settings;
                OnSettingsLoaded?.Invoke(settings);
                return settings;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is InvalidDataException)
            {
                NotifyPersistenceError(exception);
                return null;
            }
        }

        /// <summary>Serializza e salva la configurazione corrente.</summary>
        public bool Save()
        {
            return CurrentSettings != null && Save(CurrentSettings);
        }

        /// <summary>Serializza e salva la configurazione indicata.</summary>
        public bool Save(TSettings settings)
        {
            if (settings == null)
            {
                NotifyPersistenceError(new ArgumentNullException(nameof(settings)));
                return false;
            }

            string temporaryFilePath = _filePath + TemporaryFileSuffix;

            try
            {
                string directoryPath = Path.GetDirectoryName(_filePath);
                Directory.CreateDirectory(directoryPath);

                string json = JsonUtility.ToJson(settings, prettyPrint: true);
                File.WriteAllText(temporaryFilePath, json);

                if (File.Exists(_filePath))
                {
                    File.Replace(temporaryFilePath, _filePath, null);
                }
                else
                {
                    File.Move(temporaryFilePath, _filePath);
                }

                CurrentSettings = settings;
                OnSettingsSaved?.Invoke(settings);
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
            {
                TryDeleteTemporaryFile(temporaryFilePath);
                NotifyPersistenceError(exception);
                return false;
            }
        }

        /// <summary>Ripristina una nuova configurazione di default e la salva.</summary>
        public TSettings Reset()
        {
            TSettings settings = new TSettings();

            if (!Save(settings))
            {
                return null;
            }

            OnSettingsReset?.Invoke(settings);
            return settings;
        }

        private void NotifyPersistenceError(Exception exception)
        {
            Debug.LogException(exception);
            OnPersistenceError?.Invoke(exception);
        }

        private static void TryDeleteTemporaryFile(string temporaryFilePath)
        {
            try
            {
                if (File.Exists(temporaryFilePath))
                {
                    File.Delete(temporaryFilePath);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
