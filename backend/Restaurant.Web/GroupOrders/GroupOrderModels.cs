using Amazon.DynamoDBv2.DataModel;
using APIHandler.Models.Dishes;

namespace Restaurant.Web.GroupOrders;

public record GroupDish(DishResponse Dish, int Quantity, decimal Total, string Note = "");
public record GroupParticipant(string Id, string Name, bool IsOwner, bool IsSubmitted,
    bool Included, List<GroupDish> Items, decimal Total);
public record GroupOrderView(string ReservationId, string GroupReservationId, string TableName,
    string LocationAddress, DateTime StartTime, string Status, bool CanSubmit, string? BlockReason,
    List<GroupParticipant> Participants, List<GroupDish> ConsolidatedItems, decimal Total,
    int ReadyGuests, int JoinedGuests, int InvitedGuests, string? OrderId, string Revision);
public sealed class GroupOrderSubmission
{
    [DynamoDBHashKey]
    public required string ReservationId { get; set; }
    public required GroupOrderView View { get; set; }
}
