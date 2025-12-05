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

        private string _lastStatus = "Waiting for settings...";
        private int? _lastSuggestedAmount;

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
                    _lastStatus = "Disabled or missing interval.";
                    return;
                }

                if (_manualEndUntil.HasValue)
                {
                    if (now < _manualEndUntil.Value)
                    {
                        _nextReminderAt = _manualEndUntil;
                        _lastStatus = $"Manually ended until {_manualEndUntil:HH:mm}.";
                        return;
                    }

                    _manualEndUntil = null;
                    _waterRepository.SetManualEndUntil(null);
                    _nextReminderAt = null;
                    _lastStatus = "Manual end expired; resuming.";
                }

                var (dayStart, dayEnd, inWindow) = GetCurrentWaterDayRange(now);
                if (!inWindow)
                {
                    if (now < dayStart)
                    {
                        _nextReminderAt = dayStart;
                        _lastStatus = $"Waiting for window start at {dayStart:HH:mm}.";
                    }
                    else
                    {
                        _nextReminderAt = ComputeNextDayStart(now, _dayStart);
                        _lastStatus = $"Window closed; next day start {_nextReminderAt:HH:mm}.";
                    }
                    return;
                }

                var goal = _waterRepository.GetDailyGoal();
                if (goal <= 0)
                {
                    _nextReminderAt = now.AddMinutes(10);
                    _lastStatus = "No daily goal set.";
                    return;
                }

                var entries = _waterRepository.GetEntriesInRange(dayStart, dayEnd);
                var alreadyDrunk = entries.Sum(e => e.AmountMl);
                var remaining = goal - alreadyDrunk;

                if (remaining <= 0)
                {
                    _nextReminderAt = ComputeNextDayStart(now, _dayStart);
                    _lastStatus = "Goal reached; waiting for next day.";
                    return;
                }

                if (!_nextReminderAt.HasValue)
                {
                    _nextReminderAt = now;
                    _lastStatus = "Scheduling first reminder now.";
                }

                if (now < _nextReminderAt.Value)
                {
                    _lastStatus = $"Waiting until {_nextReminderAt:HH:mm}.";
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
                    _lastStatus = "Computed amount <= 0; pushing next check.";
                    return;
                }

                var remainingAfter = Math.Max(0, remaining - amount);

                _lastSuggestedAmount = amount;
                _notificationService.ShowWaterReminder(
                    amount,
                    remainingAfter,
                    goal,
                    () => ConfirmDrink(amount),
                    () => SkipUntilNext(intervalMinutes));

                _nextReminderAt = now.Add(_interval);
                _lastStatus = $"Notified {amount} ml; next at {_nextReminderAt:HH:mm}.";
            }
        }

        private void ConfirmDrink(int amount)
        {
            lock (_lock)
            {
                _waterRepository.AddEntry(amount, DateTime.Now);
                _nextReminderAt = DateTime.Now.Add(_interval);
                _lastStatus = $"Logged {amount} ml; next at {_nextReminderAt:HH:mm}.";
            }
        }

        private void SkipUntilNext(int intervalMinutes)
        {
            lock (_lock)
            {
                _nextReminderAt = DateTime.Now.AddMinutes(intervalMinutes);
                _lastStatus = $"Skipped; next at {_nextReminderAt:HH:mm}.";
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

        public WaterDebugInfo GetDebugInfo()
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                var (windowStart, windowEnd, inWindow) = GetCurrentWaterDayRange(now);
                var goal = _waterRepository.GetDailyGoal();

                var entries = _waterRepository.GetEntriesInRange(windowStart, windowEnd);
                var total = entries.Sum(e => e.AmountMl);
                var remaining = Math.Max(0, goal - total);

                TimeSpan? countdown = null;
                if (_nextReminderAt.HasValue)
                {
                    var span = _nextReminderAt.Value - now;
                    countdown = span < TimeSpan.Zero ? TimeSpan.Zero : span;
                }

                return new WaterDebugInfo
                {
                    Enabled = _enabled,
                    Interval = _interval,
                    DayStart = _dayStart,
                    DayEnd = _dayEnd,
                    ManualEndUntil = _manualEndUntil,
                    NextReminderAt = _nextReminderAt,
                    WindowStart = windowStart,
                    WindowEnd = windowEnd,
                    InWindow = inWindow,
                    DailyGoal = goal,
                    TotalToday = total,
                    Remaining = remaining,
                    Countdown = countdown,
                    Status = _lastStatus,
                    LastSuggestedAmount = _lastSuggestedAmount,
                    Now = now
                };
            }
        }
    }

    public class WaterDebugInfo
    {
        public bool Enabled { get; init; }
        public TimeSpan Interval { get; init; }
        public TimeSpan DayStart { get; init; }
        public TimeSpan DayEnd { get; init; }
        public DateTime? ManualEndUntil { get; init; }
        public DateTime? NextReminderAt { get; init; }
        public DateTime WindowStart { get; init; }
        public DateTime WindowEnd { get; init; }
        public bool InWindow { get; init; }
        public int DailyGoal { get; init; }
        public int TotalToday { get; init; }
        public int Remaining { get; init; }
        public TimeSpan? Countdown { get; init; }
        public string Status { get; init; } = string.Empty;
        public int? LastSuggestedAmount { get; init; }
        public DateTime Now { get; init; }
    }
}
