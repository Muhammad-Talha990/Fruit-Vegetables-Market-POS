using System;
using System.Collections.Generic;
using GroceryPOS.Data.Repositories;
using GroceryPOS.Helpers;
using GroceryPOS.Models;

namespace GroceryPOS.Services
{
    /// <summary>
    /// Business logic for customer management and credit lookup.
    /// Validates phone uniqueness, enforces soft-delete, and fetches pending credit.
    /// </summary>
    public class CustomerService
    {
        private readonly CustomerRepository _customerRepo;
        private readonly BillRepository _billRepo;

        public CustomerService(CustomerRepository customerRepo, BillRepository billRepo)
        {
            _customerRepo = customerRepo;
            _billRepo     = billRepo;
        }

        // ────────────────────────────────────────────
        //  REGISTRATION
        // ────────────────────────────────────────────

        public void RegisterCustomer(Customer customer)
        {
            ValidateCustomerFields(customer, isUpdate: false);
            _customerRepo.Save(customer);
            AppLogger.Info($"CustomerService: Registered new customer '{customer.FullName}' (ID: {customer.CustomerId}).");
        }

        // ────────────────────────────────────────────
        //  UPDATE
        // ────────────────────────────────────────────

        public void UpdateCustomer(Customer customer)
        {
            ValidateCustomerFields(customer, isUpdate: true);
            _customerRepo.Update(customer);
            AppLogger.Info($"CustomerService: Updated customer '{customer.FullName}' (ID: {customer.CustomerId}).");
        }

        // ────────────────────────────────────────────
        //  SOFT DELETE / REACTIVATE
        // ────────────────────────────────────────────

        public bool DeactivateCustomer(int customerId)
        {
            bool success = _customerRepo.SoftDelete(customerId);
            if (success)
                AppLogger.Info($"CustomerService: Deactivated customer ID {customerId}.");
            return success;
        }

        public bool ReactivateCustomer(int customerId)
        {
            bool success = _customerRepo.Reactivate(customerId);
            if (success)
                AppLogger.Info($"CustomerService: Reactivated customer ID {customerId}.");
            return success;
        }

        // ────────────────────────────────────────────
        //  QUERIES
        // ────────────────────────────────────────────

        /// <summary>
        /// All customers for management grid (includes inactive).
        /// Each customer has PendingCredit populated.
        /// </summary>
        public List<Customer> GetAllCustomers(bool activeOnly = false) =>
            _customerRepo.GetAll(activeOnly);

        public Customer? GetCustomerByPhone(string phone) =>
            _customerRepo.GetByPhone(NormalizePhone(phone));

        public Customer? GetCustomerById(int id) =>
            _customerRepo.GetById(id);

        public List<Customer> SearchCustomers(string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return new List<Customer>();
            return _customerRepo.Search(query);
        }

        public double GetPendingCredit(int customerId) =>
            _customerRepo.GetPendingCredit(customerId);

        public (int BillCount, double TotalAmount) GetCustomerStats(int customerId) =>
            _billRepo.GetCustomerStats(customerId);

        public Bill? GetLatestBill(int customerId) =>
            _billRepo.GetLatestBillByCustomerId(customerId);

        // ────────────────────────────────────────────
        //  Validation helper
        // ────────────────────────────────────────────

        private void ValidateCustomerFields(Customer customer, bool isUpdate)
        {
            if (string.IsNullOrWhiteSpace(customer.FullName))
                throw new ArgumentException("Customer name is required.");

            if (string.IsNullOrWhiteSpace(customer.PrimaryPhone))
                throw new ArgumentException("Primary phone is required.");

            string rawPhone   = customer.PrimaryPhone.Trim();
            string normalized = NormalizePhone(rawPhone);

            if (!rawPhone.StartsWith("0") || normalized.Length != 11)
                throw new ArgumentException("Invalid primary phone. Must start with '0' and be 11 digits (e.g. 03001234567).");

            // Check uniqueness — exclude self on update
            var existing = _customerRepo.GetByPhone(normalized);
            if (existing != null && existing.CustomerId != customer.CustomerId)
                throw new InvalidOperationException("A customer with this primary phone number already exists.");

            customer.PrimaryPhone = normalized;

            if (!string.IsNullOrWhiteSpace(customer.SecondaryPhone))
            {
                string rawPhone2   = customer.SecondaryPhone.Trim();
                string normalized2 = NormalizePhone(rawPhone2);

                if (!rawPhone2.StartsWith("0") || normalized2.Length != 11)
                    throw new ArgumentException("Invalid secondary phone. Must start with '0' and be 11 digits.");

                if (normalized == normalized2)
                    throw new ArgumentException("Primary and secondary phone numbers cannot be the same.");

                var existing2 = _customerRepo.GetByPhone(normalized2);
                if (existing2 != null && existing2.CustomerId != customer.CustomerId)
                    throw new InvalidOperationException("A customer with this secondary phone number already exists.");

                customer.SecondaryPhone = normalized2;
            }
        }

        public string NormalizePhone(string phone)
        {
            if (string.IsNullOrEmpty(phone)) return string.Empty;
            var result = new System.Text.StringBuilder();
            foreach (char c in phone)
                if (char.IsDigit(c)) result.Append(c);
            return result.ToString();
        }
    }
}
