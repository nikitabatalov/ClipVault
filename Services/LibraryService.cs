using System.IO;
using System.Text.Json;
using ClipVault.Models;

namespace ClipVault.Services;

public static class LibraryService
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string StateFile => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClipVault", "library.json");
    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mov", ".mkv", ".avi", ".webm", ".wmv", ".m4v", ".mpg", ".mpeg", ".ts" };

    public static LibraryState Load()
    {
        try
        {
            if (!File.Exists(StateFile)) return new LibraryState();
            var state = JsonSerializer.Deserialize<LibraryState>(File.ReadAllText(StateFile), Options) ?? new LibraryState();
            state.Games ??= GamePresets.Create();
            state.Friends ??= new();
            state.Annotations = new Dictionary<string, ClipAnnotation>(state.Annotations ?? new(), StringComparer.OrdinalIgnoreCase);
            return state;
        }
        catch (Exception ex)
        {
            // Preserve the corrupt file for manual recovery rather than silently overwriting it.
            try { File.Copy(StateFile, StateFile + ".broken-" + DateTime.Now.ToString("yyyyMMddHHmmss"), false); } catch { }
            System.Windows.MessageBox.Show("Настройки ClipVault не удалось прочитать: " + ex.Message + "\nБудут загружены стандартные настройки. Резервная копия создана рядом с файлом настроек.", "ClipVault", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            return new LibraryState();
        }
    }

    public static void Save(LibraryState state)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StateFile)!);
        var temp = StateFile + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Options));
        File.Move(temp, StateFile, true);
    }

    public static string Relative(string root, string path) => System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');

    public static string ValidateFolderName(string value)
    {
        value = value.Trim();
        if (string.IsNullOrWhiteSpace(value) || value is "." or ".." || value.EndsWith('.') || value.EndsWith(' ') || value.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || value.Contains('/') || value.Contains('\\'))
            throw new ArgumentException("Невозможно использовать такое имя папки.");
        var baseName = value.Split('.')[0].ToUpperInvariant();
        if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(baseName) || Enumerable.Range(1, 9).Any(n => baseName == $"COM{n}" || baseName == $"LPT{n}"))
            throw new ArgumentException("Это имя зарезервировано Windows.");
        return value;
    }

    /// <summary>
    /// The picker takes a DRIVE or PARENT folder. The actual library is always
    /// &lt;selected&gt;\ClipVault, unless the selected directory is ClipVault already.
    /// This guarantees F:\ClipVault\CS2\3 фрага instead of F:\CS2\3 фрага.
    /// </summary>
    public static string ResolveLibraryRoot(string selectedFolder)
    {
        if (string.IsNullOrWhiteSpace(selectedFolder)) throw new ArgumentException("Выбери диск или папку для библиотеки.");
        var absolute = System.IO.Path.GetFullPath(selectedFolder.Trim());
        var volumeRoot = System.IO.Path.GetPathRoot(absolute)!;
        // Never strip the trailing slash from F:\ or a UNC share root.
        var path = string.Equals(absolute, volumeRoot, StringComparison.OrdinalIgnoreCase)
            ? absolute : absolute.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
        if (string.Equals(System.IO.Path.GetFileName(path), "ClipVault", StringComparison.OrdinalIgnoreCase)) return path;
        return System.IO.Path.Combine(path, "ClipVault");
    }

    /// <summary>
    /// Move only KNOWN game directories from the former root. Do not touch other
    /// personal files or folders. Directory.Move is cheap on the same volume.
    /// Existing target directories are deliberately not overwritten or merged.
    /// Relative annotation keys stay valid for folders successfully moved.
    /// </summary>
    public static (int Moved, List<string> Skipped) MigrateLegacyGameFolders(string previousRoot, string newRoot, IEnumerable<GameRule> games)
    {
        var moved = 0;
        var skipped = new List<string>();
        Directory.CreateDirectory(newRoot);
        foreach (var folderName in games.Select(g => g.Folder).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var validName = ValidateFolderName(folderName);
            var source = System.IO.Path.Combine(previousRoot, validName);
            var destination = System.IO.Path.Combine(newRoot, validName);
            if (!Directory.Exists(source)) continue;
            try
            {
                if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                {
                    skipped.Add(validName + " (ссылка/точка соединения)");
                    continue;
                }
                if (Directory.Exists(destination))
                {
                    skipped.Add(validName + " (уже есть в ClipVault)");
                    continue;
                }
                Directory.Move(source, destination);
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(validName + " (" + ex.Message + ")");
            }
        }
        return (moved, skipped);
    }

    public static void EnsureFolders(LibraryState state, GameRule game)
    {
        if (string.IsNullOrWhiteSpace(state.RootFolder)) throw new InvalidOperationException("Сначала укажите корневую папку.");
        var gameFolder = ValidateFolderName(game.Folder);
        Directory.CreateDirectory(System.IO.Path.Combine(state.RootFolder, gameFolder));
        foreach (var category in game.Categories)
            Directory.CreateDirectory(System.IO.Path.Combine(state.RootFolder, gameFolder, ValidateFolderName(category)));
    }

    public static string UniqueDestination(string directory, string fileName)
    {
        var target = System.IO.Path.Combine(directory, fileName);
        if (!File.Exists(target)) return target;
        string stem = System.IO.Path.GetFileNameWithoutExtension(fileName), ext = System.IO.Path.GetExtension(fileName);
        for (int i = 2; i < 100000; i++)
        {
            target = System.IO.Path.Combine(directory, $"{stem} ({i}){ext}");
            if (!File.Exists(target)) return target;
        }
        throw new IOException("Не удалось подобрать свободное имя файла.");
    }

    public static List<ClipItem> Scan(LibraryState state)
    {
        var result = new List<ClipItem>();
        string root = state.RootFolder;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return result;
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            try
            {
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    try { if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) == 0) pending.Push(sub); } catch { }
                }
                foreach (var path in Directory.EnumerateFiles(dir))
                {
                    if (!VideoExtensions.Contains(System.IO.Path.GetExtension(path))) continue;
                    try
                    {
                        string rel = Relative(root, path);
                        var components = rel.Split('/');
                        var game = state.Games.FirstOrDefault(x => x.Folder.Equals(components[0], StringComparison.OrdinalIgnoreCase));
                        string gameName = game?.Name ?? (components.Length >= 2 ? components[0] : "Без игры");
                        string category = components.Length >= 3 ? components[1] : "Без категории";
                        var info = new FileInfo(path);
                        state.Annotations.TryGetValue(rel, out var annotation);
                        result.Add(new ClipItem
                        {
                            Path = path, RelativePath = rel, FileName = info.Name,
                            Game = gameName, Category = category, Bytes = info.Length,
                            Created = info.LastWriteTime,
                            ColorKey = annotation?.ColorKey ?? "none", Icon = annotation?.Icon ?? "",
                            FriendIds = annotation?.FriendIds?.ToList() ?? new(),
                            FriendNames = string.Join(", ", state.Friends.Where(f => annotation?.FriendIds?.Contains(f.Id) == true).Select(f => f.Name))
                        });
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result.OrderByDescending(c => c.Created).ToList();
    }
}
