using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
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

    private readonly Dictionary<string, Grid> _pages = new();
    private readonly TranslateTransform _slide = new();
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#Eaff00"));
    private static readonly IBrush GoodBrush = new SolidColorBrush(Color.Parse("#00FFCC"));
    private static readonly IBrush MutedBrush = new SolidColorBrush(Color.Parse("#8C8C80"));
    private static readonly IBrush CardBrush = new SolidColorBrush(Color.Parse("#181810"));

    public MainWindow()
    {
        InitializeComponent();
        DisksList.ItemsSource = DiskRows;
        ExplorerGrid.ItemsSource = ExplorerRows;
        AnalysisGrid.ItemsSource = AnalysisRows;
        CleanerList.ItemsSource = CleanerRows;
        StartupGrid.ItemsSource = StartupRows;
        _pages["dashboard"] = PageDashboard;
        _pages["explorer"] = PageExplorer;
        _pages["analysis"] = PageAnalysis;
        _pages["cleaner"] = PageCleaner;
        _pages["startup"] = PageStartup;
        _pages["tips"] = PageTips;
        _pages["license"] = PageLicense;
        _activePage = PageDashboard;
        ContentHost.RenderTransform = _slide;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (string.IsNullOrEmpty(AnalysisPath.Text)) AnalysisPath.Text = Path.GetPathRoot(Environment.CurrentDirectory) ?? "/";
        RefreshDisks();
        ExplorerNavigate(_currentDir);
        LoadCleanerCategories();
        RefreshStartup();
        RefreshTips();
        RefreshHistory();
        RefreshLicense();
        try { SelfAuto.IsChecked = StartupService.IsSelfAutostart(); } catch { }
        HighlightNav("dashboard");
        // GPU fade-in
        _ = FadeIn();
    }

    private async Task FadeIn()
    {
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(420),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 1d) } },
            }
        };
        Opacity = 0;
        await anim.RunAsync(this);
    }

    private void SetStatus(string t) => StatusText.Text = t;
    private void Ui(Action a) => Avalonia.Threading.Dispatcher.UIThread.Post(a);

    // ---- навигация с GPU-анимацией (fade + slide, FillMode.Forward фиксирует конец) ----
    private void Nav_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string id) return;
        var order = new[] { "dashboard", "explorer", "analysis", "cleaner", "startup", "tips", "license" };
        var next = _pages[id];
        if (next == _activePage) return;
        string curKey = _pages.First(k => k.Value == _activePage).Key;
        int dir = Array.IndexOf(order, id) > Array.IndexOf(order, curKey) ? 1 : -1;
        HighlightNav(id);
        SwitchTo(next, dir);
    }

    private void HighlightNav(string id)
    {
        if (BtnDashboard.Parent is not StackPanel panel) return;
        foreach (var child in panel.Children.OfType<Button>())
        {
            bool active = (string?)child.Tag == id;
            child.Background = active ? CardBrush : Brushes.Transparent;
            child.Foreground = active ? AccentBrush : MutedBrush;
        }
    }

    private void GoLicense()
    {
        var next = _pages["license"];
        if (next == _activePage) return;
        HighlightNav("license");
        SwitchTo(next, 1);
    }

    private void SwitchTo(Grid next, int direction)
    {
        var prev = _activePage;
        _activePage = next;
        if (prev != null) prev.IsVisible = false;
        next.IsVisible = true;
        _ = AnimateSwitch(direction);
    }

    private async Task AnimateSwitch(int direction)
    {
        var fade = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(260),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(OpacityProperty, 1d) } },
            }
        };
        var slide = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(260),
            FillMode = FillMode.Forward,
            Easing = new Avalonia.Animation.Easings.CubicEaseInOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0d), Setters = { new Setter(TranslateTransform.XProperty, 60d * direction) } },
                new KeyFrame { Cue = new Cue(1d), Setters = { new Setter(TranslateTransform.XProperty, 0d) } },
            }
        };
        ContentHost.Opacity = 0;
        await Task.WhenAll(fade.RunAsync(ContentHost), slide.RunAsync(_slide));
        ContentHost.Opacity = 1;
        _slide.X = 0;
    }

    private bool IsPro() => LicenseService.Current().IsPro;

    private async Task<bool> RequirePro(string feature)
    {
        if (IsPro()) return true;
        await Dialogs.Info(this, "Нужен Pro", $"{feature} доступно в Pro.\nОткрой вкладку «Лицензия» и введи ключ из бота.");
        GoLicense();
        return false;
    }

    // ---- dashboard ----
    private void RefreshDisks_Click(object? sender, RoutedEventArgs e) { RefreshDisks(); RefreshTips(); }
    private void RefreshDisks()
    {
        DiskRows.Clear();
        foreach (var d in DiskService.ListDisks())
            DiskRows.Add(new DiskRow(d.Device, $"{FormatHelper.HumanSize(d.Used)} занято из {FormatHelper.HumanSize(d.Total)} ({d.Percent:F0}%)", d.Percent));
    }

    private async void QuickScan_Click(object? sender, RoutedEventArgs e)
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
                Ui(() => QuickProgress.Value = (idx + 1) / (double)Math.Max(cats.Count, 1));
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

    private async void QuickClean_Click(object? sender, RoutedEventArgs e)
    {
        if (!await Dialogs.Confirm(this, "Подтверждение", $"Будут очищены временные файлы (~{FormatHelper.HumanSize(_quickTotal)}). Продолжить?")) return;
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
        await Dialogs.Info(this, "Готово", $"Освобождено: {FormatHelper.HumanSize(freed)}");
        try { HistoryService.Add(freed, $"Быстрая очистка {DateTime.Now:dd.MM HH:mm}"); RefreshHistory(); } catch { }
        QuickCleanBtn.IsEnabled = false;
        QuickResult.Text = "Нажмите «Быстрое сканирование мусора», чтобы проверить снова.";
        RefreshDisks();
        SetStatus("Готово");
    }

    // ---- explorer ----
    private void ExplorerGo_Click(object? sender, RoutedEventArgs e) => ExplorerNavigate(ExplorerPath.Text?.Trim() ?? "");
    private void ExplorerHome_Click(object? sender, RoutedEventArgs e) => ExplorerNavigate(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    private void ExplorerUp_Click(object? sender, RoutedEventArgs e)
    {
        var parent = Directory.GetParent(_currentDir.TrimEnd(Path.DirectorySeparatorChar));
        if (parent != null) ExplorerNavigate(parent.FullName);
    }
    private async void ExplorerBrowse_Click(object? sender, RoutedEventArgs e)
    {
        var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (sp == null) return;
        var res = await sp.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { AllowMultiple = false });
        if (res.Count > 0) ExplorerNavigate(res[0].Path.LocalPath);
    }
    private void ExplorerGrid_DblTap(object? sender, TappedEventArgs e)
    {
        if (ExplorerGrid.SelectedItem is ExplorerRow r && r.IsDir) ExplorerNavigate(r.Path);
    }
    private void ExplorerNavigate(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) path = _currentDir;
        if (!Directory.Exists(path)) { _ = Dialogs.Info(this, "Ошибка", $"Папка не найдена:\n{path}"); return; }
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
        catch (Exception ex) { _ = Dialogs.Info(this, "Ошибка", ex.Message); }
        ExplorerInfo.Text = $"Объектов: {ExplorerRows.Count}";
    }
    private async void ExplorerSizes_Click(object? sender, RoutedEventArgs e)
    {
        SetStatus("Подсчёт размеров папок...");
        var rows = ExplorerRows.Where(r => r.IsDir).ToList();
        await Task.Run(() =>
        {
            foreach (var r in rows)
            {
                long sz = CleanerService.GetDirSize(r.Path);
                r.Size = sz;
                Ui(() => r.SizeText = FormatHelper.HumanSize(sz));
            }
        });
        SetStatus("Готово");
    }
    private async void ExplorerDelete_Click(object? sender, RoutedEventArgs e)
    {
        var sel = ExplorerGrid.SelectedItems?.Cast<ExplorerRow>().ToList() ?? new();
        if (sel.Count == 0) return;
        if (!await Dialogs.Confirm(this, "Подтверждение", $"Удалить {sel.Count} объект(ов) в корзину?")) return;
        foreach (var r in sel)
            try { CleanerService.DeletePath(r.Path, true); } catch (Exception ex) { await Dialogs.Info(this, "Ошибка", $"{r.Path}\n{ex.Message}"); }
        ExplorerNavigate(_currentDir);
    }

    // ---- analysis ----
    private async void AnalysisStart_Click(object? sender, RoutedEventArgs e)
    {
        if (!await RequirePro("Анализ диска")) return;
        string root = AnalysisPath.Text?.Trim() ?? "";
        if (!Directory.Exists(root)) { await Dialogs.Info(this, "Ошибка", "Укажите существующую папку"); return; }
        AnalysisRows.Clear();
        _analysisCts = new CancellationTokenSource();
        AnalysisStopBtn.IsEnabled = true;
        bool byFiles = ModeFiles.IsChecked != false;
        SetStatus("Анализ диска запущен...");
        var ct = _analysisCts.Token;
        try
        {
            var res = await Task.Run(() =>
                byFiles
                    ? AnalysisService.FindLargestFiles(root, 300, ct, c => Ui(() => AnalysisStatus.Text = $"Объектов: {c}"))
                    : AnalysisService.FindLargestFolders(root, ct, c => Ui(() => AnalysisStatus.Text = $"Папок: {c}")), ct);
            foreach (var (sz, p) in res)
                AnalysisRows.Add(new AnalysisRow(p, FormatHelper.HumanSize(sz)));
            AnalysisStatus.Text = $"Готово. Найдено: {res.Count}";
        }
        catch (OperationCanceledException) { AnalysisStatus.Text = "Остановлено"; }
        AnalysisStopBtn.IsEnabled = false;
        SetStatus("Готово");
    }
    private void AnalysisStop_Click(object? sender, RoutedEventArgs e) { _analysisCts?.Cancel(); AnalysisStopBtn.IsEnabled = false; }
    private async void AnalysisBrowse_Click(object? sender, RoutedEventArgs e)
    {
        var sp = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (sp == null) return;
        var res = await sp.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { AllowMultiple = false });
        if (res.Count > 0) AnalysisPath.Text = res[0].Path.LocalPath;
    }
    private async void AnalysisDelete_Click(object? sender, RoutedEventArgs e)
    {
        var sel = AnalysisGrid.SelectedItems?.Cast<AnalysisRow>().ToList() ?? new();
        if (sel.Count == 0) return;
        if (!await Dialogs.Confirm(this, "Подтверждение", $"Удалить {sel.Count} объект(ов)?")) return;
        foreach (var r in sel)
        {
            try { CleanerService.DeletePath(r.Path, true); AnalysisRows.Remove(r); }
            catch (Exception ex) { await Dialogs.Info(this, "Ошибка", $"{r.Path}\n{ex.Message}"); }
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
    private async void CleanerRefresh_Click(object? sender, RoutedEventArgs e)
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
                    Ui(() => r.SizeText = FormatHelper.HumanSize(sz));
                }
                else { r.SizeText = "папка не найдена"; r.Size = 0; }
            }
        });
        UpdateCleanerTotal();
        SetStatus("Готово");
    }
    private async void CleanerClean_Click(object? sender, RoutedEventArgs e)
    {
        var sel = CleanerRows.Where(r => r.Selected).ToList();
        if (sel.Count == 0) { await Dialogs.Info(this, "Очистка", "Отметьте хотя бы одну категорию."); return; }
        if (sel.Any(r => r.IsPro) && !await RequirePro("Категории PRO")) return;
        long total = sel.Sum(r => r.Size);
        if (!await Dialogs.Confirm(this, "Подтверждение", $"Будет освобождено примерно {FormatHelper.HumanSize(total)}. Продолжить?")) return;
        SetStatus("Очистка...");
        long freed = 0;
        await Task.Run(() =>
        {
            foreach (var r in sel)
            {
                Ui(() => CleanerLog.Text += $"Очистка: {r.Name}...\n");
                if (r.Kind == "recycle_bin") { try { CleanerService.EmptyRecycleBin(); } catch { } }
                else if (r.Kind == "dns")
                {
                    string res = CleanerService.FlushDns();
                    Ui(() => CleanerLog.Text += $"  {res}\n");
                }
                else
                {
                    var (f, _) = CleanerService.ClearFolderContents(r.Path, log: m => Ui(() => CleanerLog.Text += "  " + m + "\n"));
                    freed += f;
                    Ui(() => CleanerLog.Text += $"  Готово: {r.Name} — {FormatHelper.HumanSize(f)}\n");
                }
            }
        });
        CleanerLog.Text += $"Итого: {FormatHelper.HumanSize(freed)}\n";
        try { HistoryService.Add(freed, $"Очистка {DateTime.Now:dd.MM HH:mm}: {string.Join(", ", sel.Select(s => s.Name))}"); RefreshHistory(); } catch { }
        SetStatus("Готово");
        CleanerRefresh_Click(sender, e);
        RefreshDisks();
    }
    private async void CleanerPreview_Click(object? sender, RoutedEventArgs e)
    {
        if (!await RequirePro("Предпросмотр")) return;
        var sel = CleanerRows.Where(r => r.Selected).ToList();
        if (sel.Count == 0) { await Dialogs.Info(this, "Очистка", "Отметьте хотя бы одну категорию."); return; }
        SetStatus("Предпросмотр...");
        string report = "";
        await Task.Run(() =>
        {
            var lines = new List<string>();
            foreach (var r in sel)
            {
                if (r.Kind == "recycle_bin") { lines.Add($"{r.Name}: корзина (будет очищена целиком)"); continue; }
                if (r.Kind == "dns") { lines.Add($"{r.Name}: выполнит сброс DNS"); continue; }
                var items = CleanerService.PreviewFolder(r.Path, 15);
                long sum = items.Sum(x => x.Size);
                lines.Add($"{r.Name} — {FormatHelper.HumanSize(sum)} (топ {items.Count}):");
                foreach (var (p, s, d) in items.Take(10))
                    lines.Add($"  {(d ? "[папка]" : "[файл]")} {FormatHelper.HumanSize(s)}  {p}");
            }
            report = string.Join("\n", lines);
        });
        CleanerLog.Text += "=== ПРЕДПРОСМОТР (ничего не удалено) ===\n" + report + "\n==========================================\n";
        SetStatus("Готово");
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

    private async void RestorePoint_Click(object? sender, RoutedEventArgs e)
    {
        if (!PlatformHelper.IsWindows)
        {
            await Dialogs.Info(this, "Точка восстановления", "Точек восстановления вне Windows нет.\nНа Linux/macOS удаление идёт через корзину системы — файлы можно вернуть оттуда.");
            return;
        }
        SetStatus("Создание точки восстановления...");
        bool ok = await Task.Run(() => SafetyService.CreateRestorePoint());
        await Dialogs.Info(this, "Точка восстановления",
            ok ? "Точка восстановления создана." : "Не удалось. Запусти программу от администратора.");
        SetStatus("Готово");
    }

    private async void Whitelist_Click(object? sender, RoutedEventArgs e)
    {
        var list = SafetyService.LoadWhitelist();
        string? text = await Dialogs.TextInput(this, "Белый список — никогда не удалять",
            "По одному пути на строку:", string.Join("\n", list));
        if (text == null) return;
        SafetyService.SaveWhitelist(text.Split('\n').Select(s => s.Trim()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList());
    }

    // ---- startup ----
    private void RefreshStartup() => StartupRefresh_Click(null, new RoutedEventArgs());
    private void StartupRefresh_Click(object? sender, RoutedEventArgs e)
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
    private async void StartupDisable_Click(object? sender, RoutedEventArgs e)
    {
        if (!await RequirePro("Управление автозагрузкой")) return;
        foreach (var idx in (StartupGrid.SelectedItems?.Cast<StartupRow>().ToList() ?? new()))
        {
            var item = _startupItems.FirstOrDefault(x => x.Name == idx.Name);
            if (item != null && item.Enabled) try { StartupService.Disable(item); } catch (Exception ex) { await Dialogs.Info(this, "Ошибка", ex.Message); }
        }
        RefreshStartup();
    }
    private async void StartupEnable_Click(object? sender, RoutedEventArgs e)
    {
        if (!await RequirePro("Управление автозагрузкой")) return;
        foreach (var idx in (StartupGrid.SelectedItems?.Cast<StartupRow>().ToList() ?? new()))
        {
            var item = _startupItems.FirstOrDefault(x => x.Name == idx.Name);
            if (item != null && !item.Enabled) try { StartupService.Enable(item); } catch (Exception ex) { await Dialogs.Info(this, "Ошибка", ex.Message); }
        }
        RefreshStartup();
    }
    private async void SelfAuto_Click(object? sender, RoutedEventArgs e)
    {
        try { StartupService.SetSelfAutostart(SelfAuto.IsChecked == true); }
        catch (Exception ex) { await Dialogs.Info(this, "Ошибка", ex.Message); }
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
                LicenseStatus.Foreground = AccentBrush;
                LicenseExpiry.Text = st.Expiry != null ? $"Активен до {st.Expiry:dd.MM.yyyy}" : "Активен";
                LicenseMsg.Text = "";
            }
            else
            {
                LicenseStatus.Text = "🟢 Free";
                LicenseStatus.Foreground = GoodBrush;
                LicenseExpiry.Text = "Pro не активирован";
                if (!string.IsNullOrEmpty(st.Error) && !string.IsNullOrEmpty(LicenseService.Load()))
                    LicenseMsg.Text = st.Error;
            }
        }
        catch { }
    }

    private void LicenseActivate_Click(object? sender, RoutedEventArgs e)
    {
        var st = LicenseService.Validate(LicenseKeyBox.Text);
        if (st.IsPro)
        {
            LicenseService.Save(LicenseKeyBox.Text?.Trim() ?? "");
            LicenseMsg.Foreground = new SolidColorBrush(Color.Parse("#00FFCC"));
            LicenseMsg.Text = $"✅ Pro активирован{(st.Expiry != null ? $" до {st.Expiry:dd.MM.yyyy}" : "")}!";
        }
        else
        {
            LicenseMsg.Foreground = new SolidColorBrush(Color.Parse("#FF3355"));
            LicenseMsg.Text = "❌ " + (st.Error ?? "Неверный ключ");
        }
        RefreshLicense();
    }

    private void LicenseRemove_Click(object? sender, RoutedEventArgs e)
    {
        LicenseService.Remove();
        LicenseKeyBox.Text = "";
        RefreshLicense();
    }

    private void HwidCopy_Click(object? sender, RoutedEventArgs e)
    {
        try { TopLevel.GetTopLevel(this)?.Clipboard?.SetTextAsync(HwidBox.Text ?? ""); } catch { }
    }

    // ---- rows ----
    public sealed record DiskRow(string Device, string Info, double Percent);
    public sealed class ExplorerRow : INotifyPropertyChanged
    {
        public string Name { get; }
        public string Path { get; }
        public bool IsDir { get; }
        private string _sizeText;
        public string SizeText { get => _sizeText; set { _sizeText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText))); } }
        public string MTime { get; }
        public long Size { get; set; }
        public ExplorerRow(string n, string p, bool d, string s, string m, long sz) { Name = n; Path = p; IsDir = d; _sizeText = s; MTime = m; Size = sz; }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    public sealed record AnalysisRow(string Path, string SizeText);
    public sealed class CleanerRow : INotifyPropertyChanged
    {
        public string Id { get; }
        public string Name { get; }
        public string Desc { get; }
        public string? Path { get; }
        public string Kind { get; }
        public bool IsPro { get; }
        private bool _sel;
        public bool Selected { get => _sel; set { _sel = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected))); } }
        private string _sizeText = "не проверено";
        public string SizeText { get => _sizeText; set { _sizeText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SizeText))); } }
        public long Size { get; set; }
        public CleanerRow(string id, string n, string d, string? p, string k, bool isPro = false) { Id = id; Name = n; Desc = d; Path = p; Kind = k; IsPro = isPro; }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
    public sealed record StartupRow(string Name, string Command, string Status);
}
