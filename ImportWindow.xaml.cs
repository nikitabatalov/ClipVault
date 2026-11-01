using System.IO;
using System.Windows;
using System.Windows.Controls;
using ClipVault.Models;
using ClipVault.Services;

namespace ClipVault;

public partial class ImportWindow : Window
{
    private readonly LibraryState _state;
    private readonly List<string> _files;
    private readonly List<(string Id, System.Windows.Controls.CheckBox Box)> _friends = new();
    public GameRule? ChosenGame => GamePicker.SelectedItem as GameRule;
    public string ChosenCategory => CategoryPicker.SelectedItem as string ?? "Прочее";
    public string ChosenColor => TagOptions.ColorFromLabel(ColorPicker.SelectedItem as string);
    public string ChosenIcon => TagOptions.IconFromLabel(IconPicker.SelectedItem as string);
    public List<string> ChosenFriends => _friends.Where(x => x.Box.IsChecked == true).Select(x => x.Id).ToList();
    public bool ShouldMove => MoveFiles.IsChecked == true;

    public ImportWindow(LibraryState state, IEnumerable<string> paths, GameRule? selectedGame)
    {
        InitializeComponent();
        _state = state;
        _files = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        FilesLabel.Text = $"Выбрано файлов: {_files.Count}. Укажи, куда их сохранить.";
        GamePicker.ItemsSource = state.Games;
        GamePicker.SelectedItem = selectedGame ?? state.Games.FirstOrDefault();
        ColorPicker.ItemsSource = TagOptions.ColorLabels;
        ColorPicker.SelectedIndex = 0;
        IconPicker.ItemsSource = TagOptions.IconLabels;
        IconPicker.SelectedIndex = 0;
        FriendsPanel.Children.Clear();
        if (state.Friends.Count == 0)
            FriendsPanel.Children.Add(new TextBlock { Text = "Пока нет друзей. Добавь их в разделе «Друзья».", Foreground = System.Windows.Media.Brushes.Gray, FontSize = 12 });
        foreach (var friend in state.Friends)
        {
            var box = new System.Windows.Controls.CheckBox { Content = friend.Name };
            _friends.Add((friend.Id, box));
            FriendsPanel.Children.Add(box);
        }
    }

    private void GamePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryPicker is null) return;
        CategoryPicker.ItemsSource = ChosenGame?.Categories;
        CategoryPicker.SelectedIndex = 0;
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        if (ChosenGame is null || CategoryPicker.SelectedItem is null)
        {
            MessageBox.Show("Выбери игру и тип момента.", "ClipVault", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
