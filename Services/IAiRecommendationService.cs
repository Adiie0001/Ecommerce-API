using EcommerceAPI.Models;

namespace EcommerceAPI.Services
{
    public class AiRecommendationResult
    {
        public Product Product { get; set; } = null!;
        public int MatchConfidence { get; set; }
        public string AiExplanation { get; set; } = string.Empty;
    }

    public interface IAiRecommendationService
    {
        Task<List<AiRecommendationResult>> GetRecommendationsAsync(string naturalLanguageQuery, IEnumerable<Product> allProducts);
    }
}
