using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ReminderApp.Models;

namespace ReminderApp.Services
{
    public class WaterRepository
    {
        private readonly string _filePath;
        private WaterData _data = new WaterData();

        public WaterRepository()
        {
            _filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "water.json");
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
                var loaded = JsonSerializer.Deserialize<WaterData>(json, options);
                if (loaded != null)
                {
                    _data = loaded;
                }
            }
            catch
            {
                _data = new WaterData();
            }
        }

        private void SaveToFile()
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            var json = JsonSerializer.Serialize(_data, options);
            File.WriteAllText(_filePath, json);
        }

        // --- Goal ---

        public int GetDailyGoal()
        {
            return _data.DailyGoalMl;
        }

        public void SetDailyGoal(int goalMl)
        {
            _data.DailyGoalMl = goalMl;
            SaveToFile();
        }

        // --- Reminder settings (enabled + interval) ---

        public (bool enabled, int intervalMinutes) GetReminderSettings()
        {
            return (_data.WaterReminderEnabled, _data.WaterReminderIntervalMinutes);
        }

        public void SetReminderSettings(bool enabled, int intervalMinutes)
        {
            _data.WaterReminderEnabled = enabled;
            _data.WaterReminderIntervalMinutes = intervalMinutes;
            SaveToFile();
        }

        // --- Day window settings (start/end times) ---

        public (TimeSpan start, TimeSpan end) GetDayWindowSettings()
        {
            if (!TimeSpan.TryParse(_data.DayStartTime, out var start))
                start = TimeSpan.FromHours(9); // 09:00 default

            if (!TimeSpan.TryParse(_data.DayEndTime, out var end))
                end = TimeSpan.FromHours(2); // 02:00 default (ertesi gün)

            return (start, end);
        }

        public void SetDayWindowSettings(TimeSpan start, TimeSpan end)
        {
            _data.DayStartTime = start.ToString(@"hh\:mm");
            _data.DayEndTime = end.ToString(@"hh\:mm");
            SaveToFile();
        }

        // --- Manual end ("bugünlük suyu bitir") ---

        public DateTime? GetManualEndUntil()
        {
            return _data.ManualEndUntil;
        }

        public void SetManualEndUntil(DateTime? until)
        {
            _data.ManualEndUntil = until;
            SaveToFile();
        }

        // --- Entries ---

        public List<WaterEntry> GetEntriesForDate(DateTime date)
        {
            var d = date.Date;
            return _data.Entries
                .Where(e => e.Timestamp.Date == d)
                .OrderBy(e => e.Timestamp)
                .ToList();
        }

        public List<WaterEntry> GetEntriesInRange(DateTime start, DateTime end)
        {
            return _data.Entries
                .Where(e => e.Timestamp >= start && e.Timestamp < end)
                .OrderBy(e => e.Timestamp)
                .ToList();
        }

        public void AddEntry(int amountMl, DateTime timestamp)
        {
            var entry = new WaterEntry
            {
                Timestamp = timestamp,
                AmountMl = amountMl
            };

            _data.Entries.Add(entry);
            SaveToFile();
        }

        public void DeleteEntry(Guid id)
        {
            var entry = _data.Entries.FirstOrDefault(e => e.Id == id);
            if (entry != null)
            {
                _data.Entries.Remove(entry);
                SaveToFile();
            }
        }
    }
}
