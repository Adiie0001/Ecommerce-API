using System.ComponentModel.DataAnnotations;

namespace EcommerceAPI.DTOs
{
    public class CreateProductDto
    {
        [Required]
        [MaxLength(100)]
        public required string Name { get; set; }

        [Required]
        [MaxLength(500)]
        public required string Description { get; set; }

        [Range(0.01, 100000.00)]
        public decimal Price { get; set; }

        [Range(0, 10000)]
        public int Stock { get; set; }
    }
}
