using FileAction.Core;
using FileAction.Core.Operations;

namespace FileAction.Desktop;

public static class DesktopOperations
{
    /// <summary>Conversions and tools (no UI needed), as used by the CLI and the wheel.</summary>
    public static IReadOnlyList<Operation> Conversions(DesktopSettings settings) => [.. Catalog.Build(), new TesseractOcr(settings)];
}
