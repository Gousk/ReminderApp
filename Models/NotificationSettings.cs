using System;

namespace ReminderApp.Models
{
    public enum NotificationLevel
    {
        Normal,
        Important,
        Critical
    }

    public class NotificationSettings
    {
        // Visual channel – for now we only have overlay popups,
        // but this lets us add toast / full-screen later.
        public bool UseOverlay { get; set; } = true;

        // Sound settings
        public bool PlaySound { get; set; } = true;

        // Optional custom WAV path. If null/empty, we play a default sound.
        public string? SoundPath { get; set; }

        // 0.0 – 1.0 (reserved for future volume control)
        public double Volume { get; set; } = 1.0;

        // Importance level (affects popup color & default sound)
        public NotificationLevel Level { get; set; } = NotificationLevel.Normal;
    }
}
