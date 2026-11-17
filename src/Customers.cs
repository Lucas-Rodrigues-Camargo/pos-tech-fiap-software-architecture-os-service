using System.Security.Cryptography;
using System.Text;
using Platform;
public static class Customers
{
    public static Guid Id(string cpf) => new(SHA256.HashData(Encoding.UTF8.GetBytes("customer:" + cpf)).AsSpan(0,16));
    public static void MapCustomerEndpoints(this WebApplication app)
    {
        app.MapPut("/api/customers/{cpf}", async (string cpf, CustomerRequest request, IStateStore store, CancellationToken ct) => {
            if (!Cpf.IsValid(cpf) || string.IsNullOrWhiteSpace(request.Name)) return Results.BadRequest();
            var id = Id(cpf);
            var state = await store.Get(id, ct) ?? new State { Id = id };
            var version = state.Version;
            state.Data = new Payload(Cpf: cpf, Description: request.Name);
            state.Move(request.Active ? "CustomerActive" : "CustomerInactive");
            await store.Save(state, version, ct);
            return Results.Ok(new { id, request.Active });
        }).RequireAuthorization("Employee");
        app.MapPost("/internal/auth/customers", async (CustomerLookup lookup, HttpContext context,
            IConfiguration config, IStateStore store, CancellationToken ct) => {
            var secret = config["Internal:Key"];
            var supplied = context.Request.Headers["X-Internal-Key"].ToString();
            if (string.IsNullOrEmpty(secret) || !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(secret)), SHA256.HashData(Encoding.UTF8.GetBytes(supplied))))
                return Results.Unauthorized();
            var state = await store.Get(Id(lookup.Cpf), ct);
            if (state == null) return Results.NotFound();
            return Results.Ok(new { id = state.Id, name = state.Data.Description, document = state.Data.Cpf,
                is_active = state.Status == "CustomerActive" });
        }).AllowAnonymous();
    }
}
public sealed record CustomerRequest(string Name, bool Active = true);
public sealed record CustomerLookup(string Cpf);
