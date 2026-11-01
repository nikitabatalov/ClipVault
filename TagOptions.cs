namespace ClipVault;

public static class TagOptions
{
    private static readonly (string Label, string Key)[] Colors =
    {
        ("⚪ Без цвета", "none"), ("🟠 Оранжевый", "orange"), ("🔴 Красный", "red"),
        ("🟢 Зелёный", "green"), ("🔵 Синий", "blue"), ("🟣 Фиолетовый", "purple")
    };
    private static readonly (string Label, string Key)[] Icons =
    {
        ("Без иконки", ""), ("🔥 Огонь", "🔥"), ("😂 Смешно", "😂"),
        ("🏆 Победа", "🏆"), ("🎯 Точный момент", "🎯"), ("⭐ Избранное", "⭐"),
        ("💀 Фраг", "💀"), ("⚡ Жёстко", "⚡"), ("❤️ Любимое", "❤️"), ("🎬 Кино", "🎬")
    };
    public static string[] ColorLabels => Colors.Select(x => x.Label).ToArray();
    public static string[] IconLabels => Icons.Select(x => x.Label).ToArray();
    public static string ColorFromLabel(string? label) => Colors.FirstOrDefault(x => x.Label == label).Key ?? "none";
    public static string IconFromLabel(string? label) => Icons.FirstOrDefault(x => x.Label == label).Key ?? "";
    public static string LabelForColor(string key) => Colors.FirstOrDefault(x => x.Key == key).Label ?? Colors[0].Label;
    public static string LabelForIcon(string key) => Icons.FirstOrDefault(x => x.Key == key).Label ?? Icons[0].Label;
}
