namespace SmartCraftTask.Auth;

/// <summary>
/// Stands in for a real identity provider. Two fixed accounts, passwords compared in plain text,
/// which is enough to demonstrate the authorisation wiring and nothing more. A real service would
/// delegate to an identity provider, or at minimum hash and salt against a users table.
/// </summary>
public sealed class DevUserStore
{
    private static readonly Dictionary<string, (string Password, string Role)> Users =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["manager"] = ("manager-secret", Roles.WarehouseManager),
            ["operator"] = ("operator-secret", Roles.StockOperator)
        };

    /// <summary>The user's role, or null when the credentials do not match.</summary>
    public string? FindRole(string username, string password) =>
        Users.TryGetValue(username, out var user) && user.Password == password
            ? user.Role
            : null;
}
