using TUnit.Core;

namespace Restaurant_Aqa_Framework.GroupPreOrder;

[Category("group-preorder")]
[Category("group-l3")]
public sealed class GroupLevel3Tests
{
    [Test]
    public async Task RemovingSubmittedGuestUpdatesSnapshotAndWaiterOrderExcludesTheirDishes()
    {
        await using var group = await GroupFixture.CreateAsync();
        await group.CreateOrderAsync();
        var removed = await group.JoinAsync("Removed");
        var kept = await group.JoinAsync("Kept");
        await group.AddDishAsync(removed, group.FirstDish, 3);
        await group.AddDishAsync(kept, group.SecondDish, 2);
        await group.SubmitGuestAsync(removed);
        await group.SubmitGuestAsync(kept);

        var before = GroupAssertions.Participants(await group.SnapshotAsync());
        GroupAssertions.Equal(2, before.Length, "participants before removal");
        GroupAssertions.Equal(group.FirstDish.Price * 3 + group.SecondDish.Price * 2,
            before.Sum(p => GroupFixture.Read(p, "total").GetDecimal()), "total before removal");

        var deletion = await group.RemoveAsync(removed.Id);
        var after = GroupAssertions.Participants(deletion);
        GroupAssertions.Equal(1, after.Length, "one participant remains");
        GroupAssertions.Equal(kept.Id, GroupFixture.Read(after.Single(), "id").GetString(), "correct participant remains");
        GroupAssertions.Equal(group.SecondDish.Price * 2, deletion.Field("total").GetDecimal(), "new group total");
        GroupAssertions.Equal(1, GroupAssertions.Participants(await group.SnapshotAsync()).Length, "subsequent snapshot is updated");

        var submitted = await group.SubmitAsync();
        GroupFixture.Require(submitted, 200, "submit without removed guest");
        var submittedItem = submitted.Body.EnumerateArray().Single();
        GroupAssertions.Equal(group.SecondDish.Id, GroupFixture.Read(submittedItem, "dishId").GetString(), "only kept guest dish submitted");
        GroupAssertions.Equal(2, GroupFixture.Read(submittedItem, "quantity").GetInt32(), "kept guest quantity");

        var waiterOrder = await group.Api.GetAsync($"orders/{group.ReservationId}", await group.WaiterTokenAsync());
        GroupFixture.Require(waiterOrder, 200, "waiter reads order after removal");
        var orderItem = waiterOrder.Field("orderItems").EnumerateArray().Single();
        GroupAssertions.Equal(group.SecondDish.Id, GroupFixture.Read(GroupFixture.Read(orderItem, "dish"), "id").GetString(), "removed guest dish absent from waiter order");
    }

    [Test]
    public async Task RemovingLastGuestLeavesEmptyGroup()
    {
        await using var group = await GroupFixture.CreateAsync();
        var only = await group.JoinAsync("OnlyGuest");
        await group.AddDishAsync(only, group.FirstDish, 1);
        var result = await group.RemoveAsync(only.Id);
        GroupAssertions.Equal(0, GroupAssertions.Participants(result).Length, "last guest removed");
        GroupAssertions.Equal(0m, result.Field("total").GetDecimal(), "empty group total");
    }

    [Test]
    public async Task GuestFromAnotherGroupCannotBeRemoved()
    {
        await using var first = await GroupFixture.CreateAsync();
        await using var second = await GroupFixture.CreateAsync();
        var otherGuest = await second.JoinAsync("OtherGroupGuest");
        GroupAssertions.Status(await first.RemoveAsync(otherGuest.Id), 400, "cross-group deletion");
        GroupAssertions.Equal(1, GroupAssertions.Participants(await second.SnapshotAsync()).Length, "other group remains intact");
    }

    [Test]
    public async Task UnknownGuestCannotBeRemoved()
    {
        await using var group = await GroupFixture.CreateAsync();
        GroupAssertions.Status(await group.RemoveAsync(Guid.NewGuid().ToString("N")), 404, "unknown guest");
    }

    [Test]
    public async Task NonOwnerCannotRemoveGuest()
    {
        await using var group = await GroupFixture.CreateAsync();
        var guest = await group.JoinAsync("Protected");
        var other = await group.NewCustomerTokenAsync();
        GroupAssertions.Status(await group.RemoveAsync(guest.Id, other), 403, "non-owner deletion");
        GroupAssertions.Equal(1, GroupAssertions.Participants(await group.SnapshotAsync()).Length, "guest not removed");
    }

    [Test]
    public async Task OwnerSessionCannotBeRemoved()
    {
        await using var group = await GroupFixture.CreateAsync();
        var ownerSession = await group.Api.GetAsync($"reservations/{group.ReservationId}/guest-session-id", group.OwnerToken);
        GroupFixture.Require(ownerSession, 200, "get owner session");
        GroupAssertions.Status(await group.RemoveAsync(ownerSession.Text("guestSessionId")), 400, "owner session deletion");
    }
}
