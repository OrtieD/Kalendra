using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Kalendra.Views;

/// <summary>Tiny modal dialogs: ask for a text, or ask yes/no.</summary>
public static class TextPrompt
{
    public static async Task<string?> Ask(Window owner, string title, string question, string initial)
    {
        var box = new TextBox { Text = initial, Width = 280 };
        var w = Dialog(title, question, box, "OK", out var ok, out var cancel);
        string? result = null;
        ok.Click += (_, _) => { result = box.Text; w.Close(); };
        cancel.Click += (_, _) => w.Close();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { result = box.Text; w.Close(); } };
        w.Opened += (_, _) => { box.Focus(); box.SelectAll(); };
        await w.ShowDialog(owner);
        return result;
    }

    public static async Task<bool> Confirm(Window owner, string title, string question)
    {
        var w = Dialog(title, question, null, "Löschen", out var ok, out var cancel);
        var result = false;
        ok.Click += (_, _) => { result = true; w.Close(); };
        cancel.Click += (_, _) => w.Close();
        await w.ShowDialog(owner);
        return result;
    }

    public static async Task Info(Window owner, string title, string message)
    {
        var w = Dialog(title, message, null, "OK", out var ok, out var cancel);
        cancel.IsVisible = false;
        ok.Click += (_, _) => w.Close();
        await w.ShowDialog(owner);
    }

    /// <summary>Returns 0 or 1 for the chosen option, -1 for cancel.</summary>
    public static async Task<int> Choose(Window owner, string title, string question, string first, string second)
    {
        var w = Dialog(title, question, null, first, out var ok, out var cancel);
        var other = new Button { Content = second };
        ((StackPanel)ok.Parent!).Children.Insert(1, other);
        var result = -1;
        ok.Click += (_, _) => { result = 0; w.Close(); };
        other.Click += (_, _) => { result = 1; w.Close(); };
        cancel.Click += (_, _) => w.Close();
        await w.ShowDialog(owner);
        return result;
    }

    static Window Dialog(string title, string question, Control? input, string okText, out Button ok, out Button cancel)
    {
        ok = new Button { Content = okText, IsDefault = true, Classes = { "accent" } };
        cancel = new Button { Content = "Abbrechen", IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(ok);
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = question, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 320 });
        if (input != null) panel.Children.Add(input);
        panel.Children.Add(buttons);
        return new Window
        {
            Title = title, Content = panel, SizeToContent = SizeToContent.WidthAndHeight, CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false,
        };
    }
}
