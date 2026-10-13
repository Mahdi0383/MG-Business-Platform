using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MG.BusinessPlatform.Desktop.Services;

public static class ConfirmationDialog
{
    public static bool Confirm(Window owner, string title, string message, string confirmText = "تأیید")
    {
        var window = new Window
        {
            Title = title,
            Owner = owner,
            Width = 480,
            Height = 230,
            MinWidth = 420,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FlowDirection = FlowDirection.RightToLeft,
            Background = new SolidColorBrush(Color.FromRgb(243, 245, 249)),
            ShowInTaskbar = false
        };

        var layout = new Grid { Margin = new Thickness(18) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 36, 59)),
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(heading, 0);

        var body = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.FromRgb(51, 65, 85))
        };
        Grid.SetRow(body, 1);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var confirm = new Button { Content = confirmText, MinWidth = 100, Margin = new Thickness(4) };
        confirm.Click += (_, _) => window.DialogResult = true;
        var cancel = new Button { Content = "انصراف", MinWidth = 90, Margin = new Thickness(4) };
        cancel.Click += (_, _) => window.DialogResult = false;
        buttons.Children.Add(confirm);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 2);

        layout.Children.Add(heading);
        layout.Children.Add(body);
        layout.Children.Add(buttons);
        window.Content = layout;
        window.Loaded += (_, _) => confirm.Focus();
        return window.ShowDialog() == true;
    }
}
