using System.Windows;
using ReminderApp.Models;

namespace ReminderApp.UI
{
    public partial class NotificationWindow : Window
    {
        private readonly PopupSettings _popupSettings;

        public NotificationWindow(Reminder reminder, PopupSettings popupSettings)
        {
            InitializeComponent();

            _popupSettings = popupSettings;

            TitleText.Text = string.IsNullOrWhiteSpace(reminder.Title) ? "Reminder" : reminder.Title;
            MessageText.Text = reminder.Message ?? string.Empty;
            TimeText.Text = reminder.ScheduledTime.ToString("g");

            Loaded += (_, _) =>
            {
                ApplyPopupSettings();
                PositionWindow();
            };
        }

        private void ApplyPopupSettings()
        {
            if (_popupSettings.Width > 0)
                Width = _popupSettings.Width;
            if (_popupSettings.Height > 0)
                Height = _popupSettings.Height;
        }

        private void PositionWindow()
        {
            var wa = SystemParameters.WorkArea;
            double left = wa.Right - Width - 10;
            double top = wa.Bottom - Height - 10;

            switch (_popupSettings.Position)
            {
                case NotificationPosition.BottomRight:
                    left = wa.Right - Width - 10;
                    top = wa.Bottom - Height - 10;
                    break;
                case NotificationPosition.BottomLeft:
                    left = wa.Left + 10;
                    top = wa.Bottom - Height - 10;
                    break;
                case NotificationPosition.TopRight:
                    left = wa.Right - Width - 10;
                    top = wa.Top + 10;
                    break;
                case NotificationPosition.TopLeft:
                    left = wa.Left + 10;
                    top = wa.Top + 10;
                    break;
                case NotificationPosition.Center:
                    left = wa.Left + (wa.Width - Width) / 2;
                    top = wa.Top + (wa.Height - Height) / 2;
                    break;
            }

            Left = left;
            Top = top;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
