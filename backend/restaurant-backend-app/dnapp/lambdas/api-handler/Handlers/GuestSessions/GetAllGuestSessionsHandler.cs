using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Services.GuestSessions;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GuestSessions;

public class GetAllGuestSessionsHandler(IGuestSessionService service) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("groupReservationId", out var groupReservationId))
            return ResponseBuilder.CreateErrorResponse(400, "Missing groupReservationId.", context.Logger);

        if (request.RequestContext?.Authorizer?.Claims is not { } claims ||
            !claims.TryGetValue("sub", out var userId) || string.IsNullOrWhiteSpace(userId))
            return ResponseBuilder.CreateErrorResponse(401, "Unauthorized.", context.Logger);

        try
        {
            var snapshot = await service.GetAllSessionsAsync(userId, groupReservationId);
            return ResponseBuilder.CreateJsonResponse(200, snapshot);
        }
        catch (GroupReservationNotFoundException ex) { return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger); }
        catch (ReservationNotFoundException ex) { return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger); }
        catch (ActionForbiddenException ex) { return ResponseBuilder.CreateErrorResponse(403, ex.Message, context.Logger); }
        catch (ValidationException ex) { return ResponseBuilder.CreateErrorResponse(400, ex.Message, context.Logger); }
    }
}
