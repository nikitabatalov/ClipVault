using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClipVault;

public sealed class PromptWindow : Window
{
    private readonly System.Windows.Controls.TextBox _input;
    public string Value => _input.Text.Trim();
    public static string? Ask(Window owner, string title, string description, string starting = "")
    {
        var prompt = new PromptWindow(title, description, starting) { Owner = owner };
        return prompt.ShowDialog() == true ? prompt.Value : null;
    }
    private PromptWindow(string title, string description, string starting)
    {
        Title = title + " · ClipVault";
        Width = 445; Height = 225; MinWidth = 385;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Background = (Brush)System.Windows.Application.Current.FindResource("PageBrush");
        Foreground = (Brush)System.Windows.Application.Current.FindResource("TextBrush");
        var root = new Border
        {
            Margin = new Thickness(18),
            Padding = new Thickness(18),
            Background = (Brush)System.Windows.Application.Current.FindResource("PageBrush"),
            BorderBrush = (Brush)System.Windows.Application.Current.FindResource("BorderBrushApp"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(16)
        };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var label = new TextBlock { Text = description, FontSize = 13, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 14) };
        Grid.SetRow(label, 0); grid.Children.Add(label);
        _input = new System.Windows.Controls.TextBox { Text = starting, Margin = new Thickness(0, 0, 0, 18) };
        Grid.SetRow(_input, 1); grid.Children.Add(_input);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        var cancel = new Button { Content = "Отмена", Style = (Style)System.Windows.Application.Current.FindResource("PlainButton"), Margin = new Thickness(0, 0, 9, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var okay = new Button { Content = "Сохранить", Style = (Style)System.Windows.Application.Current.FindResource("OrangeButton") };
        okay.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(Value)) DialogResult = true; };
        row.Children.Add(cancel); row.Children.Add(okay);
        Grid.SetRow(row, 2); grid.Children.Add(row);
        root.Child = grid;
        Content = root;
        Loaded += (_, _) => { _input.Focus(); _input.SelectAll(); };
        _input.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter && !string.IsNullOrWhiteSpace(Value)) DialogResult = true; };
    }
}
