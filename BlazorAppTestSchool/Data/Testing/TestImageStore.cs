namespace BlazorAppTestSchool.Data.Testing;

public sealed class TestImageStore(IWebHostEnvironment environment)
{
    private const string RelativeFolder = "uploads/test-images";
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp"
    };

    private string PhysicalFolder => Path.Combine(environment.WebRootPath, "uploads", "test-images");

    public static bool IsAllowedExtension(string fileName) => AllowedExtensions.Contains(Path.GetExtension(fileName));

    public async Task<string> SaveAsync(Stream content, string originalFileName, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(originalFileName);
        if (!AllowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Дозволені лише зображення: PNG, JPG, GIF, WEBP.");
        }

        Directory.CreateDirectory(PhysicalFolder);
        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var fullPath = Path.Combine(PhysicalFolder, fileName);
        await using (var file = File.Create(fullPath))
        {
            await content.CopyToAsync(file, cancellationToken);
        }

        return $"{RelativeFolder}/{fileName}";
    }

    public string[] List()
    {
        if (!Directory.Exists(PhysicalFolder))
        {
            return [];
        }

        return Directory.GetFiles(PhysicalFolder)
            .OrderByDescending(File.GetCreationTimeUtc)
            .Select(path => $"{RelativeFolder}/{Path.GetFileName(path)}")
            .ToArray();
    }

    public void Delete(string relativePath)
    {
        var fileName = Path.GetFileName(relativePath);
        var fullPath = Path.Combine(PhysicalFolder, fileName);
        if (Path.GetDirectoryName(fullPath) == PhysicalFolder.TrimEnd(Path.DirectorySeparatorChar) && File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }
}
