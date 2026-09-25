using Screenstop.Core.Background;
using Screenstop.Core.History;
using Screenstop.Core.Preview;
using Screenstop.Core.Settings;
using Xunit;

namespace Screenstop.Core.Tests;

/// <summary>
/// Covers the pieces the capture-preview card relies on when a user presses
/// Save: the stack showing only the newest capture, and the destination folder
/// and filename that a silent save writes to.
/// </summary>
public class PreviewSaveTests
{
    [Fact]
    public void Capacity_one_stack_keeps_only_the_newest_capture()
    {
        var stack = new PreviewStack(maxCount: 1);
        var evicted = new List<PreviewEntry>();
        stack.Evicted += evicted.Add;

        var older = Entry("older");
        var newer = Entry("newer");
        stack.Push(older);
        stack.Push(newer);

        Assert.Equal(new[] { newer }, stack.Items);
        // The evicted entry is what the presenter uses to delete its staging
        // file, so losing that callback would leak temp files.
        Assert.Equal(new[] { older }, evicted);
    }

    [Fact]
    public void Default_export_directory_is_a_Screenstop_folder_under_Pictures()
    {
        string path = SettingsStore.DefaultExportDirectoryPath;

        Assert.EndsWith("Screenstop", path);
        Assert.Equal(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenstop"),
            path);
    }

    [Fact]
    public void Save_button_uses_the_folder_only_when_configured()
    {
        // With nothing configured the Save button must open a dialog rather
        // than silently dropping the file somewhere the user did not choose.
        var defaults = new ScreenstopSettings();
        Assert.False(defaults.EffectiveSaveButtonUsesFolder);

        var explicitFolder = new ScreenstopSettings { SaveButtonUsesFolder = true };
        Assert.True(explicitFolder.EffectiveSaveButtonUsesFolder);

        // And an explicit "off" wins over AutoSave being on.
        var autoSaveButButtonOff = new ScreenstopSettings { AutoSave = true, SaveButtonUsesFolder = false };
        Assert.False(autoSaveButButtonOff.EffectiveSaveButtonUsesFolder);
    }

    [Fact]
    public void Saved_filenames_do_not_collide()
    {
        string directory = Path.Combine(Path.GetTempPath(), "ScreenstopSaveTest");
        Directory.CreateDirectory(directory);
        try
        {
            var settings = new ScreenstopSettings();
            var when = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

            string first = FileNaming.BuildFileName(settings.FileNamePattern, when, "fullscreen", "png");
            string second = FileNaming.ResolveUnique(directory, first);

            Assert.NotEqual(first, second);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PreviewEntry Entry(string name) => new(
        Path.Combine(Path.GetTempPath(), "Screenstop", name + ".png"),
        null,
        "fullscreen",
        DateTimeOffset.Now);
}
