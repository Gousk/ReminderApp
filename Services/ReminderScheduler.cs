using System;
using System.Windows.Threading;
using ReminderApp.Models;

namespace ReminderApp.Services
{
    public class ReminderScheduler
    {
        private readonly IReminderRepository _repository;
        private readonly NotificationService _notificationService;
        private readonly DispatcherTimer _timer;

        public ReminderScheduler(IReminderRepository repository, NotificationService notificationService)
        {
            _repository = repository;
            _notificationService = notificationService;

            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timer.Tick += TimerOnTick;
        }

        public void Start() => _timer.Start();
        public void Stop() => _timer.Stop();

        private void TimerOnTick(object? sender, EventArgs e)
        {
            var now = DateTime.Now;
            var dueReminders = _repository.GetDueReminders(now);

            foreach (var reminder in dueReminders)
            {
                _notificationService.ShowReminder(reminder);

                if (reminder.Type == ReminderType.OneTime || reminder.RepeatInterval == null)
                {
                    reminder.IsActive = false;
                }
                else
                {
                    reminder.NextTriggerTime = now.Add(reminder.RepeatInterval.Value);
                }

                _repository.Update(reminder);
            }
        }
    }
}
