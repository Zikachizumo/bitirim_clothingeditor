using Xunit;
using Bitirim.Clothing.Editor.Infrastructure;

namespace Bitirim.Clothing.Tests;

/// <summary>
/// Where a Browse dialog opens.
/// </summary>
/// <remarks>
/// These exist because the first attempt shipped the library path through to
/// the shell dialog exactly as the settings file stored it. The stored value
/// was <c>C:/bcc/fixtures</c> -- forward slashes, which is what the folder
/// picker writes and what a user typing a path produces. The shell resolves
/// InitialDirectory through SHCreateItemFromParsingName, which rejects that
/// outright, and the dialog did not open at all. A compiling build and a green
/// suite both said nothing; only launching the app did.
/// </remarks>
public class FilePickerStartTests
{
    private static readonly Dictionary<string, string> Nothing = new();

    private static Func<string, bool> Exists(params string[] directories) =>
        p => directories.Contains(p, StringComparer.OrdinalIgnoreCase);

    // ------------------------------------------------------------------
    // the bug
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("ydd")]
    [InlineData("ytd")]
    [InlineData("ymt")]
    [InlineData("asset")]
    public void A_forward_slash_library_path_comes_back_canonical(string kind)
    {
        var result = FilePickerStart.Resolve(
            kind, Nothing, "C:/bcc/fixtures", Exists(@"C:\bcc\fixtures"));

        Assert.Equal(@"C:\bcc\fixtures", result);
    }

    [Fact]
    public void A_trailing_separator_is_trimmed()
    {
        var result = FilePickerStart.Resolve(
            "ydd", Nothing, @"C:\bcc\fixtures\", Exists(@"C:\bcc\fixtures"));

        Assert.Equal(@"C:\bcc\fixtures", result);
    }

    [Fact]
    public void A_relative_segment_is_resolved_away()
    {
        var result = FilePickerStart.Resolve(
            "ydd", Nothing, @"C:\bcc\projects\..\fixtures", Exists(@"C:\bcc\fixtures"));

        Assert.Equal(@"C:\bcc\fixtures", result);
    }

    // ------------------------------------------------------------------
    // which kinds get the library
    // ------------------------------------------------------------------

    [Fact]
    public void An_image_picker_does_not_start_in_the_asset_library()
    {
        // Reference images come from anywhere; the library holds game files.
        var result = FilePickerStart.Resolve(
            "image", Nothing, @"C:\bcc\fixtures", Exists(@"C:\bcc\fixtures"));

        Assert.Null(result);
    }

    [Fact]
    public void An_unknown_kind_does_not_start_in_the_asset_library()
    {
        var result = FilePickerStart.Resolve(
            "spreadsheet", Nothing, @"C:\bcc\fixtures", Exists(@"C:\bcc\fixtures"));

        Assert.Null(result);
    }

    [Fact]
    public void The_kind_is_matched_regardless_of_case()
    {
        var result = FilePickerStart.Resolve(
            "YDD", Nothing, @"C:\bcc\fixtures", Exists(@"C:\bcc\fixtures"));

        Assert.Equal(@"C:\bcc\fixtures", result);
    }

    // ------------------------------------------------------------------
    // following the user
    // ------------------------------------------------------------------

    [Fact]
    public void Where_a_game_asset_was_last_picked_wins_over_the_library()
    {
        var remembered = new Dictionary<string, string>
        {
            [FilePickerStart.GroupKey("ydd")] = @"D:\mods\shirts",
        };

        var result = FilePickerStart.Resolve(
            "ydd", remembered, @"C:\bcc\fixtures",
            Exists(@"D:\mods\shirts", @"C:\bcc\fixtures"));

        Assert.Equal(@"D:\mods\shirts", result);
    }

    [Theory]
    [InlineData("ytd")]
    [InlineData("ymt")]
    [InlineData("asset")]
    public void Picking_a_drawable_moves_the_other_game_pickers_with_it(string next)
    {
        // A garment's .ydd and its .ytd live in the same DLC folder, so having
        // just chosen one there is the best guess for the other. This is the
        // whole wizard flow: Browse, Browse, Browse down one row.
        var remembered = new Dictionary<string, string>();
        remembered[FilePickerStart.GroupKey("ydd")] = @"D:\dlc\beach";

        var result = FilePickerStart.Resolve(
            next, remembered, @"C:\bcc\fixtures", Exists(@"D:\dlc\beach", @"C:\bcc\fixtures"));

        Assert.Equal(@"D:\dlc\beach", result);
    }

    [Fact]
    public void Game_assets_and_images_keep_separate_histories()
    {
        var remembered = new Dictionary<string, string>
        {
            [FilePickerStart.GroupKey("image")] = @"D:\art",
        };

        var result = FilePickerStart.Resolve(
            "ydd", remembered, @"C:\bcc\fixtures", Exists(@"D:\art", @"C:\bcc\fixtures"));

        Assert.Equal(@"C:\bcc\fixtures", result);
    }

    [Fact]
    public void A_remembered_folder_that_is_gone_falls_back_to_the_library()
    {
        // An unplugged drive must not strand the picker on a dead path.
        var remembered = new Dictionary<string, string>
        {
            [FilePickerStart.GroupKey("ydd")] = @"E:\usb\shirts",
        };

        var result = FilePickerStart.Resolve(
            "ydd", remembered, @"C:\bcc\fixtures", Exists(@"C:\bcc\fixtures"));

        Assert.Equal(@"C:\bcc\fixtures", result);
    }

    [Fact]
    public void A_remembered_image_folder_is_still_honoured()
    {
        var remembered = new Dictionary<string, string>
        {
            [FilePickerStart.GroupKey("image")] = @"D:\art",
        };

        var result = FilePickerStart.Resolve("image", remembered, null, Exists(@"D:\art"));

        Assert.Equal(@"D:\art", result);
    }

    [Fact]
    public void Every_game_asset_kind_shares_one_history_slot()
    {
        var slots = new[] { "ydd", "ytd", "ymt", "asset" }
            .Select(FilePickerStart.GroupKey)
            .Distinct()
            .ToList();

        Assert.Single(slots);
        Assert.NotEqual(slots[0], FilePickerStart.GroupKey("image"));
    }

    // ------------------------------------------------------------------
    // nothing usable
    // ------------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_library_configured_leaves_the_dialog_to_decide(string? library)
    {
        Assert.Null(FilePickerStart.Resolve("ydd", Nothing, library, Exists()));
    }

    [Fact]
    public void A_library_folder_that_does_not_exist_is_not_passed_on()
    {
        // Handing the shell a missing folder is the same crash as a bad one.
        Assert.Null(FilePickerStart.Resolve("ydd", Nothing, @"C:\gone", Exists()));
    }

    [Fact]
    public void An_unparseable_path_is_rejected_rather_than_thrown()
    {
        Assert.Null(FilePickerStart.Resolve("ydd", Nothing, "C:\\bad\0path", Exists()));
    }

    [Fact]
    public void An_existence_check_that_throws_is_treated_as_missing()
    {
        // A disconnected share can make Directory.Exists throw rather than
        // return false; the picker must still open.
        var result = FilePickerStart.Resolve(
            "ydd", Nothing, @"\\dead-host\share",
            _ => throw new IOException("the network path was not found"));

        Assert.Null(result);
    }

    [Fact]
    public void A_null_kind_is_treated_as_no_kind_rather_than_crashing()
    {
        Assert.Null(FilePickerStart.Resolve(null, Nothing, @"C:\bcc\fixtures", Exists(@"C:\bcc\fixtures")));
    }

    // ------------------------------------------------------------------
    // the real library folder
    // ------------------------------------------------------------------

    [Fact]
    public void The_recommended_library_layout_resolves()
    {
        var root = Path.Combine(TestDataRoot.Directory, "Asset Library");
        Directory.CreateDirectory(Path.Combine(root, "mp_m_freemode_01"));

        var asTyped = root.Replace('\\', '/');
        var result = FilePickerStart.Resolve("ydd", Nothing, asTyped, Directory.Exists);

        Assert.Equal(root, result);
        Assert.True(Directory.Exists(result));
    }
}
