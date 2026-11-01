using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClipVault.Models;

public sealed class LibraryState
{
    public string RootFolder { get; set; } = "";
    public List<GameRule> Games { get; set; } = GamePresets.Create();
    public List<FriendTag> Friends { get; set; } = new();
    // Keys are paths relative to the root, with / separators.
    public Dictionary<string, ClipAnnotation> Annotations { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class GameRule
{
    public string Name { get; set; } = "";
    public string Folder { get; set; } = "";
    public List<string> Categories { get; set; } = new();
    public override string ToString() => Name;
}

public sealed class FriendTag
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public override string ToString() => Name;
}

public sealed class ClipAnnotation
{
    public string ColorKey { get; set; } = "none";
    public string Icon { get; set; } = "";
    public List<string> FriendIds { get; set; } = new();
}

public sealed class ClipItem : INotifyPropertyChanged
{
    public string Path { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Game { get; init; } = "";
    public string Category { get; init; } = "";
    public long Bytes { get; init; }
    public DateTime Created { get; init; }
    public string ColorKey { get; set; } = "none";
    public string Icon { get; set; } = "";
    public List<string> FriendIds { get; set; } = new();
    public string FriendNames { get; set; } = "";
    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(FileName);
    public string SizeText => Bytes >= 1024 * 1024 * 1024 ? $"{Bytes / 1073741824d:0.0} ГБ" : $"{Bytes / 1048576d:0.0} МБ";
    public string DateText => Created.ToString("dd.MM.yyyy · HH:mm");
    public string DetailsText => string.IsNullOrWhiteSpace(FriendNames) ? Category : Category + "  ·  👥 " + FriendNames;
    public System.Windows.Media.Brush ColorBrush => ColorKey switch
    {
        "orange" => new SolidColorBrush(Color.FromRgb(255, 123, 54)),
        "red" => new SolidColorBrush(Color.FromRgb(255, 85, 100)),
        "green" => new SolidColorBrush(Color.FromRgb(70, 205, 147)),
        "blue" => new SolidColorBrush(Color.FromRgb(97, 156, 255)),
        "purple" => new SolidColorBrush(Color.FromRgb(176, 126, 250)),
        _ => new SolidColorBrush(Color.FromRgb(82, 84, 94))
    };
    private BitmapSource? _thumbnail;
    public BitmapSource? Thumbnail
    {
        get => _thumbnail;
        set { _thumbnail = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public static class GamePresets
{
    public static List<GameRule> Create() => new()
    {
        New("Counter-Strike 2", "CS2", "1 фраг", "2 фрага", "3 фрага", "4 фрага", "Ace (5 фрагов)", "Clutch 1v2", "Clutch 1v3", "Clutch 1v4", "Clutch 1v5", "Ninja Defuse", "Смешные моменты", "Красивые раунды", "Прочее"),
        New("Dota 2", "Dota 2", "First Blood", "Double Kill", "Triple Kill", "Ultra Kill", "Rampage", "Teamfight", "Сейв", "Красивые моменты", "Смешные моменты", "Прочее"),
        New("Fortnite", "Fortnite", "Victory Royale (Топ-1)", "Топ-5", "Топ-10", "1 килл", "2 килла", "3 килла", "4+ килла", "Build Fight", "Смешные моменты", "Прочее"),
        New("Valorant", "Valorant", "1 фраг", "2 фрага", "3 фрага", "4 фрага", "Ace", "Clutch", "Смешные моменты", "Прочее"),
        New("PUBG", "PUBG", "Топ-1", "Топ-5", "Топ-10", "1 килл", "2 килла", "3 килла", "4+ килла", "Смешные моменты", "Прочее"),
        New("Apex Legends", "Apex Legends", "Champion", "Squad Wipe", "1 килл", "2 килла", "3+ килла", "Movement", "Смешные моменты", "Прочее"),
        New("Call of Duty Warzone", "Warzone", "Победа", "Squad Wipe", "1 килл", "2 килла", "3+ килла", "Снайперские моменты", "Смешные моменты", "Прочее"),
        New("Rainbow Six Siege", "Rainbow Six Siege", "1 фраг", "2 фрага", "3 фрага", "4 фрага", "Ace", "Clutch", "Смешные моменты", "Прочее"),
        New("Minecraft", "Minecraft", "PvP", "Постройки", "Выживание", "Редкие моменты", "Смешные моменты", "Прочее")
    };
    private static GameRule New(string name, string folder, params string[] categories) => new() { Name = name, Folder = folder, Categories = categories.ToList() };
}
