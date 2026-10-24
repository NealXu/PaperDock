using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using PathShape = System.Windows.Shapes.Path;

namespace PaperDock
{
    internal sealed class MainWindow : Window
    {
        private readonly AppSettings _settings;
        private List<InstalledOcrPack> _ocrPacks;
        private readonly Dictionary<string, Button> _navButtons = new Dictionary<string, Button>();
        private readonly Dictionary<string, PathShape> _navIcons = new Dictionary<string, PathShape>();
        private readonly Dictionary<string, TextBlock> _navLabels = new Dictionary<string, TextBlock>();
        private readonly Dictionary<string, FrameworkElement> _views = new Dictionary<string, FrameworkElement>();

        private Grid _viewHost;
        private TextBlock _pageTitle;
        private TextBlock _pageSubtitle;
        private TextBlock _statusText;
        private Border _ocrSideDot;
        private TextBlock _ocrSideLabel;
        private TextBlock _ocrSideText;
        private Border _headerOcrDot;
        private TextBlock _headerOcrLabel;
        private Button _headerAction;
        private Button _cancelButton;
        private CancellationTokenSource _jobCancellation;
        private string _activeView = "split";
        private bool _busy;

        private string _splitSourcePath;
        private int _splitPageCount;
        private TextBlock _splitMeta;
        private TextBlock _splitSummary;
        private readonly Image[] _splitImages = new Image[4];
        private readonly TextBlock[] _splitCaptions = new TextBlock[4];
        private readonly TextBlock[] _splitFooters = new TextBlock[4];
        private readonly TextBlock[] _splitPlaceholders = new TextBlock[4];
        private readonly RadioButton[] _splitModes = new RadioButton[3];
        private RadioButton _splitNormalOutput;
        private RadioButton _splitImageOutput;
        private RadioButton _splitSearchableOutput;
        private TextBox _splitPer;
        private TextBox _splitParts;
        private TextBox _splitRanges;
        private StackPanel _splitPerPanel;
        private StackPanel _splitPartsPanel;
        private StackPanel _splitRangesPanel;
        private TextBox _splitOutputDirectory;
        private CheckBox _splitWatermarkEnabled;
        private TextBox _splitWatermarkText;
        private Button _splitStart;

        private readonly List<MergeItem> _mergeItems = new List<MergeItem>();
        private int _mergePreviewGeneration;
        private StackPanel _mergeQueue;
        private Grid _mergePreview;
        private readonly Image[] _mergeImages = new Image[4];
        private readonly TextBlock[] _mergeLabels = new TextBlock[4];
        private TextBlock _mergeMeta;
        private TextBlock _mergeSummary;
        private TextBox _mergeOutputName;
        private TextBox _mergeOutputDirectory;
        private CheckBox _mergeWatermarkEnabled;
        private TextBox _mergeWatermarkText;
        private Button _mergeStart;

        private string _convertSourcePath;
        private int _convertPageCount;
        private TextBlock _convertMeta;
        private readonly Image[] _convertImages = new Image[3];
        private readonly TextBlock[] _convertPlaceholders = new TextBlock[3];
        private readonly RadioButton[] _convertTargets = new RadioButton[3];
        private TextBox _convertOutput;
        private ComboBox _convertDpi;
        private CheckBox _convertOcr;
        private TextBlock _convertCheck;
        private TextBlock _convertEffect;
        private Button _convertStart;

        private TextBlock _systemOcrState;
        private TextBlock _settingsOcrDetails;
        private StackPanel _ocrPackList;
        private ComboBox _ocrChoice;
        private TextBox _defaultOutputDirectory;
        private bool _systemOcrAvailable;

        public MainWindow(string startupFile)
        {
            _settings = AppSettingsStore.Load();
            PrepareOutputDirectory();
            _ocrPacks = OcrPackManager.List().ToList();

            Title = "纸坞";
            var workArea = SystemParameters.WorkArea;
            MinWidth = 1000;
            MinHeight = 640;
            Width = Math.Max(MinWidth, Math.Min(1360, workArea.Width - 32));
            Height = Math.Max(MinHeight, Math.Min(820, workArea.Height - 24));
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowState = WindowState.Maximized;
            Background = Ui.Background;
            FontFamily = Ui.Font;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            BuildShell();
            SetView("split");

            Loaded += async delegate
            {
                if (!string.IsNullOrWhiteSpace(startupFile) && File.Exists(startupFile))
                {
                    await LoadSplitFile(startupFile);
                }
                await ProbeOcrAsync();
            };
            Closing += delegate
            {
                AppSettingsStore.Save(_settings);
            };
        }

        private void BuildShell()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(224) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Content = root;

            root.Children.Add(BuildSidebar());
            var content = new Grid();
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(92) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) });
            Grid.SetColumn(content, 1);
            root.Children.Add(content);

            content.Children.Add(BuildTitleBar());
            _viewHost = new Grid { Background = Ui.Background };
            Grid.SetRow(_viewHost, 1);
            content.Children.Add(_viewHost);
            content.Children.Add(BuildStatusBar());

            _views["split"] = BuildSplitView();
            _views["merge"] = BuildMergeView();
            _views["convert"] = BuildConvertView();
            _views["settings"] = BuildSettingsView();
            foreach (var view in _views.Values)
            {
                view.Visibility = Visibility.Collapsed;
                _viewHost.Children.Add(view);
            }
        }

        private FrameworkElement BuildSidebar()
        {
            var sidebar = new Border
            {
                Background = Ui.Sidebar,
                BorderBrush = Ui.Brush("#DBE4EE"),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(16, 20, 16, 16)
            };
            var panel = new Grid();
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sidebar.Child = panel;

            var brand = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(8, 0, 0, 28)
            };
            brand.Children.Add(new Border
            {
                Width = 32,
                Height = 32,
                Background = Ui.Accent,
                CornerRadius = new CornerRadius(8),
                Child = new TextBlock
                {
                    Text = "PD",
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.Bold,
                    FontSize = 9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            var brandName = Ui.Text("纸坞", 17, FontWeights.SemiBold);
            brandName.Margin = new Thickness(11, 0, 0, 0);
            brand.Children.Add(brandName);
            panel.Children.Add(brand);

            var nav = new StackPanel { Margin = new Thickness(0, 0, 0, 20) };
            nav.Children.Add(CreateNavButton("split", "拆分"));
            nav.Children.Add(CreateNavButton("merge", "合并"));
            nav.Children.Add(CreateNavButton("convert", "转换"));
            nav.Children.Add(CreateNavButton("settings", "设置"));
            Grid.SetRow(nav, 1);
            panel.Children.Add(nav);

            var footer = new StackPanel { Margin = new Thickness(8, 8, 8, 5) };
            var state = new StackPanel { Orientation = Orientation.Horizontal };
            _ocrSideDot = new Border
            {
                Width = 7,
                Height = 7,
                Background = Ui.Muted,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 7, 0)
            };
            state.Children.Add(_ocrSideDot);
            _ocrSideLabel = Ui.Text("正在检测 OCR", 11, FontWeights.SemiBold);
            state.Children.Add(_ocrSideLabel);
            footer.Children.Add(state);
            _ocrSideText = Ui.Text("正在检测系统能力", 10, FontWeights.Normal, Ui.Muted);
            _ocrSideText.Margin = new Thickness(14, 2, 0, 0);
            footer.Children.Add(_ocrSideText);
            Grid.SetRow(footer, 2);
            panel.Children.Add(footer);
            return sidebar;
        }

        private Button CreateNavButton(string view, string label)
        {
            var icon = Ui.Icon(view);
            var text = Ui.Text(label, 13);
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(icon);
            text.Margin = new Thickness(11, 0, 0, 0);
            stack.Children.Add(text);

            var button = new Button
            {
                Content = stack,
                Height = 42,
                Margin = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(11, 0, 11, 0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Ui.Muted,
                Cursor = Cursors.Hand,
                FocusVisualStyle = null
            };
            button.Click += delegate { SetView(view); };
            button.Template = NavButtonTemplate();
            AutomationProperties.SetName(button, label);
            AutomationProperties.SetAutomationId(button, "Nav" + view);
            _navButtons[view] = button;
            _navIcons[view] = icon;
            _navLabels[view] = text;
            return button;
        }

        private static ControlTemplate NavButtonTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "navBorder";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Brush("#EEF4FA"), "navBorder"));
            template.Triggers.Add(hover);
            template.Seal();
            return template;
        }

        private FrameworkElement BuildTitleBar()
        {
            var bar = new Border
            {
                Background = Ui.Brush("#FBFCFE"),
                BorderBrush = Ui.Brush("#E3E8EE"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(30, 0, 30, 0)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Child = grid;

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _pageTitle = Ui.Text("拆分 PDF", 27, FontWeights.SemiBold);
            _pageSubtitle = Ui.Text("打开 PDF 开始", 10, FontWeights.Normal, Ui.Muted);
            _pageSubtitle.FontFamily = new FontFamily("Consolas");
            _pageSubtitle.Margin = new Thickness(0, 4, 0, 0);
            titleStack.Children.Add(_pageTitle);
            titleStack.Children.Add(_pageSubtitle);
            grid.Children.Add(titleStack);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            _headerOcrDot = new Border
            {
                Width = 7,
                Height = 7,
                Background = Ui.Muted,
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 8, 0)
            };
            _headerOcrLabel = Ui.Text("检测 OCR", 11, FontWeights.Normal, Ui.Muted);
            var ocr = new Border
            {
                Height = 34,
                BorderBrush = Ui.Brush("#DCE5ED"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Background = Brushes.White,
                Padding = new Thickness(11, 0, 11, 0),
                Margin = new Thickness(0, 0, 9, 0),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        _headerOcrDot,
                        _headerOcrLabel
                    }
                }
            };
            actions.Children.Add(ocr);
            _headerAction = Ui.Button("打开 PDF");
            _headerAction.Click += async delegate { await HandleHeaderActionAsync(); };
            AutomationProperties.SetName(_headerAction, "打开 PDF");
            AutomationProperties.SetAutomationId(_headerAction, "HeaderAction");
            actions.Children.Add(_headerAction);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);
            return bar;
        }

        private FrameworkElement BuildStatusBar()
        {
            var bar = new Border
            {
                Background = Ui.Sidebar,
                BorderBrush = Ui.Brush("#DBE4EE"),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(30, 0, 30, 0)
            };
            var grid = new Grid();
            _statusText = Ui.Text("就绪", 9, FontWeights.Normal, Ui.Muted);
            _statusText.FontFamily = new FontFamily("Consolas");
            AutomationProperties.SetAutomationId(_statusText, "StatusText");
            grid.Children.Add(_statusText);
            var right = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            _cancelButton = Ui.Button("取消", ButtonKind.Quiet, 26, new Thickness(8, 0, 8, 0));
            _cancelButton.Background = Brushes.Transparent;
            _cancelButton.BorderBrush = Brushes.Transparent;
            _cancelButton.Foreground = Ui.AccentDark;
            _cancelButton.Margin = new Thickness(0, 0, 12, 0);
            _cancelButton.Visibility = Visibility.Collapsed;
            _cancelButton.Click += delegate
            {
                if (_jobCancellation != null)
                {
                    SetStatus("正在取消…");
                    _jobCancellation.Cancel();
                }
            };
            AutomationProperties.SetAutomationId(_cancelButton, "CancelJob");
            right.Children.Add(_cancelButton);
            var local = Ui.Text("本地处理 · 不上传文件", 9, FontWeights.Normal, Ui.Muted);
            local.FontFamily = new FontFamily("Consolas");
            right.Children.Add(local);
            grid.Children.Add(right);
            bar.Child = grid;
            Grid.SetRow(bar, 2);
            return bar;
        }

        private void SetView(string view)
        {
            _activeView = view;
            foreach (var pair in _views)
            {
                pair.Value.Visibility = pair.Key == view ? Visibility.Visible : Visibility.Collapsed;
            }
            foreach (var pair in _navButtons)
            {
                var active = pair.Key == view;
                pair.Value.Background = active ? Ui.AccentSoft : Brushes.Transparent;
                pair.Value.Foreground = active ? Ui.AccentDark : Ui.Muted;
                pair.Value.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
                _navIcons[pair.Key].Stroke = active ? Ui.AccentDark : Ui.Muted;
                _navLabels[pair.Key].Foreground = active ? Ui.AccentDark : Ui.Muted;
                _navLabels[pair.Key].FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
            }

            var titles = new Dictionary<string, string>
            {
                { "split", "拆分 PDF" },
                { "merge", "合并 PDF" },
                { "convert", "格式转换" },
                { "settings", "设置" }
            };
            _pageTitle.Text = titles[view];
            _headerAction.Visibility = view == "settings" ? Visibility.Collapsed : Visibility.Visible;
            var headerLabel = view == "merge" ? "添加文件" : "更换文件";
            _headerAction.Content = headerLabel;
            AutomationProperties.SetName(_headerAction, headerLabel);
            if (view == "split")
            {
                UpdateSplitHeader();
            }
            else if (view == "merge")
            {
                UpdateMergeHeader();
            }
            else if (view == "convert")
            {
                UpdateConvertHeader();
                if (_convertSourcePath == null && !string.IsNullOrWhiteSpace(_splitSourcePath))
                {
                    _ = LoadConvertFile(_splitSourcePath);
                }
            }
            else
            {
                _pageSubtitle.Text = "OCR 来源与输出默认值";
            }
            SetStatus("就绪");
        }

        private async Task HandleHeaderActionAsync()
        {
            if (_activeView == "merge")
            {
                AddMergeFiles();
                return;
            }
            var dialog = new OpenFileDialog
            {
                Filter = "PDF 文件 (*.pdf)|*.pdf",
                CheckFileExists = true,
                Multiselect = false,
                Title = "选择 PDF"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }
            if (_activeView == "convert")
            {
                await LoadConvertFile(dialog.FileName);
            }
            else
            {
                await LoadSplitFile(dialog.FileName);
            }
        }

        private FrameworkElement BuildSplitView()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var workspace = new Grid
            {
                Margin = new Thickness(28, 24, 28, 22)
            };
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
            scroll.Content = workspace;

            var left = new Grid();
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var head = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            head.Children.Add(Ui.Text("页面预览", 20, FontWeights.SemiBold));
            _splitMeta = Ui.Text("打开 PDF 后显示前 4 页", 9, FontWeights.Normal, Ui.Muted);
            _splitMeta.HorizontalAlignment = HorizontalAlignment.Right;
            _splitMeta.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(_splitMeta, 1);
            head.Children.Add(_splitMeta);
            var all = Ui.Button("查看全部", ButtonKind.Quiet, 30, new Thickness(8, 0, 8, 0));
            AutomationProperties.SetAutomationId(all, "SplitViewAll");
            all.Background = Brushes.Transparent;
            all.BorderBrush = Brushes.Transparent;
            all.Foreground = Ui.AccentDark;
            all.Click += delegate
            {
                if (_splitSourcePath == null)
                {
                    MessageBox.Show(this, "请先打开 PDF。", "纸坞");
                    return;
                }
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = _splitSourcePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception exception)
                {
                    ShowError("无法打开 PDF 阅读器", exception);
                }
            };
            Grid.SetColumn(all, 2);
            head.Children.Add(all);
            left.Children.Add(head);

            var pages = new Grid { Margin = new Thickness(0, 0, 4, 0) };
            for (var column = 0; column < 4; column++)
            {
                pages.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var card = CreateImageCard(column, _splitImages, _splitCaptions, _splitFooters, _splitPlaceholders);
                card.Margin = new Thickness(0, 0, column == 3 ? 0 : 13, 0);
                Grid.SetColumn(card, column);
                pages.Children.Add(card);
            }
            Grid.SetRow(pages, 1);
            left.Children.Add(pages);

            var foot = new Grid
            {
                Margin = new Thickness(0, 15, 0, 0)
            };
            foot.Children.Add(Ui.Text("默认显示 4 页", 9, FontWeights.Normal, Ui.Muted));
            var dpi = Ui.Text("60 DPI · 100%", 9, FontWeights.Normal, Ui.Muted);
            dpi.HorizontalAlignment = HorizontalAlignment.Right;
            foot.Children.Add(dpi);
            Grid.SetRow(foot, 2);
            left.Children.Add(foot);
            workspace.Children.Add(left);

            var settings = BuildSplitSettings();
            Grid.SetColumn(settings, 1);
            workspace.Children.Add(settings);
            return scroll;
        }

        private FrameworkElement CreateImageCard(
            int index,
            Image[] images,
            TextBlock[] captions,
            TextBlock[] footers,
            TextBlock[] placeholders)
        {
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(1) };
            images[index] = image;
            grid.Children.Add(image);
            var placeholder = Ui.Text("打开 PDF 后显示", 11, FontWeights.Normal, Ui.Muted);
            placeholder.HorizontalAlignment = HorizontalAlignment.Center;
            placeholder.VerticalAlignment = VerticalAlignment.Center;
            placeholders[index] = placeholder;
            grid.Children.Add(placeholder);
            var label = new Grid { Margin = new Thickness(3, 8, 3, 2) };
            var left = Ui.Text("—", 9, FontWeights.Normal, Ui.Muted);
            captions[index] = left;
            label.Children.Add(left);
            var right = Ui.Text("—", 9, FontWeights.Medium, Ui.Ink);
            right.HorizontalAlignment = HorizontalAlignment.Right;
            footers[index] = right;
            label.Children.Add(right);
            Grid.SetRow(label, 1);
            grid.Children.Add(label);
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = Ui.Brush("#E2E7EC"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(7),
                Height = 410,
                Child = grid
            };
        }

        private FrameworkElement BuildSplitSettings()
        {
            var panel = new StackPanel();
            panel.Children.Add(Ui.Text("拆分设置", 20, FontWeights.SemiBold));
            panel.Children.Add(FieldLabel("拆分方式", 21));

            var segments = new Grid();
            for (var index = 0; index < 3; index++)
            {
                segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            string[] labels = { "固定页数", "自定义范围", "按数量均分" };
            for (var index = 0; index < labels.Length; index++)
            {
                var radio = Ui.Segment(labels[index], "split-mode");
                radio.Tag = index;
                radio.Checked += delegate { UpdateSplitModeVisibility(); UpdateSplitSummary(); };
                _splitModes[index] = radio;
                AutomationProperties.SetAutomationId(radio, "SplitMode" + index);
                Grid.SetColumn(radio, index);
                segments.Children.Add(radio);
            }
            segments.ClipToBounds = true;
            panel.Children.Add(new Border
            {
                BorderBrush = Ui.Brush("#DCE3EA"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = segments
            });

            _splitPerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            _splitPerPanel.Children.Add(Ui.Text("每份 ", 12, FontWeights.Normal, Ui.Muted));
            _splitPer = Ui.Input("1", 36);
            AutomationProperties.SetAutomationId(_splitPer, "SplitPagesPerPart");
            AutomationProperties.SetName(_splitPer, "每份页数");
            _splitPer.Width = 68;
            _splitPer.TextChanged += delegate { UpdateSplitSummary(); };
            _splitPerPanel.Children.Add(_splitPer);
            _splitPerPanel.Children.Add(Ui.Text(" 页", 12, FontWeights.Normal, Ui.Muted));
            panel.Children.Add(_splitPerPanel);

            _splitPartsPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
            _splitPartsPanel.Children.Add(Ui.Text("拆成 ", 12, FontWeights.Normal, Ui.Muted));
            _splitParts = Ui.Input("2", 36);
            AutomationProperties.SetAutomationId(_splitParts, "SplitPartCount");
            AutomationProperties.SetName(_splitParts, "拆分份数");
            _splitParts.Width = 68;
            _splitParts.TextChanged += delegate { UpdateSplitSummary(); };
            _splitPartsPanel.Children.Add(_splitParts);
            _splitPartsPanel.Children.Add(Ui.Text(" 份", 12, FontWeights.Normal, Ui.Muted));
            panel.Children.Add(_splitPartsPanel);

            _splitRangesPanel = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
            _splitRanges = Ui.Input("1-3;4-6", 38);
            AutomationProperties.SetAutomationId(_splitRanges, "SplitRanges");
            AutomationProperties.SetName(_splitRanges, "拆分页码范围");
            _splitRanges.TextChanged += delegate { UpdateSplitSummary(); };
            _splitRangesPanel.Children.Add(_splitRanges);
            _splitRangesPanel.Children.Add(Ui.Text("多份用分号分隔，例如 1-3;4-6", 9, FontWeights.Normal, Ui.Muted));
            panel.Children.Add(_splitRangesPanel);
            _splitModes[0].IsChecked = true;

            panel.Children.Add(Ui.Divider());
            panel.Children.Add(FieldLabel("输出类型", 0));
            var outputTypes = new Grid();
            for (var index = 0; index < 3; index++)
            {
                outputTypes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            _splitNormalOutput = Ui.Segment("PDF", "split-output");
            _splitImageOutput = Ui.Segment("扫描型", "split-output");
            _splitSearchableOutput = Ui.Segment("可搜索", "split-output");
            _splitNormalOutput.IsChecked = true;
            AutomationProperties.SetAutomationId(_splitNormalOutput, "SplitOutputPdf");
            AutomationProperties.SetAutomationId(_splitImageOutput, "SplitOutputImage");
            AutomationProperties.SetAutomationId(_splitSearchableOutput, "SplitOutputSearchable");
            Grid.SetColumn(_splitNormalOutput, 0);
            Grid.SetColumn(_splitImageOutput, 1);
            Grid.SetColumn(_splitSearchableOutput, 2);
            outputTypes.Children.Add(_splitNormalOutput);
            outputTypes.Children.Add(_splitImageOutput);
            outputTypes.Children.Add(_splitSearchableOutput);
            panel.Children.Add(new Border
            {
                BorderBrush = Ui.Brush("#DCE3EA"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Child = outputTypes
            });

            var more = new Expander
            {
                Header = "更多选项",
                Margin = new Thickness(0, 17, 0, 0),
                IsExpanded = false,
                FontSize = 12,
                Foreground = Ui.Ink
            };
            var advanced = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            _splitWatermarkEnabled = new CheckBox { Content = "添加文字水印", FontSize = 11 };
            AutomationProperties.SetAutomationId(_splitWatermarkEnabled, "SplitWatermarkEnabled");
            _splitWatermarkEnabled.Checked += delegate { _splitWatermarkText.IsEnabled = true; };
            _splitWatermarkEnabled.Unchecked += delegate { _splitWatermarkText.IsEnabled = false; };
            advanced.Children.Add(_splitWatermarkEnabled);
            _splitWatermarkText = Ui.Input("样例测试 2026", 34, 11);
            AutomationProperties.SetAutomationId(_splitWatermarkText, "SplitWatermarkText");
            _splitWatermarkText.Margin = new Thickness(0, 7, 0, 10);
            _splitWatermarkText.IsEnabled = false;
            advanced.Children.Add(_splitWatermarkText);
            advanced.Children.Add(FieldLabel("输出目录", 0));
            _splitOutputDirectory = Ui.Input(_settings.OutputDirectory, 36, 10);
            AutomationProperties.SetAutomationId(_splitOutputDirectory, "SplitOutputDirectory");
            _splitOutputDirectory.FontFamily = new FontFamily("Consolas");
            advanced.Children.Add(_splitOutputDirectory);
            var choose = Ui.Button("选择目录…", ButtonKind.Secondary, 32);
            choose.Margin = new Thickness(0, 7, 0, 0);
            choose.HorizontalAlignment = HorizontalAlignment.Left;
            choose.Click += delegate
            {
                var picked = ChooseFolder(_splitOutputDirectory.Text);
                if (picked != null)
                {
                    _splitOutputDirectory.Text = picked;
                    _settings.OutputDirectory = picked;
                    AppSettingsStore.Save(_settings);
                }
            };
            advanced.Children.Add(choose);
            more.Content = advanced;
            panel.Children.Add(more);

            panel.Children.Add(Ui.Divider());
            _splitSummary = Ui.Text("打开 PDF 后显示拆分摘要。", 11, FontWeights.Normal, Ui.Muted);
            panel.Children.Add(_splitSummary);
            _splitStart = Ui.Button("开始拆分", ButtonKind.Primary, 46);
            _splitStart.Margin = new Thickness(0, 16, 0, 0);
            _splitStart.Click += async delegate { await StartSplitAsync(); };
            AutomationProperties.SetName(_splitStart, "开始拆分");
            AutomationProperties.SetAutomationId(_splitStart, "StartSplit");
            panel.Children.Add(_splitStart);

            return Ui.Card(panel, 22);
        }

        private static TextBlock FieldLabel(string value, double topMargin)
        {
            var label = Ui.Text(value, 11, FontWeights.SemiBold, Ui.Muted);
            label.Margin = new Thickness(0, topMargin, 0, 8);
            return label;
        }

        private void UpdateSplitModeVisibility()
        {
            var mode = SelectedSplitMode();
            _splitPerPanel.Visibility = mode == SplitMode.Fixed ? Visibility.Visible : Visibility.Collapsed;
            _splitPartsPanel.Visibility = mode == SplitMode.Count ? Visibility.Visible : Visibility.Collapsed;
            _splitRangesPanel.Visibility = mode == SplitMode.Ranges ? Visibility.Visible : Visibility.Collapsed;
        }

        private SplitMode SelectedSplitMode()
        {
            if (_splitModes[1].IsChecked == true) return SplitMode.Ranges;
            if (_splitModes[2].IsChecked == true) return SplitMode.Count;
            return SplitMode.Fixed;
        }

        private PdfOutputKind SelectedOutputKind()
        {
            if (_splitImageOutput.IsChecked == true) return PdfOutputKind.ImageOnly;
            if (_splitSearchableOutput.IsChecked == true) return PdfOutputKind.Searchable;
            return PdfOutputKind.Normal;
        }

        private async Task LoadSplitFile(string path)
        {
            try
            {
                SetStatus("正在读取 PDF…");
                _splitPageCount = PdfEngine.GetPageCount(path);
                _splitSourcePath = path;
                _splitMeta.Text = "第 1-" + Math.Min(4, _splitPageCount) + " 页 / 共 " + _splitPageCount + " 页";
                _splitOutputDirectory.Text = string.IsNullOrWhiteSpace(_splitOutputDirectory.Text)
                    ? _settings.OutputDirectory
                    : _splitOutputDirectory.Text;
                UpdateSplitHeader();
                UpdateSplitSummary();
                await LoadPreviewInto(_splitImages, _splitCaptions, _splitFooters, _splitPlaceholders, path, Math.Min(4, _splitPageCount));
                SetStatus("就绪 · " + _splitPageCount + " 页");
            }
            catch (Exception exception)
            {
                ShowError("无法打开 PDF", exception);
            }
        }

        private async Task LoadPreviewInto(
            Image[] images,
            TextBlock[] captions,
            TextBlock[] footers,
            TextBlock[] placeholders,
            string path,
            int count)
        {
            var files = await Task.Run(() => PdfEngine.RenderPreview(path, 4, 72));
            for (var index = 0; index < images.Length; index++)
            {
                if (index < files.Count && File.Exists(files[index]))
                {
                    images[index].Source = Ui.LoadImage(files[index]).Source;
                    images[index].Visibility = Visibility.Visible;
                    placeholders[index].Visibility = Visibility.Collapsed;
                    captions[index].Text = (index + 1).ToString("D2");
                    footers[index].Text = (index + 1) + " / " + count;
                }
                else
                {
                    images[index].Source = null;
                    images[index].Visibility = Visibility.Collapsed;
                    placeholders[index].Visibility = Visibility.Visible;
                    captions[index].Text = "—";
                    footers[index].Text = "—";
                }
            }
            if (files.Count > 0)
            {
                var folder = Path.GetDirectoryName(files[0]);
                try { Directory.Delete(folder, true); } catch { }
            }
        }

        private void UpdateSplitHeader()
        {
            if (_splitSourcePath == null)
            {
                _pageSubtitle.Text = "选择 PDF 后即可拆分";
                _headerAction.Content = "打开 PDF";
                AutomationProperties.SetName(_headerAction, "打开 PDF");
                return;
            }
            var file = new FileInfo(_splitSourcePath);
            _pageSubtitle.Text = file.Name + " · " + _splitPageCount + " 页 · " + HumanSize(file.Length);
            _headerAction.Content = "更换文件";
            AutomationProperties.SetName(_headerAction, "更换文件");
        }

        private void UpdateSplitSummary()
        {
            if (_splitSummary == null)
            {
                return;
            }
            if (_splitSourcePath == null)
            {
                _splitSummary.Text = "打开 PDF 后显示拆分摘要。";
                return;
            }
            try
            {
                var request = BuildSplitRequest();
                var groups = PageRange.BuildGroups(request, _splitPageCount);
                _splitSummary.Text = "将生成 " + groups.Count + " 个 PDF，共 " + _splitPageCount + " 页；原文件不修改。";
            }
            catch (Exception exception)
            {
                _splitSummary.Text = exception.Message;
            }
        }

        private SplitRequest BuildSplitRequest()
        {
            int pagesPerPart;
            int partCount;
            int.TryParse(_splitPer.Text, out pagesPerPart);
            int.TryParse(_splitParts.Text, out partCount);
            return new SplitRequest
            {
                SourcePath = _splitSourcePath,
                OutputDirectory = _splitOutputDirectory.Text,
                Mode = SelectedSplitMode(),
                PagesPerPart = Math.Max(1, pagesPerPart),
                PartCount = Math.Max(1, partCount),
                Ranges = _splitRanges.Text,
                OutputKind = SelectedOutputKind(),
                Watermark = _splitWatermarkEnabled.IsChecked == true ? _splitWatermarkText.Text : string.Empty
            };
        }

        private async Task StartSplitAsync()
        {
            if (_busy) return;
            if (_splitSourcePath == null)
            {
                MessageBox.Show(this, "请先打开 PDF。", "纸坞");
                return;
            }
            try
            {
                _busy = true;
                _splitStart.IsEnabled = false;
                _jobCancellation = new CancellationTokenSource();
                _cancelButton.Visibility = Visibility.Visible;
                var request = BuildSplitRequest();
                _settings.OutputDirectory = request.OutputDirectory;
                AppSettingsStore.Save(_settings);
                var progress = new Progress<string>(SetStatus);
                var outputs = await Task.Run(() => PdfEngine.Split(
                    request,
                    _settings,
                    _ocrPacks,
                    progress,
                    _jobCancellation.Token));
                SetStatus("拆分完成 · " + outputs.Count + " 个文件");
                MessageBox.Show(
                    this,
                    "拆分完成，共生成 " + outputs.Count + " 个文件。\n\n" +
                    string.Join("\n", outputs.Take(5).Select(Path.GetFileName)),
                    "纸坞");
            }
            catch (OperationCanceledException)
            {
                SetStatus("已取消");
            }
            catch (Exception exception)
            {
                ShowError("拆分失败", exception);
            }
            finally
            {
                _busy = false;
                _splitStart.IsEnabled = true;
                _cancelButton.Visibility = Visibility.Collapsed;
                if (_jobCancellation != null)
                {
                    _jobCancellation.Dispose();
                    _jobCancellation = null;
                }
            }
        }

        private FrameworkElement BuildMergeView()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var workspace = new Grid { Margin = new Thickness(28, 24, 28, 22) };
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
            scroll.Content = workspace;

            var queuePanel = new Grid();
            queuePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            queuePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            queuePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var queueHead = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            queueHead.Children.Add(Ui.Text("文件顺序", 18, FontWeights.SemiBold));
            var sortHint = Ui.Text("使用按钮调整顺序", 9, FontWeights.Normal, Ui.Muted);
            sortHint.HorizontalAlignment = HorizontalAlignment.Right;
            queueHead.Children.Add(sortHint);
            queuePanel.Children.Add(queueHead);
            var queueScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _mergeQueue = new StackPanel();
            queueScroll.Content = _mergeQueue;
            Grid.SetRow(queueScroll, 1);
            queuePanel.Children.Add(queueScroll);
            var add = Ui.Button("＋ 添加 PDF", ButtonKind.Secondary, 38);
            AutomationProperties.SetAutomationId(add, "AddMergeFiles");
            add.Margin = new Thickness(0, 12, 0, 0);
            add.Click += delegate { AddMergeFiles(); };
            Grid.SetRow(add, 2);
            queuePanel.Children.Add(add);
            workspace.Children.Add(Ui.Card(queuePanel, 18));

            var previewPanel = new Grid { Margin = new Thickness(24, 0, 24, 0) };
            previewPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            previewPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var mergeHead = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            mergeHead.Children.Add(Ui.Text("合并后预览", 18, FontWeights.SemiBold));
            _mergeMeta = Ui.Text("添加文件后显示前 4 页", 9, FontWeights.Normal, Ui.Muted);
            _mergeMeta.HorizontalAlignment = HorizontalAlignment.Right;
            mergeHead.Children.Add(_mergeMeta);
            previewPanel.Children.Add(mergeHead);
            _mergePreview = new Grid();
            for (var index = 0; index < 4; index++)
            {
                _mergePreview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(1) };
                _mergeImages[index] = image;
                var label = Ui.Text("—", 8, FontWeights.Normal, Ui.Muted);
                var cardGrid = new Grid();
                cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                cardGrid.Children.Add(image);
                label.Margin = new Thickness(3, 7, 3, 1);
                Grid.SetRow(label, 1);
                cardGrid.Children.Add(label);
                _mergeLabels[index] = label;
                var card = new Border
                {
                    Height = 390,
                    Background = Brushes.White,
                    BorderBrush = Ui.Brush("#E2E7EC"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(6),
                    Margin = new Thickness(0, 0, index == 3 ? 0 : 10, 0),
                    Child = cardGrid
                };
                Grid.SetColumn(card, index);
                _mergePreview.Children.Add(card);
            }
            Grid.SetRow(_mergePreview, 1);
            previewPanel.Children.Add(_mergePreview);
            Grid.SetColumn(previewPanel, 1);
            workspace.Children.Add(previewPanel);

            var mergeSettings = new StackPanel();
            mergeSettings.Children.Add(Ui.Text("合并设置", 20, FontWeights.SemiBold));
            mergeSettings.Children.Add(FieldLabel("输出文件", 21));
            _mergeOutputName = Ui.Input("合并结果.pdf", 36);
            AutomationProperties.SetAutomationId(_mergeOutputName, "MergeOutputName");
            mergeSettings.Children.Add(_mergeOutputName);
            mergeSettings.Children.Add(FieldLabel("输出目录", 15));
            _mergeOutputDirectory = Ui.Input(_settings.OutputDirectory, 36, 10);
            AutomationProperties.SetAutomationId(_mergeOutputDirectory, "MergeOutputDirectory");
            _mergeOutputDirectory.FontFamily = new FontFamily("Consolas");
            mergeSettings.Children.Add(_mergeOutputDirectory);
            var choose = Ui.Button("选择目录…", ButtonKind.Secondary, 32);
            choose.Margin = new Thickness(0, 7, 0, 0);
            choose.HorizontalAlignment = HorizontalAlignment.Left;
            choose.Click += delegate
            {
                var picked = ChooseFolder(_mergeOutputDirectory.Text);
                if (picked != null)
                {
                    _mergeOutputDirectory.Text = picked;
                    _settings.OutputDirectory = picked;
                    AppSettingsStore.Save(_settings);
                }
            };
            mergeSettings.Children.Add(choose);
            mergeSettings.Children.Add(Ui.Divider());
            _mergeWatermarkEnabled = new CheckBox { Content = "添加文字水印", FontSize = 11 };
            AutomationProperties.SetAutomationId(_mergeWatermarkEnabled, "MergeWatermarkEnabled");
            _mergeWatermarkText = Ui.Input("样例测试 2026", 34, 11);
            AutomationProperties.SetAutomationId(_mergeWatermarkText, "MergeWatermarkText");
            _mergeWatermarkText.Margin = new Thickness(0, 7, 0, 0);
            _mergeWatermarkText.IsEnabled = false;
            _mergeWatermarkEnabled.Checked += delegate { _mergeWatermarkText.IsEnabled = true; };
            _mergeWatermarkEnabled.Unchecked += delegate { _mergeWatermarkText.IsEnabled = false; };
            mergeSettings.Children.Add(_mergeWatermarkEnabled);
            mergeSettings.Children.Add(_mergeWatermarkText);
            mergeSettings.Children.Add(Ui.Divider());
            _mergeSummary = Ui.Text("添加文件后显示合并摘要。", 11, FontWeights.Normal, Ui.Muted);
            mergeSettings.Children.Add(_mergeSummary);
            _mergeStart = Ui.Button("开始合并", ButtonKind.Primary, 46);
            _mergeStart.Margin = new Thickness(0, 16, 0, 0);
            _mergeStart.Click += async delegate { await StartMergeAsync(); };
            AutomationProperties.SetName(_mergeStart, "开始合并");
            AutomationProperties.SetAutomationId(_mergeStart, "StartMerge");
            mergeSettings.Children.Add(_mergeStart);
            var settingsCard = Ui.Card(mergeSettings, 22);
            Grid.SetColumn(settingsCard, 2);
            workspace.Children.Add(settingsCard);
            return scroll;
        }

        private void AddMergeFiles()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PDF 文件 (*.pdf)|*.pdf",
                CheckFileExists = true,
                Multiselect = true,
                Title = "选择要合并的 PDF"
            };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }
            foreach (var file in dialog.FileNames)
            {
                AddMergePath(file);
            }
        }

        private void AddMergePath(string path)
        {
            try
            {
                if (_mergeItems.Any(item => string.Equals(item.Path, path, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }
                _mergeItems.Add(new MergeItem
                {
                    Path = path,
                    PageCount = PdfEngine.GetPageCount(path)
                });
                RefreshMergeQueue();
                UpdateMergeHeader();
                _ = RefreshMergePreviewAsync();
            }
            catch (Exception exception)
            {
                ShowError("无法添加文件", exception);
            }
        }

        private void RefreshMergeQueue()
        {
            _mergeQueue.Children.Clear();
            for (var index = 0; index < _mergeItems.Count; index++)
            {
                var item = _mergeItems[index];
                var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var number = new Border
                {
                    Width = 28,
                    Height = 35,
                    Background = Ui.Brush("#FBEFEC"),
                    BorderBrush = Ui.Brush("#B84A37"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(3),
                    Child = new TextBlock
                    {
                        Text = "PDF",
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 7,
                        FontWeight = FontWeights.Bold,
                        Foreground = Ui.Brush("#A6402D"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                row.Children.Add(number);

                var copy = new StackPanel { Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                copy.Children.Add(Ui.Text(item.DisplayName, 11, FontWeights.SemiBold));
                var rangeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
                rangeRow.Children.Add(Ui.Text(item.PageCount + " 页 · 范围 ", 9, FontWeights.Normal, Ui.Muted));
                var rangeBox = Ui.Input(item.Range, 26, 9);
                AutomationProperties.SetAutomationId(rangeBox, "MergeRange" + index);
                AutomationProperties.SetName(rangeBox, "第 " + (index + 1) + " 份页码范围");
                rangeBox.Width = 75;
                rangeBox.Padding = new Thickness(5, 2, 5, 2);
                var captured = item;
                rangeBox.LostKeyboardFocus += delegate
                {
                    captured.Range = rangeBox.Text.Trim();
                    UpdateMergeSummary();
                    _ = RefreshMergePreviewAsync();
                };
                rangeRow.Children.Add(rangeBox);
                copy.Children.Add(rangeRow);
                Grid.SetColumn(copy, 1);
                row.Children.Add(copy);

                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                actions.Children.Add(SmallAction("↑", "上移", delegate { MoveMergeItem(index, -1); }));
                actions.Children.Add(SmallAction("↓", "下移", delegate { MoveMergeItem(index, 1); }));
                actions.Children.Add(SmallAction("×", "移除", delegate
                {
                    _mergeItems.Remove(captured);
                    RefreshMergeQueue();
                    UpdateMergeSummary();
                    UpdateMergeHeader();
                    _ = RefreshMergePreviewAsync();
                }));
                Grid.SetColumn(actions, 2);
                row.Children.Add(actions);
                _mergeQueue.Children.Add(row);
            }
            UpdateMergeSummary();
        }

        private Button SmallAction(string text, string name, Action action)
        {
            var button = Ui.Button(text, ButtonKind.Quiet, 26, new Thickness(4, 0, 4, 0));
            button.MinWidth = 26;
            button.Width = 28;
            button.Margin = new Thickness(2, 0, 0, 0);
            button.Background = Brushes.Transparent;
            button.BorderBrush = Brushes.Transparent;
            button.Foreground = Ui.Muted;
            button.Click += delegate { action(); };
            AutomationProperties.SetName(button, name);
            return button;
        }

        private void MoveMergeItem(int index, int direction)
        {
            var target = index + direction;
            if (target < 0 || target >= _mergeItems.Count)
            {
                return;
            }
            var item = _mergeItems[index];
            _mergeItems.RemoveAt(index);
            _mergeItems.Insert(target, item);
            RefreshMergeQueue();
            _ = RefreshMergePreviewAsync();
        }

        private async Task RefreshMergePreviewAsync()
        {
            var generation = ++_mergePreviewGeneration;
            foreach (var image in _mergeImages)
            {
                image.Source = null;
            }
            foreach (var label in _mergeLabels)
            {
                label.Text = "—";
            }
            if (_mergeItems.Count == 0)
            {
                _mergeMeta.Text = "添加文件后显示前 4 页";
                return;
            }

            try
            {
                await Task.Delay(250);
                if (generation != _mergePreviewGeneration)
                {
                    return;
                }
                var specs = new List<MergePreviewSpec>();
                var remaining = 4;
                foreach (var item in _mergeItems)
                {
                    if (remaining <= 0) break;
                    var pages = item.SelectedPages().Take(remaining).ToList();
                    specs.Add(new MergePreviewSpec { Path = item.Path, Pages = pages });
                    remaining -= pages.Count;
                }
                _mergeMeta.Text = "正在生成预览…";
                var preview = await Task.Run(() => BuildMergePreviewFiles(specs));
                if (generation != _mergePreviewGeneration)
                {
                    foreach (var folder in preview.Select(Path.GetDirectoryName).Distinct())
                    {
                        try { Directory.Delete(folder, true); } catch { }
                    }
                    return;
                }
                for (var index = 0; index < preview.Count; index++)
                {
                    _mergeImages[index].Source = Ui.LoadImage(preview[index]).Source;
                    _mergeLabels[index].Text = "源页 " + (index + 1);
                }
                foreach (var folder in preview.Select(Path.GetDirectoryName).Distinct())
                {
                    try { Directory.Delete(folder, true); } catch { }
                }
                _mergeMeta.Text = "前 " + preview.Count + " 页";
            }
            catch (Exception exception)
            {
                _mergeMeta.Text = exception.Message;
            }
        }

        private List<string> BuildMergePreviewFiles(IReadOnlyList<MergePreviewSpec> specs)
        {
            var preview = new List<string>();
            foreach (var spec in specs)
            {
                var pages = spec.Pages.ToArray();
                if (pages.Length == 0) continue;
                var folder = AppPaths.CreateTempDirectory();
                var rendered = SystemPdfService.RenderPages(spec.Path, folder, pages, 72);
                foreach (var page in pages)
                {
                    preview.Add(rendered[page]);
                }
            }
            return preview;
        }

        private sealed class MergePreviewSpec
        {
            public string Path { get; set; }
            public List<int> Pages { get; set; }
        }

        private void UpdateMergeHeader()
        {
            if (_mergeItems.Count == 0)
            {
                _pageSubtitle.Text = "添加 PDF 文件后开始";
                return;
            }
            _pageSubtitle.Text = _mergeItems.Count + " 份文档 · " +
                _mergeItems.Sum(item => item.SelectedPages().Count) + " 页";
        }

        private void UpdateMergeSummary()
        {
            if (_mergeSummary == null) return;
            if (_mergeItems.Count == 0)
            {
                _mergeSummary.Text = "添加文件后显示合并摘要。";
                return;
            }
            try
            {
                var pages = _mergeItems.Sum(item => item.SelectedPages().Count);
                _mergeSummary.Text = "将合并 " + _mergeItems.Count + " 份文档，共 " + pages + " 页。";
            }
            catch (Exception exception)
            {
                _mergeSummary.Text = exception.Message;
            }
        }

        private async Task StartMergeAsync()
        {
            if (_busy) return;
            if (_mergeItems.Count == 0)
            {
                MessageBox.Show(this, "请先添加至少一个 PDF。", "纸坞");
                return;
            }
            try
            {
                _busy = true;
                _mergeStart.IsEnabled = false;
                _jobCancellation = new CancellationTokenSource();
                _cancelButton.Visibility = Visibility.Visible;
                var directory = _mergeOutputDirectory.Text;
                Directory.CreateDirectory(directory);
                var name = string.IsNullOrWhiteSpace(_mergeOutputName.Text)
                    ? "合并结果.pdf"
                    : _mergeOutputName.Text;
                if (!string.Equals(Path.GetExtension(name), ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    name += ".pdf";
                }
                var request = new MergeRequest
                {
                    OutputPath = Path.Combine(directory, Path.GetFileName(name)),
                    Items = _mergeItems.ToList(),
                    Watermark = _mergeWatermarkEnabled.IsChecked == true ? _mergeWatermarkText.Text : string.Empty
                };
                var progress = new Progress<string>(SetStatus);
                var output = await Task.Run(() => PdfEngine.Merge(
                    request,
                    progress,
                    _jobCancellation.Token));
                SetStatus("合并完成");
                MessageBox.Show(this, "合并完成：\n" + output, "纸坞");
            }
            catch (OperationCanceledException)
            {
                SetStatus("已取消");
            }
            catch (Exception exception)
            {
                ShowError("合并失败", exception);
            }
            finally
            {
                _busy = false;
                _mergeStart.IsEnabled = true;
                _cancelButton.Visibility = Visibility.Collapsed;
                if (_jobCancellation != null)
                {
                    _jobCancellation.Dispose();
                    _jobCancellation = null;
                }
            }
        }

        private FrameworkElement BuildConvertView()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var workspace = new Grid { Margin = new Thickness(28, 24, 28, 22) };
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
            scroll.Content = workspace;

            var left = new StackPanel();
            var head = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            head.Children.Add(Ui.Text("转换为", 20, FontWeights.SemiBold));
            _convertMeta = Ui.Text("打开 PDF 后显示检查结果", 9, FontWeights.Normal, Ui.Muted);
            _convertMeta.HorizontalAlignment = HorizontalAlignment.Right;
            head.Children.Add(_convertMeta);
            left.Children.Add(head);

            var targets = new Grid();
            for (var index = 0; index < 3; index++)
            {
                targets.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            _convertTargets[0] = Ui.TargetCard("DOCX", "Word", "文本、图片和段落", "convert-target");
            _convertTargets[1] = Ui.TargetCard("XLSX", "Excel", "规则表格", "convert-target");
            _convertTargets[2] = Ui.TargetCard("PPTX", "PPT", "整页视觉版", "convert-target");
            AutomationProperties.SetAutomationId(_convertTargets[0], "ConvertWord");
            AutomationProperties.SetAutomationId(_convertTargets[1], "ConvertExcel");
            AutomationProperties.SetAutomationId(_convertTargets[2], "ConvertPowerPoint");
            _convertTargets[0].IsChecked = true;
            for (var index = 0; index < 3; index++)
            {
                var radio = _convertTargets[index];
                radio.Margin = new Thickness(0, 0, index == 2 ? 0 : 12, 0);
                radio.Checked += delegate { UpdateConvertTarget(); };
                Grid.SetColumn(radio, index);
                targets.Children.Add(radio);
            }
            left.Children.Add(targets);

            var checkCard = new Border
            {
                Margin = new Thickness(0, 20, 0, 0),
                Padding = new Thickness(0, 16, 0, 0),
                BorderBrush = Ui.Brush("#E1E6EB"),
                BorderThickness = new Thickness(0, 1, 0, 0)
            };
            _convertCheck = Ui.Text("结构检查：等待文件。", 11, FontWeights.Normal, Ui.Muted);
            checkCard.Child = _convertCheck;
            left.Children.Add(checkCard);

            var preview = new Grid { Margin = new Thickness(0, 21, 0, 0) };
            for (var index = 0; index < 3; index++)
            {
                preview.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(5) };
                _convertImages[index] = image;
                var holder = new Grid { Height = 250, Background = Brushes.White };
                holder.Children.Add(image);
                var placeholder = Ui.Text("预览", 11, FontWeights.Normal, Ui.Muted);
                placeholder.HorizontalAlignment = HorizontalAlignment.Center;
                placeholder.VerticalAlignment = VerticalAlignment.Center;
                _convertPlaceholders[index] = placeholder;
                holder.Children.Add(placeholder);
                var card = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = Ui.Brush("#E2E7EC"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(6),
                    Margin = new Thickness(0, 0, index == 2 ? 0 : 10, 0),
                    Child = holder
                };
                Grid.SetColumn(card, index);
                preview.Children.Add(card);
            }
            left.Children.Add(preview);
            var effect = new Border
            {
                Margin = new Thickness(0, 18, 0, 0),
                Padding = new Thickness(15),
                Background = Ui.Brush("#F7F9FC"),
                BorderBrush = Ui.Brush("#E0E5EB"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            };
            _convertEffect = Ui.Text("Word 会保留文字和图片，复杂分栏不会完全还原。", 11, FontWeights.Normal, Ui.Muted);
            effect.Child = _convertEffect;
            left.Children.Add(effect);
            workspace.Children.Add(left);

            var settings = new StackPanel();
            settings.Children.Add(Ui.Text("转换设置", 20, FontWeights.SemiBold));
            settings.Children.Add(FieldLabel("输出文件", 21));
            _convertOutput = Ui.Input(PdfEngine.BuildOutputName(startupPlaceholder(), ".docx"), 36, 10);
            AutomationProperties.SetAutomationId(_convertOutput, "ConvertOutputPath");
            _convertOutput.FontFamily = new FontFamily("Consolas");
            settings.Children.Add(_convertOutput);
            var chooseOutput = Ui.Button("选择输出位置…", ButtonKind.Secondary, 32);
            chooseOutput.Margin = new Thickness(0, 7, 0, 0);
            chooseOutput.HorizontalAlignment = HorizontalAlignment.Left;
            chooseOutput.Click += delegate
            {
                var dialog = new SaveFileDialog
                {
                    Filter = TargetFilter(),
                    FileName = Path.GetFileName(_convertOutput.Text),
                    InitialDirectory = Directory.Exists(Path.GetDirectoryName(_convertOutput.Text))
                        ? Path.GetDirectoryName(_convertOutput.Text)
                        : _settings.OutputDirectory
                };
                if (dialog.ShowDialog(this) == true)
                {
                    _convertOutput.Text = dialog.FileName;
                }
            };
            settings.Children.Add(chooseOutput);
            settings.Children.Add(Ui.Divider());
            settings.Children.Add(FieldLabel("页面质量", 0));
            _convertDpi = new ComboBox { Height = 36, FontSize = 11 };
            AutomationProperties.SetAutomationId(_convertDpi, "ConvertDpi");
            _convertDpi.Items.Add(new ComboBoxItem { Content = "标准 · 150 DPI", Tag = 150 });
            _convertDpi.Items.Add(new ComboBoxItem { Content = "高清 · 200 DPI", Tag = 200 });
            _convertDpi.Items.Add(new ComboBoxItem { Content = "原始 · 300 DPI", Tag = 300 });
            _convertDpi.SelectedIndex = 1;
            settings.Children.Add(_convertDpi);
            _convertOcr = new CheckBox
            {
                Content = "扫描页自动 OCR",
                IsChecked = true,
                FontSize = 11,
                Margin = new Thickness(0, 14, 0, 0)
            };
            AutomationProperties.SetAutomationId(_convertOcr, "ConvertOcr");
            settings.Children.Add(_convertOcr);
            settings.Children.Add(Ui.Divider());
            settings.Children.Add(Ui.Text("转换只在本机完成，不上传文件。", 11, FontWeights.Normal, Ui.Muted));
            _convertStart = Ui.Button("开始转换", ButtonKind.Primary, 46);
            _convertStart.Margin = new Thickness(0, 16, 0, 0);
            _convertStart.Click += async delegate { await StartConvertAsync(); };
            AutomationProperties.SetName(_convertStart, "开始转换");
            AutomationProperties.SetAutomationId(_convertStart, "StartConvert");
            settings.Children.Add(_convertStart);
            var settingsCard = Ui.Card(settings, 22);
            Grid.SetColumn(settingsCard, 1);
            workspace.Children.Add(settingsCard);
            return scroll;
        }

        private string startupPlaceholder()
        {
            return _convertSourcePath ?? Path.Combine(_settings.OutputDirectory, "转换结果.pdf");
        }

        private string TargetFilter()
        {
            if (_convertTargets[1].IsChecked == true) return "Excel 工作簿 (*.xlsx)|*.xlsx";
            if (_convertTargets[2].IsChecked == true) return "演示文稿 (*.pptx)|*.pptx";
            return "Word 文档 (*.docx)|*.docx";
        }

        private OfficeTarget SelectedOfficeTarget()
        {
            if (_convertTargets[1].IsChecked == true) return OfficeTarget.Excel;
            if (_convertTargets[2].IsChecked == true) return OfficeTarget.PowerPoint;
            return OfficeTarget.Word;
        }

        private void UpdateConvertTarget()
        {
            if (_convertEffect == null) return;
            var target = SelectedOfficeTarget();
            var extension = target == OfficeTarget.Word ? ".docx" : target == OfficeTarget.Excel ? ".xlsx" : ".pptx";
            if (_convertSourcePath != null)
            {
                _convertOutput.Text = PdfEngine.BuildOutputName(_convertSourcePath, extension);
            }
            _convertEffect.Text = target == OfficeTarget.Word
                ? "Word 会保留文字和图片；复杂分栏、公式和浮动对象可能发生偏移。"
                : target == OfficeTarget.Excel
                    ? "Excel 只可靠还原规则表格；无框表和扫描表格可能拆错行列。"
                    : "PPT 每页输出为整页图片，视觉一致，但文字不可编辑。";
        }

        private async Task LoadConvertFile(string path)
        {
            try
            {
                _convertSourcePath = path;
                _convertPageCount = PdfEngine.GetPageCount(path);
                _convertOutput.Text = PdfEngine.BuildOutputName(
                    path,
                    SelectedOfficeTarget() == OfficeTarget.Word ? ".docx" :
                    SelectedOfficeTarget() == OfficeTarget.Excel ? ".xlsx" : ".pptx");
                _convertCheck.Text = "结构检查：" + _convertPageCount + " 页，正在读取文字…";
                UpdateConvertHeader();
                var files = await Task.Run(() => PdfEngine.RenderPreview(path, 3, 72));
                for (var index = 0; index < _convertImages.Length; index++)
                {
                    if (index < files.Count)
                    {
                        _convertImages[index].Source = Ui.LoadImage(files[index]).Source;
                        _convertPlaceholders[index].Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        _convertImages[index].Source = null;
                        _convertPlaceholders[index].Visibility = Visibility.Visible;
                    }
                }
                var folder = files.Count > 0 ? Path.GetDirectoryName(files[0]) : null;
                if (folder != null)
                {
                    try { Directory.Delete(folder, true); } catch { }
                }
                _convertCheck.Text = "结构检查：文件可打开，共 " + _convertPageCount + " 页。输出质量受 PDF 结构影响。";
                SetStatus("就绪 · " + _convertPageCount + " 页");
            }
            catch (Exception exception)
            {
                ShowError("无法打开 PDF", exception);
            }
        }

        private void UpdateConvertHeader()
        {
            if (_convertSourcePath == null)
            {
                _pageSubtitle.Text = "选择 PDF 后转换为 Word、Excel 或 PPT";
                return;
            }
            var file = new FileInfo(_convertSourcePath);
            _pageSubtitle.Text = file.Name + " · " + _convertPageCount + " 页 · " + HumanSize(file.Length);
        }

        private async Task StartConvertAsync()
        {
            if (_busy) return;
            if (_convertSourcePath == null)
            {
                MessageBox.Show(this, "请先打开 PDF。", "纸坞");
                return;
            }
            try
            {
                _busy = true;
                _convertStart.IsEnabled = false;
                _jobCancellation = new CancellationTokenSource();
                _cancelButton.Visibility = Visibility.Visible;
                var item = _convertDpi.SelectedItem as ComboBoxItem;
                var dpi = item == null ? 200 : Convert.ToInt32(item.Tag);
                var request = new ConvertRequest
                {
                    SourcePath = _convertSourcePath,
                    OutputPath = _convertOutput.Text,
                    Target = SelectedOfficeTarget(),
                    Dpi = dpi,
                    OcrScannedPages = _convertOcr.IsChecked == true
                };
                var progress = new Progress<string>(SetStatus);
                var output = await Task.Run(() => OfficeEngine.Convert(
                    request,
                    _settings,
                    _ocrPacks,
                    progress,
                    _jobCancellation.Token));
                SetStatus("转换完成");
                MessageBox.Show(this, "转换完成：\n" + output, "纸坞");
            }
            catch (OperationCanceledException)
            {
                SetStatus("已取消");
            }
            catch (Exception exception)
            {
                ShowError("转换失败", exception);
            }
            finally
            {
                _busy = false;
                _convertStart.IsEnabled = true;
                _cancelButton.Visibility = Visibility.Collapsed;
                if (_jobCancellation != null)
                {
                    _jobCancellation.Dispose();
                    _jobCancellation = null;
                }
            }
        }

        private FrameworkElement BuildSettingsView()
        {
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var workspace = new Grid { Margin = new Thickness(28, 24, 28, 22) };
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.7, GridUnitType.Star) });
            workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            scroll.Content = workspace;

            var left = new StackPanel();
            var ocrCardContent = new StackPanel();
            ocrCardContent.Children.Add(Ui.Text("OCR 来源", 20, FontWeights.SemiBold));
            ocrCardContent.Children.Add(Ui.Text(
                "可搜索 PDF 和扫描件转 Word 需要 OCR。优先使用系统能力；系统不可用时，导入离线包。",
                11,
                FontWeights.Normal,
                Ui.Muted));

            var systemRow = new Border
            {
                Background = Ui.Brush("#FAFCFE"),
                BorderBrush = Ui.Brush("#D7DED9"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12),
                Margin = new Thickness(0, 15, 0, 8)
            };
            var systemGrid = new Grid();
            systemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            systemGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var sysMark = new Border
            {
                Width = 36,
                Height = 36,
                Background = Ui.AccentSoft,
                CornerRadius = new CornerRadius(5),
                Child = new TextBlock
                {
                    Text = "SYS",
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 8,
                    Foreground = Ui.AccentDark,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            systemGrid.Children.Add(sysMark);
            var sysCopy = new StackPanel();
            sysCopy.Margin = new Thickness(12, 0, 0, 0);
            sysCopy.Children.Add(Ui.Text("Windows 系统 OCR", 11, FontWeights.SemiBold));
            _systemOcrState = Ui.Text("正在检测…", 9, FontWeights.Normal, Ui.Muted);
            sysCopy.Children.Add(_systemOcrState);
            Grid.SetColumn(sysCopy, 1);
            systemGrid.Children.Add(sysCopy);
            systemRow.Child = systemGrid;
            ocrCardContent.Children.Add(systemRow);

            _ocrPackList = new StackPanel();
            ocrCardContent.Children.Add(_ocrPackList);
            var importRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 13, 0, 0) };
            var import = Ui.Button("导入 OCR 包…", ButtonKind.Primary, 36);
            AutomationProperties.SetAutomationId(import, "ImportOcrPack");
            import.Click += delegate { ImportOcrPack(); };
            importRow.Children.Add(import);
            var openFolder = Ui.Button("打开包目录", ButtonKind.Secondary, 36);
            AutomationProperties.SetAutomationId(openFolder, "OpenOcrPackFolder");
            openFolder.Margin = new Thickness(8, 0, 0, 0);
            openFolder.Click += delegate
            {
                try { System.Diagnostics.Process.Start(AppPaths.OcrPackDirectory); } catch { }
            };
            importRow.Children.Add(openFolder);
            ocrCardContent.Children.Add(importRow);
            var importHint = Ui.Text(
                "支持 *.ocrpack.zip；导入时校验完整文件清单、逐文件 SHA-256 和 256 MB 解压上限。",
                9,
                FontWeights.Normal,
                Ui.Muted);
            importHint.Margin = new Thickness(0, 9, 0, 0);
            ocrCardContent.Children.Add(importHint);
            left.Children.Add(Ui.Card(ocrCardContent, 22, new Thickness(0, 0, 0, 16)));

            var outputCard = new StackPanel();
            outputCard.Children.Add(Ui.Text("输出默认值", 20, FontWeights.SemiBold));
            outputCard.Children.Add(FieldLabel("默认输出目录", 12));
            _defaultOutputDirectory = Ui.Input(_settings.OutputDirectory, 38, 10);
            AutomationProperties.SetAutomationId(_defaultOutputDirectory, "DefaultOutputDirectory");
            _defaultOutputDirectory.FontFamily = new FontFamily("Consolas");
            _defaultOutputDirectory.LostKeyboardFocus += delegate
            {
                if (!string.IsNullOrWhiteSpace(_defaultOutputDirectory.Text))
                {
                    _settings.OutputDirectory = _defaultOutputDirectory.Text;
                    AppSettingsStore.Save(_settings);
                }
            };
            outputCard.Children.Add(_defaultOutputDirectory);
            var choose = Ui.Button("选择目录…", ButtonKind.Secondary, 32);
            choose.HorizontalAlignment = HorizontalAlignment.Left;
            choose.Margin = new Thickness(0, 7, 0, 0);
            choose.Click += delegate
            {
                var picked = ChooseFolder(_defaultOutputDirectory.Text);
                if (picked != null)
                {
                    _defaultOutputDirectory.Text = picked;
                    _settings.OutputDirectory = picked;
                    AppSettingsStore.Save(_settings);
                }
            };
            outputCard.Children.Add(choose);
            left.Children.Add(Ui.Card(outputCard, 22));
            workspace.Children.Add(left);

            var right = new StackPanel();
            right.Children.Add(Ui.Text("当前状态", 20, FontWeights.SemiBold));
            _settingsOcrDetails = Ui.Text("应用不会自动下载或更新 OCR 包。", 11, FontWeights.Normal, Ui.Muted);
            _settingsOcrDetails.Margin = new Thickness(0, 6, 0, 15);
            right.Children.Add(_settingsOcrDetails);
            right.Children.Add(FieldLabel("默认 OCR", 0));
            _ocrChoice = new ComboBox { Height = 36, FontSize = 11, DisplayMemberPath = "Label" };
            AutomationProperties.SetAutomationId(_ocrChoice, "DefaultOcrChoice");
            _ocrChoice.SelectionChanged += delegate
            {
                var choice = _ocrChoice.SelectedItem as OcrChoice;
                if (choice == null) return;
                _settings.DefaultOcr = choice.Id == "system" ? "system" : choice.Engine;
                _settings.ActiveOcrPackId = choice.Id == "system" ? string.Empty : choice.Id;
                AppSettingsStore.Save(_settings);
                UpdateOcrIndicators();
            };
            right.Children.Add(_ocrChoice);
            var note = Ui.Text("当前版本不自动联网。没有系统 OCR 时，请导入合法授权的离线包。", 10, FontWeights.Normal, Ui.Muted);
            note.Margin = new Thickness(0, 14, 0, 0);
            right.Children.Add(note);
            var stateCard = Ui.Card(right, 22);
            Grid.SetColumn(stateCard, 1);
            workspace.Children.Add(stateCard);
            return scroll;
        }

        private sealed class OcrChoice
        {
            public string Id { get; set; }
            public string Engine { get; set; }
            public string Label { get; set; }

            public override string ToString()
            {
                return Label;
            }
        }

        private async Task ProbeOcrAsync()
        {
            try
            {
                var probe = await Task.Run(() => SystemPdfService.ProbeOcr());
                _systemOcrAvailable = probe != null && probe.available;
                _systemOcrState.Text = _systemOcrAvailable
                    ? "简体中文可用 · 无需额外空间"
                    : "简体中文不可用 · 请导入离线 OCR 包";
                _systemOcrState.Foreground = _systemOcrAvailable ? Ui.Success : Ui.Brush("#A15B00");
            }
            catch (Exception exception)
            {
                _systemOcrAvailable = false;
                _systemOcrState.Text = "检测失败";
                _systemOcrState.Foreground = Ui.Brush("#A15B00");
                _ocrSideText.Text = exception.Message;
            }
            RefreshOcrChoice();
            RefreshOcrPackList();
            UpdateOcrIndicators();
        }

        private void RefreshOcrChoice()
        {
            if (_ocrChoice == null) return;
            _ocrChoice.Items.Clear();
            _ocrChoice.Items.Add(new OcrChoice
            {
                Id = "system",
                Engine = "system",
                Label = _systemOcrAvailable
                    ? "Windows 系统 OCR"
                    : "Windows 系统 OCR（当前不可用）"
            });
            foreach (var pack in _ocrPacks)
            {
                _ocrChoice.Items.Add(new OcrChoice
                {
                    Id = pack.Manifest.Id,
                    Engine = "tesseract",
                    Label = pack.DisplayName
                });
            }
            var selectedId = _settings.DefaultOcr == "system" || string.IsNullOrWhiteSpace(_settings.ActiveOcrPackId)
                ? "system"
                : _settings.ActiveOcrPackId;
            _ocrChoice.SelectedItem = _ocrChoice.Items.Cast<OcrChoice>()
                .FirstOrDefault(item => item.Id == selectedId);
            if (_ocrChoice.SelectedItem == null)
            {
                _ocrChoice.SelectedIndex = 0;
            }
            UpdateOcrIndicators();
        }

        private void UpdateOcrIndicators()
        {
            if (_ocrSideLabel == null || _headerOcrLabel == null) return;

            var activePack = _ocrPacks.FirstOrDefault(item =>
                !string.Equals(_settings.DefaultOcr, "system", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    item.Manifest.Id,
                    _settings.ActiveOcrPackId,
                    StringComparison.OrdinalIgnoreCase));
            var available = _systemOcrAvailable || activePack != null;
            var statusBrush = available ? Ui.Success : Ui.Brush("#A15B00");
            _ocrSideDot.Background = statusBrush;
            _headerOcrDot.Background = statusBrush;
            _ocrSideLabel.Text = available ? "OCR 可用" : "OCR 不可用";
            _ocrSideLabel.Foreground = available ? Ui.Ink : Ui.Brush("#8A4B00");
            _headerOcrLabel.Text = activePack != null
                ? "离线 OCR"
                : _systemOcrAvailable ? "系统 OCR" : "OCR 不可用";
            _ocrSideText.Text = activePack != null
                ? activePack.DisplayName
                : _systemOcrAvailable ? "Windows 系统识别" : "请导入离线 OCR 包";
        }

        private void RefreshOcrPackList()
        {
            if (_ocrPackList == null) return;
            _ocrPackList.Children.Clear();
            if (_ocrPacks.Count == 0)
            {
                _ocrPackList.Children.Add(Ui.Text("尚未导入离线 OCR 包", 10, FontWeights.Normal, Ui.Muted));
                return;
            }
            foreach (var pack in _ocrPacks)
            {
                var row = new Border
                {
                    Background = Ui.Brush("#FAFCFE"),
                    BorderBrush = Ui.Brush("#DFE5EA"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var mark = new Border
                {
                    Width = 36,
                    Height = 36,
                    Background = Ui.Brush("#EEF3F7"),
                    CornerRadius = new CornerRadius(5),
                    Child = new TextBlock
                    {
                        Text = "PKG",
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 8,
                        Foreground = Ui.Brush("#5E7080"),
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                grid.Children.Add(mark);
                var copy = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
                copy.Children.Add(Ui.Text(pack.DisplayName, 11, FontWeights.SemiBold));
                copy.Children.Add(Ui.Text("已导入 · " + pack.Manifest.Language + " · 离线可用", 9, FontWeights.Normal, Ui.Muted));
                Grid.SetColumn(copy, 1);
                grid.Children.Add(copy);
                var delete = Ui.Button("移除", ButtonKind.Quiet, 28, new Thickness(8, 0, 8, 0));
                delete.Background = Brushes.White;
                delete.BorderBrush = Ui.Brush("#D7E0E8");
                delete.Click += delegate
                {
                    if (MessageBox.Show(this, "移除 OCR 包“" + pack.DisplayName + "”？", "确认", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                    {
                        return;
                    }
                    try
                    {
                        OcrPackManager.Delete(pack);
                        if (_settings.ActiveOcrPackId == pack.Manifest.Id)
                        {
                            _settings.DefaultOcr = "system";
                            _settings.ActiveOcrPackId = string.Empty;
                            AppSettingsStore.Save(_settings);
                        }
                        _ocrPacks = OcrPackManager.List().ToList();
                        RefreshOcrPackList();
                        RefreshOcrChoice();
                        UpdateOcrIndicators();
                    }
                    catch (Exception exception)
                    {
                        ShowError("无法移除 OCR 包", exception);
                    }
                };
                Grid.SetColumn(delete, 2);
                grid.Children.Add(delete);
                row.Child = grid;
                _ocrPackList.Children.Add(row);
            }
        }

        private void ImportOcrPack()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "OCR 包 (*.ocrpack.zip)|*.ocrpack.zip|ZIP 文件 (*.zip)|*.zip",
                CheckFileExists = true,
                Title = "导入离线 OCR 包"
            };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                var imported = OcrPackManager.Import(dialog.FileName);
                _ocrPacks = OcrPackManager.List().ToList();
                _settings.DefaultOcr = "tesseract";
                _settings.ActiveOcrPackId = imported.Manifest.Id;
                AppSettingsStore.Save(_settings);
                RefreshOcrPackList();
                RefreshOcrChoice();
                UpdateOcrIndicators();
                SetStatus("OCR 包已导入");
            }
            catch (Exception exception)
            {
                ShowError("OCR 包导入失败", exception);
            }
        }

        private void SetStatus(string text)
        {
            if (_statusText != null)
            {
                _statusText.Text = text;
            }
        }

        private void ShowError(string title, Exception exception)
        {
            SetStatus(title + "：" + exception.Message);
            try
            {
                File.AppendAllText(
                    Path.Combine(AppPaths.Data, "error.log"),
                    DateTime.Now.ToString("O") + " " + title + Environment.NewLine +
                    exception + Environment.NewLine + Environment.NewLine);
            }
            catch
            {
            }
            MessageBox.Show(this, exception.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private string ChooseFolder(string initial)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "选择输出目录",
                SelectedPath = Directory.Exists(initial) ? initial : _settings.OutputDirectory
            };
            return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK ? dialog.SelectedPath : null;
        }

        private void PrepareOutputDirectory()
        {
            if (string.IsNullOrWhiteSpace(_settings.OutputDirectory))
            {
                _settings.OutputDirectory = new AppSettings().OutputDirectory;
            }
            try
            {
                Directory.CreateDirectory(_settings.OutputDirectory);
            }
            catch
            {
                _settings.OutputDirectory = new AppSettings().OutputDirectory;
                try { Directory.CreateDirectory(_settings.OutputDirectory); } catch { }
            }
        }

        private static string HumanSize(long bytes)
        {
            if (bytes >= 1024 * 1024) return Math.Round(bytes / 1024.0 / 1024.0, 1) + " MB";
            if (bytes >= 1024) return Math.Round(bytes / 1024.0) + " KB";
            return bytes + " B";
        }
    }
}
