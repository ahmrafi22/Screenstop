using System.Windows;
using System.Windows.Controls;

namespace Screendrop.App.Controls;

/// <summary>
/// Supplies hint text for an empty <see cref="TextBox"/>. WPF has no native
/// placeholder, so the field templates reserve a named hint element and this
/// helper fills it once the template is applied, then shows or hides it as the
/// user types.
/// </summary>
public static class Placeholder
{
    /// <summary>Name of the hint element each field template reserves.</summary>
    private const string HintElement = "PlaceholderText";

    /// <summary>The hint shown while the field is empty. Ignored when blank.</summary>
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",
        typeof(string),
        typeof(Placeholder),
        new PropertyMetadata(string.Empty));

    public static void SetText(DependencyObject element, string? value)
    {
        element.SetValue(TextProperty, value);
        if (element is TextBox box)
        {
            Attach(box);
        }
    }

    public static string? GetText(DependencyObject element) => (string?)element.GetValue(TextProperty);

    private static void Attach(TextBox box)
    {
        if (box.IsLoaded)
        {
            Hook(box);
        }
        else
        {
            box.Loaded += (_, _) => Hook(box);
        }
    }

    private static void Hook(TextBox box)
    {
        if (box.Template?.FindName(HintElement, box) is not TextBlock hint)
        {
            return;
        }

        hint.Text = GetText(box) ?? string.Empty;
        void Update() =>
            hint.Visibility = string.IsNullOrEmpty(box.Text) ? Visibility.Visible : Visibility.Collapsed;

        box.TextChanged += OnTextChanged;
        Update();

        void OnTextChanged(object sender, TextChangedEventArgs e) => Update();
    }
}
