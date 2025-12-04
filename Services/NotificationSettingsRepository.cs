using System;
using System.IO;
using System.Text.Json;
using ReminderApp.Models;

namespace ReminderApp.Services
{
    public class NotificationSettingsRepository
    {
        private readonly string _filePath;
        private AppNotificationSettings _settings = new AppNotificationSettings();

        public NotificationSettingsRepository()
        {
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "notificationsettings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            Load();
        }

        private void Load()
        {
            if (!File.Exists(_filePath))
                return;

            try
            {
                var json = File.ReadAllText(_filePath);
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };
                var loaded = JsonSerializer.Deserialize<AppNotificationSettings>(json, options);
                if (loaded != null)
                    _settings = loaded;
            }
            catch
            {
                _settings = new AppNotificationSettings();
            }
        }

        private void Save()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(_settings, options);
            File.WriteAllText(_filePath, json);
        }

        public AppNotificationSettings Get()
        {
            return _settings;
        }

        public void Update(AppNotificationSettings settings)
        {
            _settings = settings ?? new AppNotificationSettings();
            Save();
        }
    }
}
