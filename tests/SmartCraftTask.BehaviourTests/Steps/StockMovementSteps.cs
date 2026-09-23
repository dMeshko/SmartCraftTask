using SmartCraftTask.BehaviourTests.Support;

namespace SmartCraftTask.BehaviourTests.Steps;

/// <summary>
/// Steps particular to <c>StockMovement.feature</c>. They drive the aggregate directly: no host, no
/// database, no HTTP. The rules being described belong to <see cref="Warehouse"/>, so describing
/// them anywhere further out would test the plumbing around them instead.
/// </summary>
[Binding]
public sealed class StockMovementSteps(ScenarioState state)
{
    [When("{int} of {string} are received")]
    public void WhenStockIsReceived(int quantity, string sku) =>
        state.Attempt(() => state.Warehouse.AddItem(sku, $"Stock {sku}", quantity));

    [When("the quantity of {string} is changed to {int}")]
    public void WhenTheQuantityIsChanged(string sku, int quantity)
    {
        var item = state.ItemWith(sku);

        state.Attempt(() => state.LineWasFound = state.Warehouse.TryUpdateItem(item.Id, item.Name, quantity));
    }

    [When("the line for {string} is removed")]
    public void WhenTheLineIsRemoved(string sku) =>
        state.LineWasFound = state.Warehouse.TryRemoveItem(state.ItemWith(sku).Id);

    [When("the quantity of a line held by no warehouse is changed to {int}")]
    public void WhenAnUnknownLineIsChanged(int quantity) =>
        state.LineWasFound = state.Warehouse.TryUpdateItem(Guid.NewGuid(), "Nothing", quantity);

    [When("a line held by no warehouse is removed")]
    public void WhenAnUnknownLineIsRemoved() =>
        state.LineWasFound = state.Warehouse.TryRemoveItem(Guid.NewGuid());

    [Then("the stock is refused because the SKU is already held")]
    public void ThenRefusedForDuplicateSku() => AssertDomainRefusal("Duplicate SKU");

    [Then("the stock is refused because the warehouse is not active")]
    public void ThenRefusedForInactiveWarehouse() => AssertDomainRefusal("Warehouse is not active");

    private void AssertDomainRefusal(string expectedTitle)
    {
        var refusal = Assert.IsType<DomainException>(state.Refusal);
        Assert.Equal(expectedTitle, refusal.Title);
    }
}
