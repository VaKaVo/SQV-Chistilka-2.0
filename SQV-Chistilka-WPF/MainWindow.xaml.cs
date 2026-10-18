using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SqvChistilka.Services;

namespace SqvChistilka;

public partial class MainWindow : Window
{
    private Grid? _activePage;
    private CancellationTokenSource? _analysisCts;
    private long _quickTotal;
    private string _currentDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public ObservableCollection<DiskRow> DiskRows { get; } = new();
    public ObservableCollection<ExplorerRow> ExplorerRows { get; } = new();
    public ObservableCollection<AnalysisRow> AnalysisRows { get; } = new();
    public ObservableCollection<CleanerRow> CleanerRows { get; } = new();
    public ObservableCollection<StartupRow> StartupRows { get; } = new();
    private List<StartupItem> _startupItems = new();

    public MainWindow()
    {
        InitializeComponent();
        // Акцент из Windows
        try
        {
            var accent = ThemeService.GetAccentColor();
            if (Resources["AccentBrush"] is System.Windows.Media.SolidColorBrush b)
                b.Color = accent;
        }
        catch { }

        DisksList.ItemsSource = DiskRows;
        ExplorerGrid.ItemsSource = ExplorerRows;
        AnalysisGrid.ItemsSource = AnalysisRows;
        CleanerList.ItemsSource = CleanerRows;
        StartupGrid.ItemsSource = StartupRows;

        _activePage = PageDashboard;
        Loaded += (_, _) =>
        {
            // GPU fade-in окна (opacity анимируется композитором, а не CPU)
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BeginAnimation(OpacityProperty, fade);
            RefreshDisks();
            ExplorerNavigate(_currentDir);
            LoadCleanerCategories();
            RefreshStartup();
            RefreshTips();
            RefreshHistory();
            RefreshLicense();
            try { AppVersionText.Text = AppInfo.Version; } catch { }
            try { SelfAuto.IsChecked = StartupService.IsSelfAutostart(); } catch { }
            // Подсветить Обзор стартовым
            BtnDashboard.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#181810"));
            _ = CheckUpdatesOnStartup();
        };
    }

    // ---- обновления ----
    private async Task CheckUpdatesOnStartup()
    {
        await Task.Delay(2500); // дать окну спокойно открыться
        UpdateInfo? upd = null;
        try { upd = await UpdateService.CheckAsync(); } catch { }
        if (upd == null) return;
        await Dispatcher.Invoke(async () => await OfferUpdate(upd));
    }

    private async Task CheckUpdatesManual()
    {
        SetStatus("Проверка обновлений...");
        UpdateInfo? upd = null;
        try { upd = await UpdateService.CheckAsync(); } catch { }
        SetStatus("Готово");
        if (upd == null)
        {
            MessageBox.Show(AppInfo.UpdatesConfigured ? $"У тебя последняя версия ({AppInfo.Version})." : "Обновления не настроены: впиши свой GitHub-репозиторий в Services/AppInfo.cs.",
                "Обновления", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        await OfferUpdate(upd);
    }

    private async Task OfferUpdate(UpdateInfo upd)
    {
        var res = MessageBox.Show($"Доступна версия {upd.Version} (у тебя {AppInfo.Version}).\nУстановить сейчас? Программа закроется.",
            "Доступно обновление", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;
        try
        {
            SetStatus("Скачивание обновления...");
            string setup = await UpdateService.DownloadSetupAsync(upd);
            UpdateService.RunInstallerAndExit(setup);
        }
        catch (Exception ex)
        {
            SetStatus("Готово");
            var open = MessageBox.Show($"Не скачалось само ({ex.Message}).\nОткрыть страницу релиза в браузере?",
                "Обновление", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (open == MessageBoxResult.Yes && !string.IsNullOrEmpty(upd.PageUrl))
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(upd.PageUrl) { UseShellExecute = true }); } catch { }
        }
    }

    private void CheckUpdates_Click(object sender, RoutedEventArgs e) => _ = CheckUpdatesManual();

    // ---- window chrome ----
    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
    private void Min_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Max_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    // ---- GPU slide между вкладками ----
    private readonly Dictionary<string, Grid> _pages = new();
    private void EnsurePages()
    {
        if (_pages.Count > 0) return;
        _pages["dashboard"] = PageDashboard;
        _pages["explorer"] = PageExplorer;
        _pages["analysis"] = PageAnalysis;
        _pages["cleaner"] = PageCleaner;
        _pages["startup"] = PageStartup;
        _pages["tips"] = PageTips;
        _pages["license"] = PageLicense;
    }

    private bool IsPro() => LicenseService.Current().IsPro;

    private void GoLicense()
    {
        EnsurePages();
        var next = _pages["license"];
        if (next == _activePage) return;
        SwitchTo(next, 1);
        if (BtnDashboard.Parent is StackPanel panel)
        {
            foreach (var child in panel.Children.OfType<Button>())
            {
                bool active = (string)child.Tag == "license";
                child.Background = active
                    ? new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#181810"))
                    : System.Windows.Media.Brushes.Transparent;
                if (active) { try { child.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"); } catch { } }
                else child.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8C8C80"));
            }
        }
    }

    private bool RequirePro(string feature)
    {
        if (IsPro()) return true;
        MessageBox.Show($"{feature} доступно в Pro.\nОткрой вкладку «Лицензия» и введи ключ из бота.", "Нужен Pro",
            MessageBoxButton.OK, MessageBoxImage.Information);
        GoLicense();
        return false;
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        EnsurePages();
        var btn = (Button)sender;
        string id = (string)btn.Tag;
        var order = new[] { "dashboard", "explorer", "analysis", "cleaner", "startup", "tips", "license" };
        var next = _pages[id];
        if (next == _activePage) return;
        int dir = Array.IndexOf(order, id) > Array.IndexOf(order, _pages.First(k => k.Value == _activePage).Key) ? 1 : -1;
        // Подсветка активной кнопки
        if (btn.Parent is StackPanel panel)
        {
            foreach (var child in panel.Children.OfType<Button>())
            {
                child.Background = System.Windows.Media.Brushes.Transparent;
                child.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#8C8C80"));
            }
        }
        btn.Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#181810"));
        try { btn.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"); } catch { }
        SwitchTo(next, dir);
    }

    private void SwitchTo(Grid next, int direction)
    {
        var prev = _activePage;
        _activePage = next;
        if (prev != null) prev.Visibility = Visibility.Collapsed;
        next.Visibility = Visibility.Visible;

        // Свайп: X 60*dir -> 0 + fade 0 -> 1. Всё на GPU через RenderTransform.
        // Важно: по завершении сбрасываем базовые значения, иначе со Stop анимация откатится и контент "съедет".
        ContentSlide.X = 60 * direction;
        ContentHost.Opacity = 0;
        var sb = new Storyboard();
        sb.Completed += (_, _) => { ContentSlide.X = 0; ContentHost.Opacity = 1; };
        var slide = new DoubleAnimation(60 * direction, 0, TimeSpan.FromMilliseconds(260))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(slide, ContentSlide);
        Storyboard.SetTargetProperty(slide, new PropertyPath(TranslateTransform.XProperty));
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
        {
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(fade, ContentHost);
        Storyboard.SetTargetProperty(fade, new PropertyPath(OpacityProperty));
        sb.Children.Add(slide);
        sb.Children.Add(fade);
        sb.Begin();
    }

    private void SetStatus(string t) => StatusText.Text = t;

    // ---- dashboard ----
    private void RefreshDisks_Click(object sender, RoutedEventArgs e) { RefreshDisks(); RefreshTips(); }
    private void RefreshDisks()
    {
        DiskRows.Clear();
        foreach (var d in DiskService.ListDisks())
            DiskRows.Add(new DiskRow($"{d.Device}", $"{FormatHelper.HumanSize(d.Used)} занято из {FormatHelper.HumanSize(d.Total)} ({d.Percent:F0}%)", d.Percent));
    }

    private async void QuickScan_Click(object sender, RoutedEventArgs e)
    {
        QuickCleanBtn.IsEnabled = false;
        QuickProgress.Value = 0;
        QuickResult.Text = "Сканирование...";
        SetStatus("Сканирование временных файлов...");
        var cats = CleanerService.GetTempCategories().Where(c => c.Kind == "dir" && !string.IsNullOrEmpty(c.Path) && (!c.IsPro || IsPro())).ToList();
        long total = 0;
        var details = new List<(string, long)>();
        await Task.Run(() =>
        {
            for (int i = 0; i < cats.Count; i++)
            {
                var c = cats[i];
                long sz = 0;
                try { if (Directory.Exists(c.Path!)) sz = CleanerService.GetDirSize(c.Path!); } catch { }
                total += sz;
                details.Add((c.Name, sz));
                int idx = i;
                Dispatcher.Invoke(() => QuickProgress.Value = (idx + 1) / (double)Math.Max(cats.Count, 1));
            }
        });
        _quickTotal = total;
        QuickProgress.Value = 1;
        var lines = new List<string> { $"Найдено мусора: {FormatHelper.HumanSize(total)}", "" };
        foreach (var (n, s) in details.OrderByDescending(x => x.Item2).Take(6))
            if (s > 0) lines.Add($"• {n}: {FormatHelper.HumanSize(s)}");
        QuickResult.Text = string.Join("\n", lines);
        if (total > 0) QuickCleanBtn.IsEnabled = true;
        SetStatus("Готово");
    }

    private async void QuickClean_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show($"Будут очищены временные файлы (~{FormatHelper.HumanSize(_quickTotal)}). Продолжить?", "Подтверждение",
            MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        SetStatus("Очистка...");
        long freed = 0;
        await Task.Run(() =>
        {
            foreach (var c in CleanerService.GetTempCategories().Where(c => c.Kind == "dir" && (!c.IsPro || IsPro())))
            {
                try
                {
                    if (!string.IsNullOrEmpty(c.Path) && Directory.Exists(c.Path))
                        freed += CleanerService.ClearFolderContents(c.Path).Freed;
                }
                catch { }
            }
        });
        MessageBox.Show($"Освобождено: {FormatHelper.HumanSize(freed)}");
        try { HistoryService.Add(freed, $"Быстрая очистка {DateTime.Now:dd.MM HH:mm}"); RefreshHistory(); } catch { }
        QuickCleanBtn.IsEnabled = false;
        QuickResult.Text = "Нажмите «Быстрое сканирование мусора», чтобы проверить снова.";
        RefreshDisks();
        SetStatus("Готово");
    }

    // ---- explorer ----
    private void ExplorerGo_Click(object sender, RoutedEventArgs e) => ExplorerNavigate(ExplorerPath.Text.Trim());
    private void ExplorerHome_Click(object sender, RoutedEventArgs e) => ExplorerNavigate(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    private void ExplorerUp_Click(object sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_currentDir.TrimEnd(Path.DirectorySeparatorChar));
        if (parent != null) ExplorerNavigate(parent.FullName);
    }
    private void ExplorerGrid_DblClick(object sender, MouseButtonEventArgs e)
    {
        if (ExplorerGrid.SelectedItem is ExplorerRow r && r.IsDir) ExplorerNavigate(r.Path);
    }
    private void ExplorerNavigate(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) path = _currentDir;
        if (!Directory.Exists(path)) { MessageBox.Show($"Папка не найдена:\n{path}"); return; }
        _currentDir = path;
        ExplorerPath.Text = path;
        ExplorerRows.Clear();
        try
        {
            foreach (var en in ExplorerService.ListFolder(path))
                ExplorerRows.Add(new ExplorerRow(en.Name, en.Path, en.IsDir,
                    en.IsDir ? "📁 папка" : FormatHelper.HumanSize(en.Size),
                    en.MTime == default ? "—" : en.MTime.ToString("dd.MM.yyyy HH:mm"), en.Size));
        }
        catch (Exception ex) { MessageBox.Show(ex.Message); }
        ExplorerInfo.Text = $"Объектов: {ExplorerRows.Count}";
    }
    private async void ExplorerSizes_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Подсчёт размеров папок...");
        var rows = ExplorerRows.Where(r => r.IsDir).ToList();
        await Task.Run(() =>
        {
            foreach (var r in rows)
            {
                long sz = CleanerService.GetDirSize(r.Path);
                r.Size = sz;
                Dispatcher.Invoke(() => { r.SizeText = FormatHelper.HumanSize(sz); });
            }
        });
        // refresh grid
        ExplorerGrid.Items.Refresh();
        SetStatus("Готово");
    }
    private void ExplorerDelete_Click(object sender, RoutedEventArgs e)
    {
        var sel = ExplorerGrid.SelectedItems.Cast<ExplorerRow>().ToList();
        if (sel.Count == 0) return;
        if (MessageBox.Show($"Удалить {sel.Count} объект(ов) в Корзину?", "Подтверждение", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        foreach (var r in sel)
            try { CleanerService.DeletePath(r.Path, true); } catch (Exception ex) { MessageBox.Show($"{r.Path}\n{ex.Message}"); }
        ExplorerNavigate(_currentDir);
    }

    // ---- analysis ----
    private async void AnalysisStart_Click(object sender, RoutedEventArgs e)
    {
        if (!RequirePro("Анализ диска")) return;
        string root = AnalysisPath.Text.Trim();
        if (!Directory.Exists(root)) { MessageBox.Show("Укажите существующую папку"); return; }
        AnalysisRows.Clear();
        _analysisCts = new CancellationTokenSource();
        AnalysisStopBtn.IsEnabled = true;
        bool byFiles = AnalysisMode.SelectedIndex == 0;
        SetStatus("Анализ диска запущен...");
        var ct = _analysisCts.Token;
        try
        {
            var res = await Task.Run(() =>
                byFiles
                    ? AnalysisService.FindLargestFiles(root, 300, ct, c => Dispatcher.Invoke(() => AnalysisStatus.Text = $"Объектов: {c}"))
                    : AnalysisService.FindLargestFolders(root, ct, c => Dispatcher.Invoke(() => AnalysisStatus.Text = $"Папок: {c}")), ct);
            foreach (var (sz, p) in res)
                AnalysisRows.Add(new AnalysisRow(p, FormatHelper.HumanSize(sz)));
            AnalysisStatus.Text = $"Готово. Найдено: {res.Count}";
        }
        catch (OperationCanceledException) { AnalysisStatus.Text = "Остановлено"; }
        AnalysisStopBtn.IsEnabled = false;
        SetStatus("Готово");
    }
    private void AnalysisStop_Click(object sender, RoutedEventArgs e) { _analysisCts?.Cancel(); AnalysisStopBtn.IsEnabled = false; }
    private void AnalysisDelete_Click(object sender, RoutedEventArgs e)
    {
        var sel = AnalysisGrid.SelectedItems.Cast<AnalysisRow>().ToList();
        if (sel.Count == 0) return;
        if (MessageBox.Show($"Удалить {sel.Count} объект(ов)?", "Подтверждение", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        foreach (var r in sel)
        {
            try { CleanerService.DeletePath(r.Path, true); AnalysisRows.Remove(r); }
            catch (Exception ex) { MessageBox.Show($"{r.Path}\n{ex.Message}"); }
        }
    }

    // ---- cleaner ----
    private void LoadCleanerCategories()
    {
        CleanerRows.Clear();
        foreach (var c in CleanerService.GetTempCategories())
            CleanerRows.Add(new CleanerRow(c.Id, c.IsPro ? c.Name + "  ·  PRO" : c.Name, c.Desc, c.Path, c.Kind, c.IsPro));
        foreach (var r in CleanerRows) r.PropertyChanged += (_, _) => UpdateCleanerTotal();
    }
    private void UpdateCleanerTotal()
    {
        long t = CleanerRows.Where(r => r.Selected).Sum(r => r.Size);
        CleanerTotal.Text = $"Выбрано: {FormatHelper.HumanSize(t)}";
    }
    private async void CleanerRefresh_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Проверка размеров...");
        await Task.Run(() =>
        {
            foreach (var r in CleanerRows)
            {
                if (r.Kind == "recycle_bin") { r.SizeText = "нажмите «Очистить»"; r.Size = 0; }
                else if (r.Kind == "dns") { r.SizeText = "по кнопке"; r.Size = 0; }
                else if (!string.IsNullOrEmpty(r.Path) && Directory.Exists(r.Path))
                {
                    long sz = CleanerService.GetDirSize(r.Path);
                    r.Size = sz;
                    Dispatcher.Invoke(() => r.SizeText = FormatHelper.HumanSize(sz));
                }
                else { r.SizeText = "папка не найдена"; r.Size = 0; }
            }
        });
        CleanerList.Items.Refresh();
        UpdateCleanerTotal();
        SetStatus("Готово");
    }
    private async void CleanerClean_Click(object sender, RoutedEventArgs e)
    {
        var sel = CleanerRows.Where(r => r.Selected).ToList();
        if (sel.Count == 0) { MessageBox.Show("Отметьте хотя бы одну категорию."); return; }
        if (sel.Any(r => r.IsPro) && !RequirePro("Категории PRO")) return;
        long total = sel.Sum(r => r.Size);
        if (MessageBox.Show($"Будет освобождено примерно {FormatHelper.HumanSize(total)}. Продолжить?", "Подтверждение",
            MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        SetStatus("Очистка...");
        long freed = 0;
        await Task.Run(() =>
        {
            foreach (var r in sel)
            {
                Dispatcher.Invoke(() => CleanerLog.AppendText($"Очистка: {r.Name}...\n"));
                if (r.Kind == "recycle_bin") { try { CleanerService.EmptyRecycleBin(); } catch { } }
                else if (r.Kind == "dns")
                {
                    string res = CleanerService.FlushDns();
                    string msg = $"  {res}\n";
                    Dispatcher.Invoke(() => CleanerLog.AppendText(msg));
                }
                else
                {
                    var (f, _) = CleanerService.ClearFolderContents(r.Path, log: m => Dispatcher.Invoke(() => CleanerLog.AppendText("  " + m + "\n")));
                    freed += f;
                    Dispatcher.Invoke(() => CleanerLog.AppendText($"  Готово: {r.Name} — {FormatHelper.HumanSize(f)}\n"));
                }
            }
        });
        CleanerLog.AppendText($"Итого: {FormatHelper.HumanSize(freed)}\n");
        CleanerLog.ScrollToEnd();
        try { HistoryService.Add(freed, $"Очистка {DateTime.Now:dd.MM HH:mm}: {string.Join(", ", sel.Select(s => s.Name))}"); RefreshHistory(); } catch { }
        SetStatus("Готово");
        CleanerRefresh_Click(sender, e);
        RefreshDisks();
    }

    // ---- safety + history ----
    private void RefreshHistory()
    {
        try
        {
            TotalFreedText.Text = FormatHelper.HumanSize(HistoryService.TotalFreed());
            HistoryList.ItemsSource = HistoryService.Load()
                .OrderByDescending(r => r.Date)
                .Take(20)
                .Select(r => $"{r.Date:dd.MM HH:mm} — {FormatHelper.HumanSize(r.FreedBytes)} — {r.Details}")
                .ToList();
        }
        catch { }
    }

    private void RestorePoint_Click(object sender, RoutedEventArgs e)
    {
        SetStatus("Создание точки восстановления...");
        bool ok = false;
        Task.Run(() => ok = SafetyService.CreateRestorePoint()).ContinueWith(_ =>
            Dispatcher.Invoke(() =>
            {
                MessageBox.Show(ok ? "Точка восстановления создана." : "Не удалось. Запусти программу от администратора.",
                    "Точка восстановления", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
                SetStatus("Готово");
            }));
    }

    private void Whitelist_Click(object sender, RoutedEventArgs e)
    {
        var list = SafetyService.LoadWhitelist();
        string text = string.Join("\n", list);
        var dlg = new Window
        {
            Title = "Белый список — никогда не удалять",
            Width = 560, Height = 420, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this,
            Background = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#12120B"))
        };
        var grid = new Grid { Margin = new Thickness(14) };
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var tb = new TextBox { Text = text, AcceptsReturn = true, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(tb, 0);
        grid.Children.Add(tb);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var okBtn = new Button { Content = "Сохранить", Padding = new Thickness(16, 8, 16, 8), Margin = new Thickness(0, 0, 8, 0) };
        var cancelBtn = new Button { Content = "Отмена", Padding = new Thickness(16, 8, 16, 8) };
        okBtn.Click += (_, _) =>
        {
            var items = tb.Text.Split('\n').Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
            SafetyService.SaveWhitelist(items);
            dlg.DialogResult = true;
            dlg.Close();
        };
        cancelBtn.Click += (_, _) => dlg.Close();
        panel.Children.Add(okBtn);
        panel.Children.Add(cancelBtn);
        Grid.SetRow(panel, 1);
        grid.Children.Add(panel);
        dlg.Content = grid;
        dlg.ShowDialog();
    }

    private async void CleanerPreview_Click(object sender, RoutedEventArgs e)
    {
        if (!RequirePro("Предпросмотр")) return;
        var sel = CleanerRows.Where(r => r.Selected).ToList();
        if (sel.Count == 0) { MessageBox.Show("Отметьте хотя бы одну категорию."); return; }
        SetStatus("Предпросмотр...");
        string report = "";
        await Task.Run(() =>
        {
            var lines = new List<string>();
            foreach (var r in sel)
            {
                if (r.Kind == "recycle_bin") { lines.Add($"{r.Name}: корзина (будет очищена целиком)"); continue; }
                if (r.Kind == "dns") { lines.Add($"{r.Name}: выполнит ipconfig /flushdns"); continue; }
                var items = CleanerService.PreviewFolder(r.Path, 15);
                long sum = items.Sum(x => x.Size);
                lines.Add($"{r.Name} — {FormatHelper.HumanSize(sum)} (топ {items.Count}):");
                foreach (var (p, s, d) in items.Take(10))
                    lines.Add($"  {(d ? "[папка]" : "[файл]")} {FormatHelper.HumanSize(s)}  {p}");
            }
            report = string.Join("\n", lines);
        });
        CleanerLog.AppendText("=== ПРЕДПРОСМОТР (ничего не удалено) ===\n" + report + "\n==========================================\n");
        CleanerLog.ScrollToEnd();
        SetStatus("Готово");
    }

    // ---- startup ----
    private void RefreshStartup() => StartupRefresh_Click(this, new RoutedEventArgs());
    private void StartupRefresh_Click(object sender, RoutedEventArgs e)
    {
        StartupRows.Clear();
        try
        {
            _startupItems = StartupService.List();
            foreach (var i in _startupItems)
                StartupRows.Add(new StartupRow(i.Name, i.Command, i.Enabled ? "Включено" : "Отключено"));
        }
        catch { }
    }
    private void StartupDisable_Click(object sender, RoutedEventArgs e)
    {
        if (!RequirePro("Управление автозагрузкой")) return;
        foreach (var idx in StartupGrid.SelectedItems.Cast<StartupRow>().ToList())
        {
            var item = _startupItems.FirstOrDefault(x => x.Name == idx.Name);
            if (item != null && item.Enabled) try { StartupService.Disable(item); } catch (Exception ex) { MessageBox.Show(ex.Message); }
        }
        RefreshStartup();
    }
    private void StartupEnable_Click(object sender, RoutedEventArgs e)
    {
        if (!RequirePro("Управление автозагрузкой")) return;
        foreach (var idx in StartupGrid.SelectedItems.Cast<StartupRow>().ToList())
        {
            var item = _startupItems.FirstOrDefault(x => x.Name == idx.Name);
            if (item != null && !item.Enabled) try { StartupService.Enable(item); } catch (Exception ex) { MessageBox.Show(ex.Message); }
        }
        RefreshStartup();
    }
    private void SelfAuto_Click(object sender, RoutedEventArgs e)
    {
        try { StartupService.SetSelfAutostart(SelfAuto.IsChecked == true); } catch (Exception ex) { MessageBox.Show(ex.Message); }
    }

    private void RefreshTips()
    {
        TipsList.ItemsSource = DiskService.GenerateTips(DiskService.ListDisks()).Select(t => $"{t.Icon}  {t.Text}").ToList();
    }

    // ---- license ----
    private void RefreshLicense()
    {
        try
        {
            HwidBox.Text = LicenseService.GetHwid();
            var st = LicenseService.Current();
            if (st.IsPro)
            {
                LicenseStatus.Text = "🟡 Pro";
                try { LicenseStatus.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"); } catch { }
                LicenseExpiry.Text = st.Expiry != null ? $"Активен до {st.Expiry:dd.MM.yyyy}" : "Активен";
                LicenseMsg.Text = "";
            }
            else
            {
                LicenseStatus.Text = "🟢 Free";
                try { LicenseStatus.Foreground = (System.Windows.Media.Brush)FindResource("GoodBrush"); } catch { }
                LicenseExpiry.Text = "Pro не активирован";
                if (!string.IsNullOrEmpty(st.Error) && !string.IsNullOrEmpty(LicenseService.Load()))
                    LicenseMsg.Text = st.Error;
            }
        }
        catch { }
    }

    private void LicenseActivate_Click(object sender, RoutedEventArgs e)
    {
        var st = LicenseService.Validate(LicenseKeyBox.Text);
        if (st.IsPro)
        {
            LicenseService.Save(LicenseKeyBox.Text.Trim());
            LicenseMsg.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00FFCC"));
            LicenseMsg.Text = $"✅ Pro активирован{(st.Expiry != null ? $" до {st.Expiry:dd.MM.yyyy}" : "")}!";
        }
        else
        {
            LicenseMsg.Foreground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF3355"));
            LicenseMsg.Text = "❌ " + (st.Error ?? "Неверный ключ");
        }
        RefreshLicense();
    }

    private void LicenseRemove_Click(object sender, RoutedEventArgs e)
    {
        LicenseService.Remove();
        LicenseKeyBox.Text = "";
        RefreshLicense();
    }

    private void HwidCopy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(HwidBox.Text); } catch { }
    }

    // ---- rows ----
    public sealed record DiskRow(string Device, string Info, double Percent);
    public sealed class ExplorerRow : INotifyPropertyChanged
    {
        public string Name { get; } public string Path { get; } public bool IsDir { get; }
        private string _sizeText; public string SizeText { get => _sizeText; set { _sizeText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText))); } }
        public string MTime { get; } public long Size { get; set; }
        public ExplorerRow(string n, string p, bool d, string s, string m, long sz) { Name = n; Path = p; IsDir = d; _sizeText = s; MTime = m; Size = sz; }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    public sealed record AnalysisRow(string Path, string SizeText);
    public sealed class CleanerRow : INotifyPropertyChanged
    {
        public string Id { get; } public string Name { get; } public string Desc { get; } public string? Path { get; } public string Kind { get; }
        public bool IsPro { get; }
        private bool _sel; public bool Selected { get => _sel; set { _sel = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); } }
        private string _sizeText = "не проверено"; public string SizeText { get => _sizeText; set { _sizeText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText))); } }
        public long Size { get; set; }
        public CleanerRow(string id, string n, string d, string? p, string k, bool isPro = false) { Id = id; Name = n; Desc = d; Path = p; Kind = k; IsPro = isPro; }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    public sealed record StartupRow(string Name, string Command, string Status);
}
