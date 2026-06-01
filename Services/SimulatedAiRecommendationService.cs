using System.Security.Cryptography;
using System.Text;
using EcommerceAPI.Models;

namespace EcommerceAPI.Services
{
    public class SimulatedAiRecommendationService : IAiRecommendationService
    {
        public Task<List<AiRecommendationResult>> GetRecommendationsAsync(string naturalLanguageQuery, IEnumerable<Product> allProducts)
        {
            var results = new List<AiRecommendationResult>();
            var queryLower = naturalLanguageQuery.ToLowerInvariant();
            
            // In a real production scenario, this service would send the naturalLanguageQuery along with the 
            // product catalog to an LLM (like Google Gemini) or an embedding model to compute vector similarity.
            // For this portfolio demonstration, we simulate NLP scoring based on query keyword overlap and deterministic hashing.

            var keywords = queryLower.Split(new[] { ' ', ',', '.', '?' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Where(k => k.Length > 2)
                                     .ToList();

            foreach (var product in allProducts)
            {
                int score = 0;
                var productText = $"{product.Name} {product.Description}".ToLowerInvariant();

                // Simple keyword matching for relevance
                foreach (var keyword in keywords)
                {
                    if (productText.Contains(keyword))
                    {
                        score += 30; // High bump for direct keyword hit
                    }
                }

                // Deterministic base score so it feels "dynamic" but stable for the same query
                using var md5 = MD5.Create();
                var hashInput = $"{naturalLanguageQuery}_{product.Id}";
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(hashInput));
                int baseScore = 20 + (hash[0] % 40); // Base random score 20-60

                int finalScore = Math.Min(99, baseScore + score);

                // Only recommend products that have a decent score
                if (finalScore > 50)
                {
                    results.Add(new AiRecommendationResult
                    {
                        Product = product,
                        MatchConfidence = finalScore,
                        AiExplanation = GenerateExplanation(finalScore, product, keywords)
                    });
                }
            }

            // Return top 3 recommendations sorted by confidence
            var topRecommendations = results.OrderByDescending(r => r.MatchConfidence).Take(3).ToList();
            
            // If nothing matched, fallback to highest deterministic score to show *something*
            if (!topRecommendations.Any() && allProducts.Any())
            {
                var fallbackProduct = allProducts.First();
                topRecommendations.Add(new AiRecommendationResult
                {
                    Product = fallbackProduct,
                    MatchConfidence = 65,
                    AiExplanation = $"AI fallback suggestion: While not a direct match to your query, {fallbackProduct.Name} is a highly rated product you might find interesting."
                });
            }

            return Task.FromResult(topRecommendations);
        }

        private string GenerateExplanation(int confidence, Product product, List<string> keywords)
        {
            var matchedKeyword = keywords.FirstOrDefault(k => product.Name.ToLowerInvariant().Contains(k) || product.Description.ToLowerInvariant().Contains(k));
            
            if (confidence > 85)
                return $"AI Analysis: This {product.Name} is highly relevant to your query for '{matchedKeyword}'. It matches your specific criteria.";
            else if (confidence > 70)
                return $"AI Analysis: Based on your request, this {product.Name} is a solid option to consider.";
            else
                return $"AI Analysis: This product shares some characteristics with your search intent.";
        }
    }
}
