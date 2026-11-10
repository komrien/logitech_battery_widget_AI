using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LogitechBatteryWidget;

public partial class MainWindow : Window
{
    static readonly Brush Fg = Hex("#E8E8EC");
    static readonly Brush Sub = Hex("#8A8C96");
    static readonly Brush Track = Hex("#33353D");
    static readonly Brush Charge = Hex("#42A5F5");
    static readonly Brush Red = Hex("#EF5350");

    readonly WidgetConfig _config = WidgetConfig.Load();
    readonly GHubClient _client = new();
    readonly DispatcherTimer _dbTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    bool _live;
    string? _lastRender;

    public MainWindow()
    {
        InitializeComponent();
        Left = _config.X;
        Top = _config.Y;
        Topmost = _config.Topmost;
        TopmostItem.IsChecked = _config.Topmost;

        _client.Updated += devices => Dispatcher.Invoke(() =>
        {
            _live = true;
            _dbTimer.Stop();
            RenderLive(devices);
        });
        _client.Offline += _ => Dispatcher.Invoke(() =>
        {
            if (!_live) return;
            _live = false;
            RenderDb();
        });

        _dbTimer.Tick += (_, _) => RenderDb();
        RenderDb();                 // 연결 전까지는 G HUB 저장값 표시
        _client.Start();
        Closed += (_, _) => _client.Dispose();
    }

    static SolidColorBrush Hex(string s)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s));
        b.Freeze();
        return b;
    }

    static Brush LevelColor(int pct) =>
        pct <= 15 ? Red : pct <= 35 ? Hex("#FFB300") : Hex("#4CAF50");

    // ---- 그리기 ----------------------------------------------------------
    void AddDevice(string name, int pct, string status, Brush statusColor, Brush barColor)
    {
        var row = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };

        var top = new DockPanel { LastChildFill = false };
        var nameText = Text("🖱  " + name, 12, Sub);
        nameText.Margin = new Thickness(0, 0, 12, 0);
        nameText.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(nameText, Dock.Left);
        var pctText = Text($"{pct}%", 21, barColor, bold: true);
        DockPanel.SetDock(pctText, Dock.Right);
        top.Children.Add(nameText);
        top.Children.Add(pctText);
        row.Children.Add(top);

        var bar = new Grid { Height = 8, Margin = new Thickness(0, 4, 0, 2) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(pct, 1), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - pct, 0), GridUnitType.Star) });
        var track = new Rectangle { Fill = Track, RadiusX = 4, RadiusY = 4 };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Rectangle { Fill = barColor, RadiusX = 4, RadiusY = 4 });
        row.Children.Add(bar);

        row.Children.Add(Text(status, 11, statusColor));
        DeviceList.Children.Add(row);
    }

    static TextBlock Text(string s, double size, Brush color, bool bold = false) => new()
    {
        Text = s,
        FontSize = size,
        Foreground = color,
        FontFamily = new FontFamily("Segoe UI"),
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
    };

    void RenderLive(IReadOnlyList<BatteryInfo> devices)
    {
        var key = string.Join("|", devices);
        if (key == _lastRender) return;
        _lastRender = key;
        DeviceList.Children.Clear();

        if (devices.Count == 0)
        {
            DeviceList.Children.Add(Text("연결된 무선 기기가 없어요", 12, Sub));
            return;
        }
        foreach (var d in devices)
        {
            if (d.FullyCharged)
                AddDevice(d.Name, d.Percentage, "✔ 충전 완료", Charge, Charge);
            else if (d.Charging)
                AddDevice(d.Name, d.Percentage, "⚡ 충전 중", Charge, Charge);
            else
            {
                var status = "배터리 사용 중";
                if (d.HoursLeft is > 0) status += $" · 약 {d.HoursLeft:0}시간 남음";
                AddDevice(d.Name, d.Percentage, status, Sub, LevelColor(d.Percentage));
            }
        }
    }

    void RenderDb()
    {
        _lastRender = null;
        DeviceList.Children.Clear();
        try
        {
            var devices = SettingsDbReader.Read();
            if (devices.Count == 0)
                DeviceList.Children.Add(Text("배터리 정보가 있는 기기가 없어요", 12, Sub));
            foreach (var (name, pct, time) in devices)
            {
                var when = "";
                if (time is { } t)
                {
                    var mins = (int)(DateTimeOffset.UtcNow - t).TotalMinutes;
                    when = " · " + (mins < 1 ? "방금" : mins < 60 ? $"{mins}분 전" : t.ToLocalTime().ToString("MM/dd HH:mm"));
                }
                AddDevice(name, pct, $"G HUB 연결 대기 중 (저장값{when})", Sub, LevelColor(pct));
            }
        }
        catch (Exception e)
        {
            DeviceList.Children.Add(Text("G HUB 데이터를 읽지 못했어요", 12, Red));
            var msg = e.Message.Length > 60 ? e.Message[..60] : e.Message;
            DeviceList.Children.Add(Text(msg, 11, Sub));
        }
        _dbTimer.Start();
    }

    // ---- 조작 ------------------------------------------------------------
    void OnDragStart(object sender, MouseButtonEventArgs e) => DragMove();

    void OnDragEnd(object sender, MouseButtonEventArgs e)
    {
        _config.X = Left;
        _config.Y = Top;
        _config.Save();
    }

    void OnRefresh(object sender, RoutedEventArgs e)
    {
        if (_live) _client.RequestRefresh();
        else RenderDb();
    }

    void OnToggleTopmost(object sender, RoutedEventArgs e)
    {
        Topmost = TopmostItem.IsChecked;
        _config.Topmost = Topmost;
        _config.Save();
    }

    void OnExit(object sender, RoutedEventArgs e) => Close();
}
