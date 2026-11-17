using System.Text.Json;
using Platform;
namespace Tests;
public class ContractTests
{
    [Fact]
    public void SerializerMatchesVersionedContract()
    {
        using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "message-v1.schema.json")));
        var message = Message.Create(Guid.NewGuid(), "OpenOrder", "os",
            new Payload(Cpf: "52998224725", Plate: "BRA2E19", Description: "Repair", Amount: 150m));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(message, JsonDefaults.Options));
        Assert.Equal(schema.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order(),
            json.RootElement.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(schema.RootElement.GetProperty("properties").GetProperty("data").GetProperty("properties")
            .EnumerateObject().Select(p => p.Name).Order(),
            json.RootElement.GetProperty("data").EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(schema.RootElement.GetProperty("properties").GetProperty("version").GetProperty("const").GetInt32(),
            json.RootElement.GetProperty("version").GetInt32());
        Assert.Contains(message.Type, schema.RootElement.GetProperty("properties").GetProperty("type")
            .GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
    }
}
