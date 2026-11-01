using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ClipVault.Models;
using ClipVault.Services;
using Microsoft.Win32;

namespace ClipVault;

public partial class MainWindow : Window
{
    private readonly LibraryState _state;
    private readonly ObservableCollection<ClipItem> _visible = new();
    private readonly Dictionary<string, BitmapSource> _thumbnails = new(StringComparer.OrdinalIgnoreCase);
    private List<ClipItem> _all = new();
    private FileSystemWatcher? _watcher;
    private readonly DispatcherTimer _debounce;
    private int _refreshVersion;
    private bool _suppressFilters;

    public MainWindow()
    {
        InitializeComponent();
        _state = LibraryService.Load();
        ClipList.ItemsSource = _visible;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(850) };
        _debounce.Tick += async (_, _) => { _debounce.Stop(); await RefreshLibraryAsync(); };
        IconFilter.ItemsSource = new[] { "Все иконки" }.Concat(TagOptions.IconLabels.Skip(1)).ToArray();
        ColorFilter.ItemsSource = new[] { "Все цвета" }.Concat(TagOptions.ColorLabels.Skip(1)).ToArray();
        IconFilter.SelectedIndex = 0;
        ColorFilter.SelectedIndex = 0;
        LoadRulePickers();
        RefreshFriendFilter();
        ShowPage("library");
        Loaded += async (_, _) =>
        {
            UpgradeLegacyRootOnStartup();
            UpdateRootUi();
            StartWatching();
            await RefreshLibraryAsync();
        };
        Closed += (_, _) => { _watcher?.Dispose(); _debounce.Stop(); };
    }

    private void Save() => LibraryService.Save(_state);
    private void UpgradeLegacyRootOnStartup()
    {
        if (string.IsNullOrWhiteSpace(_state.RootFolder)) return;
        try
        {
            var previous = _state.RootFolder;
            var normalized = LibraryService.ResolveLibraryRoot(previous);
            if (string.Equals(previous.TrimEnd('\\', '/'), normalized.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)) return;
            // Older releases allowed a drive root, for example F:\, as the library root.
            // Ask before moving ANY files; only game folders from current templates qualify.
            var answer = MessageBox.Show(
                $"В новой версии библиотека хранится в отдельной папке:\n{normalized}\n\n" +
                $"Раньше использовалась папка:\n{previous}\n\n" +
                "Перенести папки известных игр (CS2, Dota 2 и т. д.) в ClipVault? " +
                "Другие файлы на диске не будут затронуты.\n\n" +
                "Да — перенести игровые папки. Нет — создать новую библиотеку и оставить старые папки на месте.",
                "ClipVault · Обновление расположения", MessageBoxButton.YesNo, MessageBoxImage.Question);
            Directory.CreateDirectory(normalized);
            if (answer == MessageBoxResult.Yes)
            {
                var (moved, skipped) = LibraryService.MigrateLegacyGameFolders(previous, normalized, _state.Games);
                MessageBox.Show($"Перемещено папок игр: {moved}." +
                    (skipped.Count > 0 ? "\nНе перенесены:\n" + string.Join("\n", skipped.Take(8)) + "\n\nИх можно перенести вручную через Проводник." : ""),
                    "ClipVault · Перенос папок", MessageBoxButton.OK,
                    skipped.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            _state.RootFolder = normalized;
            Save();
        }
        catch (Exception ex)
        {
            ShowError(ex);
            // A failed migration must not silently redirect or delete any existing clips.
        }
    }

    private void Status(string message) => StatusLabel.Text = message;
    private void UpdateRootUi()
    {
        SetupBanner.Visibility = string.IsNullOrWhiteSpace(_state.RootFolder) ? Visibility.Visible : Visibility.Collapsed;
        RootPathLabel.Text = string.IsNullOrWhiteSpace(_state.RootFolder) ? "Не выбрана" : _state.RootFolder;
        if (!string.IsNullOrWhiteSpace(_state.RootFolder) && !Directory.Exists(_state.RootFolder))
        {
            SetupBanner.Visibility = Visibility.Visible;
            Status("Папка библиотеки недоступна. Подключи диск или выбери другую папку.");
        }
    }

    private void ShowPage(string page)
    {
        LibraryPage.Visibility = page == "library" ? Visibility.Visible : Visibility.Collapsed;
        RulesPage.Visibility = page == "rules" ? Visibility.Visible : Visibility.Collapsed;
        FriendsPage.Visibility = page == "friends" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = page == "settings" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in new[] { LibraryNav, RulesNav, FriendsNav, SettingsNav })
        {
            button.Background = Brushes.Transparent;
            button.Foreground = new SolidColorBrush(Color.FromRgb(178, 179, 188));
        }
        var active = page switch { "rules" => RulesNav, "friends" => FriendsNav, "settings" => SettingsNav, _ => LibraryNav };
        active.Background = new SolidColorBrush(Color.FromRgb(52, 37, 29));
        active.Foreground = Brushes.White;
        (PageTitle.Text, PageSubtitle.Text) = page switch
        {
            "rules" => ("Игры и папки", "Собери свою логику автоматической сортировки"),
            "friends" => ("Друзья", "Отмечай, с кем играл, и находи совместные моменты"),
            "settings" => ("Настройки", "Твоя библиотека, твои правила"),
            _ => ("Библиотека моментов", "Все твои лучшие моменты — в одном месте")
        };
    }
    private void LibraryNav_Click(object sender, RoutedEventArgs e) => ShowPage("library");
    private void RulesNav_Click(object sender, RoutedEventArgs e) => ShowPage("rules");
    private void FriendsNav_Click(object sender, RoutedEventArgs e) => ShowPage("friends");
    private void SettingsNav_Click(object sender, RoutedEventArgs e) => ShowPage("settings");

    private void LoadRulePickers()
    {
        _suppressFilters = true;
        string currentFilter = GameFilter.SelectedItem as string ?? "Все игры";
        GameFilter.ItemsSource = new[] { "Все игры" }.Concat(_state.Games.Select(g => g.Name)).ToArray();
        GameFilter.SelectedItem = GameFilter.Items.Cast<string>().Contains(currentFilter) ? currentFilter : "Все игры";
        RulesGameList.ItemsSource = null;
        RulesGameList.ItemsSource = _state.Games.ToList();
        if (RulesGameList.Items.Count > 0 && RulesGameList.SelectedIndex < 0) RulesGameList.SelectedIndex = 0;
        _suppressFilters = false;
        UpdateCategoryChoices();
    }
    private void RefreshFriendFilter()
    {
        _suppressFilters = true;
        string old = FriendFilter.SelectedItem as string ?? "Все друзья";
        FriendFilter.ItemsSource = new[] { "Все друзья" }.Concat(_state.Friends.Select(x => x.Name)).ToArray();
        FriendFilter.SelectedItem = FriendFilter.Items.Cast<string>().Contains(old) ? old : "Все друзья";
        FriendsList.ItemsSource = null;
        FriendsList.ItemsSource = _state.Friends.ToList();
        FriendsTotal.Text = _state.Friends.Count.ToString();
        _suppressFilters = false;
        ApplyFilters();
    }
    private void UpdateCategoryChoices()
    {
        if (CategoryFilter is null) return;
        var name = GameFilter.SelectedItem as string;
        IEnumerable<string> categories = name is not null && name != "Все игры"
            ? _state.Games.FirstOrDefault(g => g.Name == name)?.Categories ?? new List<string>()
            : _all.Select(c => c.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToArray();
        CategoryFilter.ItemsSource = new[] { "Все категории" }.Concat(categories.Distinct(StringComparer.OrdinalIgnoreCase)).ToList();
        CategoryFilter.SelectedIndex = 0;
    }

    private void StartWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        if (!Directory.Exists(_state.RootFolder)) return;
        try
        {
            _watcher = new FileSystemWatcher(_state.RootFolder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileChanged;
            _watcher.Renamed += OnRenamed;
            _watcher.Error += (_, _) => Dispatcher.BeginInvoke(new Action(() => { StartWatching(); ScheduleRefresh(); }));
        }
        catch (Exception ex) { Status("Наблюдение за папкой недоступно: " + ex.Message); }
    }
    private void OnRenamed(object sender, RenamedEventArgs args)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (IsInsideRoot(args.OldFullPath) && IsInsideRoot(args.FullPath))
                {
                    string oldPrefix = LibraryService.Relative(_state.RootFolder, args.OldFullPath);
                    string newPrefix = LibraryService.Relative(_state.RootFolder, args.FullPath);
                    var keys = _state.Annotations.Keys.Where(k => k.Equals(oldPrefix, StringComparison.OrdinalIgnoreCase) || k.StartsWith(oldPrefix + "/", StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (string key in keys)
                    {
                        var item = _state.Annotations[key];
                        _state.Annotations.Remove(key);
                        _state.Annotations[newPrefix + key.Substring(oldPrefix.Length)] = item;
                    }
                    if (keys.Count > 0) Save();
                }
            }
            catch { /* File system notifications are best-effort. */ }
            ScheduleRefresh();
        }));
    }
    private void OnFileChanged(object sender, FileSystemEventArgs e) => ScheduleRefresh();
    private void ScheduleRefresh()
    {
        Dispatcher.BeginInvoke(new Action(() => { _debounce.Stop(); _debounce.Start(); }));
    }

    private async Task RefreshLibraryAsync()
    {
        int version = ++_refreshVersion;
        Status("Сканирование библиотеки...");
        try
        {
            var clips = await Task.Run(() => LibraryService.Scan(_state));
            if (version != _refreshVersion) return;
            _all = clips;
            ClipTotal.Text = _all.Count.ToString();
            GamesTotal.Text = _all.Select(x => x.Game).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString();
            TaggedTotal.Text = _all.Count(x => x.ColorKey != "none" || !string.IsNullOrEmpty(x.Icon) || x.FriendIds.Count > 0).ToString();
            UpdateCategoryChoicesPreserve();
            ApplyFilters();
            Status($"Найдено видео: {_all.Count}  ·  Папка: {(string.IsNullOrWhiteSpace(_state.RootFolder) ? "не выбрана" : _state.RootFolder)}");
        }
        catch (Exception ex) { Status("Ошибка чтения библиотеки: " + ex.Message); }
    }

    private void UpdateCategoryChoicesPreserve()
    {
        var selected = CategoryFilter.SelectedItem as string ?? "Все категории";
        _suppressFilters = true;
        UpdateCategoryChoices();
        if (CategoryFilter.Items.Cast<string>().Contains(selected)) CategoryFilter.SelectedItem = selected;
        _suppressFilters = false;
    }

    private void GameFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilters || CategoryFilter is null) return;
        _suppressFilters = true;
        UpdateCategoryChoices();
        _suppressFilters = false;
        ApplyFilters();
    }
    private void Filters_Changed(object sender, TextChangedEventArgs e) => ApplyFilters();
    private void Filters_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilters();
    private void CategoryFilter_Changed(object sender, SelectionChangedEventArgs e) => ApplyFilters();

    private void ApplyFilters()
    {
        if (_suppressFilters || ClipList is null || GameFilter is null || ColorFilter is null || IconFilter is null || FriendFilter is null || CategoryFilter is null) return;
        var game = GameFilter.SelectedItem as string ?? "Все игры";
        var category = CategoryFilter.SelectedItem as string ?? "Все категории";
        var icon = IconFilter.SelectedItem as string ?? "Все иконки";
        var color = ColorFilter.SelectedItem as string ?? "Все цвета";
        var friend = FriendFilter.SelectedItem as string ?? "Все друзья";
        var friendId = _state.Friends.FirstOrDefault(x => x.Name == friend)?.Id;
        var query = SearchBox?.Text.Trim() ?? "";
        var results = _all.Where(c =>
            (game == "Все игры" || c.Game == game) &&
            (category == "Все категории" || c.Category == category) &&
            (icon == "Все иконки" || c.Icon == TagOptions.IconFromLabel(icon)) &&
            (color == "Все цвета" || c.ColorKey == TagOptions.ColorFromLabel(color)) &&
            (friend == "Все друзья" || (friendId is not null && c.FriendIds.Contains(friendId))) &&
            (string.IsNullOrWhiteSpace(query) || (c.DisplayName + " " + c.Game + " " + c.Category + " " + c.FriendNames).Contains(query, StringComparison.OrdinalIgnoreCase))
        ).ToList();
        _visible.Clear();
        foreach (var clip in results)
        {
            if (_thumbnails.TryGetValue(clip.Path, out var image)) clip.Thumbnail = image;
            _visible.Add(clip);
        }
        CountLabel.Text = $"МОМЕНТЫ  ·  {results.Count}";
        EmptyClips.Visibility = results.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _ = LoadVisibleThumbnailsAsync(results.Take(72).ToList());
    }

    private async Task LoadVisibleThumbnailsAsync(List<ClipItem> clips)
    {
        using var gate = new SemaphoreSlim(3);
        var tasks = clips.Where(x => x.Thumbnail is null).Select(async clip =>
        {
            await gate.WaitAsync();
            try
            {
                var thumb = await Task.Run(() => ThumbnailService.TryGet(clip.Path));
                if (thumb is not null)
                    await Dispatcher.InvokeAsync(() => { clip.Thumbnail = thumb; if (_thumbnails.Count < 350) _thumbnails[clip.Path] = thumb; });
            }
            finally { gate.Release(); }
        });
        try { await Task.WhenAll(tasks); } catch { /* A thumbnail is optional. */ }
    }

    private void ChooseRoot_Click(object sender, RoutedEventArgs e)
    {
        // Native WPF folder picker in .NET 8; avoids mixing Windows Forms into WPF.
        var dialog = new OpenFolderDialog
        {
            Title = "Выбери диск F:\\ или папку — внутри будет создан ClipVault",
            Multiselect = false,
            InitialDirectory = Directory.Exists(_state.RootFolder)
                ? _state.RootFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            string actualRoot = LibraryService.ResolveLibraryRoot(dialog.FolderName);
            Directory.CreateDirectory(actualRoot);
            if (!string.Equals(_state.RootFolder, actualRoot, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(_state.RootFolder) &&
                MessageBox.Show("При смене корневой папки файлы автоматически не переносятся. Метки связаны с относительными путями, поэтому при переходе на другую библиотеку часть меток может не соответствовать файлам. Продолжить?", "Смена библиотеки", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _state.RootFolder = actualRoot;
            _thumbnails.Clear();
            Save();
            UpdateRootUi(); StartWatching();
            Status("Библиотека: " + actualRoot);
            _ = RefreshLibraryAsync();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void OpenRoot_Click(object sender, RoutedEventArgs e)
    {
        if (!Directory.Exists(_state.RootFolder)) { MessageBox.Show("Папка недоступна."); return; }
        try { Process.Start(new ProcessStartInfo(_state.RootFolder) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex); }
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshLibraryAsync();

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsureRoot()) return;
        var picker = new OpenFileDialog
        {
            Title = "Выбери игровые видео",
            Multiselect = true,
            Filter = "Видео (*.mp4;*.mkv;*.mov;*.avi;*.webm;*.wmv;*.m4v;*.mpg;*.mpeg;*.ts)|*.mp4;*.mkv;*.mov;*.avi;*.webm;*.wmv;*.m4v;*.mpg;*.mpeg;*.ts|Все файлы (*.*)|*.*"
        };
        if (picker.ShowDialog(this) == true) _ = ImportFilesAsync(picker.FileNames);
    }
    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }
    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (!EnsureRoot()) return;
        var paths = e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
        // Support folders dropped into ClipVault as well as individual video files.
        var files = new List<string>();
        foreach (var path in paths)
        {
            try
            {
                if (File.Exists(path) && LibraryService.VideoExtensions.Contains(System.IO.Path.GetExtension(path))) files.Add(path);
                else if (Directory.Exists(path))
                    files.AddRange(Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).Where(x => LibraryService.VideoExtensions.Contains(System.IO.Path.GetExtension(x))));
            }
            catch (Exception ex) { Status("Некоторые файлы при перетаскивании пропущены: " + ex.Message); }
        }
        if (files.Count > 0) _ = ImportFilesAsync(files);
        else MessageBox.Show("Не найдено поддерживаемых видео.", "ClipVault", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private bool EnsureRoot()
    {
        if (Directory.Exists(_state.RootFolder)) return true;
        MessageBox.Show("Сначала выбери доступную корневую папку в настройках.", "ClipVault", MessageBoxButton.OK, MessageBoxImage.Information);
        ChooseRoot_Click(this, new RoutedEventArgs());
        return Directory.Exists(_state.RootFolder);
    }

    private async Task ImportFilesAsync(IEnumerable<string> input)
    {
        var paths = input.Where(x => File.Exists(x) && LibraryService.VideoExtensions.Contains(System.IO.Path.GetExtension(x))).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (paths.Length == 0) return;
        var game = _state.Games.FirstOrDefault(x => x.Name == (GameFilter.SelectedItem as string));
        var dialog = new ImportWindow(_state, paths, game) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.ChosenGame is null) return;
        var chosenGame = dialog.ChosenGame;
        var chosenCategory = dialog.ChosenCategory;
        var chosenColor = dialog.ChosenColor;
        var chosenIcon = dialog.ChosenIcon;
        var chosenFriends = dialog.ChosenFriends;
        var move = dialog.ShouldMove;
        Status($"Импорт файлов: {paths.Length}...");
        var imported = new List<(string OldPath, string NewPath)>();
        var errors = new List<string>();
        await Task.Run(() =>
        {
            string folder;
            try
            {
                LibraryService.EnsureFolders(_state, chosenGame);
                folder = System.IO.Path.Combine(_state.RootFolder, chosenGame.Folder, chosenCategory);
                Directory.CreateDirectory(folder);
            }
            catch (Exception ex) { errors.Add(ex.Message); return; }
            foreach (var path in paths)
            {
                try
                {
                    var directTarget = System.IO.Path.Combine(folder, System.IO.Path.GetFileName(path));
                    string dest;
                    if (System.IO.Path.GetFullPath(path).Equals(System.IO.Path.GetFullPath(directTarget), StringComparison.OrdinalIgnoreCase)) dest = path;
                    else
                    {
                        dest = LibraryService.UniqueDestination(folder, System.IO.Path.GetFileName(path));
                        if (move) File.Move(path, dest);
                        else File.Copy(path, dest);
                    }
                    imported.Add((path, dest));
                }
                catch (Exception ex) { errors.Add(System.IO.Path.GetFileName(path) + ": " + ex.Message); }
            }
        });
        foreach (var (oldPath, newPath) in imported)
        {
            if (IsInsideRoot(oldPath) && move) _state.Annotations.Remove(LibraryService.Relative(_state.RootFolder, oldPath));
            var key = LibraryService.Relative(_state.RootFolder, newPath);
            _state.Annotations[key] = new ClipAnnotation { ColorKey = chosenColor, Icon = chosenIcon, FriendIds = chosenFriends.ToList() };
        }
        try { Save(); } catch (Exception ex) { errors.Add("Не удалось сохранить метки: " + ex.Message); }
        await RefreshLibraryAsync();
        if (errors.Count > 0) MessageBox.Show($"Готово: {imported.Count} из {paths.Length}.\n\nОшибки:\n" + string.Join("\n", errors.Take(8)), "ClipVault", MessageBoxButton.OK, MessageBoxImage.Warning);
        else Status($"Импортировано: {imported.Count}. Файлы сохранены в {chosenGame.Folder} / {chosenCategory}");
    }

    private bool IsInsideRoot(string path)
    {
        if (string.IsNullOrWhiteSpace(_state.RootFolder)) return false;
        var root = System.IO.Path.GetFullPath(_state.RootFolder).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
        return System.IO.Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private void ClipList_RightClick(object sender, MouseButtonEventArgs e)
    {
        DependencyObject? candidate = e.OriginalSource as DependencyObject;
        while (candidate is not null && candidate is not ListBoxItem)
            candidate = VisualTreeHelper.GetParent(candidate);
        if (candidate is ListBoxItem row) ClipList.SelectedItem = row.DataContext;
    }

    private ClipItem? Selected() => ClipList.SelectedItem as ClipItem;
    private void ClipList_DoubleClick(object sender, MouseButtonEventArgs e) { if (Selected() is { } clip) OpenClip(clip); }
    private void OpenSelected_Click(object sender, RoutedEventArgs e) { if (Selected() is { } clip) OpenClip(clip); }
    private void OpenClip(ClipItem clip)
    {
        if (!File.Exists(clip.Path)) { MessageBox.Show("Видеофайл больше не найден."); _ = RefreshLibraryAsync(); return; }
        new PlayerWindow(clip) { Owner = this }.ShowDialog();
    }

    private async void EditSelected_Click(object sender, RoutedEventArgs e)
    {
        var clip = Selected();
        if (clip is null) { MessageBox.Show("Сначала выбери видео."); return; }
        if (!File.Exists(clip.Path)) { MessageBox.Show("Файл не найден."); return; }
        var dialog = new EditClipWindow(_state, clip) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try
        {
            string newPath = clip.Path;
            if (dialog.ShouldMove && dialog.ChosenGame is { } game)
            {
                LibraryService.EnsureFolders(_state, game);
                var folder = System.IO.Path.Combine(_state.RootFolder, game.Folder, dialog.ChosenCategory);
                Directory.CreateDirectory(folder);
                var desired = System.IO.Path.Combine(folder, clip.FileName);
                if (!System.IO.Path.GetFullPath(clip.Path).Equals(System.IO.Path.GetFullPath(desired), StringComparison.OrdinalIgnoreCase))
                {
                    newPath = LibraryService.UniqueDestination(folder, clip.FileName);
                    File.Move(clip.Path, newPath);
                    _state.Annotations.Remove(clip.RelativePath);
                    _thumbnails.Remove(clip.Path);
                }
            }
            var rel = LibraryService.Relative(_state.RootFolder, newPath);
            _state.Annotations[rel] = new ClipAnnotation { ColorKey = dialog.ChosenColor, Icon = dialog.ChosenIcon, FriendIds = dialog.ChosenFriends };
            Save();
            await RefreshLibraryAsync();
            Status("Метки видео сохранены.");
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void RevealSelected_Click(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } clip) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{clip.Path}\"") { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } clip) return;
        try { Clipboard.SetText(clip.Path); Status("Путь к видео скопирован."); } catch (Exception ex) { ShowError(ex); }
    }
    private void ShareSelected_Click(object sender, RoutedEventArgs e)
    {
        if (Selected() is not { } clip) return;
        var dialog = new SaveFileDialog { Title = "Сохранить копию видео для друга", FileName = clip.FileName, Filter = "Видео|*" + System.IO.Path.GetExtension(clip.FileName) + "|Все файлы|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            if (System.IO.Path.GetFullPath(dialog.FileName).Equals(System.IO.Path.GetFullPath(clip.Path), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Нельзя копировать файл поверх самого себя.");
            File.Copy(clip.Path, dialog.FileName, true);
            Status("Копия видео сохранена: " + dialog.FileName);
            MessageBox.Show("Копия готова. Можешь отправить этот файл другу любым удобным способом.", "ClipVault", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void RulesGameList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateRuleDetails();
    private void UpdateRuleDetails()
    {
        if (RulesCategoryList is null) return;
        var game = RulesGameList.SelectedItem as GameRule;
        RulesGameTitle.Text = game?.Name ?? "Выбери игру слева";
        RulesGamePath.Text = game is null ? "" : "Путь: " +
            (string.IsNullOrWhiteSpace(_state.RootFolder) ? "ClipVault" : _state.RootFolder) +
            " \\ " + game.Folder + "   ·   " + game.Categories.Count + " категорий";
        RulesCategoryList.ItemsSource = game?.Categories.ToArray();
    }
    private void AddGame_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptWindow.Ask(this, "Новая игра", "Название игры (например, The Finals):");
        if (name is null) return;
        try
        {
            LibraryService.ValidateFolderName(name);
            if (_state.Games.Any(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || g.Folder.Equals(name, StringComparison.OrdinalIgnoreCase)))
                throw new ArgumentException("Такая игра уже есть.");
            var game = new GameRule { Name = name, Folder = name, Categories = new() { "Красивые моменты", "Смешные моменты", "Прочее" } };
            _state.Games.Add(game); Save(); LoadRulePickers(); RulesGameList.SelectedItem = game;
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void RemoveGame_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGameList.SelectedItem is not GameRule game) return;
        if (MessageBox.Show($"Убрать «{game.Name}» из шаблонов? Существующие папки и видео останутся на диске.", "ClipVault", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _state.Games.Remove(game); Save(); LoadRulePickers();
    }
    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGameList.SelectedItem is not GameRule game) return;
        var name = PromptWindow.Ask(this, "Новая категория", $"Название новой папки для {game.Name}:");
        if (name is null) return;
        try
        {
            LibraryService.ValidateFolderName(name);
            if (game.Categories.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Такая папка уже есть в шаблоне.");
            game.Categories.Add(name); Save(); UpdateRuleDetails();
            UpdateCategoryChoicesPreserve();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void RenameCategory_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGameList.SelectedItem is not GameRule game || RulesCategoryList.SelectedItem is not string oldName) return;
        var name = PromptWindow.Ask(this, "Переименование", "Новое имя правила (существующие папки не переименуются):", oldName);
        if (name is null || name == oldName) return;
        try
        {
            LibraryService.ValidateFolderName(name);
            if (game.Categories.Contains(name, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Такая папка уже есть в шаблоне.");
            game.Categories[game.Categories.IndexOf(oldName)] = name; Save();
            UpdateRuleDetails();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void DeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGameList.SelectedItem is not GameRule game || RulesCategoryList.SelectedItem is not string category) return;
        if (MessageBox.Show($"Убрать «{category}» из шаблона? Файлы и папки на диске не будут удалены.", "ClipVault", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        game.Categories.Remove(category); Save();
        UpdateRuleDetails();
    }
    private void CreateFolders_Click(object sender, RoutedEventArgs e)
    {
        if (RulesGameList.SelectedItem is not GameRule game || !EnsureRoot()) return;
        try { LibraryService.EnsureFolders(_state, game); Status($"Созданы папки: {game.Folder} / {game.Categories.Count} категорий."); _ = RefreshLibraryAsync(); MessageBox.Show("Папки созданы (существующие не затронуты).", "ClipVault"); }
        catch (Exception ex) { ShowError(ex); }
    }

    private void AddFriend_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptWindow.Ask(this, "Добавить друга", "Имя / ник друга:");
        if (name is null) return;
        if (_state.Friends.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show("Такой друг уже добавлен."); return; }
        _state.Friends.Add(new FriendTag { Name = name }); Save(); RefreshFriendFilter();
    }
    private void RenameFriend_Click(object sender, RoutedEventArgs e)
    {
        if (FriendsList.SelectedItem is not FriendTag friend) return;
        var name = PromptWindow.Ask(this, "Переименовать друга", "Новое имя / ник:", friend.Name);
        if (name is null) return;
        if (_state.Friends.Any(f => f.Id != friend.Id && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show("Такое имя уже есть."); return; }
        friend.Name = name; Save(); RefreshFriendFilter(); _ = RefreshLibraryAsync();
    }
    private void DeleteFriend_Click(object sender, RoutedEventArgs e)
    {
        if (FriendsList.SelectedItem is not FriendTag friend) return;
        if (MessageBox.Show($"Удалить метку друга «{friend.Name}»? Видео останутся.", "ClipVault", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _state.Friends.Remove(friend);
        foreach (var item in _state.Annotations.Values) item.FriendIds.Remove(friend.Id);
        Save(); RefreshFriendFilter(); _ = RefreshLibraryAsync();
    }
    private async void ExportFriend_Click(object sender, RoutedEventArgs e)
    {
        if (FriendsList.SelectedItem is not FriendTag friend)
        {
            MessageBox.Show("Выбери друга в списке.", "ClipVault", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var clips = _all.Where(c => c.FriendIds.Contains(friend.Id) && File.Exists(c.Path)).ToList();
        if (clips.Count == 0)
        {
            MessageBox.Show("Пока нет клипов с меткой этого друга.", "ClipVault", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var folderPicker = new OpenFolderDialog
        {
            Title = $"Куда экспортировать {clips.Count} видео для {friend.Name}?",
            Multiselect = false,
            InitialDirectory = Directory.Exists(_state.RootFolder)
                ? _state.RootFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)
        };
        if (folderPicker.ShowDialog(this) != true) return;
        var targetFolder = folderPicker.FolderName;
        if (MessageBox.Show($"Скопировать {clips.Count} клипов в выбранную папку? Исходные файлы останутся на месте.", "Экспорт моментов", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        int success = 0;
        var errors = new List<string>();
        Status("Экспортируем моменты друга...");
        await Task.Run(() =>
        {
            foreach (var clip in clips)
            {
                try
                {
                    var safeGame = LibraryService.ValidateFolderName(clip.Game);
                    var outputDir = System.IO.Path.Combine(targetFolder, safeGame);
                    Directory.CreateDirectory(outputDir);
                    var dest = LibraryService.UniqueDestination(outputDir, clip.FileName);
                    if (System.IO.Path.GetFullPath(clip.Path).Equals(System.IO.Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Папка экспорта не должна совпадать с исходным файлом.");
                    File.Copy(clip.Path, dest);
                    success++;
                }
                catch (Exception ex) { errors.Add(clip.FileName + ": " + ex.Message); }
            }
        });
        Status($"Экспортировано клипов для {friend.Name}: {success}");
        MessageBox.Show($"Скопировано клипов: {success} из {clips.Count}." + (errors.Count > 0 ? "\n\nОшибки:\n" + string.Join("\n", errors.Take(5)) : ""), "ClipVault", MessageBoxButton.OK, errors.Count > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }
    private void ShowError(Exception ex) => MessageBox.Show(ex.Message, "ClipVault · Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
}
