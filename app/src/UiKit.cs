using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using PathShape = System.Windows.Shapes.Path;

namespace PaperDock
{
    internal enum ButtonKind
    {
        Primary,
        Secondary,
        Quiet
    }

    internal static class Ui
    {
        public static readonly Brush Ink = Brush("#1B1F23");
        public static readonly Brush Muted = Brush("#5D6873");
        public static readonly Brush Line = Brush("#DFE4EA");
        public static readonly Brush Background = Brush("#F3F6F9");
        public static readonly Brush Sidebar = Brush("#F7FAFF");
        public static readonly Brush Accent = Brush("#0F6CBD");
        public static readonly Brush AccentDark = Brush("#0B568F");
        public static readonly Brush AccentSoft = Brush("#E6F1FB");
        public static readonly Brush Success = Brush("#0F7B5E");
        public static readonly Brush SuccessSoft = Brush("#E6F4EE");
        public static readonly FontFamily Font = new FontFamily("Microsoft YaHei UI");
        public static readonly FontFamily DisplayFont = new FontFamily("Segoe UI, Microsoft YaHei UI");

        public static SolidColorBrush Brush(string value)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        }

        public static TextBlock Text(
            string value,
            double size = 13,
            FontWeight? weight = null,
            Brush foreground = null)
        {
            return new TextBlock
            {
                Text = value,
                FontFamily = Font,
                FontSize = size,
                FontWeight = weight ?? FontWeights.Normal,
                Foreground = foreground ?? Ink,
                TextWrapping = TextWrapping.WrapWithOverflow,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static Border Card(UIElement child, double padding = 22, Thickness? margin = null)
        {
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(padding),
                Margin = margin ?? new Thickness(0),
                Child = child,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 16,
                    ShadowDepth = 2,
                    Opacity = 0.07,
                    Color = Colors.Black
                }
            };
        }

        public static Button Button(
            string label,
            ButtonKind kind = ButtonKind.Secondary,
            double height = 36,
            Thickness? padding = null)
        {
            var button = new Button
            {
                Content = label,
                Height = height,
                MinWidth = 64,
                Padding = padding ?? new Thickness(14, 0, 14, 0),
                FontFamily = Font,
                FontSize = kind == ButtonKind.Primary ? 13 : 12,
                FontWeight = kind == ButtonKind.Primary ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = kind == ButtonKind.Primary ? Brushes.White : Ink,
                Background = kind == ButtonKind.Primary ? Accent : Brushes.White,
                BorderBrush = kind == ButtonKind.Primary ? AccentDark : Brush("#D5DDE6"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                SnapsToDevicePixels = true,
                FocusVisualStyle = FocusStyle()
            };
            button.Template = ButtonTemplate(kind);
            return button;
        }

        public static TextBox Input(string value = "", double height = 36, double fontSize = 12)
        {
            return new TextBox
            {
                Text = value ?? string.Empty,
                Height = height,
                FontFamily = Font,
                FontSize = fontSize,
                Foreground = Ink,
                Background = Brushes.White,
                BorderBrush = Brush("#D7E0E8"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 5, 9, 5),
                VerticalContentAlignment = VerticalAlignment.Center,
                FocusVisualStyle = FocusStyle()
            };
        }

        public static StackPanel Stack(params UIElement[] children)
        {
            var panel = new StackPanel();
            foreach (var child in children)
            {
                panel.Children.Add(child);
            }
            return panel;
        }

        public static DockPanel Row(params UIElement[] children)
        {
            var panel = new DockPanel { LastChildFill = true };
            foreach (var child in children)
            {
                panel.Children.Add(child);
            }
            return panel;
        }

        public static PathShape Icon(string name, Brush brush = null)
        {
            var data = name == "merge"
                ? "M5,3 L13,3 L18,8 L18,11 L8,11 L8,21 L5,21 Z M8,11 L16,11 L20,15 L20,21 L8,21 Z"
                : name == "convert"
                    ? "M4,8 L17,8 M14,5 L17,8 L14,11 M20,16 L7,16 M10,13 L7,16 L10,19"
                    : name == "settings"
                        ? "M4,7 L20,7 M4,17 L20,17 M9,7 A2,2 0 1 0 9,11 A2,2 0 1 0 9,7 M15,17 A2,2 0 1 0 15,21 A2,2 0 1 0 15,17"
                        : "M4,3 L20,3 L20,21 L4,21 Z M12,3 L12,21 M8,8 L16,8 M8,12 L16,12";
            return new PathShape
            {
                Data = Geometry.Parse(data),
                Stroke = brush ?? Muted,
                StrokeThickness = 1.7,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Fill = Brushes.Transparent,
                Width = 18,
                Height = 18,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static Border Divider()
        {
            return new Border
            {
                Height = 1,
                Background = Line,
                Margin = new Thickness(0, 18, 0, 18)
            };
        }

        public static Image LoadImage(string path, bool stretchUniform = true)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return new Image
            {
                Source = bitmap,
                Stretch = stretchUniform ? Stretch.Uniform : Stretch.UniformToFill,
                SnapsToDevicePixels = true
            };
        }

        public static UIElement PagePreview(string imagePath, string caption, string footer)
        {
            var image = LoadImage(imagePath);
            var card = new Border
            {
                Background = Brushes.White,
                BorderBrush = Brush("#E2E7EC"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(7),
                Margin = new Thickness(0, 0, 12, 0)
            };
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(new Border
            {
                Background = Brushes.White,
                Child = image
            });
            var label = new Grid { Margin = new Thickness(2, 8, 2, 1) };
            label.Children.Add(Text(caption, 9, FontWeights.Normal, Muted));
            var right = Text(footer, 9, FontWeights.Medium, Ink);
            right.HorizontalAlignment = HorizontalAlignment.Right;
            label.Children.Add(right);
            Grid.SetRow(label, 1);
            grid.Children.Add(label);
            card.Child = grid;
            return card;
        }

        public static RadioButton Segment(string label, string group)
        {
            var radio = new RadioButton
            {
                Content = label,
                GroupName = group,
                FontFamily = Font,
                FontSize = 12,
                Foreground = Muted,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                Height = 37,
                Margin = new Thickness(0),
                Cursor = Cursors.Hand,
                FocusVisualStyle = FocusStyle()
            };
            radio.Template = SegmentTemplate();
            return radio;
        }

        public static RadioButton TargetCard(string code, string title, string description, string group)
        {
            var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(Text(code, 9, FontWeights.Normal, Brush("#75818C")));
            stack.Children.Add(Text(title, 23, FontWeights.SemiBold, Ink));
            stack.Children.Add(Text(description, 10, FontWeights.Normal, Muted));

            var radio = new RadioButton
            {
                Content = stack,
                GroupName = group,
                Height = 132,
                Background = Brushes.White,
                BorderBrush = Brush("#E0E5EB"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Cursor = Cursors.Hand,
                FocusVisualStyle = FocusStyle()
            };
            radio.Template = TargetTemplate();
            return radio;
        }

        private static ControlTemplate ButtonTemplate(ButtonKind kind)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "border";
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty,
                kind == ButtonKind.Primary ? AccentDark : Brush("#F4F8FC"), "border"));
            var pressed = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(UIElement.OpacityProperty, 0.76));
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.42));
            template.Triggers.Add(hover);
            template.Triggers.Add(pressed);
            template.Triggers.Add(disabled);
            template.Seal();
            return template;
        }

        private static ControlTemplate SegmentTemplate()
        {
            var template = new ControlTemplate(typeof(RadioButton))
            {
                VisualTree = new FrameworkElementFactory(typeof(Border))
            };
            var root = (FrameworkElementFactory)template.VisualTree;
            root.Name = "segmentBorder";
            root.SetValue(Border.BorderBrushProperty, Brush("#DCE3EA"));
            root.SetValue(Border.BorderThicknessProperty, new Thickness(0, 0, 1, 0));
            root.SetValue(Border.BackgroundProperty, Brush("#F7F9FC"));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            root.AppendChild(presenter);
            var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            on.Setters.Add(new Setter(Border.BackgroundProperty, AccentSoft, "segmentBorder"));
            on.Setters.Add(new Setter(Control.ForegroundProperty, AccentDark));
            on.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            template.Triggers.Add(on);
            template.Seal();
            return template;
        }

        private static ControlTemplate TargetTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "targetBorder";
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            border.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
            var template = new ControlTemplate(typeof(RadioButton)) { VisualTree = border };
            var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            on.Setters.Add(new Setter(Border.BorderBrushProperty, Accent, "targetBorder"));
            on.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "targetBorder"));
            on.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#F4F9FE"), "targetBorder"));
            template.Triggers.Add(on);
            template.Seal();
            return template;
        }

        private static Style FocusStyle()
        {
            var style = new Style(typeof(Control));
            var setter = new Setter(Control.TemplateProperty, new ControlTemplate(typeof(Control))
            {
                VisualTree = CreateFocusBorder()
            });
            style.Setters.Add(setter);
            style.Seal();
            return style;
        }

        private static FrameworkElementFactory CreateFocusBorder()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BorderBrushProperty, Brush("#7FB3DE"));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1.5));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            border.SetValue(Border.MarginProperty, new Thickness(-2));
            return border;
        }
    }
}
