using System;
using System.IO;
using System.Text.Json;
using L5ArgentinaLauncher.Models;

namespace L5ArgentinaLauncher.Services
{
    /// <summary>Carga/guarda la config del usuario en %APPDATA% (spec §13).</summary>
    public class ConfigService
    {
        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        public UserConfig Load()
        {
            UserConfig config = null;
            try
            {
                if (File.Exists(AppConstants.ConfigPath))
                {
                    var json = File.ReadAllText(AppConstants.ConfigPath);
                    config = JsonSerializer.Deserialize<UserConfig>(json, JsonOpts);
                }
            }
            catch
            {
                // Config corrupta: arrancamos con una nueva en vez de morir.
                config = null;
            }

            config = config ?? new UserConfig();

            // Aplicar el default hardcodeado del manifest si el usuario no lo cambió.
            if (string.IsNullOrWhiteSpace(config.ManifestUrl))
                config.ManifestUrl = AppConstants.DefaultManifestUrl;

            if (config.CachedDatabases == null)
                config.CachedDatabases = new System.Collections.Generic.Dictionary<string, CachedFile>();

            return config;
        }

        // Las descargas en segundo plano guardan desde otro hilo mientras la UI guarda desde el suyo
        // (ADR-064): sin el lock, dos Save a la vez se pisan el mismo .tmp.
        private static readonly object SaveLock = new object();

        public void Save(UserConfig config)
        {
            lock (SaveLock)
            {
                Directory.CreateDirectory(AppConstants.AppDataDir);
                var json = JsonSerializer.Serialize(config, JsonOpts);
                // Escritura atómica: a temporal y luego replace, para no dejar config a medias.
                var tmp = AppConstants.ConfigPath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(AppConstants.ConfigPath))
                    File.Replace(tmp, AppConstants.ConfigPath, null);
                else
                    File.Move(tmp, AppConstants.ConfigPath);
            }
        }
    }
}
