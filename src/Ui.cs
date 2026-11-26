using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskMate
{
    public static class Ui
    {
        public const string Ink = "#253D34", Muted = "#59685E", Green = "#315E48", Paper = "#FAF8F2";
        static readonly Dictionary<string, SolidColorBrush> brushes = new Dictionary<string, SolidColorBrush>();
        public static SolidColorBrush Color(string hex)
        {
            SolidColorBrush result;
            if (!brushes.TryGetValue(hex, out result))
            {
                result = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
                result.Freeze();
                brushes[hex] = result;
            }
            return result;
        }
        public static TextBlock Text(string text, double size, string color)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = Color(color), TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center };
        }
        public static TextBlock Heading(string text, double size)
        {
            TextBlock label = Text(text, size, Ink); label.FontWeight = FontWeights.SemiBold; return label;
        }
        public static StackPanel Stack(params UIElement[] items)
        {
            StackPanel panel = new StackPanel();
            foreach (UIElement item in items) if (item != null) panel.Children.Add(item);
            return panel;
        }
        public static WrapPanel Row(params UIElement[] items)
        {
            WrapPanel panel = new WrapPanel();
            foreach (UIElement item in items) if (item != null) panel.Children.Add(item);
            return panel;
        }
        public static Border Card(UIElement content)
        {
            return new Border { Background = Color("#FFFDF8"), BorderBrush = Color("#E1DFD2"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(14), Padding = new Thickness(19,16,19,16), Margin = new Thickness(0,0,0,12), Child = content };
        }
        public static TextBox Input(string value)
        {
            return new TextBox { Text = value ?? "", FontSize = 14, Padding = new Thickness(9,6,9,6), Background = Color("#FFFDF9"),
                BorderBrush = Color("#B8BFB3"), Foreground = Color(Ink), VerticalContentAlignment = VerticalAlignment.Center,
                MinHeight = 34, Margin = new Thickness(0,3,7,5) };
        }
        public static ControlTemplate ButtonTemplate()
        {
            var chrome = new FrameworkElementFactory(typeof(Border), "Chrome");
            chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            chrome.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            chrome.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            chrome.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            chrome.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));
            presenter.SetValue(ContentPresenter.RecognizesAccessKeyProperty, true);
            chrome.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = chrome };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.83, "Chrome")); template.Triggers.Add(hover);
            var down = new Trigger { Property = System.Windows.Controls.Button.IsPressedProperty, Value = true };
            down.Setters.Add(new Setter(UIElement.OpacityProperty, 0.69, "Chrome")); template.Triggers.Add(down);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5, "Chrome")); template.Triggers.Add(disabled);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, Color("#B17A42"), "Chrome"));
            focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Chrome"));
            template.Triggers.Add(focus);
            return template;
        }
        static ControlTemplate buttonTemplate;
        public static Button Button(string label, Action click, bool primary = false)
        {
            if (buttonTemplate == null) buttonTemplate = ButtonTemplate();
            Button button = new Button { Content = label, Cursor = Cursors.Hand, FontSize = 13, FontWeight = FontWeights.Medium,
                Padding = new Thickness(13,7,13,7), MinHeight = 33, Margin = new Thickness(0,3,7,3),
                Foreground = Color(primary ? "#FFFFFF" : Ink), Background = Color(primary ? Green : "#F1F1E8"),
                BorderBrush = Color(primary ? Green : "#D7DBCF"), BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Center, Template = buttonTemplate };
            if (click != null) button.Click += delegate { click(); };
            return button;
        }
        public static TextBlock Icon(string glyph, double size, string color)
        {
            return new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = size,
                Foreground = Color(color), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        }
        static readonly Dictionary<string, BitmapImage> images = new Dictionary<string, BitmapImage>();
        public static BitmapImage Bitmap(string path)
        {
            BitmapImage img;
            if (images.TryGetValue(path, out img)) return img;
            img = new BitmapImage();
            using (var file = File.OpenRead(path))
            {
                img.BeginInit(); img.CacheOption = BitmapCacheOption.OnLoad; img.StreamSource = file; img.EndInit();
            }
            img.Freeze(); images[path] = img; return img;
        }
        public static Image Picture(string path, double width, double height)
        {
            return new Image { Source = Bitmap(path), Width = width, Height = height, Stretch = Stretch.Uniform };
        }
    }
}
