using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ClipVault.Models;

namespace ClipVault;

public partial class EditClipWindow : Window
{
    private readonly List<(string Id, System.Windows.Controls.CheckBox Box)> _friends = new();
    public GameRule? ChosenGame => GamePicker.SelectedItem as GameRule;
    public string ChosenCategory => CategoryPicker.SelectedItem as string ?? "Прочее";
    public string ChosenColor => TagOptions.ColorFromLabel(ColorPicker.SelectedItem as string);
    public string ChosenIcon => TagOptions.IconFromLabel(IconPicker.SelectedItem as string);
    public List<string> ChosenFriends => _friends.Where(x => x.Box.IsChecked == true).Select(x => x.Id).ToList();
    public bool ShouldMove => MoveFile.IsChecked == true;

    public EditClipWindow(LibraryState state, ClipItem clip)
    {
        InitializeComponent();
        FileLabel.Text = clip.FileName;
        GamePicker.ItemsSource = state.Games;
        var game = state.Games.FirstOrDefault(g => g.Name.Equals(clip.Game, StringComparison.OrdinalIgnoreCase));
        GamePicker.SelectedItem = game ?? state.Games.FirstOrDefault();
        if (game != null && game.Categories.Contains(clip.Category)) CategoryPicker.SelectedItem = clip.Category;
        else MoveFile.IsChecked = false;
        ColorPicker.ItemsSource = TagOptions.ColorLabels;
        ColorPicker.SelectedItem = TagOptions.LabelForColor(clip.ColorKey);
        IconPicker.ItemsSource = TagOptions.IconLabels;
        IconPicker.SelectedItem = TagOptions.LabelForIcon(clip.Icon);
        if (state.Friends.Count == 0)
            FriendsPanel.Children.Add(new TextBlock { Text = "Нет друзей. Добавь их в разделе «Друзья».", Foreground = Brushes.Gray });
        foreach (var friend in state.Friends)
        {
            var cb = new System.Windows.Controls.CheckBox { Content = friend.Name, IsChecked = clip.FriendIds.Contains(friend.Id) };
            _friends.Add((friend.Id, cb));
            FriendsPanel.Children.Add(cb);
        }
    }

    private void GamePicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryPicker is null) return;
        CategoryPicker.ItemsSource = ChosenGame?.Categories;
        CategoryPicker.SelectedIndex = 0;
    }
    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (ChosenGame is null || CategoryPicker.SelectedItem is null)
        {
            MessageBox.Show("Выбери игру и категорию.", "ClipVault", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
