using System;
using System.Linq;
using System.Text.Json.Nodes;
using QuickerPlaces.Models;
using QuickerPlaces.Services;
using QuickerPlaces.Services.Remote;
using QuickerPlaces.Services.Sessions;
using QuickerPlaces.Tests.Fakes;
using Xunit;

namespace QuickerPlaces.Tests;

/// <summary>
/// The pipe between qp and the app (RemoteCommandServer/Client, over a real
/// named pipe) and the shared operations (StoreOperations) it carries.
/// </summary>
public sealed class RemoteCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 9, 30, 0, TimeSpan.Zero);

    private static (StoreOperations Ops, PlacesService Places, SessionStore Sessions) NewOperations()
    {
        var clock = new ManualTimeProvider(Now, TestZones.PlusTen);
        var places = new PlacesService(new FakePlacesStorage(), clock);
        var sessions = new SessionStore(new FakePlacesStorage { StoreFilePath = @"C:\fake\sessions.json" }, clock);
        return (new StoreOperations(() => places, () => sessions), places, sessions);
    }

    private static OperationRequest AddUrl(string alias) => new(StoreOperations.AddPlace,
        new JsonObject { ["alias"] = alias, ["type"] = "url", ["resource"] = $"https://{alias.ToLowerInvariant()}.example.com" });

    [Fact]
    public void ARequest_RoundTripsOverARealPipe()
    {
        var (ops, places, _) = NewOperations();
        var pipe = "QuickerPlacesTests." + Guid.NewGuid().ToString("N");
        OperationEffect? seen = null;
        using var server = new RemoteCommandServer(pipe, request =>
        {
            var reply = ops.Execute(request, out var effect);
            seen = effect;
            return reply;
        });
        server.Start();

        var first = RemoteCommandClient.Send(pipe, AddUrl("Wiki"), TimeSpan.FromSeconds(5));
        var second = RemoteCommandClient.Send(pipe, AddUrl("wiki"), TimeSpan.FromSeconds(5));

        Assert.True(first.Ok);
        Assert.Equal(places.Places.Single().Id.ToString(), first.Data!["id"]!.GetValue<string>());
        Assert.Equal("Wiki", seen!.AddedPlace?.Alias ?? places.Places.Single().Alias);
        Assert.False(second.Ok);
        Assert.Equal(OperationErrors.Invalid, second.Code);
    }

    [Fact]
    public void NoServer_TimesOut()
    {
        Assert.ThrowsAny<Exception>(() => RemoteCommandClient.Send("QuickerPlacesTests.none." + Guid.NewGuid().ToString("N"), AddUrl("A"), TimeSpan.FromMilliseconds(200)));
    }

    [Fact]
    public void Add_ReportsTheAddedPlace_ForTheAppToShow()
    {
        var (ops, _, _) = NewOperations();

        var reply = ops.Execute(AddUrl("Wiki"), out var effect);

        Assert.True(reply.Ok);
        Assert.Equal("Wiki", effect.AddedPlace!.Alias);
    }

    [Fact]
    public void AFailedSave_IsAnError_ButStillReportsTheChange()
    {
        var storage = new FakePlacesStorage { FailEveryWrite = true };
        var failing = new PlacesService(storage, new ManualTimeProvider(Now));
        failing.TryAdd("Docs", PlaceType.Url, "https://docs.example.com", out var docs, out _);
        var failingOps = new StoreOperations(() => failing, () => throw new InvalidOperationException());

        var reply = failingOps.Execute(new OperationRequest(StoreOperations.SetPlaceTags,
            new JsonObject { ["id"] = docs!.Id.ToString(), ["tags"] = new JsonArray("a") }), out var effect);

        Assert.False(reply.Ok);
        Assert.Equal(OperationErrors.SaveFailed, reply.Code);
        Assert.Same(docs, effect.ChangedPlace);
        Assert.Equal(new[] { "a" }, docs.Tags);
    }

    [Fact]
    public void AnUnknownId_IsNotFound()
    {
        var (ops, _, _) = NewOperations();

        var reply = ops.Execute(new OperationRequest(StoreOperations.RecordPlaceOpen, new JsonObject { ["id"] = Guid.NewGuid().ToString() }), out _);

        Assert.Equal(OperationErrors.NotFound, reply.Code);
    }

    [Fact]
    public void AMalformedRequest_IsUsage()
    {
        var (ops, _, _) = NewOperations();

        Assert.Equal(OperationErrors.Usage, ops.Execute(new OperationRequest(StoreOperations.RecordPlaceOpen, new JsonObject { ["id"] = "not-a-guid" }), out _).Code);
        Assert.Equal(OperationErrors.Usage, ops.Execute(new OperationRequest("places.explode", new JsonObject()), out _).Code);
        Assert.Equal(OperationErrors.Usage, ops.Execute(new OperationRequest(StoreOperations.AddPlace, new JsonObject()), out _).Code);
    }

    [Fact]
    public void CreateSession_ThenMarkOpened()
    {
        var (ops, _, sessions) = NewOperations();
        var files = new JsonArray(@"C:\Jobs\Acme\report.pdf");

        var created = ops.Execute(new OperationRequest(StoreOperations.CreateSession,
            new JsonObject { ["name"] = "Audit", ["tags"] = new JsonArray("acme"), ["files"] = files }), out var effect);
        Assert.True(created.Ok, created.Message);
        Assert.True(effect.SessionsChanged);

        var id = created.Data!["id"]!.GetValue<string>();
        Assert.True(ops.Execute(new OperationRequest(StoreOperations.MarkSessionOpened, new JsonObject { ["id"] = id }), out _).Ok);
        Assert.Single(sessions.Find(id)!.OpenedAt);
    }
}
