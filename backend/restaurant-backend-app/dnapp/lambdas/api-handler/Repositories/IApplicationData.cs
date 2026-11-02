using Amazon.DynamoDBv2.DataModel;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Repositories;

// Transitional port for handlers that originally accessed DynamoDB directly.
public interface IApplicationData
{
    Task<T> LoadAsync<T>(object key, object config);
    Task SaveAsync<T>(T value, object config);
    ApplicationQuery<T> ScanAsync<T>(IEnumerable<ScanCondition>? conditions, object config);
    ApplicationQuery<T> QueryAsync<T>(object key, object config);
    ApplicationBatch<T> CreateBatchGet<T>(object config);
}

public sealed class ApplicationQuery<T>(Func<Task<List<T>>> load)
{
    public Task<List<T>> GetRemainingAsync() => load();
}

public sealed class ApplicationBatch<T>(Func<object,Task<T>> load)
{
    private readonly List<object> keys = [];
    public List<T> Results { get; } = [];
    public void AddKey(object key) => keys.Add(key);
    public async Task ExecuteAsync()
    { foreach(var key in keys) { var value=await load(key);if(value is not null)Results.Add(value); } }
}

public sealed class DynamoApplicationData(IDynamoDBContext context) : IApplicationData
{
    public Task<T> LoadAsync<T>(object key, object config) => config is LoadConfig load
        ? context.LoadAsync<T>(key,load) : context.LoadAsync<T>(key,(DynamoDBOperationConfig)config);
    public Task SaveAsync<T>(T value, object config) => config is SaveConfig save
        ? context.SaveAsync(value,save) : context.SaveAsync(value,(DynamoDBOperationConfig)config);
    public ApplicationQuery<T> ScanAsync<T>(IEnumerable<ScanCondition>? conditions,object config) => new(() => config is ScanConfig scan
        ? context.ScanAsync<T>(conditions,scan).GetRemainingAsync() : context.ScanAsync<T>(conditions,(DynamoDBOperationConfig)config).GetRemainingAsync());
    public ApplicationQuery<T> QueryAsync<T>(object key,object config) => new(() => config is QueryConfig query
        ? context.QueryAsync<T>(key,query).GetRemainingAsync() : context.QueryAsync<T>(key,(DynamoDBOperationConfig)config).GetRemainingAsync());
    public ApplicationBatch<T> CreateBatchGet<T>(object config) => new(key => LoadAsync<T>(key,config));
}
