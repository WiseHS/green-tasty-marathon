using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.APIGatewayEvents;
using APIHandler.Entities;
using APIHandler.Constants;
using Restaurant.Web.Persistence;
using System.Security.Cryptography;
using System.Text.Json;

namespace Restaurant.Web.GroupOrders;

public sealed class GuestDishRequest
{
    [DynamoDBHashKey] public required string Id { get; set; }
    public required string SessionId { get; set; }
    public required string DishId { get; set; }
    public required string Note { get; set; }
}

public sealed class GuestDishRequestService(PostgresStore store)
{
    public const int MaximumLength = 300;

    public async Task<APIGatewayProxyResponse> Handle(string sessionId, string? dishId, string method, string authorization, string body)
    {
        var session = await store.Get<GuestSession>(sessionId);
        if (session is null || !Authorized(session, authorization)) return Reply(401, new {error = "A valid guest session is required."});
        var selected = session.Dishes.Select(item => item.DishId).ToHashSet();
        if (method == "GET" && dishId is null)
        {
            var notes = await store.Find<GuestDishRequest>(nameof(GuestDishRequest.SessionId), sessionId);
            return Reply(200, new {notes = notes.Where(note => selected.Contains(note.DishId)).Select(note => new {note.DishId, note.Note})});
        }
        if (method != "PUT" || dishId is null) return Reply(405, new {error = "This request method is not supported."});
        var group = await store.Get<GroupReservation>(session.GroupReservationId);
        var reservation = group is null ? null : await store.Get<Reservation>(group.ReservationId);
        if (session.IsSubmitted || group?.Status != GroupReservationStatus.Open || reservation?.Status != ReservationStatus.Reserved ||
            DateTime.UtcNow > reservation.StartTime.ToUniversalTime().AddMinutes(-30))
            return Reply(409, new {error = "This selection is locked. Requests cannot be changed after submission or the pre-order deadline."});
        if (!selected.Contains(dishId)) return Reply(404, new {error = "Add this dish to your cart before saving a request."});
        string note;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("note", out var value) || value.ValueKind != JsonValueKind.String)
                return Reply(400, new {error = "A text request is required."});
            var raw = value.GetString()!;
            if (raw.Length > MaximumLength || raw.Any(character => char.IsControl(character) && character is not '\n' and not '\r' and not '\t'))
                return Reply(400, new {error = "Requests must be plain text of at most 300 characters."});
            note = raw.Replace("\r\n", "\n").Trim();
        }
        catch (JsonException) { return Reply(400, new {error = "A text request is required."}); }
        var entity = new GuestDishRequest {Id = $"{sessionId}|{dishId}", SessionId = sessionId, DishId = dishId, Note = note};
        if (note.Length == 0) await store.Delete(entity);
        else await store.Save(entity);
        return Reply(200, new {dishId, note});
    }

    public async Task Prune(string sessionId)
    {
        var session = await store.Get<GuestSession>(sessionId);
        var selected = session?.Dishes.Select(item => item.DishId).ToHashSet() ?? [];
        foreach (var note in await store.Find<GuestDishRequest>(nameof(GuestDishRequest.SessionId), sessionId))
            if (!selected.Contains(note.DishId)) await store.Delete(note);
    }

    private static bool Authorized(GuestSession session, string authorization)
    {
        if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || session.SessionTokenHash is null) return false;
        try
        {
            var token = Convert.FromBase64String(authorization[7..]);
            return token.Length == 32 && CryptographicOperations.FixedTimeEquals(SHA256.HashData(token), Convert.FromBase64String(session.SessionTokenHash));
        }
        catch (FormatException) { return false; }
    }

    private static APIGatewayProxyResponse Reply(int status, object value) => new()
    {
        StatusCode = status, Body = JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        Headers = new Dictionary<string,string> {{"Content-Type", "application/json"}}
    };
}
