using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ReminderApp.Models;

namespace ReminderApp.Services
{
    public class FileReminderRepository : IReminderRepository
    {
        private readonly string _filePath;
        private readonly List<Reminder> _reminders = new();

        public FileReminderRepository()
        {
            // You can change this to AppData if you want:
            // var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            // _filePath = Path.Combine(appData, "ReminderApp", "reminders.json");
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reminders.json");

            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            LoadFromFile();
        }

        private void LoadFromFile()
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
                var list = JsonSerializer.Deserialize<List<Reminder>>(json, options);
                if (list != null)
                {
                    _reminders.Clear();
                    _reminders.AddRange(list);
                }
            }
            catch
            {
                // If file is corrupt, ignore and start fresh
                _reminders.Clear();
            }
        }

        private void SaveToFile()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(_reminders, options);
            File.WriteAllText(_filePath, json);
        }

        public IEnumerable<Reminder> GetAll() => _reminders;

        public IEnumerable<Reminder> GetDueReminders(DateTime now)
        {
            return _reminders
                .Where(r => r.IsActive && r.NextTriggerTime <= now)
                .ToList();
        }

        public IEnumerable<Reminder> GetByDate(DateTime date)
        {
            var d = date.Date;
            return _reminders
                .Where(r => r.ScheduledTime.Date == d)
                .OrderBy(r => r.ScheduledTime)
                .ToList();
        }

        public void Add(Reminder reminder)
        {
            _reminders.Add(reminder);
            SaveToFile();
        }

        public void Update(Reminder reminder)
        {
            // For JSON list, object reference is already in list;
            // we just persist.
            SaveToFile();
        }

        public void Delete(Guid id)
        {
            var r = _reminders.FirstOrDefault(x => x.Id == id);
            if (r != null)
            {
                _reminders.Remove(r);
                SaveToFile();
            }
        }
    }
}
