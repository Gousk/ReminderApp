namespace ReminderApp.Models
{
    public enum NotificationPosition
    {
        BottomRight,
        BottomLeft,
        TopRight,
        TopLeft,
        Center
    }

    public class PopupSettings
    {
        public double Width { get; set; } = 320;
        public double Height { get; set; } = 160;
        public NotificationPosition Position { get; set; } = NotificationPosition.BottomRight;
        public bool PlaySound { get; set; } = true;
    }

    public class AppNotificationSettings
    {
        public PopupSettings ReminderPopup { get; set; } = new PopupSettings();
        public PopupSettings WaterPopup { get; set; } = new PopupSettings();
    }
}
