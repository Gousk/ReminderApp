using System.IO;
using System.Media;
using System.Threading.Tasks;
using System.Windows;
using ReminderApp.Models;
using ReminderApp.UI;

namespace ReminderApp.Services
{
    public class NotificationService
    {
        private readonly NotificationSettingsRepository _notificationSettingsRepository;

        public NotificationService(NotificationSettingsRepository notificationSettingsRepository)
        {
            _notificationSettingsRepository = notificationSettingsRepository;
        }

        // --- Reminder notifications ---

        public void ShowReminder(Reminder reminder)
        {
            var appSettings = _notificationSettingsRepository.Get();
            var popupSettings = appSettings.ReminderPopup ?? new PopupSettings();

            // Visual popup (per-reminder UseOverlay + global ayarlara göre)
            if (reminder.NotificationSettings.UseOverlay)
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var window = new NotificationWindow(reminder, popupSettings);
                    window.Show();
                });
            }

            // Ses: global ve reminder bazlı ayarlar birlikte
            if (popupSettings.PlaySound && reminder.NotificationSettings.PlaySound)
            {
                PlayReminderSound(reminder.NotificationSettings);
            }
        }

        private void PlayReminderSound(NotificationSettings settings)
        {
            // Önce custom path varsa onu dene
            if (!string.IsNullOrWhiteSpace(settings.SoundPath) && File.Exists(settings.SoundPath))
            {
                try
                {
                    using var player = new SoundPlayer(settings.SoundPath);
                    player.Play();
                    return;
                }
                catch
                {
                    // ignore, fallback'a geç
                }
            }

            // Seviye'ye göre default system sound
            switch (settings.Level)
            {
                case NotificationLevel.Normal:
                    SystemSounds.Asterisk.Play();
                    break;

                case NotificationLevel.Important:
                    SystemSounds.Exclamation.Play();
                    break;

                case NotificationLevel.Critical:
                    Task.Run(async () =>
                    {
                        SystemSounds.Hand.Play();
                        await Task.Delay(500);
                        SystemSounds.Hand.Play();
                    });
                    break;
            }
        }

        // --- Water notifications ---

        public void ShowWaterReminder(int amountMl, int remainingMl, int goalMl, System.Action onConfirm, System.Action? onSkip)
        {
            var appSettings = _notificationSettingsRepository.Get();
            var popupSettings = appSettings.WaterPopup ?? new PopupSettings();

            Application.Current.Dispatcher.Invoke(() =>
            {
                var window = new WaterNotificationWindow(amountMl, remainingMl, goalMl, onConfirm, onSkip, popupSettings);
                window.Show();
            });

            if (popupSettings.PlaySound)
            {
                SystemSounds.Asterisk.Play();
            }
        }
    }
}
