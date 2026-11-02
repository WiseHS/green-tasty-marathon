using Amazon.DynamoDBv2.DataModel;
using APIHandler.Entities;
using APIHandler.Repositories;

namespace Restaurant.Web.Persistence;

public sealed class PostgresApplicationData(PostgresStore store) : IApplicationData
{
    public async Task<T> LoadAsync<T>(object key,object config) => (await store.Get<T>(key.ToString()!))!;
    public Task SaveAsync<T>(T value,object config) => store.Save(value);
    public ApplicationQuery<T> ScanAsync<T>(IEnumerable<ScanCondition>? conditions,object config)
    {
        if(conditions?.Any()==true)throw new NotSupportedException("Filtered scans must use a domain repository");
        return new(()=>store.Find<T>());
    }
    public ApplicationQuery<T> QueryAsync<T>(object key,object config)
    {
        if(typeof(T)!=typeof(Reservation))throw new NotSupportedException("Query must use a domain repository");
        return new(()=>store.Find<T>(nameof(Reservation.TableId),key.ToString()));
    }
    public ApplicationBatch<T> CreateBatchGet<T>(object config) => new(key=>LoadAsync<T>(key,config));
}
