using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using QuickerPlaces.Models;
using QuickerPlaces.Services.Sessions;

namespace QuickerPlaces.Services.Remote;

/// <summary>
/// Every change qp can make, as named operations on the live stores. The CLI
/// runs them itself when the app is closed, and sends them over the pipe
/// (RemoteCommandServer) when it is open, where the app runs them on the
/// services it already holds. So a change follows one set of rules either
/// way, and with the app open it is saved by the app, never written beside it.
///
/// UI-free, like the services it calls. Places are named by id: the caller
/// resolves aliases itself, from what it reads.
/// </summary>
public sealed class StoreOperations
{
    public const string AddPlace = "places.add";
    public const string SetPlaceTags = "places.setTags";
    public const string SetPlaceNote = "places.setNote";
    public const string RecordPlaceOpen = "places.recordOpen";
    public const string CreateSession = "sessions.create";
    public const string MarkSessionOpened = "sessions.markOpened";

    private readonly Func<PlacesService> _places;
    private readonly Func<SessionStore> _sessions;

    /// <param name="places">The places service, made when an operation first needs it (so a sessions change never opens places.json).</param>
    /// <param name="sessions">The session store, likewise.</param>
    public StoreOperations(Func<PlacesService> places, Func<SessionStore> sessions)
    {
        _places = places;
        _sessions = sessions;
    }

    /// <summary>Runs <paramref name="request"/>; <paramref name="effect"/> says what changed, for the app to show.</summary>
    public OperationReply Execute(OperationRequest request, out OperationEffect effect)
    {
        effect = OperationEffect.None;
        try
        {
            var args = request.Args;
            switch (request.Op)
            {
                case AddPlace:
                    return Add(args, out effect);
                case SetPlaceTags:
                {
                    var (service, place) = Place(args);
                    effect = Saved(service.SetTags(place, Strings(args, "tags")), changed: place);
                    return Reply(new JsonObject { ["id"] = place.Id.ToString() });
                }
                case SetPlaceNote:
                {
                    var (service, place) = Place(args);
                    effect = Saved(service.SetNote(place, args["note"]?.GetValue<string>()), changed: place);
                    return Reply(new JsonObject { ["id"] = place.Id.ToString() });
                }
                case RecordPlaceOpen:
                {
                    var (service, place) = Place(args);
                    effect = Saved(service.RecordOpen(place), changed: place);
                    return Reply(new JsonObject { ["id"] = place.Id.ToString() });
                }
                case CreateSession:
                    return NewSession(args, out effect);
                case MarkSessionOpened:
                {
                    var store = Sessions();
                    var id = Required(args, "id");
                    if (store.Find(id) is null)
                        throw new OperationException(OperationErrors.NotFound, "That session no longer exists.");
                    effect = Saved(store.MarkOpened(id), sessionsChanged: true);
                    return Reply(new JsonObject { ["id"] = id });
                }
                default:
                    throw new OperationException(OperationErrors.Usage, $"Unknown operation \"{request.Op}\".");
            }
        }
        catch (OperationException ex)
        {
            // A failed save still changed what the app holds; it must show it.
            effect = ex.Effect ?? OperationEffect.None;
            return OperationReply.Fail(ex.Code, ex.Message, ex.Details);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or System.Text.Json.JsonException)
        {
            return OperationReply.Fail(OperationErrors.Usage, $"Malformed {request.Op} request: {ex.Message}");
        }
    }

    private OperationReply Add(JsonObject args, out OperationEffect effect)
    {
        var service = Places();
        var type = Required(args, "type") switch
        {
            "folder" => PlaceType.Folder,
            "url" => PlaceType.Url,
            _ => throw new OperationException(OperationErrors.Usage, "type must be folder or url.")
        };

        var validation = service.TryAdd(Required(args, "alias"), type, Required(args, "resource"), out var place, out var persistence);
        if (!validation.Success)
            throw new OperationException(OperationErrors.Invalid, validation.ErrorMessage ?? "That place can't be saved.");
        effect = Saved(persistence, added: place);

        if (args["tags"] is JsonArray)
            effect = Saved(service.SetTags(place!, Strings(args, "tags")), added: place);
        if (args["note"]?.GetValue<string>() is { } note)
            effect = Saved(service.SetNote(place!, note), added: place);

        return Reply(new JsonObject { ["id"] = place!.Id.ToString() });
    }

    private OperationReply NewSession(JsonObject args, out OperationEffect effect)
    {
        var store = Sessions();
        var validation = store.TryCreate(Required(args, "name"), Strings(args, "tags"), Strings(args, "files"), out var created, out var persistence);
        if (!validation.Success)
            throw new OperationException(OperationErrors.Invalid, validation.ErrorMessage ?? "That session can't be saved.");

        effect = Saved(persistence, sessionsChanged: true);
        return Reply(new JsonObject { ["id"] = created!.Id });
    }

    private PlacesService Places()
    {
        var service = _places();
        if (service.LoadOutcome is not (StoreLoadOutcome.Ok or StoreLoadOutcome.NotPresent) || service.IsRecoveryUnresolved)
            throw new OperationException(OperationErrors.StoreUnavailable, "Your places can't be changed until QuickerPlaces has recovered them. Open QuickerPlaces to check.");
        return service;
    }

    private SessionStore Sessions()
    {
        var store = _sessions();
        if (!store.IsAvailable)
            throw new OperationException(OperationErrors.StoreUnavailable, store.Notice ?? "Sessions can't be changed right now.");
        return store;
    }

    private (PlacesService Service, Place Place) Place(JsonObject args)
    {
        var service = Places();
        var id = Guid.Parse(Required(args, "id"));
        var place = service.Places.Concat(service.RecentlyDeleted).FirstOrDefault(p => p.Id == id)
            ?? throw new OperationException(OperationErrors.NotFound, "That place no longer exists.");
        return (service, place);
    }

    /// <summary>A save the disk refused is an error: the change stays in memory (and, in the app, behind its Retry banner), but it isn't stored.</summary>
    private static OperationEffect Saved(PersistenceResult persistence, Place? added = null, Place? changed = null, bool sessionsChanged = false)
    {
        var effect = new OperationEffect(added, changed, sessionsChanged, persistence);
        if (!persistence.Saved)
            throw new OperationException(OperationErrors.SaveFailed, persistence.UserMessage ?? "The change couldn't be saved.") { Effect = effect };
        return effect;
    }

    private static OperationReply Reply(JsonObject data) => OperationReply.Success(data);

    private static string Required(JsonObject args, string name)
        => args[name]?.GetValue<string>() ?? throw new OperationException(OperationErrors.Usage, $"Missing \"{name}\".");

    private static List<string> Strings(JsonObject args, string name)
        => args[name] is JsonArray array ? array.Select(n => n?.GetValue<string>() ?? "").ToList() : new List<string>();
}

/// <summary>What an operation changed, so the app can bring its window up to date. Set even when the save then failed.</summary>
public sealed record OperationEffect(Place? AddedPlace, Place? ChangedPlace, bool SessionsChanged, PersistenceResult Persistence)
{
    public static OperationEffect None { get; } = new(null, null, false, PersistenceResult.Ok());
}

/// <summary>A refused operation, with an error code from <see cref="OperationErrors"/>.</summary>
public sealed class OperationException : Exception
{
    public OperationException(string code, string message, JsonObject? details = null) : base(message)
    {
        Code = code;
        Details = details;
    }

    public string Code { get; }

    public JsonObject? Details { get; }

    public OperationEffect? Effect { get; init; }
}

/// <summary>The error codes operations return: the same strings as qp's own (QuickerPlaces.Cli.ErrorCodes).</summary>
public static class OperationErrors
{
    public const string Usage = "usage";
    public const string NotFound = "not_found";
    public const string Invalid = "invalid";
    public const string StoreUnavailable = "store_unavailable";
    public const string SaveFailed = "save_failed";
}
