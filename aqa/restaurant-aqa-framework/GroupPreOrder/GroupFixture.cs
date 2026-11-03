using System.Text.Json;
using Restaurant_Aqa_Framework.Configuration;

namespace Restaurant_Aqa_Framework.GroupPreOrder;

internal sealed record Guest(string Id, string Token, string Name);
internal sealed record Dish(string Id, string Name, decimal Price);

/// <summary>One independent booking per test. The booking is cancelled after the test;
/// generated Cognito customers have unique emails because the public API has no user deletion route.</summary>
internal sealed class GroupFixture : IAsyncDisposable
{
    private readonly GroupApi _api = new();
    private string? _reservationId;

    public GroupApi Api => _api;
    public string OwnerToken { get; private set; } = string.Empty;
    public string ReservationId => _reservationId ?? throw new InvalidOperationException("Booking was not created.");
    public string GroupId { get; private set; } = string.Empty;
    public string TableId { get; private set; } = string.Empty;
    public Dish FirstDish { get; private set; } = null!;
    public Dish SecondDish { get; private set; } = null!;

    public static async Task<GroupFixture> CreateAsync()
    {
        var fixture = new GroupFixture();
        try
        {
            fixture.OwnerToken = await fixture.CustomerTokenAsync(
                Environment.GetEnvironmentVariable("AQA_OWNER_USERNAME"),
                Environment.GetEnvironmentVariable("AQA_OWNER_PASSWORD"));
            await fixture.CreateBookingAsync();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public async Task<string> NewCustomerTokenAsync() => await CustomerTokenAsync(null, null);

    private async Task<string> CustomerTokenAsync(string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            email = $"aqa-group-{Guid.NewGuid():N}@example.com";
            password = "AqaPass9!";
            var registration = await _api.PostAsync("register", new
            {
                firstName = "Aqa", lastName = "Owner", email, password, confirmPassword = password
            });
            Require(registration, 200, "register test customer");
        }

        var login = await _api.PostAsync("signin", new { email, password });
        Require(login, 200, "sign in test customer");
        return login.Text("idToken");
    }

    private async Task CreateBookingAsync()
    {
        // Random future dates keep parallel runs independent on a shared table.
        string? lastFailure = null;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var start = DateTime.UtcNow.Date.AddDays(Random.Shared.Next(30, 395))
                .AddHours(Random.Shared.Next(11, 19));
            var query = $"available-tables?capacity=4&startTime={Uri.EscapeDataString(start.ToString("O"))}";
            var tables = await _api.GetAsync(query);
            Require(tables, 200, "find available table");

            var preferred = Environment.GetEnvironmentVariable("TESTTABLE_ID") ?? Settings.Current.TestTableId;
            foreach (var table in tables.Body.EnumerateArray())
            {
                var id = Read(table, "id").GetString()!;
                if (!string.IsNullOrEmpty(preferred) && id != preferred) continue;
                var locationId = Read(table, "locationId").GetString()!;
                var menu = await _api.GetAsync($"locations/{Uri.EscapeDataString(locationId)}/dishes");
                Require(menu, 200, "read table location menu");
                var dishes = menu.Body.EnumerateArray()
                    .Where(d => Read(d, "state").ToString().Equals("Available", StringComparison.OrdinalIgnoreCase))
                    .Select(d => new Dish(
                        Read(d, "id").GetString()!,
                        Read(d, "name").GetString()!,
                        Read(d, "price").GetDecimal()))
                    .Take(2).ToArray();
                if (dishes.Length < 2) continue;

                var booking = await _api.PostAsync("reservations", new
                {
                    tableId = id, startTime = start, endTime = start.AddMinutes(90), guestCount = 4
                }, OwnerToken);
                if (booking.Status != 201)
                {
                    lastFailure = $"table {id}: HTTP {booking.Status} {booking.Raw}";
                    if (!string.IsNullOrEmpty(preferred) && !booking.Raw.Contains("overlap", StringComparison.OrdinalIgnoreCase))
                        Require(booking, 201, "create group booking");
                    continue;
                }
                _reservationId = booking.Text("id");
                TableId = id;
                FirstDish = dishes[0];
                SecondDish = dishes[1];

                var invite = await _api.GetAsync($"reservations/{ReservationId}/invite-token", OwnerToken);
                Require(invite, 200, "read invitation token");
                var details = await _api.GetAsync($"guest/invites/{Uri.EscapeDataString(invite.Text("token"))}");
                Require(details, 200, "resolve invitation");
                GroupId = details.Text("groupReservationId");
                return;
            }
        }

        throw new InvalidOperationException($"No suitable table with two available dishes was found. Set TESTTABLE_ID and ensure its location has a waiter and menu. Last failure: {lastFailure}");
    }

    public async Task<Guest> JoinAsync(string name)
    {
        var response = await _api.PostAsync("guest/sessions", new { groupReservationId = GroupId, guestName = name });
        Require(response, 200, "join group");
        return new Guest(response.Text("sessionId"), response.Text("sessionToken"), name);
    }

    public async Task AddDishAsync(Guest guest, Dish dish, int quantity)
    {
        var response = await _api.PostAsync($"guest/sessions/{guest.Id}/dishes", new { dishId = dish.Id, quantity }, guest.Token);
        Require(response, 200, "add guest dish");
    }

    public async Task SubmitGuestAsync(Guest guest)
    {
        var response = await _api.PostAsync($"guest/sessions/{guest.Id}/submit", new { }, guest.Token);
        Require(response, 200, "submit guest cart");
    }

    public async Task CreateOrderAsync()
    {
        var response = await _api.PostAsync("orders", new
        {
            reservationId = ReservationId,
            orderItems = new[] { new { dishId = FirstDish.Id, quantity = 1 } }
        }, OwnerToken);
        Require(response, 201, "create booking order");
    }

    public async Task<string> WaiterTokenAsync()
    {
        var (username, password) = Settings.Current.RequireWaiter();
        var login = await _api.PostAsync("signin", new { email = username, password });
        Require(login, 200, "sign in waiter");
        return login.Text("idToken");
    }

    public Task<ApiReply> SnapshotAsync(string? token = null) => _api.GetAsync($"guest/groups/{GroupId}/sessions", token ?? OwnerToken);
    public Task<ApiReply> SubmitAsync(string? token = null, string? reservationId = null) =>
        _api.PostAsync($"group-reservations/{GroupId}/submit", new { reservationId = reservationId ?? ReservationId }, token ?? OwnerToken);
    public Task<ApiReply> RemoveAsync(string guestId, string? token = null) =>
        _api.DeleteAsync($"guest/groups/{GroupId}/sessions/{guestId}", token: token ?? OwnerToken);

    public async ValueTask DisposeAsync()
    {
        if (_reservationId is not null)
        {
            try
            {
                var result = await _api.DeleteAsync("reservations", new { reservationId = _reservationId }, OwnerToken);
                if (result.Status != 200)
                    Console.Error.WriteLine($"[AQA cleanup] reservation {_reservationId}: {result.Status} {result.Raw}");
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"[AQA cleanup] reservation {_reservationId}: {error.Message}");
            }
        }
        _api.Dispose();
    }

    public static void Require(ApiReply reply, int status, string action)
    {
        if (reply.Status != status)
            throw new InvalidOperationException($"Cannot {action}: expected HTTP {status}, got {reply.Status}: {reply.Raw}");
    }

    public static JsonElement Read(JsonElement obj, string name)
    {
        foreach (var property in obj.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        throw new InvalidOperationException($"Missing '{name}' in {obj}");
    }
}
