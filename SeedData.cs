using EcommerceAPI.Data;
using EcommerceAPI.Models;

namespace EcommerceAPI
{
    public static class SeedData
    {
        public static void Initialize(AppDbContext context)
        {
            // Seed Users if empty
            if (!context.Users.Any())
            {
                context.Users.AddRange(
                    new User
                    {
                        Username = "admin",
                        Email = "admin@test.com",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123")
                    },
                    new User
                    {
                        Username = "testuser",
                        Email = "user@test.com",
                        PasswordHash = BCrypt.Net.BCrypt.HashPassword("User@123")
                    }
                );
            }

            // Seed Products if empty
            if (!context.Products.Any())
            {
                context.Products.AddRange(
                    new Product { Name = "Wireless Bluetooth Headphones", Description = "Premium noise-cancelling over-ear headphones with 30hr battery life", Price = 2999.99m, Stock = 50 },
                    new Product { Name = "Mechanical Gaming Keyboard", Description = "RGB backlit mechanical keyboard with blue switches, TKL layout", Price = 4499.00m, Stock = 30 },
                    new Product { Name = "4K USB-C Monitor", Description = "27-inch 4K IPS display with USB-C power delivery and HDR support", Price = 24999.00m, Stock = 15 },
                    new Product { Name = "Ergonomic Office Chair", Description = "Mesh back ergonomic chair with lumbar support and adjustable armrests", Price = 12999.00m, Stock = 20 },
                    new Product { Name = "Portable SSD 1TB", Description = "Ultra-fast NVMe portable SSD with USB 3.2 Gen 2 — 1050 MB/s read speed", Price = 6999.00m, Stock = 75 },
                    new Product { Name = "Laptop Stand Aluminium", Description = "Adjustable aluminium laptop stand, compatible with all 13-17 inch laptops", Price = 1499.00m, Stock = 100 },
                    new Product { Name = "Wireless Charging Pad", Description = "15W fast wireless charger compatible with Qi-enabled devices", Price = 799.00m, Stock = 200 },
                    new Product { Name = "USB-C Hub 7-in-1", Description = "7-port USB-C hub: HDMI 4K, 3x USB-A 3.0, SD card, USB-C PD 100W", Price = 2199.00m, Stock = 60 },
                    new Product { Name = "Webcam 1080p HD", Description = "Full HD 1080p webcam with built-in stereo microphone and auto-focus", Price = 3299.00m, Stock = 45 },
                    new Product { Name = "Smart LED Desk Lamp", Description = "Touch-controlled LED desk lamp with 5 colour temperatures and USB charging port", Price = 1899.00m, Stock = 80 },
                    new Product { Name = "Mechanical Pencil Set", Description = "Professional drafting set with 0.3, 0.5, 0.7mm pencils", Price = 699.00m, Stock = 150 },
                    new Product { Name = "Mouse Pad XL", Description = "Extended gaming mouse pad 900x400mm with non-slip rubber base", Price = 899.00m, Stock = 120 }
                );
            }

            context.SaveChanges();
        }
    }
}
