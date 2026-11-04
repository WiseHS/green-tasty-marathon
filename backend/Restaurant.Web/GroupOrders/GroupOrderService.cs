using Amazon.Lambda.APIGatewayEvents;
using APIHandler.Entities;
using APIHandler.Constants;
using APIHandler.Constants.Dishes;
using APIHandler.Constants.Orders;
using APIHandler.Mappers;
using Restaurant.Web.Persistence;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace Restaurant.Web.GroupOrders;

// Invoked inside the host's existing serialized PostgreSQL transaction for writes.
public sealed class GroupOrderService(PostgresStore store)
{
    public static List<OrderItem> Consolidate(IEnumerable<OrderItem> ownerItems, IEnumerable<GuestSession> sessions)
        => ownerItems.Concat(sessions.Where(session => session.IsSubmitted && !session.IsOwnerSession)
            .SelectMany(session => session.Dishes))
            .GroupBy(item => item.DishId).Select(group => new OrderItem
            {
                DishId = group.Key, Quantity = checked(group.Sum(item => item.Quantity)), State = OrderItemState.Pending
            }).ToList();

    public async Task<APIGatewayProxyResponse> Handle(string reservationId, string method, Dictionary<string,string> claims, string body)
    {
        var reservation = await store.Get<Reservation>(reservationId);
        if (reservation is null) return Reply(404, new { error = "Reservation not found." });
        var owner = reservation.CustomerId == claims.GetValueOrDefault("sub");
        var waiter = reservation.WaiterId == claims.GetValueOrDefault("sub") &&
            claims.GetValueOrDefault("cognito:groups", "").Split(',').Contains("Waiter");
        if (!owner && !waiter || method == "POST" && !owner)
            return Reply(403, new { error = "Only the reservation owner may send a group order." });
        var snapshot = await store.Get<GroupOrderSubmission>(reservationId);
        if (snapshot is not null) return Reply(200, snapshot.View); // retry-safe, no duplicate addition
        if (!owner) return Reply(404, new { error = "The group order has not been sent yet." });
        var group = (await store.Find<GroupReservation>(nameof(GroupReservation.ReservationId), reservationId)).FirstOrDefault();
        if (group is null) return Reply(404, new { error = "Group reservation not found." });
        var sessions = await store.Find<GuestSession>(nameof(GuestSession.GroupReservationId), group.GroupReservationId);
        var order = (await store.Find<Order>(nameof(Order.ReservationId), reservationId))
            .FirstOrDefault(value => value.Status != OrderStatus.Cancelled);
        var ownerItems = order?.Dishes ?? [];
        var merged = Consolidate(ownerItems, sessions);
        var allItemIds = ownerItems.Concat(sessions.SelectMany(session => session.Dishes))
            .Select(item => item.DishId).Distinct().ToList();
        var dishes = new Dictionary<string,Dish>();
        foreach (var dishId in allItemIds)
        {
            var dish = await store.Get<Dish>(dishId);
            if (dish is not null) dishes[dishId] = dish;
        }
        List<GroupDish> Display(IEnumerable<OrderItem> items) => items.OrderBy(item => item.DishId).Select(item =>
        {
            if (!dishes.TryGetValue(item.DishId, out var dish)) return null;
            return new GroupDish(DishMapper.ToDishResponse(dish), item.Quantity, dish.Price * item.Quantity);
        }).OfType<GroupDish>().ToList();
        var table = await store.Get<Table>(reservation.TableId);
        var location = table is null ? null : await store.Get<Location>(table.LocationId);
        var participants = new List<GroupParticipant>();
        var ownDisplay = Display(ownerItems);
        // A genuinely empty group has no participants yet (the homework API contract).
        // Show the organizer alongside joined guests or their own selected dishes.
        if (ownDisplay.Count > 0 || sessions.Any(session => !session.IsOwnerSession))
            participants.Add(new("owner", "Your order", true, order?.Status is OrderStatus.SubmittedByCustomer or OrderStatus.Submitted,
                true, ownDisplay, ownDisplay.Sum(item => item.Total)));
        foreach (var session in sessions.Where(session => !session.IsOwnerSession).OrderBy(session => session.Id))
        {
            var items = Display(session.Dishes);
            participants.Add(new(session.Id, session.GuestName, false, session.IsSubmitted,
                session.IsSubmitted, items, items.Sum(item => item.Total)));
        }
        string? blocked = reservation.Status != ReservationStatus.Reserved ? "The reservation is no longer open for pre-orders."
            : group.Status != GroupReservationStatus.Open ? "The group is closed."
            : DateTime.UtcNow > reservation.StartTime.ToUniversalTime().AddMinutes(-30) ? "Group orders close 30 minutes before the reservation."
            : order?.Status == OrderStatus.Submitted || ownerItems.Any(item => item.State != OrderItemState.Pending)
                ? "The restaurant has already processed your order. Contact the waiter before making changes."
            : merged.Count == 0 ? "Add your dishes or wait for a guest to submit their selection."
            : order is null ? "The reservation order was not found. Refresh the reservation or contact the restaurant."
            : merged.Any(item => item.Quantity <= 0 || !dishes.TryGetValue(item.DishId, out var dish) || dish.State != DishState.Available)
                ? "An included dish is unavailable. Review the participant orders before sending." : null;
        var consolidated = Display(merged);
        var view = new GroupOrderView(reservationId, group.GroupReservationId,
            table is null ? "Table unavailable" : $"Table {table.TableNumber}", location?.Address ?? "",
            reservation.StartTime, "open", blocked is null, blocked, participants, consolidated,
            consolidated.Sum(item => item.Total), sessions.Count(session => !session.IsOwnerSession && session.IsSubmitted),
            sessions.Count(session => !session.IsOwnerSession), Math.Max(0, reservation.GuestCount - 1), order?.OrderId, "");
        view = view with { Revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(view)))) };
        if (method == "GET") return Reply(200, view);
        if (blocked is not null) return Reply(400, new { error = blocked });
        try
        {
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("revision", out var revision) || revision.GetString() != view.Revision)
                return Reply(409, new { error = "Participant selections changed. Refresh and review the group order again." });
        }
        catch (JsonException) { return Reply(400, new { error = "A reviewed group-order revision is required." }); }
        order ??= new Order
        {
            OrderId = Guid.NewGuid().ToString(), CustomerId = reservation.CustomerId!, ReservationId = reservationId,
            ReservationAddress = location?.Address ?? "", ReservationTime = $"{reservation.StartTime:MMM dd, yyyy, HH:mm} - {reservation.EndTime:HH:mm}", Dishes = []
        };
        order.Dishes = merged;
        order.Status = OrderStatus.SubmittedByCustomer;
        group.Status = GroupReservationStatus.Submitted;
        view = view with { Status = "submitted", CanSubmit = false, BlockReason = null, OrderId = order.OrderId };
        await store.Save(order);
        await store.Save(group);
        await store.Save(new GroupOrderSubmission { ReservationId = reservationId, View = view });
        return Reply(200, view);
    }
    private static APIGatewayProxyResponse Reply(int status, object body) => new()
    {
        StatusCode = status, Body = JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        Headers = new Dictionary<string,string> { ["Content-Type"] = "application/json" }
    };
}
