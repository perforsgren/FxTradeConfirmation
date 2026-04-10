namespace FxTradeConfirmation.Services;

/// <summary>
/// Activates the Bloomberg Terminal window and pastes an OVML command
/// into a new tab for execution.
/// </summary>
public interface IBloombergPaster
{
    /// <summary>
    /// Finds the Bloomberg Terminal window, activates it, opens a new tab,
    /// pastes <paramref name="ovmlText"/> and presses Enter.
    /// Returns true if Bloomberg was found and the paste sequence completed.
    /// </summary>
    Task<bool> PasteOvmlAsync(string ovmlText);
}
