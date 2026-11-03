using System.Text.Json;

namespace Restaurant_Aqa_Framework.GroupPreOrder;

internal static class GroupAssertions
{
    public static JsonElement[] Participants(ApiReply response)
    {
        GroupFixture.Require(response, 200, "get group snapshot");
        var participants = response.Field("participants").EnumerateArray().ToArray();
        var sum = participants.Sum(p => GroupFixture.Read(p, "total").GetDecimal());
        Equal(sum, response.Field("total").GetDecimal(), "group total must equal participant totals");
        foreach (var participant in participants)
        {
            var items = GroupFixture.Read(participant, "items").EnumerateArray();
            var itemSum = items.Sum(i =>
                GroupFixture.Read(i, "quantity").GetInt32() *
                GroupFixture.Read(GroupFixture.Read(i, "dish"), "price").GetDecimal());
            Equal(itemSum, GroupFixture.Read(participant, "total").GetDecimal(), "participant total must equal item totals");
        }
        return participants;
    }

    public static JsonElement Participant(JsonElement[] participants, Guest guest) =>
        participants.Single(p => GroupFixture.Read(p, "id").GetString() == guest.Id);

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    public static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Status(ApiReply response, int expected, string message) =>
        Equal(expected, response.Status, $"{message}; body: {response.Raw}");

    public static void Denied(ApiReply response, string message) =>
        True(response.Status is 401 or 403, $"{message}: expected 401 or 403, got {response.Status}: {response.Raw}");
}
