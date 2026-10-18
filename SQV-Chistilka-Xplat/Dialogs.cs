using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace SqvChistilka;

// Минимальные модальные диалоги в фирменной тёмной теме (без внешних пакетов).
public static class Dialogs
{
    private static readonly Color BgAlt = Color.Parse("#12120B");
    private static readonly Color Accent = Color.Parse("#Eaff00");

    private static Window Build(Window owner, string title, Control body, params Button[] buttons)
    {
        var panel = new StackPanel { Spacing = 12, Margin = new Thickness(18) };
        panel.Children.Add(body);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var b in buttons) row.Children.Add(b);
        panel.Children.Add(row);
        var w = new Window
        {
            Title = title,
            Content = panel,
            Width = 480,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(BgAlt),
        };
        return w;
    }

    private static Button Btn(string text, bool accent)
    {
        return new Button
        {
            Content = text,
            Padding = new Thickness(16, 8, 16, 8),
            Background = accent ? new SolidColorBrush(Accent) : new SolidColorBrush(Color.Parse("#181810")),
            Foreground = accent ? Brushes.Black : Brushes.White,
            FontWeight = FontWeight.Bold,
            CornerRadius = new CornerRadius(8),
        };
    }

    public static async Task Info(Window owner, string title, string text)
    {
        var tcs = new TaskCompletionSource();
        var ok = Btn("OK", true);
        var w = Build(owner, title, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, ok);
        ok.Click += (_, _) => { w.Close(); tcs.SetResult(); };
        await w.ShowDialog<object?>(owner).ContinueWith(_ => tcs.TrySetResult());
        await tcs.Task;
    }

    public static async Task<bool> Confirm(Window owner, string title, string text)
    {
        var yes = Btn("Да", true);
        var no = Btn("Нет", false);
        var w = Build(owner, title, new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, yes, no);
        var tcs = new TaskCompletionSource<bool>();
        yes.Click += (_, _) => { w.Close(true); };
        no.Click += (_, _) => { w.Close(false); };
        var r = await w.ShowDialog<bool?>(owner);
        tcs.SetResult(r == true);
        return await tcs.Task;
    }

    public static async Task<string?> TextInput(Window owner, string title, string prompt, string initial = "")
    {
        var tb = new TextBox { Text = initial, AcceptsReturn = true, MinHeight = 120 };
        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
        body.Children.Add(tb);
        var save = Btn("Сохранить", true);
        var cancel = Btn("Отмена", false);
        var w = Build(owner, title, body, save, cancel);
        save.Click += (_, _) => { w.Close(tb.Text); };
        cancel.Click += (_, _) => { w.Close(null); };
        return await w.ShowDialog<string?>(owner);
    }
}
