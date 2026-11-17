using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Platform;
namespace Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string Key = "test-only-jwt-key-with-at-least-32-bytes";
    public readonly MemoryStore Store = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:Key", Key).UseSetting("Messaging:Enabled", "false").UseSetting("Internal:Key", "internal-test");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IStateStore>(); services.AddSingleton<IStateStore>(Store);
        });
    }
    public static string Token(string role, string cpf = "52998224725") =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            "OficinaMecanica", "OficinaMecanica.Customer",
            new[] { new Claim("role", role), new Claim("cpf", cpf) },
            expires: DateTime.UtcNow.AddMinutes(10),
            signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)), SecurityAlgorithms.HmacSha256)));
}
public class ApiTests
{
    [Fact]
    public async Task AuthOwnershipHealthAndCorrelation()
    {
        using var app = new ApiFactory(); using var client = app.CreateClient();
        var id = Guid.NewGuid(); var route = "/api/orders/" + id;
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(route)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token("Customer"));

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);
        await app.Store.Save(new State { Id = id, Data = new(Cpf: "52998224725") }, 0);
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "test-correlation");
        var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("test-correlation", response.Headers.GetValues("X-Correlation-ID").Single());
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token("Customer", "11144477735"));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token("Admin"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/openapi/v1.json")).StatusCode);
    }
    [Fact]
    public async Task CommandRequiresIdempotencyKey()
    {
        using var app = new ApiFactory(); using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token("Admin"));
        var result = await client.PostAsJsonAsync("/api/orders", new { orderId = Guid.NewGuid(), cpf = "52998224725", plate = "BRA2E19", description = "Leak" });
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
    }
}
