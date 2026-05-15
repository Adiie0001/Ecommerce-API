using System.Collections.Generic;
using System.Threading.Tasks;
using EcommerceAPI.Controllers;
using EcommerceAPI.Models;
using EcommerceAPI.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EcommerceAPI.Tests
{
    public class ProductControllerTests
    {
        private async Task<AppDbContext> GetDatabaseContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: System.Guid.NewGuid().ToString())
                .Options;
            var databaseContext = new AppDbContext(options);
            databaseContext.Database.EnsureCreated();
            
            if (await databaseContext.Products.CountAsync() <= 0)
            {
                for (int i = 1; i <= 10; i++)
                {
                    databaseContext.Products.Add(
                    new Product()
                    {
                        Id = i,
                        Name = $"Test Product {i}",
                        Description = "Description",
                        Price = 10.99m * i,
                        Stock = 10 * i
                    });
                    await databaseContext.SaveChangesAsync();
                }
            }
            return databaseContext;
        }

        [Fact]
        public async Task GetProducts_ReturnsAllProducts()
        {
            var dbContext = await GetDatabaseContext();
            var controller = new ProductController(dbContext);

            var result = await controller.GetProducts(1, 10);
            
            var actionResult = Assert.IsType<ActionResult<IEnumerable<Product>>>(result);
            var products = Assert.IsAssignableFrom<IEnumerable<Product>>(actionResult.Value);
            Assert.Equal(10, products.Count());
        }

        [Fact]
        public async Task GetProduct_WithValidId_ReturnsProduct()
        {
            var dbContext = await GetDatabaseContext();
            var controller = new ProductController(dbContext);

            var result = await controller.GetProduct(1);
            
            var actionResult = Assert.IsType<ActionResult<Product>>(result);
            Assert.NotNull(actionResult.Value);
            Assert.Equal(1, actionResult.Value.Id);
        }

        [Fact]
        public async Task GetProduct_WithInvalidId_ReturnsNotFound()
        {
            var dbContext = await GetDatabaseContext();
            var controller = new ProductController(dbContext);

            var result = await controller.GetProduct(999);
            
            Assert.IsType<NotFoundObjectResult>(result.Result);
        }

        [Fact]
        public async Task CreateProduct_ValidProduct_ReturnsCreatedAtAction()
        {
            var dbContext = await GetDatabaseContext();
            var controller = new ProductController(dbContext);
            var newProduct = new Product { Id = 11, Name = "New", Description = "Desc", Price = 100, Stock = 5 };

            var result = await controller.CreateProduct(newProduct);
            
            var actionResult = Assert.IsType<CreatedAtActionResult>(result.Result);
            var product = Assert.IsType<Product>(actionResult.Value);
            Assert.Equal(11, product.Id);
        }

        [Fact]
        public async Task DeleteProduct_ValidId_ReturnsNoContent()
        {
            var dbContext = await GetDatabaseContext();
            var controller = new ProductController(dbContext);

            var result = await controller.DeleteProduct(1);
            
            Assert.IsType<NoContentResult>(result);
            Assert.Null(await dbContext.Products.FindAsync(1));
        }
    }
}
