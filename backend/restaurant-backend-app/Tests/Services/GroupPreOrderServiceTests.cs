using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Services.GroupReservations;
using Moq;

namespace APIHandler.Tests.Services;

public class GroupPreOrderServiceTests
{
    private readonly Mock<IGroupReservationRepository> groups = new();
    private readonly Mock<IGuestSessionRepository> sessions = new();
    private readonly Mock<IReservationRepository> reservations = new();
    private readonly Mock<IOrderRepository> orders = new();

    private GroupPreOrderService CreateService() => new(groups.Object, sessions.Object, reservations.Object, orders.Object);

    private void SetupOwner()
    {
        groups.Setup(x => x.GetGroupReservationByIdAsync("group-1")).ReturnsAsync(
            new GroupReservation { GroupReservationId = "group-1", ReservationId = "reservation-1", ReservationToken = "token", Status = GroupReservationStatus.Open });
        groups.Setup(x => x.GetByReservationIdAsync("reservation-1")).ReturnsAsync(
            new GroupReservation { GroupReservationId = "group-1", ReservationId = "reservation-1", ReservationToken = "token", Status = GroupReservationStatus.Open });
        reservations.Setup(x => x.GetReservationByIdAsync("reservation-1")).ReturnsAsync(
            new Reservation { Id = "reservation-1", TableId = "table", WaiterId = "waiter", CustomerId = "owner-1" });
    }

    [Fact]
    public async Task GetOwnerSessionIdAsync_ReturnsExistingSession()
    {
        SetupOwner();
        sessions.Setup(x => x.GetAllByGroupReservationIdAsync("group-1")).ReturnsAsync([
            new GuestSession { Id = "guest", GuestName = "Andrii", Dishes = [] },
            new GuestSession { Id = "owner-session", GuestName = "Owner", Dishes = [], IsOwnerSession = true, OwnerUserId = "owner-1" }
        ]);

        Assert.Equal("owner-session", await CreateService().GetOwnerSessionIdAsync("owner-1", "reservation-1"));
        sessions.Verify(x => x.SaveAsync(It.IsAny<GuestSession>()), Times.Never);
    }

    [Fact]
    public async Task GetOwnerSessionIdAsync_CreatesSessionWhenMissing()
    {
        SetupOwner();
        sessions.Setup(x => x.GetAllByGroupReservationIdAsync("group-1")).ReturnsAsync([]);

        var id = await CreateService().GetOwnerSessionIdAsync("owner-1", "reservation-1");

        Assert.NotEmpty(id);
        sessions.Verify(x => x.SaveAsync(It.Is<GuestSession>(s => s.Id == id && s.IsOwnerSession && s.OwnerUserId == "owner-1" && s.GroupReservationId == "group-1")), Times.Once);
    }

    [Fact]
    public async Task GetOwnerSessionIdAsync_RejectsNonOwner()
    {
        SetupOwner();
        await Assert.ThrowsAsync<ActionForbiddenException>(() => CreateService().GetOwnerSessionIdAsync("other", "reservation-1"));
    }

    [Fact]
    public async Task GetOwnerSessionIdAsync_Returns404ForMissingReservation()
    {
        await Assert.ThrowsAsync<ReservationNotFoundException>(() => CreateService().GetOwnerSessionIdAsync("owner-1", "missing"));
    }

    [Fact]
    public async Task SubmitAsync_ConsolidatesOnlySubmittedSessions()
    {
        SetupOwner();
        sessions.Setup(x => x.GetAllByGroupReservationIdAsync("group-1")).ReturnsAsync([
            new GuestSession { Id = "a", GuestName = "A", Dishes = [new OrderItem { DishId = "dish-1", Quantity = 2 }], IsSubmitted = true },
            new GuestSession { Id = "b", GuestName = "B", Dishes = [new OrderItem { DishId = "dish-1", Quantity = 1 }, new OrderItem { DishId = "dish-2", Quantity = 2 }], IsSubmitted = true },
            new GuestSession { Id = "c", GuestName = "C", Dishes = [new OrderItem { DishId = "dish-3", Quantity = 9 }] }
        ]);
        var order = new Order { OrderId = "order-1", CustomerId = "owner-1", ReservationId = "reservation-1", ReservationAddress = "address", ReservationTime = "time", Dishes = [] };
        orders.Setup(x => x.GetOrderByReservationIdAsync("reservation-1")).ReturnsAsync(order);

        var result = await CreateService().SubmitAsync("owner-1", "group-1", "reservation-1");

        Assert.Equal(2, result.Count);
        Assert.Contains(result, x => x.DishId == "dish-1" && x.Quantity == 3);
        Assert.Contains(result, x => x.DishId == "dish-2" && x.Quantity == 2);
        Assert.DoesNotContain(result, x => x.DishId == "dish-3");
        Assert.Equal(OrderStatus.SubmittedByCustomer, order.Status);
        orders.Verify(x => x.SaveOrderAsync(order), Times.Once);
        groups.Verify(x => x.SaveGroupReservationAsync(It.Is<GroupReservation>(g => g.Status == GroupReservationStatus.Submitted)), Times.Once);
    }

    [Fact]
    public async Task SubmitAsync_RejectsNoSubmittedSessionsWithoutChangingOrder()
    {
        SetupOwner();
        sessions.Setup(x => x.GetAllByGroupReservationIdAsync("group-1")).ReturnsAsync([]);
        await Assert.ThrowsAsync<ValidationException>(() => CreateService().SubmitAsync("owner-1", "group-1", "reservation-1"));
        orders.Verify(x => x.SaveOrderAsync(It.IsAny<Order>()), Times.Never);
    }

    [Fact]
    public async Task SubmitAsync_RejectsEmptySubmittedSessions()
    {
        SetupOwner();
        sessions.Setup(x => x.GetAllByGroupReservationIdAsync("group-1")).ReturnsAsync([
            new GuestSession { Id = "a", GuestName = "A", Dishes = [], IsSubmitted = true }
        ]);
        await Assert.ThrowsAsync<ValidationException>(() => CreateService().SubmitAsync("owner-1", "group-1", "reservation-1"));
    }

    [Fact]
    public async Task SubmitAsync_RejectsMissingOrder()
    {
        SetupOwner();
        sessions.Setup(x => x.GetAllByGroupReservationIdAsync("group-1")).ReturnsAsync([
            new GuestSession { Id = "a", GuestName = "A", Dishes = [new OrderItem { DishId = "dish-1", Quantity = 1 }], IsSubmitted = true }
        ]);
        await Assert.ThrowsAsync<ValidationException>(() => CreateService().SubmitAsync("owner-1", "group-1", "reservation-1"));
    }

    [Fact]
    public async Task SubmitAsync_RejectsWrongReservationId()
    {
        SetupOwner();
        await Assert.ThrowsAsync<ValidationException>(() => CreateService().SubmitAsync("owner-1", "group-1", "other-reservation"));
    }

    [Fact]
    public async Task SubmitAsync_RejectsNonOwner()
    {
        SetupOwner();
        await Assert.ThrowsAsync<ActionForbiddenException>(() => CreateService().SubmitAsync("other", "group-1", "reservation-1"));
    }

    [Fact]
    public async Task SubmitAsync_Returns404ForMissingGroup()
    {
        await Assert.ThrowsAsync<GroupReservationNotFoundException>(() => CreateService().SubmitAsync("owner-1", "missing", "reservation-1"));
    }
}
