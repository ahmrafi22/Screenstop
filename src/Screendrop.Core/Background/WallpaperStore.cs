using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace Screendrop.Core.Background;

/// A downloadable wallpaper pack (mac `AnnotationWallpaperPack` parity).
public sealed record WallpaperPack(string PackId, string Title, string Subtitle, string RemoteUrl, string AuthorName, string AuthorUrl)
{
    public static readonly IReadOnlyList<WallpaperPack> BuiltIn = new[]
    {
        new WallpaperPack(
            "uihssn", "UIHSSN", "Wallpaper Pack",
            "https://static.fayazahmed.com/uihssn-wallpaper-pack.zip",
            "Ahmed Hassan", "https://x.com/uihssn"),
        new WallpaperPack(
            "fayaz", "Fayazara", "Author Picks",
            "https://static.fayazahmed.com/fayaz-wallpaper-pack.zip",
            "Fayaz Ahmed", "https://x.com/fayazara"),
    };
}

public sealed class WallpaperPackException : Exception
{
    public WallpaperPackException(string message) : base(message)
    {
    }
}

/// Filesystem-backed wallpaper library (mac `AnnotationWallpaperStore`
/// parity). Packs install into `%APPDATA%\Screendrop\Wallpapers\<packid>`;
/// recents are remembered in a small JSON index beside the settings.
public sealed class WallpaperStore
{
    public const int MaxRecentCount = 24;

    private static readonly string[] SupportedExtensions =
        ["bmp", "gif", "jpeg", "jpg", "png", "tif", "tiff", "webp"];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private readonly string _wallpapersDirectory;
    private readonly string _recentPathsFile;

    public WallpaperStore(string? appDataDirectory = null)
    {
        string root = appDataDirectory
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Screendrop");
        _wallpapersDirectory = Path.Combine(root, "Wallpapers");
        _recentPathsFile = Path.Combine(root, "recent-wallpapers.json");
    }

    public string WallpapersDirectory => _wallpapersDirectory;

    public IReadOnlyList<string> RecentWallpapers { get; private set; } = [];

    public IReadOnlyDictionary<string, IReadOnlyList<string>> InstalledByPackId { get; private set; }
        = new Dictionary<string, IReadOnlyList<string>>();

    public void Reload()
    {
        RecentWallpapers = LoadRecentPaths();

        var installed = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var pack in WallpaperPack.BuiltIn)
        {
            installed[pack.PackId] = EnumerateWallpapers(Path.Combine(_wallpapersDirectory, pack.PackId));
        }

        InstalledByPackId = installed;
    }

    public IReadOnlyList<string> WallpapersFor(WallpaperPack pack) =>
        InstalledByPackId.TryGetValue(pack.PackId, out var list) ? list : [];

    public bool IsInstalled(WallpaperPack pack) => WallpapersFor(pack).Count > 0;

    public static bool IsSupportedImageFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        string name = Path.GetFileName(path);
        if (name.StartsWith("._", StringComparison.Ordinal))
        {
            return false;
        }

        if (path.Replace('\\', '/').Contains("__MACOSX", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return SupportedExtensions.Contains(extension);
    }

    public void AddRecent(string path)
    {
        string standardized = Path.GetFullPath(path);
        if (!IsSupportedImageFile(standardized))
        {
            return;
        }

        var paths = LoadRecentPaths().ToList();
        if (paths.Contains(standardized, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        paths.Insert(0, standardized);
        var filtered = paths
            .Where(File.Exists)
            .Where(IsSupportedImageFile)
            .Take(MaxRecentCount)
            .ToList();

        SaveRecentPaths(filtered);
        RecentWallpapers = filtered;
    }

    public async Task InstallPackAsync(WallpaperPack pack, CancellationToken cancellationToken = default)
    {
        string targetDirectory = Path.Combine(_wallpapersDirectory, pack.PackId);
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"Screendrop-Wallpapers-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(tempRoot);
            string archivePath = Path.Combine(tempRoot, "pack.zip");
            string extractedPath = Path.Combine(tempRoot, "Extracted");

            using (var response = await Http.GetAsync(pack.RemoteUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                if (!response.IsSuccessStatusCode)
                {
                    throw new WallpaperPackException($"Download failed with HTTP {(int)response.StatusCode}.");
                }

                await using var fileStream = File.Create(archivePath);
                await response.Content.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            ZipFile.ExtractToDirectory(archivePath, extractedPath, overwriteFiles: true);

            if (EnumerateWallpapers(extractedPath).Count == 0)
            {
                throw new WallpaperPackException("No supported images were found in this pack.");
            }

            Directory.CreateDirectory(_wallpapersDirectory);
            if (Directory.Exists(targetDirectory))
            {
                Directory.Delete(targetDirectory, recursive: true);
            }

            Directory.Move(extractedPath, targetDirectory);
        }
        catch (WallpaperPackException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            throw new WallpaperPackException($"Download failed: {ex.Message}");
        }
        catch (InvalidDataException)
        {
            throw new WallpaperPackException("Could not unpack the wallpaper pack.");
        }
        catch (IOException ex)
        {
            throw new WallpaperPackException($"Could not install the pack: {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }

    private static IReadOnlyList<string> EnumerateWallpapers(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(directory, "*.*", SearchOption.AllDirectories)
            .Where(IsSupportedImageFile)
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private List<string> LoadRecentPaths()
    {
        try
        {
            if (!File.Exists(_recentPathsFile))
            {
                return [];
            }

            var paths = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_recentPathsFile), JsonOptions);
            return paths ?? [];
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [];
        }
    }

    private void SaveRecentPaths(List<string> paths)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_recentPathsFile)!);
            string tempPath = _recentPathsFile + $".tmp.{Environment.ProcessId}";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(paths, JsonOptions));
            File.Move(tempPath, _recentPathsFile, overwrite: true);
        }
        catch (IOException)
        {
        }
    }
}
