using System.Collections.Generic;
using System.Threading.Tasks;
using GroceryPOS.Models;
using GroceryPOS.Data.Repositories;

namespace GroceryPOS.Services
{
    public class SupplierService
    {
        private readonly SupplierRepository _repository;

        public SupplierService(SupplierRepository repository)
        {
            _repository = repository;
        }

        public async Task<List<Supplier>> GetAllSuppliersAsync()
        {
            return await Task.Run(() => _repository.GetAll());
        }

        public async Task<bool> SaveSupplierAsync(Supplier supplier, bool isUpdate)
        {
            return await Task.Run(() => isUpdate ? _repository.Update(supplier) : _repository.Add(supplier));
        }

        public async Task<bool> DeleteSupplierAsync(string phone)
        {
            return await Task.Run(() => _repository.Delete(phone));
        }

        public async Task<List<SupplierProduct>> GetProductsBySupplierAsync(string phone)
        {
            return await Task.Run(() => _repository.GetProductsBySupplier(phone));
        }

        public async Task<List<Supplier>> GetSuppliersByProductAsync(int productId)
        {
            return await Task.Run(() => _repository.GetSuppliersByProduct(productId));
        }

        public async Task<bool> AssignProductAsync(SupplierProduct mapping)
        {
            return await Task.Run(() => _repository.AssignProduct(mapping));
        }

        public async Task<bool> UnassignProductAsync(string phone, int productId)
        {
            return await Task.Run(() => _repository.UnassignProduct(phone, productId));
        }
    }
}
