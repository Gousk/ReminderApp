using System.Windows;
using ReminderApp.Models;

namespace ReminderApp.UI
{
    public partial class WaterNotificationWindow : Window
    {
        private readonly System.Action _onConfirm;
        private readonly System.Action? _onSkip;
        private readonly PopupSettings _popupSettings;

        public WaterNotificationWindow(int amountMl, int remainingMl, int goalMl, System.Action onConfirm, System.Action? onSkip, PopupSettings popupSettings)
        {
            InitializeComponent();
            _onConfirm = onConfirm;
            _onSkip = onSkip;
            _popupSettings = popupSettings;

            MessageText.Text = $"Drink {amountMl} ml of water.";
            ProgressText.Text = $"Remaining today: {remainingMl} ml (goal {goalMl} ml).";

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

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            _onConfirm?.Invoke();
            Close();
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            _onSkip?.Invoke();
            Close();
        }
    }
}
