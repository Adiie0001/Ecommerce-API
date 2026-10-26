using EcommerceAPI.Infrastructure.Startup;

var builder = WebApplication.CreateBuilder(args);
builder.AddApiServices();

var app = builder.Build();
app.UseApiPipeline();
await app.InitializeAsync();
await app.RunAsync();

// Makes the entry point visible to WebApplicationFactory in the integration tests.
public partial class Program;
