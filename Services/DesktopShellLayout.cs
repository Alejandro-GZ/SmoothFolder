namespace SmoothFolder.Services;

/// <summary>
/// Explorer desktop layouts SmoothFolder knows how to recognize.
///
/// The names describe the hierarchy that hosts SHELLDLL_DefView, not a public
/// Windows API contract. Unknown-but-Explorer-owned layouts are intentionally
/// accepted in compatibility mode so a future Windows build does not make all
/// folders disappear solely because a class arrangement changed.
/// </summary>
public enum DesktopShellLayout
{
    Unknown = 0,

    /// <summary>
    /// Modern Windows 11 "raised desktop": Progman has
    /// WS_EX_NOREDIRECTIONBITMAP and hosts the layered SHELLDLL_DefView.
    /// </summary>
    RaisedProgman,

    /// <summary>
    /// Classic layout where SHELLDLL_DefView is hosted by a top-level WorkerW.
    /// </summary>
    ClassicWorkerW,

    /// <summary>
    /// Older/simple layout where SHELLDLL_DefView is directly under Progman.
    /// </summary>
    ProgmanHosted,

    /// <summary>
    /// The hierarchy is owned by the same Explorer process and is structurally
    /// usable, but does not match one of SmoothFolder's named layouts.
    /// </summary>
    CompatibleUnknown
}
