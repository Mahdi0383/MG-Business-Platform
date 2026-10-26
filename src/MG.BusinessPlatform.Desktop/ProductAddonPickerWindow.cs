using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Desktop;

public sealed class ProductAddonPickerWindow : Window
{
    private readonly ObservableCollection<ProductAddonPickerRow> _rows;

    public IReadOnlyList<ProductAddonSnapshot> SelectedAddons => _rows
        .Where(row => row.IsSelected)
        .Select(row => new ProductAddonSnapshot(row.AddonId, row.Name, row.Price))
        .ToArray();

    public ProductAddonPickerWindow(
        string productName,
        IReadOnlyCollection<ProductAddonRecord> availableAddons,
        IReadOnlyCollection<ProductAddonSnapshot> selectedAddons)
    {
        Title = $"افزودنی‌های {productName}";
        Width = 500;
        Height = 520;
        MinWidth = 420;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        Background = new SolidColorBrush(Color.FromRgb(245, 247, 250));
        FlowDirection = FlowDirection.RightToLeft;

        var selectedIds = selectedAddons.Select(addon => addon.AddonId).ToHashSet();
        _rows = new ObservableCollection<ProductAddonPickerRow>(availableAddons
            .Where(addon => addon.IsActive)
            .OrderBy(addon => addon.Name)
            .Select(addon => new ProductAddonPickerRow
            {
                AddonId = addon.Id,
                Name = addon.Name,
                Price = addon.Price,
                IsSelected = selectedIds.Contains(addon.Id)
            }));

        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleText = new TextBlock
        {
            Text = $"افزودنی‌های قابل انتخاب برای «{productName}»",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        };
        Grid.SetRow(titleText, 0);
        root.Children.Add(titleText);

        var grid = new DataGrid
        {
            ItemsSource = _rows,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            IsReadOnly = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            Background = Brushes.White,
            Margin = new Thickness(0, 0, 0, 10)
        };
        grid.Columns.Add(new DataGridCheckBoxColumn
        {
            Header = "انتخاب",
            Binding = new Binding(nameof(ProductAddonPickerRow.IsSelected))
            {
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            },
            Width = new DataGridLength(70)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "نام افزودنی",
            Binding = new Binding(nameof(ProductAddonPickerRow.Name)),
            IsReadOnly = true,
            Width = new DataGridLength(2, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = "قیمت افزوده",
            Binding = new Binding(nameof(ProductAddonPickerRow.Price)) { StringFormat = "N2" },
            IsReadOnly = true,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        Grid.SetRow(grid, 1);
        root.Children.Add(grid);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var saveButton = new Button { Content = "ذخیره انتخاب", MinWidth = 120, Margin = new Thickness(4), IsDefault = true };
        saveButton.Click += (_, _) => DialogResult = true;
        var cancelButton = new Button { Content = "انصراف", MinWidth = 90, Margin = new Thickness(4), IsCancel = true };
        cancelButton.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(saveButton);
        buttons.Children.Add(cancelButton);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
    }

    private sealed class ProductAddonPickerRow
    {
        public Guid AddonId { get; init; }
        public string Name { get; init; } = string.Empty;
        public decimal Price { get; init; }
        public bool IsSelected { get; set; }
    }
}
