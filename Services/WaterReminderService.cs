using System;
using System.Linq;
using System.Timers;
using ReminderApp.Models;

namespace ReminderApp.Services
{
    public class WaterReminderService
    {
        private readonly WaterRepository _waterRepository;
        private readonly NotificationService _notificationService;
        private readonly Timer _timer;
        private bool _isTickRunning;

        private bool _enabled;
        private TimeSpan _interval;
        private TimeSpan _dayStart;
        private TimeSpan _dayEnd;

        private DateTime? _manualEndUntil;
        private DateTime _nextTriggerTime = DateTime.MaxValue;

        public WaterReminderService(WaterRepository waterRepository, NotificationService notificationService)
        {
            _waterRepository = waterRepository;
            _notificationService = notificationService;

            _manualEndUntil = _waterRepository.GetManualEndUntil();

            _timer = new Timer
            {
                Interval = 1000,
                AutoReset = true,
                Enabled = true
            };

            _timer.Elapsed += TimerOnTick;
            _timer.Start();
        }

        /// <summary>
        /// Tüm ayarları dışarıdan güncelliyoruz:
        /// - enabled: hatırlatma açık mı
        /// - interval: kaç dakikada bir
        /// - dayStart / dayEnd: gün penceresi (örn. 09:00–02:00)
        /// </summary>
        public void ApplySettings(bool enabled, TimeSpan interval, TimeSpan dayStart, TimeSpan dayEnd)
        {
            _enabled = enabled;
            _interval = interval.TotalMinutes <= 0 ? TimeSpan.FromMinutes(60) : interval;
            _dayStart = dayStart;
            _dayEnd = dayEnd;

            if (_enabled)
                _nextTriggerTime = DateTime.Now.Add(_interval);
            else
                _nextTriggerTime = DateTime.MaxValue;
        }

        /// <summary>
        /// "Bugünlük suyu bitir" butonu.
        /// Bugünkü su günü biter, reminder'lar bir SONRAKİ gün başlangıcına kadar durur.
        /// </summary>
        public void EndToday()
        {
            var now = DateTime.Now;
            var nextStart = ComputeNextDayStart(now, _dayStart);
            _manualEndUntil = nextStart;
            _waterRepository.SetManualEndUntil(_manualEndUntil);
        }

        private DateTime ComputeNextDayStart(DateTime now, TimeSpan dayStart)
        {
            var todayStart = now.Date + dayStart;
            if (now < todayStart)
                return todayStart;
            return todayStart.AddDays(1);
        }

        private void TimerOnTick(object? sender, EventArgs e)
        {
            if (_isTickRunning)
                return;

            _isTickRunning = true;

            if (!_enabled || _interval <= TimeSpan.Zero)
            {
                _isTickRunning = false;
                return;
            }

            var now = DateTime.Now;

            // Eğer "bugünlük suyu bitir" aktifse ve halen süresini doldurmadıysa, hiçbir şey yapma.
            if (_manualEndUntil.HasValue)
            {
                if (now < _manualEndUntil.Value)
                {
                    _isTickRunning = false;
                    return;
                }

                // Süre doldu, ertesi gün başladı → tekrar normal çalışmaya dönebiliriz.
                _manualEndUntil = null;
                _waterRepository.SetManualEndUntil(null);
            }

            var (dayStart, dayEnd, inWindow) = GetCurrentWaterDayRange(now);

            if (!inWindow)
            {
                // Gün penceresi dışındaysak su hatırlatması yapmıyoruz.
                // Eğer pencere henüz başlamadıysa first trigger'ı pencere başlangıcının sonrasına koyabiliriz.
                if (now < dayStart)
                {
                    _nextTriggerTime = dayStart.Add(_interval);
                }
                else
                {
                    _nextTriggerTime = DateTime.MaxValue;
                }
                _isTickRunning = false;
                return;
            }

            // Pencerenin içindeyiz.
            if (_nextTriggerTime == DateTime.MaxValue || _nextTriggerTime < now || _nextTriggerTime >= dayEnd)
            {
                _nextTriggerTime = now.Add(_interval);
            }

            if (now >= _nextTriggerTime && now < dayEnd)
            {
                TriggerWaterReminder(now, dayStart, dayEnd);
                _nextTriggerTime = now.Add(_interval);
            }

            _isTickRunning = false;
        }

        /// <summary>
        /// dayStart / dayEnd: bu su gününün gerçek zaman aralığı
        /// </summary>
        private void TriggerWaterReminder(DateTime now, DateTime dayStart, DateTime dayEnd)
        {
            var goal = _waterRepository.GetDailyGoal();
            if (goal <= 0) return;

            // Bu su gününde (dayStart–dayEnd) içilen toplam
            var entries = _waterRepository.GetEntriesInRange(dayStart, dayEnd);
            var alreadyDrunk = entries.Sum(e => e.AmountMl);
            var remaining = goal - alreadyDrunk;

            if (remaining <= 0)
            {
                // Hedefe ulaşılmış, artık hatırlatma yapma
                return;
            }

            var remainingMinutes = (int)(dayEnd - now).TotalMinutes;
            if (remainingMinutes <= 0)
                return;

            var intervalMinutes = (int)_interval.TotalMinutes;
            if (intervalMinutes <= 0) intervalMinutes = 60;

            int remainingReminders = Math.Max(1, remainingMinutes / intervalMinutes);

            var rawAmount = (int)Math.Ceiling((double)remaining / remainingReminders);

            // 100 ml adımına yukarı yuvarla
            int amount = RoundUpToStep(rawAmount, 100);
            if (amount > remaining) amount = remaining;
            if (amount <= 0) return;

            _notificationService.ShowWaterReminder(amount, () =>
            {
                _waterRepository.AddEntry(amount, DateTime.Now);
            });
        }

        private int RoundUpToStep(int value, int step)
        {
            if (value <= 0) return 0;
            return ((value + step - 1) / step) * step;
        }

        /// <summary>
        /// Şu anki zamana göre su gününün start/end aralığını ve
        /// şu an bu pencerenin içinde miyiz bilgisini döndürür.
        /// Cross-midnight (örn. 09:00–02:00) durumları da destekler.
        /// </summary>
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

                // Gün penceresi bitti, bir sonraki gün
                var nextStart = today.AddDays(1) + _dayStart;
                var nextEnd = today.AddDays(1) + _dayEnd;
                return (nextStart, nextEnd, false);
            }
            else
            {
                // Örn: 09:00–02:00
                // İki pencere olabilir:
                // 1) Dün 09:00 → Bugün 02:00
                // 2) Bugün 09:00 → Yarın 02:00

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

                // Pencere dışında: sıradaki pencereyi tahmin et
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
