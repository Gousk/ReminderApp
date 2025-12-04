using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Drawing;
using ReminderApp.Models;
using ReminderApp.Services;
using ReminderApp.UI;
using WinForms = System.Windows.Forms;

namespace ReminderApp
{
    public partial class MainWindow : Window
    {
        private readonly IReminderRepository _repository;
        private readonly NotificationSettingsRepository _notificationSettingsRepository;
        private readonly NotificationService _notificationService;
        private readonly ReminderScheduler _scheduler;
        private readonly WaterRepository _waterRepository;
        private readonly WaterReminderService _waterReminderService;
        private bool _waterSettingsInitialized = false;

        // System tray (nullable yaptık, sonra null-check ile kullanacağız)
        private WinForms.NotifyIcon? _trayIcon;
        private bool _isRealExit = false;

        public MainWindow()
        {
            InitializeComponent();

            _repository = new FileReminderRepository();
            _notificationSettingsRepository = new NotificationSettingsRepository();
            _notificationService = new NotificationService(_notificationSettingsRepository);
            _scheduler = new ReminderScheduler(_repository, _notificationService);
            _scheduler.Start();

            _waterRepository = new WaterRepository();
            _waterReminderService = new WaterReminderService(_waterRepository, _notificationService);

            CalendarControl.SelectedDate = DateTime.Today;
            UpdateForSelectedDate();
            UpdateWaterUIForSelectedDate();

            // --- Water settings ---

            // Goal
            WaterGoalBox.Text = _waterRepository.GetDailyGoal().ToString();

            // Reminder interval + enabled
            var (enabled, intervalMinutes) = _waterRepository.GetReminderSettings();
            if (intervalMinutes <= 0) intervalMinutes = 60;

            WaterReminderEnabledCheckBox.IsChecked = enabled;

            if (intervalMinutes % 60 == 0 && intervalMinutes >= 60)
            {
                WaterReminderUnitBox.SelectedIndex = 1; // Hours
                WaterReminderIntervalBox.Text = (intervalMinutes / 60).ToString();
            }
            else
            {
                WaterReminderUnitBox.SelectedIndex = 0; // Minutes
                WaterReminderIntervalBox.Text = intervalMinutes.ToString();
            }

            // Day window
            var (start, end) = _waterRepository.GetDayWindowSettings();
            WaterDayStartBox.Text = start.ToString(@"hh\:mm");
            WaterDayEndBox.Text = end.ToString(@"hh\:mm");

            _waterReminderService.ApplySettings(enabled, TimeSpan.FromMinutes(intervalMinutes), start, end);
            _waterSettingsInitialized = true;

            // --- Notification settings UI'ı doldur ---
            LoadNotificationSettingsIntoUI();

            StatusText.Text = "Scheduler running. Use the tabs for reminders, water tracking, and notification settings.";

            // --- System tray setup ---
            InitializeTrayIcon();

            // Minimize to tray on start
            Loaded += MainWindow_Loaded;
            StateChanged += MainWindow_StateChanged;
        }

        private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
        {
            // Uygulama açıldığında direkt tray'e gizle
            WindowState = WindowState.Minimized;
            Hide();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
        }

        private void InitializeTrayIcon()
        {
            System.Drawing.Icon trayIconImage;

            try
            {
                var iconPath = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Assets",
                    "appicon.ico");

                if (System.IO.File.Exists(iconPath))
                {
                    trayIconImage = new System.Drawing.Icon(iconPath);
                }
                else
                {
                    // Debug amaçlı: ilk seferde gör, sonra istersen MessageBox'ı silersin
                    MessageBox.Show($"Tray icon not found at:\n{iconPath}", "Tray Icon",
                        MessageBoxButton.OK, MessageBoxImage.Warning);

                    trayIconImage = System.Drawing.SystemIcons.Application;
                }
            }
            catch
            {
                trayIconImage = System.Drawing.SystemIcons.Application;
            }

            _trayIcon = new WinForms.NotifyIcon
            {
                Text = "Reminder App",
                Icon = trayIconImage,
                Visible = true
            };

            _trayIcon.DoubleClick += (_, _) => ShowFromTray();

            var menu = new WinForms.ContextMenuStrip();
            var openItem = new WinForms.ToolStripMenuItem("Open");
            openItem.Click += (_, _) => ShowFromTray();

            var exitItem = new WinForms.ToolStripMenuItem("Exit");
            exitItem.Click += (_, _) => ExitFromTray();

            menu.Items.Add(openItem);
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = menu;
        }


        private void ShowFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;

            Activate();
        }

        private void ExitFromTray()
        {
            _isRealExit = true;
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isRealExit)
            {
                // X'e basınca gerçekten kapanma, tray'e gizlen
                e.Cancel = true;
                Hide();
                return;
            }

            // Gerçek çıkış
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            base.OnClosing(e);
        }

        private DateTime SelectedDate => CalendarControl.SelectedDate ?? DateTime.Today;

        private void CalendarControl_SelectedDatesChanged(object? sender, SelectionChangedEventArgs e)
        {
            UpdateForSelectedDate();
            UpdateWaterUIForSelectedDate();
        }

        // ===== Reminders =====

        private void UpdateForSelectedDate()
        {
            var date = SelectedDate.Date;
            DateTitle.Text = date.ToLongDateString();

            var reminders = _repository.GetByDate(date).ToList();
            RemindersListBox.ItemsSource = reminders;
        }

        private void AddReminder_Click(object sender, RoutedEventArgs e)
        {
            var editor = new ReminderEditorWindow(SelectedDate)
            {
                Owner = this
            };

            if (editor.ShowDialog() == true)
            {
                _repository.Add(editor.Reminder);
                UpdateForSelectedDate();
                StatusText.Text = $"Reminder added for {editor.Reminder.ScheduledTime:g}.";
            }
        }

        private void EditReminder_Click(object sender, RoutedEventArgs e)
        {
            if (RemindersListBox.SelectedItem is not Reminder selected)
            {
                MessageBox.Show(this, "Select a reminder to edit.", "Edit",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var editor = new ReminderEditorWindow(selected)
            {
                Owner = this
            };

            if (editor.ShowDialog() == true)
            {
                _repository.Update(selected);
                UpdateForSelectedDate();
                StatusText.Text = $"Reminder updated for {selected.ScheduledTime:g}.";
            }
        }

        private void DeleteReminder_Click(object sender, RoutedEventArgs e)
        {
            if (RemindersListBox.SelectedItem is not Reminder selected)
            {
                MessageBox.Show(this, "Select a reminder to delete.", "Delete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(this,
                $"Delete reminder \"{selected.Title}\"?",
                "Delete",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _repository.Delete(selected.Id);
                UpdateForSelectedDate();
                StatusText.Text = "Reminder deleted.";
            }
        }

        // ===== Water tracker =====

        private void UpdateWaterUIForSelectedDate()
        {
            var date = SelectedDate.Date;
            var entries = _waterRepository.GetEntriesForDate(date);

            WaterEntriesListBox.ItemsSource = entries;

            var total = entries.Sum(e => e.AmountMl);
            var goal = _waterRepository.GetDailyGoal();

            int percent = 0;
            if (goal > 0)
            {
                percent = (int)Math.Round((double)total * 100 / goal);
                if (percent > 999) percent = 999;
            }

            WaterSummaryText.Text =
                $"Date: {date:d} | Total: {total} ml / Goal: {goal} ml ({percent}%)";
        }

        private void WaterReminderSettingChanged(object sender, RoutedEventArgs e)
        {
            ApplyWaterReminderSettingsImmediate();
        }

        private void WaterReminderIntervalBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyWaterReminderSettingsImmediate();
        }

        private void ApplyWaterReminderSettingsImmediate()
        {
            if (!_waterSettingsInitialized)
                return;

            bool enabled = WaterReminderEnabledCheckBox.IsChecked == true;

            if (!int.TryParse(WaterReminderIntervalBox.Text, out var intervalValue) || intervalValue <= 0)
                return;

            int intervalMinutes = intervalValue;
            if (WaterReminderUnitBox.SelectedItem is ComboBoxItem item &&
                (item.Content as string) == "Hours")
            {
                intervalMinutes = intervalValue * 60;
            }

            if (intervalMinutes <= 0)
                intervalMinutes = 60;

            _waterRepository.SetReminderSettings(enabled, intervalMinutes);

            if (!TimeSpan.TryParse(WaterDayStartBox.Text, out var startTime) ||
                !TimeSpan.TryParse(WaterDayEndBox.Text, out var endTime))
            {
                var window = _waterRepository.GetDayWindowSettings();
                startTime = window.start;
                endTime = window.end;
            }

            _waterReminderService.ApplySettings(enabled, TimeSpan.FromMinutes(intervalMinutes), startTime, endTime);

            StatusText.Text =
                $"Water reminders {(enabled ? "enabled" : "disabled")}, interval {intervalMinutes} minutes.";
        }

        private void SaveWaterSettings_Click(object sender, RoutedEventArgs e)
        {
            // Goal
            if (!int.TryParse(WaterGoalBox.Text, out var goal) || goal <= 0)
            {
                MessageBox.Show(this, "Daily goal must be a positive number in ml.",
                    "Water goal", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _waterRepository.SetDailyGoal(goal);

            // Reminder enabled + interval
            bool enabled = WaterReminderEnabledCheckBox.IsChecked == true;

            if (!int.TryParse(WaterReminderIntervalBox.Text, out var intervalValue) || intervalValue <= 0)
            {
                MessageBox.Show(this, "Reminder interval must be a positive number.",
                    "Water reminder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int intervalMinutes;
            if (WaterReminderUnitBox.SelectedItem is ComboBoxItem item &&
                (item.Content as string) == "Hours")
            {
                intervalMinutes = intervalValue * 60;
            }
            else
            {
                intervalMinutes = intervalValue;
            }

            if (intervalMinutes <= 0) intervalMinutes = 60;

            _waterRepository.SetReminderSettings(enabled, intervalMinutes);

            // Day window
            if (!TimeSpan.TryParse(WaterDayStartBox.Text, out var startTime))
            {
                MessageBox.Show(this, "Day start time must be in format HH:mm.",
                    "Water day", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TimeSpan.TryParse(WaterDayEndBox.Text, out var endTime))
            {
                MessageBox.Show(this, "Day end time must be in format HH:mm.",
                    "Water day", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _waterRepository.SetDayWindowSettings(startTime, endTime);

            _waterReminderService.ApplySettings(enabled, TimeSpan.FromMinutes(intervalMinutes), startTime, endTime);

            UpdateWaterUIForSelectedDate();
            StatusText.Text =
                $"Water settings saved. Goal: {goal} ml, reminders every {intervalMinutes} minutes, window {startTime:hh\\:mm}–{endTime:hh\\:mm}.";
        }

        private void EndWaterDay_Click(object sender, RoutedEventArgs e)
        {
            _waterReminderService.EndToday();
            StatusText.Text = $"Today's water reminders ended. They will resume next day at {WaterDayStartBox.Text}.";
        }

        private void AddWaterAmount(int amount)
        {
            if (amount <= 0) return;

            var now = DateTime.Now;
            var timestamp = SelectedDate.Date
                .AddHours(now.Hour)
                .AddMinutes(now.Minute)
                .AddSeconds(now.Second);

            _waterRepository.AddEntry(amount, timestamp);
            UpdateWaterUIForSelectedDate();
            StatusText.Text = $"Added {amount} ml water for {SelectedDate:d}.";
        }

        private void AddWater100_Click(object sender, RoutedEventArgs e)
        {
            AddWaterAmount(100);
        }

        private void AddWater200_Click(object sender, RoutedEventArgs e)
        {
            AddWaterAmount(200);
        }

        private void AddWater300_Click(object sender, RoutedEventArgs e)
        {
            AddWaterAmount(300);
        }

        private void AddWaterCustom_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(CustomWaterBox.Text, out var amount) || amount <= 0)
            {
                MessageBox.Show(this, "Custom amount must be a positive number in ml.",
                    "Water amount", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AddWaterAmount(amount);
        }

        private void DeleteWaterEntry_Click(object sender, RoutedEventArgs e)
        {
            if (WaterEntriesListBox.SelectedItem is not WaterEntry selected)
            {
                MessageBox.Show(this, "Select a water entry to delete.",
                    "Delete water entry", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(this,
                $"Delete entry {selected.AmountMl} ml at {selected.Timestamp:t}?",
                "Delete water entry",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _waterRepository.DeleteEntry(selected.Id);
                UpdateWaterUIForSelectedDate();
                StatusText.Text = "Water entry deleted.";
            }
        }

        // ===== Notification settings UI =====

        private void LoadNotificationSettingsIntoUI()
        {
            var settings = _notificationSettingsRepository.Get();

            // Reminder popup
            ReminderPopupWidthBox.Text = settings.ReminderPopup.Width.ToString("F0");
            ReminderPopupHeightBox.Text = settings.ReminderPopup.Height.ToString("F0");
            SetPositionCombo(ReminderPositionBox, settings.ReminderPopup.Position);
            ReminderPlaySoundCheckBox.IsChecked = settings.ReminderPopup.PlaySound;

            // Water popup
            WaterPopupWidthBox.Text = settings.WaterPopup.Width.ToString("F0");
            WaterPopupHeightBox.Text = settings.WaterPopup.Height.ToString("F0");
            SetPositionCombo(WaterPositionBox, settings.WaterPopup.Position);
            WaterPlaySoundCheckBox.IsChecked = settings.WaterPopup.PlaySound;
        }

        private void SetPositionCombo(ComboBox combo, NotificationPosition position)
        {
            var target = position.ToString();
            foreach (ComboBoxItem item in combo.Items)
            {
                if ((item.Content as string) == target)
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
            combo.SelectedIndex = 0;
        }

        private NotificationPosition GetPositionFromCombo(ComboBox combo)
        {
            if (combo.SelectedItem is ComboBoxItem item && item.Content is string s)
            {
                if (Enum.TryParse<NotificationPosition>(s, out var pos))
                    return pos;
            }

            return NotificationPosition.BottomRight;
        }

        private void SaveNotificationSettings_Click(object sender, RoutedEventArgs e)
        {
            var settings = _notificationSettingsRepository.Get();

            // Reminder popup
            if (!double.TryParse(ReminderPopupWidthBox.Text, out var rW) || rW <= 0)
                rW = 320;
            if (!double.TryParse(ReminderPopupHeightBox.Text, out var rH) || rH <= 0)
                rH = 160;

            settings.ReminderPopup.Width = rW;
            settings.ReminderPopup.Height = rH;
            settings.ReminderPopup.Position = GetPositionFromCombo(ReminderPositionBox);
            settings.ReminderPopup.PlaySound = ReminderPlaySoundCheckBox.IsChecked == true;

            // Water popup
            if (!double.TryParse(WaterPopupWidthBox.Text, out var wW) || wW <= 0)
                wW = 320;
            if (!double.TryParse(WaterPopupHeightBox.Text, out var wH) || wH <= 0)
                wH = 160;

            settings.WaterPopup.Width = wW;
            settings.WaterPopup.Height = wH;
            settings.WaterPopup.Position = GetPositionFromCombo(WaterPositionBox);
            settings.WaterPopup.PlaySound = WaterPlaySoundCheckBox.IsChecked == true;

            _notificationSettingsRepository.Update(settings);

            StatusText.Text = "Notification settings saved.";
        }

        // ===== Notification test buttons =====

        private void TestReminderNotification_Click(object sender, RoutedEventArgs e)
        {
            var testReminder = new Reminder
            {
                Title = "Test Reminder",
                Message = "This is how reminder notifications look with current settings.",
                ScheduledTime = DateTime.Now,
                NextTriggerTime = DateTime.Now,
                NotificationSettings = new NotificationSettings()
            };

            _notificationService.ShowReminder(testReminder);
            StatusText.Text = "Test reminder notification sent.";
            ;
        }

        private void TestWaterNotification_Click(object sender, RoutedEventArgs e)
        {
            const int testAmountMl = 250;

            _notificationService.ShowWaterReminder(testAmountMl, () =>
            {
                // Testte loglama istemiyorsan boş bırak.
                // Eğer testte bile su loglansın istersen:
                // _waterRepository.AddEntry(testAmountMl, DateTime.Now);
                // UpdateWaterUIForSelectedDate();
            });

            StatusText.Text = "Test water notification sent.";
        }
    }
}
