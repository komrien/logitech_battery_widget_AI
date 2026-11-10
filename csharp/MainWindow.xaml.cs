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

    // 위치 저장: 움직임이 멈추고 0.5초 뒤에 한 번 저장
    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };

    public MainWindow()
    {
        InitializeComponent();
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = _config.X;
        Top = _config.Y;
        Topmost = _config.Topmost;
        TopmostItem.IsChecked = _config.Topmost;

        SourceInitialized += (_, _) => NativeWindow.HideFromAltTab(this);
        Loaded += (_, _) => KeepOnScreen();

        // 어떤 방법으로 옮겨지든(드래그 등) 위치가 바뀌면 저장
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SavePosition(); };
        LocationChanged += (_, _) => { _saveTimer.Stop(); _saveTimer.Start(); };
        Closing += (_, _) => SavePosition();

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
    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    void AddDevice(string name, int pct, string status, Brush statusColor, Brush barColor)
    {
        // 기기가 여러 개면 사이에 구분선
        if (DeviceList.Children.Count > 0)
            DeviceList.Children.Add(new Rectangle { Height = 1, Fill = Track, Margin = new Thickness(0, 10, 0, 10) });

        var row = new StackPanel();

        // 1행: [아이콘 이름] ........ [76%]
        var top = new Grid();
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = "",                // Mouse 아이콘
            FontFamily = IconFont,
            FontSize = 14,
            Foreground = Sub,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 1, 8, 0),
        });
        var nameText = Text(name, 12.5, Sub);
        nameText.VerticalAlignment = VerticalAlignment.Center;
        nameText.TextTrimming = TextTrimming.CharacterEllipsis;
        left.Children.Add(nameText);
        top.Children.Add(left);

        var pctText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        pctText.Inlines.Add(new System.Windows.Documents.Run(pct.ToString())
            { FontSize = 24, FontWeight = FontWeights.Bold, Foreground = barColor });
        pctText.Inlines.Add(new System.Windows.Documents.Run("%")
            { FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = barColor });
        Grid.SetColumn(pctText, 1);
        top.Children.Add(pctText);
        row.Children.Add(top);

        // 2행: 막대
        var bar = new Grid { Height = 8, Margin = new Thickness(0, 6, 0, 8) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(pct, 1), GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - pct, 0), GridUnitType.Star) });
        var track = new Rectangle { Fill = Track, RadiusX = 4, RadiusY = 4 };
        Grid.SetColumnSpan(track, 2);
        bar.Children.Add(track);
        bar.Children.Add(new Rectangle { Fill = barColor, RadiusX = 4, RadiusY = 4 });
        row.Children.Add(bar);

        // 3행: 상태
        row.Children.Add(Text(status, 11.5, statusColor));
        DeviceList.Children.Add(row);
    }

    static TextBlock Text(string s, double size, Brush color, bool bold = false) => new()
    {
        Text = s,
        FontSize = size,
        Foreground = color,
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
    void OnDragStart(object sender, MouseButtonEventArgs e)
    {
        DragMove();          // 놓을 때까지 여기서 멈춰 있다가 돌아옴
        SavePosition();
    }

    void OnDragEnd(object sender, MouseButtonEventArgs e) => SavePosition();

    void SavePosition()
    {
        if (WindowState != WindowState.Normal || double.IsNaN(Left) || double.IsNaN(Top)) return;
        if (_config.X == Left && _config.Y == Top) return;
        _config.X = Left;
        _config.Y = Top;
        _config.Save();
    }

    /// <summary>저장된 위치가 지금 연결된 모니터 밖이면(모니터 분리 등) 주 화면 안으로 옮긴다.</summary>
    void KeepOnScreen()
    {
        double vl = SystemParameters.VirtualScreenLeft, vt = SystemParameters.VirtualScreenTop;
        double vr = vl + SystemParameters.VirtualScreenWidth, vb = vt + SystemParameters.VirtualScreenHeight;
        // 위젯 일부(40px 이상)가 화면 안에 보이면 그대로 둔다
        if (Left + 40 <= vr && Left + ActualWidth - 40 >= vl && Top + 20 <= vb && Top + ActualHeight - 20 >= vt
            && NativeWindow.IsOnAnyMonitor(this))
            return;
        Left = 40;
        Top = 40;
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
