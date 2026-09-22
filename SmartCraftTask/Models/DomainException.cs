namespace SmartCraftTask.Models;

/// <summary>
/// A business rule the caller could not have known was broken until the aggregate was loaded —
/// a duplicate SKU, stock added to a deactivated warehouse. Surfaced as 409 Conflict.
/// Programming errors (a negative quantity that validation should already have rejected)
/// throw the usual argument exceptions instead, so they stay visible as bugs.
/// </summary>
public sealed class DomainException(string title, string message) : Exception(message)
{
    public string Title { get; } = title;
}
