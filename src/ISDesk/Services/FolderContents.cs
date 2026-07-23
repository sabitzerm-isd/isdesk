using System.IO;

namespace ISDesk.Services;

public static class FolderContents
{
    private static readonly HashSet<string> IgnoredNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "desktop.ini", "thumbs.db"
    };

    private static readonly string[] ShortcutExtensions = { ".lnk", ".url", ".appref-ms" };

    /// Sichtbare Eintraege: keine Hidden/System, kein desktop.ini/thumbs.db;
    /// Ordner zuerst, dann Dateien, jeweils nach Name (OrdinalIgnoreCase).
    public static IReadOnlyList<string> ListVisibleEntries(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            return Array.Empty<string>();

        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = new DirectoryInfo(folderPath).EnumerateFileSystemInfos();
        }
        catch (Exception)
        {
            return Array.Empty<string>();
        }

        var folders = new List<string>();
        var files = new List<string>();

        foreach (var entry in entries)
        {
            if (IgnoredNames.Contains(entry.Name)) continue;
            if (IsHiddenOrSystem(entry)) continue;

            if ((entry.Attributes & FileAttributes.Directory) == FileAttributes.Directory)
                folders.Add(entry.FullName);
            else
                files.Add(entry.FullName);
        }

        folders.Sort(CompareByFileName);
        files.Sort(CompareByFileName);

        var result = new List<string>(folders.Count + files.Count);
        result.AddRange(folders);
        result.AddRange(files);
        return result;
    }

    /// Anzeigename: .lnk/.url/.appref-ms → Dateiname ohne Extension,
    /// sonst Dateiname mit Extension; Ordner → Ordnername.
    public static string GetDisplayName(string path)
    {
        if (Directory.Exists(path))
            return new DirectoryInfo(path).Name;

        var ext = Path.GetExtension(path);
        foreach (var shortcutExt in ShortcutExtensions)
        {
            if (ext.Equals(shortcutExt, StringComparison.OrdinalIgnoreCase))
                return Path.GetFileNameWithoutExtension(path);
        }
        return Path.GetFileName(path);
    }

    private static int CompareByFileName(string a, string b)
        => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase);

    private static bool IsHiddenOrSystem(FileSystemInfo entry)
    {
        try
        {
            var attr = entry.Attributes;
            return (attr & FileAttributes.Hidden) == FileAttributes.Hidden
                || (attr & FileAttributes.System) == FileAttributes.System;
        }
        catch (Exception)
        {
            return true; // Attribute nicht lesbar → sicherheitshalber ausblenden
        }
    }
}
