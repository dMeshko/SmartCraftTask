namespace SmartCraftTask.Auth;

/// <summary>
/// Stands in for a real identity provider. Fixed accounts, passwords compared in plain text,
/// which is enough to demonstrate the authorisation wiring and nothing more. A real service would
/// delegate to an identity provider, or at minimum hash and salt against a users table.
/// </summary>
public sealed class DevUserStore
{
    private static readonly Dictionary<string, (string Password, string[] Roles)> Users =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Runs the warehouses and everything in them.
            ["manager"] = ("manager-secret",
            [
                Roles.WarehouseManager,
                Roles.WarehouseReader,
                Roles.StockOperator,
                Roles.StockReader
            ]),

            // Moves stock, and can see the warehouses it sits in, but cannot run them.
            ["operator"] = ("operator-secret",
            [
                Roles.WarehouseReader,
                Roles.StockOperator,
                Roles.StockReader
            ]),

            // Read-only, warehouses only: stock endpoints are closed to them.
            ["warehouse-viewer"] = ("warehouse-viewer-secret", [Roles.WarehouseReader]),

            // Read-only, stock only: cannot list the warehouses themselves.
            ["stock-viewer"] = ("stock-viewer-secret", [Roles.StockReader])
        };

    /// <summary>The user's roles, or null when the credentials do not match.</summary>
    public IReadOnlyCollection<string>? FindRoles(string username, string password) =>
        Users.TryGetValue(username, out var user) && user.Password == password
            ? user.Roles
            : null;
}
