using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EcommerceAPI.Models;
using EcommerceAPI.DTOs;
using EcommerceAPI.Data;
using Microsoft.EntityFrameworkCore;
using AutoMapper;

namespace EcommerceAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProductController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly EcommerceAPI.Services.IAiRecommendationService _aiService;
        private readonly IMapper _mapper;

        public ProductController(AppDbContext context, EcommerceAPI.Services.IAiRecommendationService aiService, IMapper mapper)
        {
            _context = context;
            _aiService = aiService;
            _mapper = mapper;
        }

        /// <summary>Get all products (public) with pagination</summary>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            var products = await _context.Products
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            
            return Ok(_mapper.Map<IEnumerable<ProductDto>>(products));
        }

        /// <summary>Discover products using AI Natural Language Query (public)</summary>
        [HttpGet("recommend")]
        public async Task<ActionResult<IEnumerable<EcommerceAPI.Services.AiRecommendationResult>>> GetAiRecommendations([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest(new { message = "Search query is required." });
            }

            var allProducts = await _context.Products.ToListAsync();
            var recommendations = await _aiService.GetRecommendationsAsync(query, allProducts);

            return Ok(recommendations);
        }

        /// <summary>Get product by ID (public)</summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<ProductDto>> GetProduct(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound(new { message = "Product not found" });
            
            return Ok(_mapper.Map<ProductDto>(product));
        }

        /// <summary>Create a new product (requires JWT auth)</summary>
        [Authorize]
        [HttpPost]
        public async Task<ActionResult<ProductDto>> CreateProduct([FromBody] CreateProductDto createProductDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var product = _mapper.Map<Product>(createProductDto);
            _context.Products.Add(product);
            await _context.SaveChangesAsync();
            
            var productDto = _mapper.Map<ProductDto>(product);
            return CreatedAtAction(nameof(GetProduct), new { id = product.Id }, productDto);
        }

        /// <summary>Update an existing product (requires JWT auth)</summary>
        [Authorize]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateProduct(int id, [FromBody] UpdateProductDto updateProductDto)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound(new { message = "Product not found" });

            _mapper.Map(updateProductDto, product);
            
            _context.Entry(product).State = EntityState.Modified;
            await _context.SaveChangesAsync();
            
            return NoContent();
        }

        /// <summary>Delete a product (requires JWT auth)</summary>
        [Authorize]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProduct(int id)
        {
            var product = await _context.Products.FindAsync(id);
            if (product == null) return NotFound(new { message = "Product not found" });
            
            _context.Products.Remove(product);
            await _context.SaveChangesAsync();
            
            return NoContent();
        }
    }
}
