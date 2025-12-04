using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ReminderApp.Models;

namespace ReminderApp.UI
{
    public partial class ReminderEditorWindow : Window
    {
        public Reminder Reminder { get; private set; }

        public ReminderEditorWindow(DateTime initialDate)
            : this(new Reminder
            {
                ScheduledTime = initialDate.Date.AddHours(DateTime.Now.Hour).AddMinutes(5),
                NextTriggerTime = initialDate.Date.AddHours(DateTime.Now.Hour).AddMinutes(5),
                MinutesBefore = 0
            })
        {
        }

        public ReminderEditorWindow(Reminder reminder)
        {
            InitializeComponent();
            Reminder = reminder;

            TitleBox.Text = Reminder.Title;
            MessageTextBox.Text = Reminder.Message;

            DatePicker.SelectedDate = Reminder.ScheduledTime == default
                ? DateTime.Now.Date
                : Reminder.ScheduledTime.Date;

            TimeBox.Text = Reminder.ScheduledTime == default
                ? DateTime.Now.ToString("HH:mm")
                : Reminder.ScheduledTime.ToString("HH:mm");

            MinutesBeforeBox.Text = Reminder.MinutesBefore.ToString();

            // Repeat settings
            if (Reminder.Type == ReminderType.Repeating && Reminder.RepeatInterval.HasValue)
            {
                RepeatCheckBox.IsChecked = true;
                var interval = Reminder.RepeatInterval.Value;
                // Try to guess the unit
                if (interval.TotalMinutes % (60 * 24) == 0)
                {
                    // days
                    RepeatUnitBox.SelectedIndex = 2;
                    RepeatIntervalBox.Text = (interval.TotalDays).ToString("0");
                }
                else if (interval.TotalMinutes % 60 == 0)
                {
                    // hours
                    RepeatUnitBox.SelectedIndex = 1;
                    RepeatIntervalBox.Text = (interval.TotalHours).ToString("0");
                }
                else
                {
                    // minutes
                    RepeatUnitBox.SelectedIndex = 0;
                    RepeatIntervalBox.Text = (interval.TotalMinutes).ToString("0");
                }
            }
            else
            {
                RepeatCheckBox.IsChecked = false;
                RepeatIntervalBox.Text = string.Empty;
            }

            // Level
            switch (Reminder.NotificationSettings.Level)
            {
                case NotificationLevel.Normal:
                    LevelBox.SelectedIndex = 0;
                    break;
                case NotificationLevel.Important:
                    LevelBox.SelectedIndex = 1;
                    break;
                case NotificationLevel.Critical:
                    LevelBox.SelectedIndex = 2;
                    break;
            }

            PlaySoundCheckBox.IsChecked = Reminder.NotificationSettings.PlaySound;
            SoundPathText.Text = string.IsNullOrWhiteSpace(Reminder.NotificationSettings.SoundPath)
                ? "(Default sound)"
                : Reminder.NotificationSettings.SoundPath;
        }

        private void ChooseSound_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "WAV files (*.wav)|*.wav|All files (*.*)|*.*"
            };

            if (dlg.ShowDialog() == true)
            {
                Reminder.NotificationSettings.SoundPath = dlg.FileName;
                SoundPathText.Text = dlg.FileName;
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TitleBox.Text))
            {
                MessageBox.Show(this, "Title is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (DatePicker.SelectedDate == null)
            {
                MessageBox.Show(this, "Date is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!TimeSpan.TryParse(TimeBox.Text, out var timeOfDay))
            {
                MessageBox.Show(this, "Time must be in format HH:mm.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var date = DatePicker.SelectedDate.Value;

            // Minutes-before
            int minutesBefore = 0;
            if (!string.IsNullOrWhiteSpace(MinutesBeforeBox.Text) &&
                !int.TryParse(MinutesBeforeBox.Text, out minutesBefore))
            {
                MessageBox.Show(this, "Minutes before must be a number.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (minutesBefore < 0) minutesBefore = 0;

            // Repeat
            bool isRepeating = RepeatCheckBox.IsChecked == true;
            TimeSpan? repeatInterval = null;

            if (isRepeating)
            {
                if (string.IsNullOrWhiteSpace(RepeatIntervalBox.Text) ||
                    !int.TryParse(RepeatIntervalBox.Text, out var intervalValue) ||
                    intervalValue <= 0)
                {
                    MessageBox.Show(this, "Repeat interval must be a positive number.", "Validation",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var unitItem = RepeatUnitBox.SelectedItem as ComboBoxItem;
                var unitText = (unitItem?.Content as string) ?? "Minutes";

                repeatInterval = unitText switch
                {
                    "Days" => TimeSpan.FromDays(intervalValue),
                    "Hours" => TimeSpan.FromHours(intervalValue),
                    _ => TimeSpan.FromMinutes(intervalValue)
                };
            }

            var scheduled = date.Date + timeOfDay;
            var offset = TimeSpan.FromMinutes(minutesBefore);
            var firstTrigger = scheduled - offset;

            // If trigger time is already in the past, fall back to scheduled time
            if (firstTrigger < DateTime.Now)
                firstTrigger = scheduled;

            Reminder.Title = TitleBox.Text.Trim();
            Reminder.Message = MessageTextBox.Text.Trim();
            Reminder.ScheduledTime = scheduled;
            Reminder.MinutesBefore = minutesBefore;
            Reminder.NextTriggerTime = firstTrigger;
            Reminder.IsActive = true;

            if (isRepeating && repeatInterval.HasValue)
            {
                Reminder.Type = ReminderType.Repeating;
                Reminder.RepeatInterval = repeatInterval;
            }
            else
            {
                Reminder.Type = ReminderType.OneTime;
                Reminder.RepeatInterval = null;
            }

            // Level
            NotificationLevel level = NotificationLevel.Normal;
            if (LevelBox.SelectedItem is ComboBoxItem levelItem)
            {
                var text = levelItem.Content as string;
                level = text switch
                {
                    "Important" => NotificationLevel.Important,
                    "Critical" => NotificationLevel.Critical,
                    _ => NotificationLevel.Normal
                };
            }

            Reminder.NotificationSettings.Level = level;
            Reminder.NotificationSettings.PlaySound = PlaySoundCheckBox.IsChecked == true;
            Reminder.NotificationSettings.UseOverlay = true;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
