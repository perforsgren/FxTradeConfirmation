namespace FxTradeConfirmation.Services;

/// <summary>
/// Determines whether a clipboard text looks like an FX option request.
/// </summary>
public interface IOptionQueryFilter
{
    /// <summary>
    /// Returns true if <paramref name="text"/> contains at least one option keyword.
    /// </summary>
    bool IsOptionQuery(string? text);

    /// <summary>
    /// Reloads the keyword list from disk (e.g. after the file has been updated).
    /// </summary>
    void Reload();
}
