using System;

namespace ReminderApp.Models
{
    public class WaterEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime Timestamp { get; set; }
        public int AmountMl { get; set; }
    }
}
