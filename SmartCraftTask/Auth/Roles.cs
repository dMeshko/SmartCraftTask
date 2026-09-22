namespace SmartCraftTask.Auth;

/// <summary>
/// The two roles this service recognises. A manager runs the warehouses themselves; an operator
/// only moves stock within them.
/// </summary>
public static class Roles
{
    public const string WarehouseManager = "WarehouseManager";

    public const string StockOperator = "StockOperator";
}

public static class Policies
{
    /// <summary>Creating, changing and deleting warehouses. Managers only.</summary>
    public const string ManageWarehouses = "ManageWarehouses";

    /// <summary>Adding, changing and removing stock lines. Managers and operators.</summary>
    public const string ManageStock = "ManageStock";
}
