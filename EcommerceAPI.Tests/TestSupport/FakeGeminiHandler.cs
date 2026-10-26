using System.Net;
using System.Text;
using System.Text.Json;

namespace EcommerceAPI.Tests.TestSupport;

/// <summary>Stands in for the Gemini HTTP endpoint and records what the app sent.</summary>
public sealed class FakeGeminiHandler : HttpMessageHandler
{
    private readonly List<RecordedRequest> _requests = [];
    private readonly object _gate = new();

    /// <summary>Decides the response. Defaults to HTTP 500 so a test that forgets to arrange it exercises the fallback.</summary>
    public Func<RecordedRequest, HttpResponseMessage> Responder { get; set; } =
        _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);

    public IReadOnlyList<RecordedRequest> Requests
    {
        get
        {
            lock (_gate)
            {
                return _requests.ToList();
            }
        }
    }

    public void Reset()
    {
        lock (_gate)
        {
            _requests.Clear();
        }

        Responder = _ => new HttpResponseMessage(HttpStatusCode.InternalServerError);
    }

    /// <summary>Answers every call with a well-formed Gemini envelope around the given picks.</summary>
    public void RespondWithPicks(params (int ProductId, int Score, string Reason)[] picks) =>
        Responder = _ => Envelope(JsonSerializer.Serialize(picks.Select(p => new { productId = p.ProductId, score = p.Score, reason = p.Reason })));

    public static HttpResponseMessage Envelope(string innerText)
    {
        var body = JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = innerText } } } } }
        });
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(
            request.Method,
            request.RequestUri!,
            request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase),
            body);

        lock (_gate)
        {
            _requests.Add(recorded);
        }

        return Responder(recorded);
    }

    public sealed record RecordedRequest(
        HttpMethod Method,
        Uri Uri,
        IReadOnlyDictionary<string, string> Headers,
        string Body);
}
