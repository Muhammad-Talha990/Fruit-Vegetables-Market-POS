using System.Collections.Generic;
using FruitVegetableMarketPOS.Data.Repositories;
using FruitVegetableMarketPOS.Models;

namespace FruitVegetableMarketPOS.Services
{
    public class CategoryService
    {
        private readonly CategoryRepository _repo;

        public CategoryService(CategoryRepository repo)
        {
            _repo = repo;
        }

        public List<Category> GetAllActive() => _repo.GetAllActive();

        public List<Category> GetAll() => _repo.GetAll();

        public Category? GetById(int categoryId) => _repo.GetById(categoryId);

        public int Add(Category category)
        {
            if (string.IsNullOrWhiteSpace(category.Name))
                throw new System.ArgumentException("Category name is required.");
            return _repo.Add(category);
        }

        public void Update(Category category) => _repo.Update(category);
    }
}
