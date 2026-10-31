using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Services.GroupReservations;
using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GroupReservations;

public class SubmitGroupPreOrderHandler(IGroupPreOrderService service) : IApiRequestHandler
{
    private record SubmitRequest(string? ReservationId);

    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("groupReservationId", out var groupReservationId))
            return ResponseBuilder.CreateErrorResponse(400, "Missing groupReservationId.", context.Logger);

        if (request.RequestContext?.Authorizer?.Claims is not { } claims ||
            !claims.TryGetValue("sub", out var userId) || string.IsNullOrWhiteSpace(userId))
            return ResponseBuilder.CreateErrorResponse(401, "Unauthorized.", context.Logger);

        string? reservationId;
        try
        {
            reservationId = JsonSerializer.Deserialize<SubmitRequest>(request.Body ?? "", new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })?.ReservationId;
        }
        catch (JsonException)
        {
            return ResponseBuilder.CreateErrorResponse(400, "Invalid request body.", context.Logger);
        }
        if (string.IsNullOrWhiteSpace(reservationId))
            return ResponseBuilder.CreateErrorResponse(400, "Missing reservationId.", context.Logger);

        try
        {
            var items = await service.SubmitAsync(userId, groupReservationId, reservationId);
            return ResponseBuilder.CreateJsonResponse(200, items);
        }
        catch (GroupReservationNotFoundException ex) { return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger); }
        catch (ReservationNotFoundException ex) { return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger); }
        catch (ActionForbiddenException ex) { return ResponseBuilder.CreateErrorResponse(403, ex.Message, context.Logger); }
        catch (ValidationException ex) { return ResponseBuilder.CreateErrorResponse(400, ex.Message, context.Logger); }
    }
}
