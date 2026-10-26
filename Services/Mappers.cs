using EcommerceAPI.Contracts;
using EcommerceAPI.Domain;

namespace EcommerceAPI.Services;

/// <summary>Explicit entity-to-contract mapping. No reflection, so a rename breaks the build instead of a runtime mapping.</summary>
internal static class Mappers
{
    public static ProductDto ToDto(this Product product) => new(
        product.Id,
        product.Name,
        product.Description,
        product.Price,
        product.Stock,
        product.ConcurrencyStamp.ToString("N"),
        product.CreatedAt,
        product.UpdatedAt);

    public static UserDto ToDto(this AppUser user) => new(user.Id, user.Email, user.DisplayName, user.Role.ToString());
}
