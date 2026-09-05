using System.Runtime.CompilerServices;

namespace Bitirim.Clothing.Tests;

/// <summary>
/// Redirects application data for the whole test run.
/// </summary>
/// <remarks>
/// Logs, package workspaces, thumbnail caches and recovery records all live
/// under <c>Paths.Root</c>. Without this they would land in the developer's
/// real <c>%APPDATA%\Bitirim\ClothingCreator</c> — a test run would fill their
/// error log with deliberately-corrupt-project exceptions and leave extracted
/// package workspaces behind.
///
/// <c>Paths.Root</c> caches on first read, so the redirect has to be in place
/// before any test constructs anything. A module initializer runs before the
/// first member of this assembly is touched, which is exactly that point —
/// earlier than any xUnit fixture can be.
/// </remarks>
internal static class TestDataRoot
{
    public static string Directory { get; private set; } = string.Empty;

    [ModuleInitializer]
    internal static void Redirect()
    {
        Directory = Path.Combine(
            Path.GetTempPath(), "bcc_testdata_" + Guid.NewGuid().ToString("N")[..8]);
        System.IO.Directory.CreateDirectory(Directory);
        Environment.SetEnvironmentVariable("BCC_DATA_ROOT", Directory);
    }
}
