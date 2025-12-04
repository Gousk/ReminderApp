using System;
using System.Collections.Generic;

namespace ReminderApp.Models
{
    public class WaterData
    {
        // Default daily goal in ml
        public int DailyGoalMl { get; set; } = 2000;

        public List<WaterEntry> Entries { get; set; } = new List<WaterEntry>();

        // Built-in water reminder settings
        public bool WaterReminderEnabled { get; set; } = false;

        // Interval in minutes (we'll show as minutes/hours in UI)
        public int WaterReminderIntervalMinutes { get; set; } = 60;

        // Day window (string olarak saklıyoruz, runtime'da TimeSpan'e parse edeceğiz)
        // Örn: "09:00", "02:00"
        public string DayStartTime { get; set; } = "09:00";
        public string DayEndTime { get; set; } = "02:00";

        // "Bugünlük suyu bitir" butonuna basıldığında,
        // hatırlatmaların yeniden başlaması gereken zamanı tutuyoruz.
        // Şu an < ManualEndUntil ise reminder YOK.
        public DateTime? ManualEndUntil { get; set; } = null;
    }
}
