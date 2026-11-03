using TUnit.Core;

namespace Restaurant_Aqa_Framework.GroupPreOrder;

[Category("group-preorder")]
[Category("group-l2")]
public sealed class GroupLevel2Tests
{
    [Test]
    public async Task OwnerConsolidatesOnlySubmittedCartsIncludingOwnAndWaiterSeesTheOrder()
    {
        await using var group = await GroupFixture.CreateAsync();
        await group.CreateOrderAsync();
        var first = await group.JoinAsync("First");
        var second = await group.JoinAsync("Second");
        var pending = await group.JoinAsync("Pending");
        await group.AddDishAsync(first, group.FirstDish, 2);
        await group.AddDishAsync(second, group.FirstDish, 1);
        await group.AddDishAsync(second, group.SecondDish, 2);
        await group.AddDishAsync(pending, group.SecondDish, 9);
        await group.SubmitGuestAsync(first);
        await group.SubmitGuestAsync(second);
        var ownerSession = await group.Api.GetAsync($"reservations/{group.ReservationId}/guest-session-id", group.OwnerToken);
        GroupFixture.Require(ownerSession, 200, "get owner session id");

        var before = GroupAssertions.Participants(await group.SnapshotAsync());
        GroupAssertions.Equal(4, before.Length, "owner and three guests visible");
        GroupAssertions.True(before.Any(p => GroupFixture.Read(p, "id").GetString() == ownerSession.Text("guestSessionId")), "owner session visible");
        GroupAssertions.True(!GroupFixture.Read(GroupAssertions.Participant(before, pending), "isSubmitted").GetBoolean(), "pending guest excluded from submit");

        var submitted = await group.SubmitAsync();
        GroupFixture.Require(submitted, 200, "submit consolidated order");
        var items = submitted.Body.EnumerateArray().ToDictionary(
            i => GroupFixture.Read(i, "dishId").GetString()!,
            i => GroupFixture.Read(i, "quantity").GetInt32());
        GroupAssertions.Equal(2, items.Count, "two distinct dishes consolidated");
        GroupAssertions.Equal(4, items[group.FirstDish.Id], "owner and guests' first dish quantities");
        GroupAssertions.Equal(2, items[group.SecondDish.Id], "pending cart excluded");

        var status = await group.Api.GetAsync($"guest/groups/{group.GroupId}/status");
        GroupFixture.Require(status, 200, "read group status");
        GroupAssertions.Equal("Submitted", status.Text("status"), "group status");

        var waiter = await group.WaiterTokenAsync();
        var order = await group.Api.GetAsync($"orders/{group.ReservationId}", waiter);
        GroupFixture.Require(order, 200, "waiter reads submitted order");
        GroupAssertions.Equal(3, order.Field("status").GetInt32(), "SubmittedByCustomer order status");
        var waiterItems = order.Field("orderItems").EnumerateArray().ToDictionary(
            i => GroupFixture.Read(GroupFixture.Read(i, "dish"), "id").GetString()!,
            i => GroupFixture.Read(i, "quantity").GetInt32());
        GroupAssertions.Equal(4, waiterItems[group.FirstDish.Id], "waiter first dish quantity");
        GroupAssertions.Equal(2, waiterItems[group.SecondDish.Id], "waiter second dish quantity");
    }

    [Test]
    public async Task GroupWithoutSubmittedSessionsCannotBeSubmitted()
    {
        await using var group = await GroupFixture.CreateAsync();
        await group.CreateOrderAsync();
        var pending = await group.JoinAsync("Pending");
        await group.AddDishAsync(pending, group.FirstDish, 1);
        GroupAssertions.Status(await group.SubmitAsync(), 400, "no submitted guest session");
    }

    [Test]
    public async Task EmptyCartCannotBeSubmittedAndGroupRemainsOpen()
    {
        await using var group = await GroupFixture.CreateAsync();
        await group.CreateOrderAsync();
        var empty = await group.JoinAsync("Empty");
        GroupAssertions.Status(await group.Api.PostAsync($"guest/sessions/{empty.Id}/submit", new { }, empty.Token), 400, "empty guest cart");
        GroupAssertions.Status(await group.SubmitAsync(), 400, "group with no submitted dishes");
        var status = await group.Api.GetAsync($"guest/groups/{group.GroupId}/status");
        GroupAssertions.Equal("Open", status.Text("status"), "group remains open");
    }

    [Test]
    public async Task SubmittedGuestWithoutAnOrderCannotSubmitGroup()
    {
        await using var group = await GroupFixture.CreateAsync();
        var guest = await group.JoinAsync("Guest");
        await group.AddDishAsync(guest, group.FirstDish, 1);
        await group.SubmitGuestAsync(guest);
        GroupAssertions.Status(await group.SubmitAsync(), 400, "missing order");
    }

    [Test]
    public async Task UnknownGroupCannotBeSubmitted()
    {
        await using var group = await GroupFixture.CreateAsync();
        var response = await group.Api.PostAsync($"group-reservations/{Guid.NewGuid():N}/submit",
            new { reservationId = group.ReservationId }, group.OwnerToken);
        GroupAssertions.Status(response, 404, "unknown group");
    }

    [Test]
    public async Task UnknownReservationHasNoOwnerSession()
    {
        await using var group = await GroupFixture.CreateAsync();
        var response = await group.Api.GetAsync($"reservations/{Guid.NewGuid():N}/guest-session-id", group.OwnerToken);
        GroupAssertions.Status(response, 404, "unknown reservation");
    }

    [Test]
    public async Task UnknownReservationCannotBeUsedToSubmitAnExistingGroup()
    {
        await using var group = await GroupFixture.CreateAsync();
        var response = await group.SubmitAsync(reservationId: Guid.NewGuid().ToString("N"));
        GroupAssertions.Status(response, 404, "unknown reservation on group submit");
    }

    [Test]
    public async Task ReservationFromAnotherGroupCannotBeUsedForSubmit()
    {
        await using var group = await GroupFixture.CreateAsync();
        await using var other = await GroupFixture.CreateAsync();
        var response = await group.SubmitAsync(reservationId: other.ReservationId);
        GroupAssertions.Status(response, 400, "reservation does not belong to group");
    }

    [Test]
    public async Task SubmittedGroupCannotBeSubmittedTwice()
    {
        await using var group = await GroupFixture.CreateAsync();
        await group.CreateOrderAsync();
        var guest = await group.JoinAsync("Guest");
        await group.AddDishAsync(guest, group.FirstDish, 1);
        await group.SubmitGuestAsync(guest);
        GroupFixture.Require(await group.SubmitAsync(), 200, "first submission");
        GroupAssertions.Status(await group.SubmitAsync(), 400, "repeat submission");
    }

    [Test]
    public async Task OnlyOwnerCanRetrieveOwnerSessionAndSubmitGroup()
    {
        await using var group = await GroupFixture.CreateAsync();
        var other = await group.NewCustomerTokenAsync();
        var session = await group.Api.GetAsync($"reservations/{group.ReservationId}/guest-session-id", other);
        GroupAssertions.Status(session, 403, "other customer owner session lookup");
        GroupAssertions.Status(await group.SubmitAsync(other), 403, "other customer group submission");
    }
}
