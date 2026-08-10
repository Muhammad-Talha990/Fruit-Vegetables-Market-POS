using FruitVegetableMarketPOS.Data.Repositories;
using FruitVegetableMarketPOS.Helpers;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Services
{
    public class DailyClosingService
    {
        private readonly DailyClosingRepository _closingRepo;
        private readonly BillRepository _billRepo;

        public DailyClosingService(DailyClosingRepository closingRepo, BillRepository billRepo)
        {
            _closingRepo = closingRepo;
            _billRepo = billRepo;
        }

        public DailyClosing? GetByDate(string businessDate)
            => _closingRepo.GetByDate(businessDate);

        public DailyClosing? GetToday()
            => _closingRepo.GetByDate(DateTimeHelper.GetBusinessDate());

        public bool IsClosed(string businessDate)
            => _closingRepo.IsClosed(businessDate);

        public DailyClosing ComputeForDate(string businessDate)
            => _billRepo.ComputeDailyAggregates(businessDate);

        public DailyClosing CloseDay(string businessDate, int? userId, string? notes = null)
        {
            var aggregates = _billRepo.ComputeDailyAggregates(businessDate);
            aggregates.ClosedByUserId = userId;
            aggregates.Notes = notes;
            aggregates.Status = "Closed";
            aggregates.ClosedAt = DateTimeHelper.CaptureTransactionTime();
            return _closingRepo.CloseDay(aggregates);
        }

        public DailyClosing UpsertOpenDay(string businessDate)
        {
            var aggregates = _billRepo.ComputeDailyAggregates(businessDate);
            aggregates.Status = "Open";
            return _closingRepo.Upsert(aggregates);
        }
    }
}
