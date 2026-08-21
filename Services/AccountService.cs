using System;
using System.Collections.Generic;
using FruitVegetableMarketPOS.Data.Repositories;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Services
{
    /// <summary>
    /// Service for managing payment accounts.
    /// Provides accounts for the billing view and reporting.
    /// </summary>
    public class AccountService
    {
        private readonly AccountRepository _accountRepo;

        public AccountService(AccountRepository accountRepo)
        {
            _accountRepo = accountRepo;
        }

        public List<Account> GetActiveAccounts()
        {
            return _accountRepo.GetActiveAccounts();
        }

        public List<Account> GetOnlinePaymentAccounts() => GetActiveAccounts();

        public Account? GetAccountById(int id)
        {
            return _accountRepo.GetById(id);
        }
    }
}
