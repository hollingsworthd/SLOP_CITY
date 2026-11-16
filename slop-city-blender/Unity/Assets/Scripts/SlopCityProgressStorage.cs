using UnityEngine;

namespace SlopCity
{
    /// <summary>
    /// Small shared persistence gateway for gameplay progress. The public game
    /// stays offline-capable through PlayerPrefs; a private composition can
    /// install a city-scoped store before scene setup without teaching shared
    /// gameplay about accounts, manifests, or service routes.
    /// </summary>
    public interface ISlopCityProgressStorage
    {
        string GetString(string key, string defaultValue);
        void SetString(string key, string value);
        void Save();
    }

    public static class SlopCityProgressStorage
    {
        private sealed class PlayerPrefsStore : ISlopCityProgressStorage
        {
            public string GetString(string key, string defaultValue) => PlayerPrefs.GetString(key, defaultValue);
            public void SetString(string key, string value) => PlayerPrefs.SetString(key, value);
            public void Save() => PlayerPrefs.Save();
        }

        private static ISlopCityProgressStorage current = new PlayerPrefsStore();

        /// <summary>Installs the process-local gameplay store before scene setup.</summary>
        public static void Install(ISlopCityProgressStorage storage)
        {
            current = storage ?? new PlayerPrefsStore();
        }

        public static string GetString(string key, string defaultValue = "") => current.GetString(key, defaultValue);
        public static void SetString(string key, string value) => current.SetString(key, value);
        public static void Save() => current.Save();
    }
}
