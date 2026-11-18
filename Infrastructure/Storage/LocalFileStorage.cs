using Microsoft.Extensions.Options;
using TranslationBackend.Application;
using TranslationBackend.Application.Interface.IService;

namespace TranslationBackend.Infrastructure.Storage;

/// <summary>
/// Files live under one root folder that the backend and the extraction service share:
/// &lt;root&gt;/&lt;jobId&gt;/input/source.&lt;ext&gt; (the upload) and &lt;root&gt;/&lt;jobId&gt;/output/ (what the service writes).
/// Paths kept in the database and sent to the service are relative to the root; every path is checked to stay inside it.
/// </summary>
public class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(IOptions<StorageOptions> options)
    {
        if (string.IsNullOrWhiteSpace(options.Value.Root))
            throw new InvalidOperationException("Storage:Root must be set to the folder shared with the extraction service.");
        _root = Path.GetFullPath(options.Value.Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Directory.CreateDirectory(_root);
    }

    public async Task<string> SaveUploadAsync(Guid jobId, string extension, Stream content, CancellationToken ct = default)
    {
        var relative = $"{jobId:D}/input/source{extension.ToLowerInvariant()}";
        var full = Resolve(relative) ?? throw new InvalidOperationException("Upload path left the storage root.");
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var file = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, ct);
        return relative;
    }

    public string OutputDirectory(Guid jobId) => $"{jobId:D}/output";

    public string? Resolve(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var full = Path.GetFullPath(Path.Combine(_root, relativePath));
        return IsInside(full) ? full : null;
    }

    public string? ToRelative(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(_root, path));
        return IsInside(full) ? Path.GetRelativePath(_root, full).Replace('\\', '/') : null;
    }

    public void DeleteJobFolder(Guid jobId)
    {
        var folder = Resolve($"{jobId:D}");
        if (folder != null && Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private bool IsInside(string full)
        => full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
