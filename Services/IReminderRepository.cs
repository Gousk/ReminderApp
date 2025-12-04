using System;
using System.Collections.Generic;
using ReminderApp.Models;

namespace ReminderApp.Services
{
    public interface IReminderRepository
    {
        IEnumerable<Reminder> GetAll();
        IEnumerable<Reminder> GetDueReminders(DateTime now);
        IEnumerable<Reminder> GetByDate(DateTime date);

        void Add(Reminder reminder);
        void Update(Reminder reminder);
        void Delete(Guid id);
    }
}
