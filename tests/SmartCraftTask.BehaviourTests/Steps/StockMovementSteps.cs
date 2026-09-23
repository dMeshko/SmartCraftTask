namespace SmartCraftTask.BehaviourTests.Steps;

/// <summary>
/// Steps for <c>StockMovement.feature</c>. They drive the aggregate directly: no host, no database,
/// no HTTP. The rules being described belong to <see cref="Warehouse"/>, so describing them anywhere
/// further out would test the plumbing around them instead.
/// </summary>
/// <remarks>
/// Reqnroll builds a fresh instance of this class per scenario, so the fields below are scenario
/// state and need no resetting between them.
/// </remarks>
[Binding]
public sealed class StockMovementSteps
{
    private Warehouse _warehouse = null!;

    /// <summary>What the aggregate threw, if it did. A scenario that expects a refusal asserts on it.</summary>
    private Exception? _refusal;

    /// <summary>False when the aggregate was asked to change or remove a line it does not hold.</summary>
    private bool? _lineWasFound;

    [Given("an active warehouse {string}")]
    public void GivenAnActiveWarehouse(string code) =>
        _warehouse = Warehouse.Register(
            code,
            $"Depot {code}",
            new Address { Street = "Karl Johans gate 1", PostalCode = "0154", City = "Oslo", Country = "Norway" },
            capacityInPallets: 1200);

    [Given("the warehouse has been deactivated")]
    public void GivenTheWarehouseHasBeenDeactivated() => _warehouse.Deactivate();

    [Given("the warehouse already holds {int} of {string}")]
    public void GivenTheWarehouseAlreadyHolds(int quantity, string sku) =>
        _warehouse.AddItem(sku, $"Stock {sku}", quantity);

    [When("{int} of {string} are received")]
    public void WhenStockIsReceived(int quantity, string sku) =>
        _refusal = Record(() => _warehouse.AddItem(sku, $"Stock {sku}", quantity));

    [When("the quantity of {string} is changed to {int}")]
    public void WhenTheQuantityIsChanged(string sku, int quantity)
    {
        var item = ItemWith(sku);

        _refusal = Record(() => _lineWasFound = _warehouse.TryUpdateItem(item.Id, item.Name, quantity));
    }

    [When("the line for {string} is removed")]
    public void WhenTheLineIsRemoved(string sku) => _lineWasFound = _warehouse.TryRemoveItem(ItemWith(sku).Id);

    [When("the quantity of a line held by no warehouse is changed to {int}")]
    public void WhenAnUnknownLineIsChanged(int quantity) =>
        _lineWasFound = _warehouse.TryUpdateItem(Guid.NewGuid(), "Nothing", quantity);

    [When("a line held by no warehouse is removed")]
    public void WhenAnUnknownLineIsRemoved() => _lineWasFound = _warehouse.TryRemoveItem(Guid.NewGuid());

    [Then("the warehouse holds {int} stock line(s)")]
    public void ThenTheWarehouseHolds(int count) => Assert.Equal(count, _warehouse.Items.Count);

    [Then("the warehouse holds no stock lines")]
    public void ThenTheWarehouseHoldsNothing() => Assert.Empty(_warehouse.Items);

    [Then("{string} shows a quantity of {int}")]
    public void ThenTheQuantityIs(string sku, int quantity) => Assert.Equal(quantity, ItemWith(sku).Quantity);

    [Then("the stock is refused because the SKU is already held")]
    public void ThenRefusedForDuplicateSku() => AssertDomainRefusal("Duplicate SKU");

    [Then("the stock is refused because the warehouse is not active")]
    public void ThenRefusedForInactiveWarehouse() => AssertDomainRefusal("Warehouse is not active");

    [Then("the change is refused because a quantity cannot be negative")]
    public void ThenRefusedForNegativeQuantity()
    {
        // Not a DomainException: a negative quantity is something validation at the edge should
        // already have rejected, so reaching the aggregate with one is a programming error and stays
        // visible as the argument exception it is.
        var refusal = Assert.IsType<ArgumentOutOfRangeException>(_refusal);
        Assert.Contains("Quantity cannot be negative", refusal.Message, StringComparison.Ordinal);
    }

    [Then("the warehouse reports that it holds no such line")]
    public void ThenNoSuchLine() => Assert.False(_lineWasFound);

    private void AssertDomainRefusal(string expectedTitle)
    {
        var refusal = Assert.IsType<DomainException>(_refusal);
        Assert.Equal(expectedTitle, refusal.Title);
    }

    private Item ItemWith(string sku) =>
        Assert.Single(_warehouse.Items, item => string.Equals(item.Sku, sku, StringComparison.OrdinalIgnoreCase));

    /// <summary>Runs the action, keeping any exception for a Then step to assert on.</summary>
    private static Exception? Record(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }
}
