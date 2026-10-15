using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NexoHub.Controls;

/// <summary>
/// Clickable key map taken from the AK820 Pro driver's layout file.
/// Colors shown here are Nexo-local profile overrides; this control never talks to HID.
/// </summary>
public sealed class AjazzKeyboardMap : UserControl
{
    private static readonly KeyDefinition[] Layout = LoadLayout();
    private readonly Canvas _canvas;

    public static readonly DependencyProperty KeyColorsProperty = DependencyProperty.Register(
        nameof(KeyColors), typeof(Dictionary<string, string>), typeof(AjazzKeyboardMap),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnKeyColorsChanged));

    public static readonly DependencyProperty SelectedKeyCodeProperty = DependencyProperty.Register(
        nameof(SelectedKeyCode), typeof(string), typeof(AjazzKeyboardMap), new FrameworkPropertyMetadata("0x29"));

    public static readonly DependencyProperty SelectedKeyNameProperty = DependencyProperty.Register(
        nameof(SelectedKeyName), typeof(string), typeof(AjazzKeyboardMap), new FrameworkPropertyMetadata("Esc"));

    public Dictionary<string, string>? KeyColors
    {
        get => (Dictionary<string, string>?)GetValue(KeyColorsProperty);
        set => SetValue(KeyColorsProperty, value);
    }

    public string SelectedKeyCode
    {
        get => (string)GetValue(SelectedKeyCodeProperty);
        private set => SetCurrentValue(SelectedKeyCodeProperty, value);
    }

    public string SelectedKeyName
    {
        get => (string)GetValue(SelectedKeyNameProperty);
        private set => SetCurrentValue(SelectedKeyNameProperty, value);
    }

    public AjazzKeyboardMap()
    {
        _canvas = new Canvas
        {
            Width = 800,
            Height = 300,
            Background = new SolidColorBrush(Color.FromRgb(28, 39, 45))
        };
        Content = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = _canvas };
        Loaded += (_, _) => RefreshColors();
    }

    public void RefreshColors()
    {
        _canvas.Children.Clear();
        foreach (var key in Layout)
        {
            var selected = key.Code.Equals(SelectedKeyCode, StringComparison.OrdinalIgnoreCase);
            var border = new Border
            {
                Width = key.Width,
                Height = key.Height,
                CornerRadius = new CornerRadius(3),
                Background = ColorFor(key.Code),
                BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(83, 214, 194)) : new SolidColorBrush(Color.FromRgb(108, 126, 136)),
                BorderThickness = selected ? new Thickness(2.5) : new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = $"{key.Name} · HID {key.Code}",
                Child = new TextBlock
                {
                    Text = key.Name,
                    Foreground = Brushes.White,
                    FontSize = Math.Clamp(Math.Min(key.Width, key.Height) * 0.32, 6, 10),
                    TextAlignment = TextAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(1, 0, 1, 0)
                }
            };
            AutomationProperties.SetName(border, $"Tecla {key.Name}, código HID {key.Code}");
            Canvas.SetLeft(border, key.Left);
            Canvas.SetTop(border, key.Top);
            border.MouseLeftButtonDown += (_, args) =>
            {
                SelectedKeyCode = key.Code;
                SelectedKeyName = key.Name;
                RefreshColors();
                args.Handled = true;
            };
            _canvas.Children.Add(border);
        }
    }

    private static void OnKeyColorsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        if (sender is AjazzKeyboardMap map && map.IsLoaded) map.RefreshColors();
    }

    private Brush ColorFor(string keyCode)
    {
        if (KeyColors is not null && KeyColors.TryGetValue(keyCode, out var hex) &&
            System.Text.RegularExpressions.Regex.IsMatch(hex, "^[0-9A-Fa-f]{6}$") &&
            ColorConverter.ConvertFromString("#" + hex) is Color color)
            return new SolidColorBrush(color);

        return new SolidColorBrush(Color.FromRgb(75, 89, 98));
    }

    private static KeyDefinition[] LoadLayout()
    {
        using var stream = typeof(AjazzKeyboardMap).Assembly.GetManifestResourceStream("NexoHub.Assets.ajazz-ak820pro-layout.json")
            ?? throw new InvalidDataException("No se encontró el mapa local de teclas del AK820 Pro.");
        var keys = JsonSerializer.Deserialize<KeyDefinition[]>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("El mapa local de teclas del AK820 Pro está vacío.");
        if (keys.Length != 81 || keys.Any(key => string.IsNullOrWhiteSpace(key.Name) || string.IsNullOrWhiteSpace(key.Code)))
            throw new InvalidDataException("El mapa local de teclas del AK820 Pro no coincide con las 81 teclas esperadas.");
        return keys;
    }

    private sealed record KeyDefinition(string Name, string Code, double Left, double Top, double Width, double Height);
}
