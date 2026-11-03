using TUnit.Core;

namespace Restaurant_Aqa_Framework.GroupPreOrder;

[Category("group-preorder")]
[Category("group-l1")]
public sealed class GroupLevel1Tests
{
    [Test]
    public async Task OwnerSeesGuestsDishesQuantitiesTotalsAndPendingState()
    {
        await using var group = await GroupFixture.CreateAsync();
        var andrii = await group.JoinAsync("Andrii");
        var olena = await group.JoinAsync("Olena");
        await group.AddDishAsync(andrii, group.FirstDish, 2);
        await group.AddDishAsync(olena, group.SecondDish, 1);
        await group.SubmitGuestAsync(andrii);

        var people = GroupAssertions.Participants(await group.SnapshotAsync());
        GroupAssertions.Equal(2, people.Length, "two guests joined");
        var first = GroupAssertions.Participant(people, andrii);
        var second = GroupAssertions.Participant(people, olena);
        GroupAssertions.Equal("Andrii", GroupFixture.Read(first, "name").GetString(), "first guest name");
        GroupAssertions.Equal("Guest", GroupFixture.Read(first, "type").GetString(), "participant type");
        GroupAssertions.True(GroupFixture.Read(first, "isSubmitted").GetBoolean(), "Andrii submitted");
        GroupAssertions.True(!GroupFixture.Read(second, "isSubmitted").GetBoolean(), "Olena is pending");
        var item = GroupFixture.Read(first, "items").EnumerateArray().Single();
        GroupAssertions.Equal(group.FirstDish.Id, GroupFixture.Read(GroupFixture.Read(item, "dish"), "id").GetString(), "first dish");
        GroupAssertions.Equal(2, GroupFixture.Read(item, "quantity").GetInt32(), "first quantity");
        GroupAssertions.Equal(group.FirstDish.Price * 2, GroupFixture.Read(first, "total").GetDecimal(), "first total");
        GroupAssertions.Equal(group.SecondDish.Price, GroupFixture.Read(second, "total").GetDecimal(), "second total");
    }

    [Test]
    public async Task TotalChangesWhenGuestChangesCartAndEmptyGuestRemainsVisible()
    {
        await using var group = await GroupFixture.CreateAsync();
        var guest = await group.JoinAsync("CartOwner");
        var empty = await group.JoinAsync("NoDishes");
        var before = GroupAssertions.Participants(await group.SnapshotAsync());
        GroupAssertions.Equal(0m, GroupFixture.Read(GroupAssertions.Participant(before, empty), "total").GetDecimal(), "empty guest total");
        GroupAssertions.Equal(0, GroupFixture.Read(GroupAssertions.Participant(before, empty), "items").GetArrayLength(), "empty guest cart");

        await group.AddDishAsync(guest, group.FirstDish, 1);
        var afterOne = await group.SnapshotAsync();
        GroupAssertions.Equal(group.FirstDish.Price, afterOne.Field("total").GetDecimal(), "total after first item");
        await group.AddDishAsync(guest, group.FirstDish, 2);
        var afterThree = await group.SnapshotAsync();
        GroupAssertions.Equal(group.FirstDish.Price * 3, afterThree.Field("total").GetDecimal(), "total after cart edit");
        GroupAssertions.Equal(2, GroupAssertions.Participants(afterThree).Length, "empty guest stays in group");
    }

    [Test]
    public async Task NewGroupHasNoParticipantsAndZeroTotal()
    {
        await using var group = await GroupFixture.CreateAsync();
        var response = await group.SnapshotAsync();
        GroupAssertions.Equal(0, GroupAssertions.Participants(response).Length, "new group participants");
        GroupAssertions.Equal(0m, response.Field("total").GetDecimal(), "new group total");
    }

    [Test]
    public async Task AnotherCustomerCannotReadTheOwnersGroup()
    {
        await using var group = await GroupFixture.CreateAsync();
        var other = await group.NewCustomerTokenAsync();
        GroupAssertions.Status(await group.SnapshotAsync(other), 403, "another customer must be forbidden");
    }

    [Test]
    public async Task AnonymousAndGuestTokensCannotReadTheOwnersGroup()
    {
        await using var group = await GroupFixture.CreateAsync();
        var guest = await group.JoinAsync("Guest");
        var path = $"guest/groups/{group.GroupId}/sessions";
        GroupAssertions.Denied(await group.Api.GetAsync(path), "anonymous must be denied");
        GroupAssertions.Denied(await group.Api.GetAsync(path, guest.Token), "guest must be denied");
    }

    [Test]
    public async Task WaiterCannotReadTheOwnersGroup()
    {
        await using var group = await GroupFixture.CreateAsync();
        GroupAssertions.Status(await group.SnapshotAsync(await group.WaiterTokenAsync()), 403, "waiter must be forbidden");
    }

    [Test]
    public async Task UnknownGroupReturnsNotFound()
    {
        await using var group = await GroupFixture.CreateAsync();
        var missing = await group.Api.GetAsync($"guest/groups/{Guid.NewGuid():N}/sessions", group.OwnerToken);
        GroupAssertions.Status(missing, 404, "unknown group");
    }
}
