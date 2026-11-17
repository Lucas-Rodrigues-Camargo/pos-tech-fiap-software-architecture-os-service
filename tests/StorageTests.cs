using Npgsql;
using Platform;
namespace Tests;

public class StorageTests
{
    [Fact]
    public async Task PostgresAtomicStateAndConflict()
    {
        var connection = Environment.GetEnvironmentVariable("TEST_POSTGRES");
        if (string.IsNullOrEmpty(connection)) throw new InvalidOperationException("Set TEST_POSTGRES to isolated test database.");
        await using var source = NpgsqlDataSource.Create(connection);
        var store = new PostgresStore(source); await store.Initialize(default);
        await Task.WhenAll(store.Initialize(default), store.Initialize(default));
        await Verify(store);
    }
    static async Task Verify(IStateStore store)
    {
        var id = Guid.NewGuid(); Assert.Null(await store.Get(id));
        var state = new State { Id = id, Status = "Waiting" };
        state.Outbox.Add(Message.Create(id, "Test", "os", new()));
        await store.Save(state, 0);
        Assert.Equal("Waiting", (await store.Get(id))!.Status);
        await Assert.ThrowsAsync<ConflictException>(() => store.Save(new State { Id = id }, 0));
        var saved = (await store.Get(id))!; saved.Move("Ready");
        await store.Save(saved, 1);
        await Assert.ThrowsAsync<ConflictException>(() => store.Save(new State { Id = id }, 1));
        Assert.Contains(await store.Pending(), x => x.Id == id);
        var clean = (await store.Get(id))!;
        clean.Outbox.Clear();
        await store.Save(clean, clean.Version);
    }
}
