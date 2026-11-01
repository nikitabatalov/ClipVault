using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ClipVault.Models;

namespace ClipVault;

public partial class PlayerWindow : Window
{
    private readonly ClipItem _clip;
    private readonly DispatcherTimer _timer;
    private bool _playing = true;
    private bool _seeking;
    public PlayerWindow(ClipItem clip)
    {
        InitializeComponent();
        _clip = clip;
        TitleText.Text = clip.FileName;
        InfoText.Text = $"{clip.Game}  /  {clip.Category}  ·  {clip.SizeText}";
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => UpdatePosition();
        Loaded += (_, _) =>
        {
            Video.Source = new Uri(_clip.Path);
            Video.Play();
            _timer.Start();
        };
        Closed += (_, _) => { _timer.Stop(); Video.Stop(); Video.Source = null; };
    }
    private void Video_MediaOpened(object sender, RoutedEventArgs e) => UpdatePosition();
    private void Video_MediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        _timer.Stop();
        MessageBox.Show("Встроенный проигрыватель Windows не может открыть этот формат. Попробуй открыть видео в установленном видеоплеере.\n\n" + e.ErrorException?.Message, "ClipVault", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void UpdatePosition()
    {
        if (!Video.NaturalDuration.HasTimeSpan) return;
        var duration = Video.NaturalDuration.TimeSpan;
        if (!_seeking) { SeekBar.Maximum = Math.Max(0.001, duration.TotalSeconds); SeekBar.Value = Math.Min(Video.Position.TotalSeconds, SeekBar.Maximum); }
        TimeText.Text = $"{Video.Position:mm\\:ss} / {duration:mm\\:ss}";
    }
    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_playing) { Video.Pause(); PlayButton.Content = "▶  Играть"; }
        else { Video.Play(); PlayButton.Content = "⏸  Пауза"; }
        _playing = !_playing;
    }
    private void Seek_Down(object sender, MouseButtonEventArgs e) => _seeking = true;
    private void Seek_Up(object sender, MouseButtonEventArgs e) { Video.Position = TimeSpan.FromSeconds(SeekBar.Value); _seeking = false; }
    private void External_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(_clip.Path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "ClipVault", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
}
