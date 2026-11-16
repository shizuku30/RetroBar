using ManagedShell.Interop;
using ManagedShell.WindowsTasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static partial class SystemIconTests
{
    public static void CheckWin7TaskButtons(Application app, string root)
    {
        using IsolatedSettings settings = new IsolatedSettings();
        settings.Set("Theme", "Windows7");
        ResourceDictionary theme = new ResourceDictionary
        {
            Source = new Uri(Path.Combine(root, "RetroBar", "Themes", "Windows7.xaml"))
        };
        app.Resources.MergedDictionaries.Add(theme);
        AlignmentWindow host = new AlignmentWindow();
        List<string> errors = new List<string>();
        int checkedButtons = 0;
        try
        {
            string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            BitmapSource[] icons = new[] { "explorer.exe", "System32/control.exe", "System32/cmd.exe", "System32/notepad.exe" }
                .Select(file => LoadApplicationIcon(Path.Combine(windows, file))).ToArray();
            var states = new[]
            {
                ("Inactive", ApplicationWindow.WindowState.Inactive, false, false),
                ("Hover", ApplicationWindow.WindowState.Inactive, true, false),
                ("Pressed", ApplicationWindow.WindowState.Inactive, true, true),
                ("Active", ApplicationWindow.WindowState.Active, false, false),
                ("Active hover", ApplicationWindow.WindowState.Active, true, false),
                ("Flashing", ApplicationWindow.WindowState.Flashing, false, false)
            };
            foreach (Orientation orientation in new[] { Orientation.Horizontal, Orientation.Vertical })
            foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            foreach (double width in new[] { 52.0, 60.0, 84.0 })
            foreach (double scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
            foreach (bool badges in new[] { false, true })
            {
                settings.Set("ShowTaskBadges", badges);
                host.Orientation = orientation;
                StackPanel scene = new StackPanel { LayoutTransform = new ScaleTransform(scale, scale) };
                StackPanel labels = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.White };
                StackPanel row = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Background = new LinearGradientBrush(Color.FromRgb(68, 157, 188), Color.FromRgb(93, 184, 214), 90)
                };
                List<Button> buttons = new List<Button>();
                for (int i = 0; i < states.Length; i++)
                {
                    var state = states[i];
                    labels.Children.Add(new TextBlock { Text = state.Item1, FontSize = 10, Foreground = Brushes.Black, Width = width, Height = 16, TextAlignment = TextAlignment.Center });
                    RetroBar.Controls.TaskButton task = new RetroBar.Controls.TaskButton
                    {
                        Width = width, Height = 40, FlowDirection = flow,
                        DataContext = new TaskButtonPreview
                        {
                            Icon = icons[i % icons.Length], OverlayIcon = icons[1], State = state.Item2
                        }
                    };
                    row.Children.Add(task);
                    Button button = (Button)task.FindName("AppButton");
                    button.ApplyTemplate();
                    // 只驱动测试控件的状态，不移动真实鼠标或切换桌面窗口。
                    SetInputState(button, typeof(UIElement), "IsMouseOverPropertyKey", state.Item3);
                    SetInputState(button, typeof(ButtonBase), "IsPressedPropertyKey", state.Item4);
                    buttons.Add(button);
                }
                scene.Children.Add(labels);
                scene.Children.Add(row);
                host.Content = scene;
                scene.Measure(new Size(2400, 200));
                scene.Arrange(new Rect(scene.DesiredSize));
                scene.UpdateLayout();
                for (int i = 0; i < buttons.Count; i++)
                {
                    Button button = buttons[i];
                    Image icon = Descendants(button).OfType<Image>().Single(image =>
                        BindingOperations.GetBinding(image, Image.SourceProperty)?.Path.Path == "Icon");
                    // 水平模式居中图标；竖向保留原有标题，居中完整的图标/标题内容区。
                    FrameworkElement content = orientation == Orientation.Horizontal
                        ? icon : (FrameworkElement)button.Template.FindName("Content", button);
                    Point center = content.TransformToAncestor(button).Transform(new Point(content.ActualWidth / 2, content.ActualHeight / 2));
                    Vector offset = center - new Point(button.ActualWidth / 2, button.ActualHeight / 2);
                    if (Math.Abs(offset.X * scale) > 0.51 || Math.Abs(offset.Y * scale) > 0.51)
                        errors.Add($"{orientation}/{flow}/width={width}/scale={scale}/badges={badges}: offset={offset}");
                    var state = states[i];
                    string imageKey = state.Item4 ? "Win7_ActivePointerOverImage"
                        : state.Item2 == ApplicationWindow.WindowState.Active ? (state.Item3 ? "Win7_ActivePointerOverImage" : "Win7_ActiveNormalImage")
                        : state.Item2 == ApplicationWindow.WindowState.Flashing ? "Win7_RequestingAttentionImage"
                        : state.Item3 ? "Win7_InactivePointerOverImage" : "Win7_InactiveNormalImage";
                    RetroBar.Controls.TaskButtonChrome chrome = (RetroBar.Controls.TaskButtonChrome)button.Template.FindName("ButtonChrome", button);
                    Assert(ReferenceEquals(chrome.Source, app.FindResource(imageKey)), $"Wrong task-button image for {state.Item1}.");
                    Assert(!chrome.IsHitTestVisible, "Decorative button chrome must not intercept input.");
                    Assert(ReferenceEquals(icon.Source, icons[i % icons.Length]), "Task-button styling changed the application's icon.");
                    checkedButtons++;
                }
                if (orientation == Orientation.Horizontal && flow == FlowDirection.LeftToRight && width == 60 && !badges)
                {
                    SaveVisual(scene, (int)Math.Ceiling(scene.DesiredSize.Width), (int)Math.Ceiling(scene.DesiredSize.Height),
                        Path.Combine(root, "artifacts", "smoke", $"net{Environment.Version.Major}", $"win7-task-buttons-{scale * 100:0}.png"));
                }
                host.Content = null;
            }
            CheckTaskButtonChromeEdges((BitmapSource)app.FindResource("Win7_InactiveNormalImage"));
            Console.WriteLine($"  Checked {checkedButtons} actual task buttons across states, widths, scales, directions and badges.");
            Assert(errors.Count == 0, $"{errors.Count} task contents are not centered:\n{string.Join("\n", errors.Take(8))}");
        }
        finally
        {
            host.Content = null;
            host.Close();
            app.Resources.MergedDictionaries.Remove(theme);
        }
    }

    private static void CheckTaskButtonChromeEdges(BitmapSource source)
    {
        BitmapSource original = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        foreach (int width in new[] { 50, 58, 82, 180 })
        {
            RetroBar.Controls.TaskButtonChrome chrome = new RetroBar.Controls.TaskButtonChrome { Source = source };
            chrome.Measure(new Size(width, 40));
            chrome.Arrange(new Rect(0, 0, width, 40));
            RenderTargetBitmap rendered = new RenderTargetBitmap(width, 40, 96, 96, PixelFormats.Pbgra32);
            rendered.Render(chrome);
            foreach (var rectangles in new[]
            {
                (new Int32Rect(0, 20, 3, 1), new Int32Rect(0, 20, 3, 1)),
                (new Int32Rect(width - 3, 20, 3, 1), new Int32Rect(source.PixelWidth - 3, 20, 3, 1)),
                (new Int32Rect(width / 2, 0, 1, 3), new Int32Rect(source.PixelWidth / 2, 0, 1, 3))
            })
            {
                byte[] actual = new byte[12];
                byte[] expected = new byte[12];
                rendered.CopyPixels(rectangles.Item1, actual, rectangles.Item1.Width * 4, 0);
                original.CopyPixels(rectangles.Item2, expected, rectangles.Item2.Width * 4, 0);
                Assert(actual.Zip(expected).All(pair => Math.Abs(pair.First - pair.Second) <= 1),
                    $"Resizing the button to {width}px blurred or widened its original black edge.");
            }
        }
    }

    private static BitmapSource LoadApplicationIcon(string path)
    {
        using System.Drawing.Icon icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
        BitmapSource bitmap = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        bitmap.Freeze();
        return bitmap;
    }

    private static void SetInputState(Button button, Type owner, string keyName, bool value)
    {
        DependencyPropertyKey key = (DependencyPropertyKey)owner.GetField(keyName, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        button.SetValue(key, value);
    }

    private sealed class TaskButtonPreview
    {
        public BitmapSource Icon { get; init; }
        public BitmapSource OverlayIcon { get; init; }
        public ApplicationWindow.WindowState State { get; init; }
        public string Title => "Visual regression";
        public int ProgressValue => -1;
        public NativeMethods.TBPFLAG ProgressState => NativeMethods.TBPFLAG.TBPF_NOPROGRESS;
    }
}
