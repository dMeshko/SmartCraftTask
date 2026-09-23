namespace SmartCraftTask.BehaviourTests.Support;

/// <summary>
/// The warehouse a scenario is about, and what happened when it was last asked to do something.
/// </summary>
/// <remarks>
/// Reqnroll builds one of these per scenario and injects it into every binding class that asks for
/// it, which is what lets the step definitions be split by feature while sharing a single subject.
/// The alternative — fields on one binding class — collapses the moment two features need the same
/// Given, because a step text may only be bound once.
/// </remarks>
public sealed class ScenarioState
{
    private Warehouse? _warehouse;

    public Warehouse Warehouse
    {
        get => _warehouse ?? throw new InvalidOperationException(
            "No warehouse in this scenario. A Given step has to register one first.");
        set => _warehouse = value;
    }

    /// <summary>What the aggregate threw, if it did. Scenarios expecting a refusal assert on this.</summary>
    public Exception? Refusal { get; set; }

    /// <summary>False when the aggregate was asked about a stock line it does not hold.</summary>
    public bool? LineWasFound { get; set; }

    /// <summary>Runs the action, keeping any exception for a Then step rather than failing here.</summary>
    public void Attempt(Action action)
    {
        try
        {
            action();
            Refusal = null;
        }
        catch (Exception exception)
        {
            Refusal = exception;
        }
    }

    public Item ItemWith(string sku) =>
        Warehouse.Items.Single(item => string.Equals(item.Sku, sku, StringComparison.OrdinalIgnoreCase));
}
