using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
namespace Tests;

public class CommandsApiTests
{
    [Fact]
    public async Task CustomersOrdersAndCancellation()
    {
        using var app = new ApiFactory(); using var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", ApiFactory.Token("Admin"));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/customers/bad", new { name = "Joao" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/customers/52998224725", new { name = "Joao", active = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/internal/auth/customers", new { cpf = "52998224725" })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Internal-Key", "internal-test");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/internal/auth/customers", new { cpf = "52998224725" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync("/internal/auth/customers", new { cpf = "11144477735" })).StatusCode);
        var id = Guid.NewGuid(); client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/orders", new { orderId = id, cpf = "52998224725", plate = "BRA2E19", description = "Leak" })).StatusCode);
        client.DefaultRequestHeaders.Remove("Idempotency-Key"); client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsync($"/api/orders/{id}/cancel", null)).StatusCode);
    }
}
