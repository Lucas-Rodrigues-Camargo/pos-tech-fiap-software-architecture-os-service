var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
builder.Services.AddHealthChecks();
var app = builder.Build();
app.MapHealthChecks("/health");
app.MapGet("/api/info", () => new { service = "os", phase = 4, status = "Domain foundation; persistence and integrations pending" });
app.Run();
public partial class Program;

