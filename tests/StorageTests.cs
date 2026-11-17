using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Npgsql;
using Platform;
namespace Tests;
public class StorageTests
{
    [Fact] public async Task PostgresAtomicStateAndConflict()
    {
        var connection=Environment.GetEnvironmentVariable("TEST_POSTGRES");
        if(string.IsNullOrEmpty(connection)) throw new InvalidOperationException("Set TEST_POSTGRES to isolated test database.");
        await using var source=NpgsqlDataSource.Create(connection);
        var store=new PostgresStore(source); await store.Initialize(default);
        await Verify(store);
    }
    [Fact] public async Task DynamoAtomicStateAndConflict()
    {
        var endpoint=Environment.GetEnvironmentVariable("TEST_AWS_ENDPOINT")??"http://localhost:4566";
        using var db=new AmazonDynamoDBClient(new Amazon.Runtime.BasicAWSCredentials("test","test"),
            new AmazonDynamoDBConfig{ServiceURL=endpoint,AuthenticationRegion="us-east-1"});
        var table="test-os-"+Guid.NewGuid().ToString("N");
        await db.CreateTableAsync(new CreateTableRequest{
            TableName=table, BillingMode=BillingMode.PAY_PER_REQUEST,
            AttributeDefinitions=[new("id",ScalarAttributeType.S)],
            KeySchema=[new("id",KeyType.HASH)]
        });
        try { await Verify(new DynamoStore(db,table)); }
        finally { await db.DeleteTableAsync(table); }
    }
    static async Task Verify(IStateStore store)
    {
        var id=Guid.NewGuid(); Assert.Null(await store.Get(id));
        var state=new State{Id=id,Status="Waiting"};
        state.Outbox.Add(Message.Create(id,"Test","os",new()));
        await store.Save(state,0);
        Assert.Equal("Waiting",(await store.Get(id))!.Status);
        await Assert.ThrowsAsync<ConflictException>(()=>store.Save(new State{Id=id},0));
        var saved=(await store.Get(id))!; saved.Move("Ready");
        await store.Save(saved,1);
        await Assert.ThrowsAsync<ConflictException>(()=>store.Save(new State{Id=id},1));
        Assert.Contains(await store.Pending(),x=>x.Id==id);
    }
}
