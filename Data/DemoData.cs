using EcommerceAPI.Domain;

namespace EcommerceAPI.Data;

/// <summary>
/// Published demo accounts for local development only. The options validator refuses them in any other environment.
/// </summary>
public static class DemoCredentials
{
    public const string AdminEmail = "admin@example.com";
    public const string AdminPassword = "Admin@Demo12345";
    public const string CustomerEmail = "customer@example.com";
    public const string CustomerPassword = "Customer@Demo12345";

    public static bool IsDemoPassword(string password) =>
        string.Equals(password, AdminPassword, StringComparison.Ordinal) ||
        string.Equals(password, CustomerPassword, StringComparison.Ordinal);
}

internal static class DemoCatalog
{
    public static IEnumerable<(string Name, string Description, decimal Price, int Stock)> Items =>
    [
        ("Wireless Mouse", "High precision wireless mouse", 29.99m, 150),
        ("Mechanical Keyboard", "Mechanical keyboard with tactile switches", 89.99m, 75),
        ("Gaming Monitor", "144Hz 1080p gaming monitor", 199.99m, 40),
        ("USB-C Hub", "7-in-1 USB-C docking station", 39.99m, 200),
        ("Laptop Stand", "Ergonomic aluminum laptop stand", 24.99m, 120),
        ("Bluetooth Headphones", "Noise-cancelling over-ear headphones", 149.99m, 60),
        ("Webcam 1080p", "HD webcam with microphone", 49.99m, 90),
        ("Desk Pad", "Large PU leather desk pad", 19.99m, 300),
        ("Smartphone Stand", "Adjustable stand for phones and tablets", 12.99m, 250),
        ("External SSD 1TB", "Portable NVMe SSD", 129.99m, 50),
        ("HDMI Cable 2m", "Braided HDMI 2.1 cable", 9.99m, 500),
        ("Ethernet Cable 5m", "Cat 6 gigabit network cable", 7.99m, 400)
    ];
}
