namespace Screendrop.Core.Preview;

public sealed record PreviewEntry(
    string ImagePath,
    string? SavedPath,
    string CaptureType,
    DateTimeOffset CapturedAt);
