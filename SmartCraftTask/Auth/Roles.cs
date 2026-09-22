namespace SmartCraftTask.Auth;

/// <summary>
/// One role per capability, so a user is described by the set of roles they hold rather than by a
/// single rank. A manager holds all four; a viewer holds one.
/// </summary>
public static class Roles
{
    /// <summary>Create, change and delete warehouses.</summary>
    public const string WarehouseManager = "WarehouseManager";

    /// <summary>Read warehouses.</summary>
    public const string WarehouseReader = "WarehouseReader";

    /// <summary>Add, change and remove stock lines.</summary>
    public const string StockOperator = "StockOperator";

    /// <summary>Read stock lines.</summary>
    public const string StockReader = "StockReader";
}

/// <summary>
/// Each policy is satisfied by exactly one role. Breadth of access comes from the roles a user
/// holds, not from policies listing alternatives, which keeps both sides easy to reason about.
/// </summary>
public static class Policies
{
    public const string ManageWarehouses = "ManageWarehouses";

    public const string ReadWarehouses = "ReadWarehouses";

    public const string ManageStock = "ManageStock";

    public const string ReadStock = "ReadStock";
}
