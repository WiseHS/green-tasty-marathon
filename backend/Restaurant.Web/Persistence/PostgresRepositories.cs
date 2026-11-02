using APIHandler.Entities;
using APIHandler.Entities.Reports;
using APIHandler.Models.Feedbacks;
using APIHandler.Models.WaiterList;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.Feedbacks;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Locations;
using APIHandler.Repositories.Notifications;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reports;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Repositories.Waiters;

namespace Restaurant.Web.Persistence;

public sealed class PostgresRepositories(PostgresStore db) : IDishRepository, ILocationRepository,
    ITableRepository, IReservationRepository, IOrderRepository, IGuestSessionRepository,
    IGroupReservationRepository, IFeedbackRepository, INotificationRepository, IWaiterRepository, IReportRepository
{
    public Task<Dish?> GetDishByIdAsync(string id) => db.Get<Dish>(id);
    public async Task<IEnumerable<Dish>> GetDishesByIdsAsync(IEnumerable<string> ids)
    {
        var result = new List<Dish>();
        foreach (var id in ids.Distinct()) if (await db.Get<Dish>(id) is { } dish) result.Add(dish);
        return result;
    }
    public async Task<IEnumerable<Dish>> GetDishesByLocationIdAsync(string id)
    {
        if (string.IsNullOrEmpty(id)) return await db.Find<Dish>();
        var dishes = await db.Find<Dish>(nameof(Dish.LocationId), id);
        var location = await db.Get<Location>(id);
        if (location?.SpecialtyDishIds is { Count: > 0 }) dishes.AddRange(await GetDishesByIdsAsync(location.SpecialtyDishIds));
        return dishes.Count == 0 ? await db.Find<Dish>() : dishes.DistinctBy(d => d.Id);
    }
    public Task SaveDishAsync(Dish value) => db.Save(value);
    public Task DeleteDishAsync(Dish value) => db.Delete(value);
    public Task<Location?> GetLocationByIdAsync(string id) => db.Get<Location>(id);
    public Task<Table?> GetTableByIdAsync(string id) => db.Get<Table>(id);
    public Task<Table?> GetTableAsync(string id) => db.Get<Table>(id);
    public async Task<IEnumerable<Location>> GetAllLocationsAsync() => await db.Find<Location>();
    public Task<List<Table>> GetTablesByLocationIdAsync(string id) => db.Find<Table>(nameof(Table.LocationId), id);
    async Task<IEnumerable<Table>> ITableRepository.GetTablesByLocationIdAsync(string id) => await GetTablesByLocationIdAsync(id);
    public async Task<IEnumerable<Table>> GetAllTablesAsync() => await db.Find<Table>();
    public Task SaveTableAsync(Table value) => db.Save(value);
    public Task DeleteTableAsync(Table value) => db.Delete(value);
    public Task SaveLocationAsync(Location value) => db.Save(value);
    public Task DeleteLocationAsync(Location value) => db.Delete(value);
    public Task<Reservation?> GetReservationByIdAsync(string id) => db.Get<Reservation>(id);
    public Task<List<Reservation>> GetReservationsByTableIdAsync(string id) => db.Find<Reservation>(nameof(Reservation.TableId), id);
    public async Task<IEnumerable<Reservation>> GetReservationsByWaiterIdAsync(string id) => await db.Find<Reservation>(nameof(Reservation.WaiterId), id);
    public async Task<IEnumerable<Reservation>> GetReservationsByCustomerIdAsync(string id) => await db.Find<Reservation>(nameof(Reservation.CustomerId), id);
    public Task SaveReservationAsync(Reservation value) => db.Save(value);
    public Task<Order?> GetOrderByIdAsync(string orderId, string customerId) => db.Get<Order>($"{orderId}|{customerId}");
    public async Task<IEnumerable<Order>> GetOrdersByCustomerIdAsync(string id) => await db.Find<Order>(nameof(Order.CustomerId), id);
    public async Task<Order?> GetOrderByReservationIdAsync(string id) => (await db.Find<Order>(nameof(Order.ReservationId), id)).FirstOrDefault();
    public Task SaveOrderAsync(Order value) => db.Save(value);
    public async Task<int> CountByGroupReservationId(string id) => (await GetAllByGroupReservationIdAsync(id)).Count;
    public async Task<GuestSession?> GetByGroupReservationId(string id) => (await GetAllByGroupReservationIdAsync(id)).FirstOrDefault();
    public Task<GuestSession?> GetByGuestSessionId(string id) => db.Get<GuestSession>(id);
    public async Task<GuestSession?> GetByTokenHash(string hash) => (await db.Find<GuestSession>(nameof(GuestSession.SessionTokenHash), hash)).FirstOrDefault();
    public Task<List<GuestSession>> GetAllByGroupReservationIdAsync(string id) => db.Find<GuestSession>(nameof(GuestSession.GroupReservationId), id);
    public Task SaveAsync(GuestSession value) => db.Save(value);
    public Task DeleteAsync(GuestSession value) => db.Delete(value);
    public async Task<GroupReservation?> GetByReservationIdAsync(string id) => (await db.Find<GroupReservation>(nameof(GroupReservation.ReservationId), id)).FirstOrDefault();
    public Task<GroupReservation?> GetGroupReservationByIdAsync(string id) => db.Get<GroupReservation>(id);
    public async Task<GroupReservation?> GetGroupReservationByTokenAsync(string token) => (await db.Find<GroupReservation>(nameof(GroupReservation.ReservationToken), token)).FirstOrDefault();
    public Task SaveGroupReservationAsync(GroupReservation value) => db.Save(value);
    public Task DeleteGroupReservationAsync(GroupReservation value) => db.Delete(value);
    public Task<List<Feedback>> GetFeedbackByLocationIdAsync(string id) => db.Find<Feedback>(nameof(Feedback.LocationId), id);
    public Task<List<Feedback>> GetFeedbackByReservationIdAsync(string id) => db.Find<Feedback>(nameof(Feedback.ReservationId), id);
    public Task SaveFeedbackAsync(Feedback value) => db.Save(value);
    public async Task<IEnumerable<Notification>> GetUnreadNotificationsByUserIdAsync(string id) => (await db.Find<Notification>(nameof(Notification.UserId), id)).Where(n => !n.IsRead);
    public Task<Notification?> GetNotificationByIdAsync(string id) => db.Get<Notification>(id);
    public Task SaveNotificationAsync(Notification value) => db.Save(value);
    public Task<WaiterListItem?> GetWaiterByIdAsync(string id) => db.Get<WaiterListItem>(id);
    public Task<List<WaiterListItem>> GetWaitersByLocationIdAsync(string id) => db.Find<WaiterListItem>(nameof(WaiterListItem.LocationId), id);
    public Task<List<WaiterListItem>> GetAllWaitersAsync() => db.Find<WaiterListItem>();
    public Task DeleteWaiterAsync(WaiterListItem value) => db.Delete(value);
    public Task SaveWaiterAsync(WaiterListItem value) => db.Save(value);
    public async Task<List<DailyAggregate>> GetAggregatesForPeriodAsync(DateTime start, DateTime end, string id) =>
        (await db.Find<DailyAggregate>(nameof(DailyAggregate.LocationId), id)).Where(a => a.Date >= start && a.Date <= end).ToList();
    public Task<List<ReportHistoryWaiter>> GetWaiterReportsByUserIdAsync(string id) => db.Find<ReportHistoryWaiter>(nameof(ReportHistoryWaiter.UserId), id);
    public Task<List<ReportHistoryLocation>> GetLocationReportsByUserIdAsync(string id) => db.Find<ReportHistoryLocation>(nameof(ReportHistoryLocation.UserId), id);
    public Task SaveReportHistoryAsync<T>(T value) where T : ReportHistoryBase => db.Save(value);
}
