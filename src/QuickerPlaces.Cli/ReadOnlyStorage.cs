using System;
using QuickerPlaces.Services;

namespace QuickerPlaces.Cli;

/// <summary>
/// A store file opened for reading only. The stores never write while
/// loading, but a damaged one asks to be set aside (Quarantine); refusing
/// that here keeps every read command from changing anything on disk, so a
/// read is always safe beside the running app.
/// </summary>
public sealed class ReadOnlyStorage : IPlacesStorage
{
    private readonly IPlacesStorage _inner;

    public ReadOnlyStorage(IPlacesStorage inner) => _inner = inner;

    public string StoreFilePath => _inner.StoreFilePath;

    public bool Exists => _inner.Exists;

    public string Read() => _inner.Read();

    public void Write(string contents) => throw new InvalidOperationException("The CLI opened this store read-only.");

    public string Quarantine(DateTimeOffset timestamp) => throw new InvalidOperationException("The CLI opened this store read-only.");
}
