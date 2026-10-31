using System.Collections.Generic;
using System.Threading.Tasks;

namespace APIHandler.Services.GroupReservations;

public record ConsolidatedDishResponse(string DishId, int Quantity);

public interface IGroupPreOrderService
{
    Task<string> GetOwnerSessionIdAsync(string userId, string reservationId);
    Task<List<ConsolidatedDishResponse>> SubmitAsync(string userId, string groupReservationId, string reservationId);
}
