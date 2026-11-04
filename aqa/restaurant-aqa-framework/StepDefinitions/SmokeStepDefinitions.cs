using Microsoft.Playwright;
using Restaurant_Aqa_Framework.Screenplay;
using Restaurant_Aqa_Framework.Screenplay.Questions;
using Restaurant_Aqa_Framework.Screenplay.Tasks;

namespace Restaurant_Aqa_Framework.StepDefinitions;

[Binding]
public sealed class SmokeStepDefinitions(Actor actor) : StepDefinitionBase(actor)
{
    [When("a guest asks for the {information}")]
    public Task WhenGuestAsksFor(string information) => Actor.AttemptsToAsync(new AskForInformation(information));

    [Then("every dish has a name, a price and a type")]
    public async Task ThenEveryDishIsComplete()
    {
        var dishes = await Actor.AsksForAsync(new TheDishesShown());
        var incomplete = dishes
            .Where(d => string.IsNullOrWhiteSpace(d.Name) || d.Price <= 0 || string.IsNullOrWhiteSpace(d.DishType))
            .Select(d => d.Id)
            .ToList();

        await Assert.That(incomplete).IsEmpty();
    }

    [When("a guest opens the home page")]
    public Task WhenGuestOpensHomePage() => Actor.AttemptsToAsync(new OpenTheHomePage());

    [Then("the home page is shown")]
    public async Task ThenHomePageIsShown() =>
        await Assert.That(await Actor.AsksForAsync(new TheHomePageLoaded())).IsTrue();

    [Then("dish and location cards have loaded images")]
    public async Task ThenCatalogImagesAreLoaded()
    {
        var page = Actor.LookingAtThePage();
        foreach (var selector in new[] { ".dish-cards .dish-card", ".location-cards app-location-card" })
        {
            var cards = page.Locator(selector);
            await cards.First.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 15000 });
            await Assert.That(await cards.CountAsync() > 0).IsTrue();
            var image = cards.First.Locator("img").First;
            await image.WaitForAsync(new() { State = WaitForSelectorState.Visible });
            await image.EvaluateAsync("img => img.decode()");
            await Assert.That(await image.EvaluateAsync<bool>("img => img.naturalWidth > 0")).IsTrue();
        }
    }

    [When("the guest chooses to book a table")]
    public Task WhenGuestChoosesBooking() =>
        Actor.LookingAtThePage().GetByRole(AriaRole.Link, new() { Name = "Book a table", Exact = true }).ClickAsync();

    [Then("the table search form is shown")]
    public async Task ThenBookingSearchIsShown()
    {
        var page = Actor.LookingAtThePage();
        await page.WaitForURLAsync("**/tables");
        await page.GetByRole(AriaRole.Button, new() { Name = "Find a Table" })
            .WaitForAsync(new() { State = WaitForSelectorState.Visible });
    }
}
