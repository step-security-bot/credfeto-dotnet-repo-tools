using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Credfeto.Dotnet.Repo.Tracking.SerializationContext;
using Credfeto.Dotnet.Repo.Tracking.Services.LoggingExtensions;
using Microsoft.Extensions.Logging;
using NonBlocking;

namespace Credfeto.Dotnet.Repo.Tracking.Services;

public sealed class TrackingCache : ITrackingCache
{
    private readonly ConcurrentDictionary<string, string> _cache;
    private readonly ILogger<TrackingCache> _logger;
    private readonly JsonTypeInfo<TrackingItems> _typeInfo;
    private bool _changed;

    public TrackingCache(ILogger<TrackingCache> logger)
    {
        this._logger = logger;
        this._cache = new(StringComparer.OrdinalIgnoreCase);

        this._typeInfo = TrackingSerializationContext.Default.GetTypeInfo(typeof(TrackingItems)) as JsonTypeInfo<TrackingItems> ?? MissingConverter();
        this._changed = false;
    }

    public async Task LoadAsync(string fileName, CancellationToken cancellationToken)
    {
        this._logger.LoadingCache(fileName);

        string content = await File.ReadAllTextAsync(path: fileName, cancellationToken: cancellationToken);

        TrackingItems? items = JsonSerializer.Deserialize(json: content, jsonTypeInfo: this._typeInfo);

        if (items is not null)
        {
            foreach ((string repoUrl, string value) in items.Cache.OrderBy(keySelector: x => x.Key, comparer: StringComparer.OrdinalIgnoreCase))
            {
                this._logger.LoadedPackageVersionFromCache(packageId: repoUrl, version: value);
                this._cache.TryAdd(key: repoUrl, value: value);
            }
        }
    }

    public Task SaveAsync(string fileName, CancellationToken cancellationToken)
    {
        if (!this._changed)
        {
            return Task.CompletedTask;
        }

        this._logger.SavingCache(fileName);

        TrackingItems toWrite = new(this._cache.ToDictionary(keySelector: x => x.Key, elementSelector: x => x.Value, comparer: StringComparer.OrdinalIgnoreCase));

        string content = JsonSerializer.Serialize(value: toWrite, jsonTypeInfo: this._typeInfo);

        return File.WriteAllTextAsync(path: fileName, contents: content, cancellationToken: cancellationToken);
    }

    public string? Get(string repoUrl)
    {
        return this._cache.TryGetValue(key: repoUrl, out string? value)
            ? value
            : null;
    }

    public void Set(string repoUrl, string? value)
    {
        bool remove = false;

        if (this._cache.TryGetValue(key: repoUrl, out string? found))
        {
            if (value is null)
            {
                remove = true;
            }
            else
            {
                if (StringComparer.Ordinal.Equals(x: value, y: found))
                {
                    return;
                }

                remove = true;
            }
        }

        if (remove)
        {
            this._changed |= this._cache.TryRemove(key: repoUrl, value: out _);
        }

        if (!string.IsNullOrWhiteSpace(value))
        {
            this._changed |= this._cache.TryAdd(key: repoUrl, value: value);
        }
    }

    [DoesNotReturn]
    private static JsonTypeInfo<TrackingItems> MissingConverter()
    {
        throw new JsonException("No converter found for type TrackingItems");
    }
}