using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Services.GroupReservations;
using System.Threading.Tasks;

namespace APIHandler.Handlers.Reservations;

public class GetOwnerGuestSessionIdHandler(IGroupPreOrderService service) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("reservationId", out var reservationId))
            return ResponseBuilder.CreateErrorResponse(400, "Missing reservationId.", context.Logger);

        if (request.RequestContext?.Authorizer?.Claims is not { } claims ||
            !claims.TryGetValue("sub", out var userId) || string.IsNullOrWhiteSpace(userId))
            return ResponseBuilder.CreateErrorResponse(401, "Unauthorized.", context.Logger);

        try
        {
            var sessionId = await service.GetOwnerSessionIdAsync(userId, reservationId);
            return ResponseBuilder.CreateJsonResponse(200, new { guestSessionId = sessionId });
        }
        catch (ReservationNotFoundException ex) { return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger); }
        catch (GroupReservationNotFoundException ex) { return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger); }
        catch (ActionForbiddenException ex) { return ResponseBuilder.CreateErrorResponse(403, ex.Message, context.Logger); }
    }
}
