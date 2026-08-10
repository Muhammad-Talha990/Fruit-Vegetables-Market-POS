using System;
using System.Collections.Generic;
using System.Linq;
using FruitVegetableMarketPOS.Data.Repositories;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Services
{
    public class DailyItemSelectionService
    {
        public const string LastPosMenuDateKey = "LastPosMenuDate";

        private readonly DailyItemSelectionRepository _repo;

        public DailyItemSelectionService(DailyItemSelectionRepository repo)
        {
            _repo = repo;
        }

        public string CurrentBusinessDate => DateTimeHelper.GetBusinessDate();

        public List<DailyItemSelection> GetVisibleForToday()
            => _repo.GetVisibleForDate(CurrentBusinessDate);

        public List<DailyItemSelection> GetVisibleForDate(string businessDate)
            => _repo.GetVisibleForDate(businessDate);

        public List<DailyItemSetRow> GetDailyItemSetForDate(string businessDate)
            => _repo.GetDailyItemSetForDate(businessDate);

        public int AddItem(int itemId, int? userId)
            => _repo.AddItem(CurrentBusinessDate, itemId, userId);

        public int AddItem(string businessDate, int itemId, int? userId)
            => _repo.AddItem(businessDate, itemId, userId);

        /// <summary>True if this catalog item is already on today's selling list.</summary>
        public bool IsOnTodayMenu(int itemId)
            => _repo.HasActiveRow(CurrentBusinessDate, itemId);

        public void RemoveItem(int dailySelectionId)
            => _repo.RemoveItem(dailySelectionId);

        public void SetAvailable(int dailySelectionId, bool isAvailable)
            => _repo.SetAvailable(dailySelectionId, isAvailable);

        public string? GetPreviousMenuDate()
            => _repo.GetPreviousMenuDate(CurrentBusinessDate);

        public bool IsDaySetupDone()
        {
            var last = _repo.GetAppSetting(LastPosMenuDateKey);
            return string.Equals(last, CurrentBusinessDate, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when a previous day's menu exists, today is still empty,
        /// and cashier has not finished Continue/Refresh for today.
        /// </summary>
        public bool NeedsNewDaySetup()
        {
            if (IsDaySetupDone())
                return false;

            if (GetVisibleForToday().Count > 0)
                return false;

            return !string.IsNullOrWhiteSpace(GetPreviousMenuDate());
        }

        public List<PreviousDayMenuItem> GetPreviousDayMenuItems()
        {
            var prevDate = GetPreviousMenuDate();
            if (string.IsNullOrWhiteSpace(prevDate))
                return new List<PreviousDayMenuItem>();

            return _repo.GetVisibleForDate(prevDate)
                .GroupBy(s => s.ItemId)
                .Select(g => g.First())
                .Select(s => new PreviousDayMenuItem
                {
                    ItemId = s.ItemId,
                    Name = s.ItemDescription ?? $"Item #{s.ItemId}",
                    NameUrdu = s.ItemNameUrdu,
                    IsSelected = true
                })
                .OrderBy(i => i.Name)
                .ToList();
        }

        public void MarkDaySetupDone()
            => _repo.SetAppSetting(LastPosMenuDateKey, CurrentBusinessDate);

        /// <summary>Clear today's menu and mark new-day setup complete.</summary>
        public void RefreshStartFresh(int? userId)
        {
            _repo.ClearForDate(CurrentBusinessDate);
            MarkDaySetupDone();
        }

        /// <summary>Replace today's menu with the checked previous-day items.</summary>
        public int ContinueWithSelected(IEnumerable<int> itemIds, int? userId)
        {
            var today = CurrentBusinessDate;
            _repo.ClearForDate(today);

            int added = 0;
            foreach (var itemId in itemIds.Distinct())
            {
                try
                {
                    _repo.AddItem(today, itemId, userId);
                    added++;
                }
                catch (InvalidOperationException)
                {
                    // already on today's list — skip
                }
            }

            MarkDaySetupDone();
            return added;
        }
    }
}
