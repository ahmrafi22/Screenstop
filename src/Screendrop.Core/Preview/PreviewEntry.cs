namespace Screendrop.Core.Preview;

public sealed record PreviewEntry(
    string StagingPath,
    string? SavedPath,
    string CaptureType,
    DateTimeOffset CapturedAt)
{
    /// The image backing the card: the saved export when one exists,
    /// otherwise the staged temporary PNG.
    public string ImagePath => SavedPath ?? StagingPath;
}
