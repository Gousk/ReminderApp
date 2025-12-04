using System;

namespace ReminderApp.Models
{
    public enum ReminderType
    {
        OneTime,
        Repeating
        // future: Weekly, Monthly, Cron-like rules, etc.
    }

    public class Reminder
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;

        // What you see on the calendar as the event time
        public DateTime ScheduledTime { get; set; }

        // When it will actually fire next
        public DateTime NextTriggerTime { get; set; }

        public ReminderType Type { get; set; } = ReminderType.OneTime;

        // If repeating: how often (every X minutes/hours/days)
        public TimeSpan? RepeatInterval { get; set; }

        // Remind X minutes before ScheduledTime (0 = at the time)
        public int MinutesBefore { get; set; } = 0;

        public bool IsActive { get; set; } = true;

        public NotificationSettings NotificationSettings { get; set; } = new NotificationSettings();
    }
}
