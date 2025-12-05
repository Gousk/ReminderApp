using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReminderApp.Models;

namespace ReminderApp.Services
{
    public class WaterReminderService
    {
        private readonly WaterRepository _waterRepository;
        private readonly NotificationService _notificationService;

        private readonly object _lock = new();
        private CancellationTokenSource _cts = new();
        private Task _runner = Task.CompletedTask;

        private bool _enabled;
        private TimeSpan _interval;
        private TimeSpan _dayStart;
        private TimeSpan _dayEnd;
        private DateTime? _manualEndUntil;

        public WaterReminderService(WaterRepository waterRepository, NotificationService notificationService)
        {
            _waterRepository = waterRepository;
            _notificationService = notificationService;
            _manualEndUntil = _waterRepository.GetManualEndUntil();
            StartLoop();
        }

        public void ApplySettings(bool enabled, TimeSpan interval, TimeSpan dayStart, TimeSpan dayEnd)
        {
            lock (_lock)
            {
                _enabled = enabled;
                _interval = interval <= TimeSpan.Zero ? TimeSpan.FromMinutes(60) : interval;
                _dayStart = dayStart;
                _dayEnd = dayEnd;
            }

            RestartLoop();
        }

        public void EndToday()
        {
            lock (_lock)
            {
                var now = DateTime.Now;
                var nextStart = ComputeNextDayStart(now, _dayStart);
                _manualEndUntil = nextStart;
                _waterRepository.SetManualEndUntil(_manualEndUntil);
            }

            RestartLoop();
        }

        public void ResetManualEnd()
        {
            lock (_lock)
            {
                _manualEndUntil = null;
                _waterRepository.SetManualEndUntil(null);
            }

            RestartLoop();
        }

        private void RestartLoop()
        {
            lock (_lock)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = new CancellationTokenSource();
                StartLoopInternal();
            }
        }

        private void StartLoop()
        {
            lock (_lock)
            {
                StartLoopInternal();
            }
        }

        private void StartLoopInternal()
        {
            var token = _cts.Token;
            _runner = Task.Run(async () => await RunAsync(token), token);
        }

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                TimeSpan delay;
                try
                {
                    delay = EvaluateAndMaybeNotify();
                }
                catch
                {
                    delay = TimeSpan.FromMinutes(5);
                }

                try
                {
                    await Task.Delay(delay, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private TimeSpan EvaluateAndMaybeNotify()
        {
            lock (_lock)
            {
                var now = DateTime.Now;

                if (!_enabled || _interval <= TimeSpan.Zero)
                {
                    return TimeSpan.FromMinutes(5);
                }

                if (_manualEndUntil.HasValue)
                {
                    if (now < _manualEndUntil.Value)
                    {
                        return ClampDelay(_manualEndUntil.Value - now);
                    }

                    _manualEndUntil = null;
                    _waterRepository.SetManualEndUntil(null);
                }

                var (dayStart, dayEnd, inWindow) = GetCurrentWaterDayRange(now);
                if (!inWindow)
                {
                    if (now < dayStart)
                    {
                        return ClampDelay(dayStart - now);
                    }

                    var nextStart = ComputeNextDayStart(now, _dayStart);
                    return ClampDelay(nextStart - now);
                }

                var goal = _waterRepository.GetDailyGoal();
                if (goal <= 0)
                {
                    return TimeSpan.FromMinutes(10);
                }

                var entries = _waterRepository.GetEntriesInRange(dayStart, dayEnd);
                var alreadyDrunk = entries.Sum(e => e.AmountMl);
                var remaining = goal - alreadyDrunk;

                if (remaining <= 0)
                {
                    var nextStart = ComputeNextDayStart(now, _dayStart);
                    return ClampDelay(nextStart - now);
                }

                var remainingMinutes = Math.Max(1, (int)(dayEnd - now).TotalMinutes);
                var intervalMinutes = Math.Max(1, (int)_interval.TotalMinutes);
                var remindersLeft = Math.Max(1, (int)Math.Ceiling(remainingMinutes / (double)intervalMinutes));

                var rawAmount = (int)Math.Ceiling((double)remaining / remindersLeft);
                var amount = RoundUpToStep(rawAmount, 50);
                if (amount > remaining) amount = remaining;
                if (amount <= 0)
                {
                    return TimeSpan.FromMinutes(intervalMinutes);
                }

                var remainingAfter = Math.Max(0, remaining - amount);

                _notificationService.ShowWaterReminder(
                    amount,
                    remainingAfter,
                    goal,
                    () => _waterRepository.AddEntry(amount, DateTime.Now),
                    null);

                return TimeSpan.FromMinutes(intervalMinutes);
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

        private TimeSpan ClampDelay(TimeSpan delay)
        {
            if (delay < TimeSpan.FromSeconds(15))
                return TimeSpan.FromSeconds(15);

            if (delay > TimeSpan.FromHours(6))
                return TimeSpan.FromHours(6);

            return delay;
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
