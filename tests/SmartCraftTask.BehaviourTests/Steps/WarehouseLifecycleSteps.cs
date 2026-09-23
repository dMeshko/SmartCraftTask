using SmartCraftTask.BehaviourTests.Support;

namespace SmartCraftTask.BehaviourTests.Steps;

/// <summary>
/// Steps particular to <c>WarehouseLifecycle.feature</c>: the warehouse's own details and its
/// availability, as opposed to the stock it holds.
/// </summary>
[Binding]
public sealed class WarehouseLifecycleSteps(ScenarioState state)
{
    [When("the warehouse is renamed to {string}")]
    public void WhenRenamed(string name) => state.Attempt(() => state.Warehouse.Rename(name));

    [When("the warehouse is relocated to {string}")]
    public void WhenRelocated(string city) =>
        state.Attempt(() => state.Warehouse.Relocate(new Address
        {
            Street = "Bryggen 12",
            PostalCode = "5003",
            City = city,
            Country = "Norway"
        }));

    [When("the capacity is changed to {int}")]
    public void WhenResized(int capacity) => state.Attempt(() => state.Warehouse.Resize(capacity));

    [When("the warehouse is deactivated")]
    public void WhenDeactivated() => state.Attempt(() => state.Warehouse.Deactivate());

    [When("the warehouse is activated")]
    public void WhenActivated() => state.Attempt(() => state.Warehouse.Activate());

    [Then("the warehouse is active")]
    public void ThenActive() => Assert.True(state.Warehouse.IsActive);

    [Then("the warehouse is not active")]
    public void ThenNotActive() => Assert.False(state.Warehouse.IsActive);

    [Then("the warehouse is named {string}")]
    public void ThenNamed(string name) => Assert.Equal(name, state.Warehouse.Name);

    [Then("the warehouse is in {string}")]
    public void ThenLocatedIn(string city) => Assert.Equal(city, state.Warehouse.Address.City);

    [Then("the capacity is {int}")]
    public void ThenCapacityIs(int capacity) => Assert.Equal(capacity, state.Warehouse.CapacityInPallets);

    [Then("the warehouse still trades as {string}")]
    public void ThenCodeIs(string code) => Assert.Equal(code, state.Warehouse.Code);

    [Then("the warehouse has never been changed")]
    public void ThenNeverChanged() => Assert.Null(state.Warehouse.UpdatedAt);

    [Then("the warehouse records that it has changed")]
    public void ThenRecordsTheChange() => Assert.NotNull(state.Warehouse.UpdatedAt);

    [Then("the change is refused because a capacity cannot be negative")]
    public void ThenRefusedForNegativeCapacity()
    {
        var refusal = Assert.IsType<ArgumentOutOfRangeException>(state.Refusal);
        Assert.Contains("Capacity cannot be negative", refusal.Message, StringComparison.Ordinal);
    }
}
