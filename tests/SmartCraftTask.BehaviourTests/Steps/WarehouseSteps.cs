using SmartCraftTask.BehaviourTests.Support;

namespace SmartCraftTask.BehaviourTests.Steps;

/// <summary>
/// Steps both features need: getting a warehouse into a given state, and the assertions about what
/// it holds. A step text can only be bound once, so anything shared lives here rather than being
/// repeated with slightly different wording in each feature's own steps.
/// </summary>
[Binding]
public sealed class WarehouseSteps(ScenarioState state)
{
    [Given("an active warehouse {string}")]
    public void GivenAnActiveWarehouse(string code) =>
        state.Warehouse = Warehouse.Register(
            code,
            $"Depot {code}",
            new Address { Street = "Karl Johans gate 1", PostalCode = "0154", City = "Oslo", Country = "Norway" },
            capacityInPallets: 1200);

    [Given("the warehouse has been deactivated")]
    public void GivenTheWarehouseHasBeenDeactivated() => state.Warehouse.Deactivate();

    [Given("the warehouse already holds {int} of {string}")]
    public void GivenTheWarehouseAlreadyHolds(int quantity, string sku) =>
        state.Warehouse.AddItem(sku, $"Stock {sku}", quantity);

    [Then("the warehouse holds {int} stock line(s)")]
    public void ThenTheWarehouseHolds(int count) => Assert.Equal(count, state.Warehouse.Items.Count);

    [Then("the warehouse holds no stock lines")]
    public void ThenTheWarehouseHoldsNothing() => Assert.Empty(state.Warehouse.Items);

    [Then("{string} shows a quantity of {int}")]
    public void ThenTheQuantityIs(string sku, int quantity) =>
        Assert.Equal(quantity, state.ItemWith(sku).Quantity);

    [Then("the change is refused because a quantity cannot be negative")]
    public void ThenRefusedForNegativeQuantity()
    {
        // Not a DomainException: a negative quantity is something validation at the edge should
        // already have rejected, so reaching the aggregate with one is a programming error and stays
        // visible as the argument exception it is.
        var refusal = Assert.IsType<ArgumentOutOfRangeException>(state.Refusal);
        Assert.Contains("Quantity cannot be negative", refusal.Message, StringComparison.Ordinal);
    }

    [Then("the warehouse reports that it holds no such line")]
    public void ThenNoSuchLine() => Assert.False(state.LineWasFound);
}
