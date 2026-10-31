using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace APIHandler.Models.GuestSessions;

public record GetAllSessionsResponse(IEnumerable<Participant> Participants, decimal Total);

public record Participant(
    string? Id,
    [property: JsonConverter(typeof(JsonStringEnumConverter<ParticipantType>))] ParticipantType Type,
    string Name,
    IEnumerable<SessionItemResponse> Items,
    decimal Total,
    bool IsSubmitted);

public record SessionItemResponse(SessionDishResponse Dish, int Quantity);

public record SessionDishResponse(string Id, string Name, decimal Price);

public enum ParticipantType
{
    Owner,
    Guest
}
