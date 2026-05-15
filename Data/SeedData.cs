using EcommerceAPI.Models;
using Microsoft.EntityFrameworkCore;
using EcommerceAPI.Services;

namespace EcommerceAPI.Data
{
    public static class SeedData
    {
        public static void Initialize(IServiceProvider serviceProvider)
        {
            using (var context = new AppDbContext(
                serviceProvider.GetRequiredService<DbContextOptions<AppDbContext>>()))
            {
                context.Database.EnsureCreated();

                

                if (!context.Users.Any())
                {
                    context.Users.AddRange(
                        new User
                        {
                            Username = "Admin User",
                            Email = "admin@test.com",
                            PasswordHash = PasswordHasher.HashPassword("Admin@123"),
                            Role = "Admin"
                        },
                        new User
                        {
                            Username = "Regular User",
                            Email = "user@test.com",
                            PasswordHash = PasswordHasher.HashPassword("User@123"),
                            Role = "Customer"
                        }
                    );
                    context.SaveChanges();
                }

                if (!context.Products.Any())
                {
                    context.Products.AddRange(
                        new Product { Name = "Wireless Mouse", Description = "High precision wireless mouse", Price = 29.99m, Stock = 150 },
                        new Product { Name = "Mechanical Keyboard", Description = "RGB mechanical keyboard", Price = 89.99m, Stock = 75 },
                        new Product { Name = "Gaming Monitor", Description = "144Hz 1080p gaming monitor", Price = 199.99m, Stock = 40 },
                        new Product { Name = "USB-C Hub", Description = "7-in-1 USB-C docking station", Price = 39.99m, Stock = 200 },
                        new Product { Name = "Laptop Stand", Description = "Ergonomic aluminum laptop stand", Price = 24.99m, Stock = 120 },
                        new Product { Name = "Bluetooth Headphones", Description = "Noise-cancelling over-ear headphones", Price = 149.99m, Stock = 60 },
                        new Product { Name = "Webcam 1080p", Description = "HD webcam with microphone", Price = 49.99m, Stock = 90 },
                        new Product { Name = "Desk Pad", Description = "Large PU leather desk pad", Price = 19.99m, Stock = 300 },
                        new Product { Name = "Smartphone Stand", Description = "Adjustable stand for phones and tablets", Price = 12.99m, Stock = 250 },
                        new Product { Name = "External SSD 1TB", Description = "Portable NVMe SSD", Price = 129.99m, Stock = 50 },
                        new Product { Name = "HDMI Cable 2m", Description = "Braided HDMI 2.1 cable", Price = 9.99m, Stock = 500 },
                        new Product { Name = "Ethernet Cable 5m", Description = "Cat 6 gigabit network cable", Price = 7.99m, Stock = 400 }
                    );
                    context.SaveChanges();
                }
            }
        }
    }
}

