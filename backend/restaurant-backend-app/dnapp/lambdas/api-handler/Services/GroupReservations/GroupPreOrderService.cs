using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace APIHandler.Services.GroupReservations;

public class GroupPreOrderService(
    IGroupReservationRepository groupRepository,
    IGuestSessionRepository sessionRepository,
    IReservationRepository reservationRepository,
    IOrderRepository orderRepository) : IGroupPreOrderService
{
    public async Task<string> GetOwnerSessionIdAsync(string userId, string reservationId)
    {
        var reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException("Reservation not found.");
        EnsureOwner(reservation, userId);

        var group = await groupRepository.GetByReservationIdAsync(reservationId)
            ?? throw new GroupReservationNotFoundException();

        var stableOwnerId = $"owner-{group.GroupReservationId}";
        var savedOwner = await sessionRepository.GetByGuestSessionId(stableOwnerId);
        var ownerSession = savedOwner is { IsOwnerSession: true } && savedOwner.GroupReservationId == group.GroupReservationId && savedOwner.OwnerUserId == userId
            ? savedOwner
            : (await sessionRepository.GetAllByGroupReservationIdAsync(group.GroupReservationId))
                .FirstOrDefault(s => s.IsOwnerSession && s.OwnerUserId == userId);

        var isNew = ownerSession is null;
        ownerSession ??= new GuestSession
        {
            Id = stableOwnerId,
            GroupReservationId = group.GroupReservationId,
            GuestName = "Owner",
            Dishes = [],
            IsOwnerSession = true,
            OwnerUserId = userId
        };

        var changed = isNew;
        if (group.Status == GroupReservationStatus.Open)
        {
            var order = await orderRepository.GetOrderByReservationIdAsync(reservationId);
            var orderItems = order?.Dishes;
            if (orderItems is { Count: > 0 })
            {
                var currentItems = ownerSession.Dishes ?? [];
                if (!ownerSession.IsSubmitted || currentItems.Count != orderItems.Count ||
                    !currentItems.Zip(orderItems).All(pair => pair.First.DishId == pair.Second.DishId && pair.First.Quantity == pair.Second.Quantity))
                {
                    ownerSession.Dishes = orderItems.Select(item => new OrderItem
                    {
                        DishId = item.DishId,
                        Quantity = item.Quantity,
                        State = item.State
                    }).ToList();
                    ownerSession.IsSubmitted = true;
                    changed = true;
                }
            }
        }

        if (changed)
            await sessionRepository.SaveAsync(ownerSession);
        return ownerSession.Id;
    }

    public async Task<List<ConsolidatedDishResponse>> SubmitAsync(string userId, string groupReservationId, string reservationId)
    {
        var group = await groupRepository.GetGroupReservationByIdAsync(groupReservationId)
            ?? throw new GroupReservationNotFoundException();
        if (!string.Equals(group.ReservationId, reservationId, StringComparison.Ordinal))
            throw new ValidationException("Reservation does not belong to this group reservation.");

        var reservation = await reservationRepository.GetReservationByIdAsync(reservationId)
            ?? throw new ReservationNotFoundException("Reservation not found.");
        EnsureOwner(reservation, userId);
        if (group.Status != GroupReservationStatus.Open)
            throw new ValidationException("Group reservation is not open for submission.");

        var sessions = await sessionRepository.GetAllByGroupReservationIdAsync(groupReservationId);
        var submitted = sessions.Where(s => s.IsSubmitted).ToArray();
        if (submitted.Length == 0)
            throw new ValidationException("No submitted guest sessions found.");

        var items = submitted.SelectMany(s => s.Dishes ?? []).ToArray();
        if (items.Length == 0)
            throw new ValidationException("Submitted guest sessions have no dishes.");

        var order = await orderRepository.GetOrderByReservationIdAsync(reservationId)
            ?? throw new ValidationException("Order not found.");

        var consolidated = items
            .GroupBy(i => i.DishId)
            .Select(g => new ConsolidatedDishResponse(g.Key, g.Sum(i => i.Quantity)))
            .ToList();

        order.Dishes = consolidated.Select(i => new OrderItem
        {
            DishId = i.DishId,
            Quantity = i.Quantity,
            State = OrderItemState.Pending
        }).ToList();
        order.Status = OrderStatus.SubmittedByCustomer;
        await orderRepository.SaveOrderAsync(order);

        group.Status = GroupReservationStatus.Submitted;
        await groupRepository.SaveGroupReservationAsync(group);
        return consolidated;
    }

    private static void EnsureOwner(Reservation reservation, string userId)
    {
        if (!string.Equals(reservation.CustomerId, userId, StringComparison.Ordinal))
            throw new ActionForbiddenException("Only the reservation owner can perform this action.");
    }
}
