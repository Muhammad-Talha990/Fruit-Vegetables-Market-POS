using System;

namespace FruitVegetableMarketPOS.Models
{
    public class ReportKpis
    {
        public double GrossSales { get; set; }
        public double Discounts { get; set; }
        public double NetSales { get; set; }
        public int BillCount { get; set; }
        public double QuantitySold { get; set; }
        public double AverageBillValue { get; set; }
        public double CashReceived { get; set; }
        public double OnlineReceived { get; set; }
        public double RecoveredCredit { get; set; }
        public double TotalReceived => CashReceived + OnlineReceived;
        public double OutstandingCredit { get; set; }
        public string PeriodLabel { get; set; } = string.Empty;

        public double PreviousNetSales { get; set; }
        public double NetSalesChange => NetSales - PreviousNetSales;
        public double NetSalesChangePercent =>
            PreviousNetSales > 0.01 ? NetSalesChange / PreviousNetSales * 100 : 0;
        public bool HasPreviousPeriod => PreviousNetSales > 0.01 || NetSales > 0.01;
        public bool IsPositiveChange => NetSalesChange >= 0;
        public string NetSalesDisplay => FormatRs(NetSales);
        public string GrossSalesDisplay => FormatRs(GrossSales);
        public string DiscountDisplay => FormatRs(Discounts);
        public string QuantityDisplay => QuantitySold.ToString("0.###");
        public string AverageBillDisplay => FormatRs(AverageBillValue);
        public string CashReceivedDisplay => FormatRs(CashReceived);
        public string OnlineReceivedDisplay => FormatRs(OnlineReceived);
        public string RecoveredCreditDisplay => FormatRs(RecoveredCredit);
        public string TotalReceivedDisplay => FormatRs(TotalReceived);
        public string OutstandingCreditDisplay => FormatRs(OutstandingCredit);
        public string NetSalesChangeDisplay =>
            !HasPreviousPeriod
                ? "vs previous period"
                : $"{(NetSalesChange >= 0 ? "▲" : "▼")}  {FormatRs(Math.Abs(NetSalesChange))}  ({NetSalesChangePercent:+0.0;-0.0;0}%)";

        private static string FormatRs(double value) => $"Rs.{value:N0}";
    }

    public class ItemSalesRow
    {
        public string ItemKey { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string? NameUrdu { get; set; }
        public double QuantitySold { get; set; }
        public double Revenue { get; set; }
        public int BillCount { get; set; }
        public int LineCount { get; set; }
        public string QuantityDisplay => QuantitySold.ToString("0.###");
        public string RevenueDisplay => $"Rs. {Revenue:N0}";
        public string DisplayName =>
            string.IsNullOrWhiteSpace(NameUrdu) ? ItemName : $"{ItemName}  {NameUrdu}";
    }

    public class ItemLineDetail
    {
        public string BillId { get; set; } = string.Empty;
        public int InternalBillId { get; set; }
        public DateTime BillDateTime { get; set; }
        public double Quantity { get; set; }
        public double Price { get; set; }
        public double LineTotal { get; set; }
        public string QuantityDisplay => Quantity.ToString("0.###");
        public string PriceDisplay => $"Rs. {Price:N0}";
        public string LineTotalDisplay => $"Rs. {LineTotal:N0}";
        public string DateDisplay => BillDateTime.ToString("dd MMM yyyy  HH:mm");
    }

    public class AccountReceiptRow
    {
        public int? AccountId { get; set; }
        public string AccountName { get; set; } = string.Empty;
        public string? AccountType { get; set; }
        public int TransactionCount { get; set; }
        public double AmountReceived { get; set; }
        public string AmountDisplay => $"Rs. {AmountReceived:N0}";
    }

    public class CustomerSalesRow
    {
        public string CustomerId { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public int BillCount { get; set; }
        public double TotalPurchases { get; set; }
        public double Payments { get; set; }
        public double Outstanding { get; set; }
        public DateTime? LastTransaction { get; set; }
        public string PurchasesDisplay => $"Rs. {TotalPurchases:N0}";
        public string PaymentsDisplay => $"Rs. {Payments:N0}";
        public string OutstandingDisplay => $"Rs. {Outstanding:N0}";
        public string LastTransactionDisplay => LastTransaction?.ToString("dd MMM yyyy") ?? "—";
    }

    public class PaymentMethodRow
    {
        public string Method { get; set; } = string.Empty;
        public int TransactionCount { get; set; }
        public double Amount { get; set; }
        public double Percent { get; set; }
        public string DisplayAmount => $"Rs. {Amount:N0}";
        public string DisplayPercent => $"{Percent:N1}%";
    }

    /// <summary>
    /// One money row on Reports → Payments: a sale bill or a credit recovery (Bill_Payments).
    /// </summary>
    public class ReceiptLedgerRow
    {
        public string Kind { get; set; } = "Bill";
        public string TypeLabel => Kind == "Payment" ? "Payment" : "Bill";
        public bool IsPayment => Kind == "Payment";
        public string BillId { get; set; } = "";
        public string PaymentId { get; set; } = "";
        public string InvoiceDisplay => IsPayment
            ? (string.IsNullOrWhiteSpace(PaymentId) ? "—" : PaymentId)
            : (string.IsNullOrWhiteSpace(BillId) ? "—" : BillId);
        public DateTime DateTime { get; set; }
        public string CustomerName { get; set; } = "";
        public string Method { get; set; } = "";
        public double Received { get; set; }
        public string ReceivedDisplay => $"Rs. {Received:N0}";
        public string DateDisplay => DateTime.ToString("dd MMM yyyy  HH:mm");
        public string TypeChipBackground => IsPayment ? "#DBEAFE" : "#DCFCE7";
        public string TypeChipForeground => IsPayment ? "#1D4ED8" : "#166534";
        public string TypeChipBorder => IsPayment ? "#93C5FD" : "#86EFAC";
    }

    public class MonthlyBucket
    {
        public string MonthKey { get; set; } = string.Empty;
        public string MonthLabel { get; set; } = string.Empty;
        public double Revenue { get; set; }
        public double Quantity { get; set; }
        public int Bills { get; set; }
        public string RevenueDisplay => $"Rs. {Revenue:N0}";
        public string QuantityDisplay => Quantity.ToString("0.###");
    }
}
