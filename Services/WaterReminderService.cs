using System;
using System.Linq;
using System.Windows.Threading;
using ReminderApp.Models;

namespace ReminderApp.Services
{
    public class WaterReminderService
    {
        private readonly WaterRepository _waterRepository;
        private readonly NotificationService _notificationService;

        private readonly object _lock = new();
        private readonly DispatcherTimer _tickTimer;

        private bool _enabled;
        private TimeSpan _interval;
        private TimeSpan _dayStart;
        private TimeSpan _dayEnd;
        private DateTime? _manualEndUntil;
        private DateTime? _nextReminderAt;

        public WaterReminderService(WaterRepository waterRepository, NotificationService notificationService)
        {
            _waterRepository = waterRepository;
            _notificationService = notificationService;
            _manualEndUntil = _waterRepository.GetManualEndUntil();

            _tickTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _tickTimer.Tick += (_, _) => Tick();
            _tickTimer.Start();
        }

        public void ApplySettings(bool enabled, TimeSpan interval, TimeSpan dayStart, TimeSpan dayEnd)
        {
            lock (_lock)
            {
                _enabled = enabled;
                _interval = interval <= TimeSpan.Zero ? TimeSpan.FromMinutes(60) : interval;
                _dayStart = dayStart;
                _dayEnd = dayEnd;
                _nextReminderAt = null;
            }
        }

        public void EndToday()
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                var nextStart = ComputeNextDayStart(now, _dayStart);
                _manualEndUntil = nextStart;
                _waterRepository.SetManualEndUntil(_manualEndUntil);
                _nextReminderAt = _manualEndUntil;
            }
        }

        public void ResetManualEnd()
        {
            lock (_lock)
            {
                _manualEndUntil = null;
                _waterRepository.SetManualEndUntil(null);
                _nextReminderAt = null;
            }
        }

        private void Tick()
        {
            try
            {
                EvaluateAndMaybeNotify();
            }
            catch
            {
                // Swallow to keep timer alive
            }
        }

        private void EvaluateAndMaybeNotify()
        {
            lock (_lock)
            {
                var now = DateTime.Now;

                if (!_enabled || _interval <= TimeSpan.Zero)
                {
                    _nextReminderAt = null;
                    return;
                }

                if (_manualEndUntil.HasValue)
                {
                    if (now < _manualEndUntil.Value)
                    {
                        _nextReminderAt = _manualEndUntil;
                        return;
                    }

                    _manualEndUntil = null;
                    _waterRepository.SetManualEndUntil(null);
                    _nextReminderAt = null;
                }

                var (dayStart, dayEnd, inWindow) = GetCurrentWaterDayRange(now);
                if (!inWindow)
                {
                    if (now < dayStart)
                    {
                        _nextReminderAt = dayStart;
                    }
                    else
                    {
                        _nextReminderAt = ComputeNextDayStart(now, _dayStart);
                    }
                    return;
                }

                var goal = _waterRepository.GetDailyGoal();
                if (goal <= 0)
                {
                    _nextReminderAt = now.AddMinutes(10);
                    return;
                }

                var entries = _waterRepository.GetEntriesInRange(dayStart, dayEnd);
                var alreadyDrunk = entries.Sum(e => e.AmountMl);
                var remaining = goal - alreadyDrunk;

                if (remaining <= 0)
                {
                    _nextReminderAt = ComputeNextDayStart(now, _dayStart);
                    return;
                }

                if (!_nextReminderAt.HasValue)
                {
                    _nextReminderAt = now;
                }

                if (now < _nextReminderAt.Value)
                {
                    return;
                }

                var remainingMinutes = Math.Max(1, (int)(dayEnd - now).TotalMinutes);
                var intervalMinutes = Math.Max(1, (int)_interval.TotalMinutes);
                var remindersLeft = Math.Max(1, (int)Math.Ceiling(remainingMinutes / (double)intervalMinutes));

                var rawAmount = (int)Math.Ceiling((double)remaining / remindersLeft);
                var amount = RoundUpToStep(rawAmount, 50);
                if (amount > remaining) amount = remaining;
                if (amount <= 0)
                {
                    _nextReminderAt = now.AddMinutes(intervalMinutes);
                    return;
                }

                var remainingAfter = Math.Max(0, remaining - amount);

                _notificationService.ShowWaterReminder(
                    amount,
                    remainingAfter,
                    goal,
                    () => ConfirmDrink(amount),
                    () => SkipUntilNext(intervalMinutes));

                _nextReminderAt = now.Add(_interval);
            }
        }

        private void ConfirmDrink(int amount)
        {
            lock (_lock)
            {
                _waterRepository.AddEntry(amount, DateTime.Now);
                _nextReminderAt = DateTime.Now.Add(_interval);
            }
        }

        private void SkipUntilNext(int intervalMinutes)
        {
            lock (_lock)
            {
                _nextReminderAt = DateTime.Now.AddMinutes(intervalMinutes);
            }
        }

        private DateTime ComputeNextDayStart(DateTime now, TimeSpan dayStart)
        {
            var todayStart = now.Date + dayStart;
            if (now < todayStart)
                return todayStart;
            return todayStart.AddDays(1);
        }

        private int RoundUpToStep(int value, int step)
        {
            if (value <= 0) return 0;
            return ((value + step - 1) / step) * step;
        }

        private (DateTime start, DateTime end, bool inWindow) GetCurrentWaterDayRange(DateTime now)
        {
            var today = now.Date;
            bool crossesMidnight = _dayEnd <= _dayStart;

            if (!crossesMidnight)
            {
                var start = today + _dayStart;
                var end = today + _dayEnd;

                if (now < start)
                {
                    return (start, end, false);
                }

                if (now >= start && now < end)
                {
                    return (start, end, true);
                }

                var nextStart = today.AddDays(1) + _dayStart;
                var nextEnd = today.AddDays(1) + _dayEnd;
                return (nextStart, nextEnd, false);
            }
            else
            {
                var todayStart = today + _dayStart;
                var todayEndNext = today.AddDays(1) + _dayEnd;

                var yesterdayStart = today.AddDays(-1) + _dayStart;
                var yesterdayEnd = today + _dayEnd;

                if (now >= yesterdayStart && now < yesterdayEnd)
                {
                    return (yesterdayStart, yesterdayEnd, true);
                }

                if (now >= todayStart && now < todayEndNext)
                {
                    return (todayStart, todayEndNext, true);
                }

                if (now < todayStart)
                {
                    return (todayStart, todayEndNext, false);
                }
                else
                {
                    var nextStart = today.AddDays(1) + _dayStart;
                    var nextEnd = today.AddDays(2) + _dayEnd;
                    return (nextStart, nextEnd, false);
                }
            }
        }
    }
}
