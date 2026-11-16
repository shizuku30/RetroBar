using ManagedShell.AppBar;
using ManagedShell.WindowsTasks;
using RetroBar.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static partial class SystemIconTests
{
    public static void CheckWin7TaskbarRows(Application app, string root)
    {
        using IsolatedSettings settings = new IsolatedSettings();
        settings.Set("Theme", "Windows7");
        settings.Set("Edge", AppBarEdge.Bottom);
        settings.Set("LockTaskbar", true);
        settings.Set("SlideTaskbarButtons", false);
        ResourceDictionary theme = new ResourceDictionary
        {
            Source = new Uri(Path.Combine(root, "RetroBar", "Themes", "Windows7.xaml"))
        };
        app.Resources.MergedDictionaries.Add(theme);
        AlignmentWindow host = new AlignmentWindow { Orientation = Orientation.Horizontal, UseLayoutRounding = true };
        List<string> errors = new List<string>();
        try
        {
            BitmapSource icon = LoadApplicationIcon(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"));
            foreach (int rows in new[] { 1, 2 })
            foreach (double dpiScale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
            foreach (double scale in new[] { 1.0, 1.5 })
            {
                settings.Set("RowCount", rows);
                settings.Set("TaskbarScale", scale);
                host.Rows = rows;
                host.UseLayoutRounding = dpiScale % 1 == 0;
                Toolbar toolbar = new Toolbar();
                ItemsControl pinnedItems = (ItemsControl)toolbar.FindName("ToolbarItems");
                pinnedItems.ItemsSource = new[]
                {
                    new PinnedButtonPreview { LargeIcon = icon, SmallIcon = icon },
                    new PinnedButtonPreview { LargeIcon = icon, SmallIcon = icon }
                };
                TaskList tasks = new TaskList { ButtonWidth = 60, ButtonsPerRow = 2 };
                ItemsControl taskItems = (ItemsControl)tasks.FindName("TasksList");
                taskItems.ItemsSource = new[]
                {
                    new TaskButtonPreview { Icon = icon, State = ApplicationWindow.WindowState.Inactive },
                    new TaskButtonPreview { Icon = icon, State = ApplicationWindow.WindowState.Active },
                    new TaskButtonPreview { Icon = icon, State = ApplicationWindow.WindowState.Flashing },
                    new TaskButtonPreview { Icon = icon, State = ApplicationWindow.WindowState.Inactive }
                };
                DockPanel strip = new DockPanel();
                strip.Children.Add(toolbar);
                strip.Children.Add(tasks);
                double height = (double)app.FindResource("TaskbarHeight") + (rows - 1) * (double)app.FindResource("TaskbarRowHeight");
                ContentControl taskbar = new ContentControl
                {
                    Width = rows == 1 ? 360 : 240, Height = height,
                    Style = (Style)app.FindResource("Taskbar"), Content = strip
                };
                Grid scene = new Grid
                {
                    Background = new SolidColorBrush(Color.FromRgb(54, 150, 188)),
                    LayoutTransform = new ScaleTransform(scale, scale)
                };
                scene.Children.Add(taskbar);
                host.Content = scene;
                scene.Measure(new Size(2400, 800));
                scene.Arrange(new Rect(scene.DesiredSize));
                scene.UpdateLayout();
                Visual dpiRoot = scene;
                while (VisualTreeHelper.GetParent(dpiRoot) is Visual parent) dpiRoot = parent;
                VisualTreeHelper.SetRootDpi(dpiRoot, new DpiScale(dpiScale, dpiScale));
                scene.Measure(new Size(2400, 800));
                scene.Arrange(new Rect(scene.DesiredSize));
                scene.UpdateLayout();

                Image[] runningIcons = Descendants(tasks).OfType<Image>().Where(image =>
                    BindingOperations.GetBinding(image, Image.SourceProperty)?.Path.Path == "Icon").ToArray();
                Image[] pinnedIcons = Descendants(toolbar).OfType<Image>().Where(image => image.Name == "ToolbarIcon").ToArray();
                Assert(runningIcons.Length == 4 && pinnedIcons.Length == 2, "Mixed taskbar rows did not use the real item templates.");
                foreach (Image image in pinnedIcons.Concat(runningIcons))
                {
                    Assert(VisualTreeHelper.GetDpi(image).DpiScaleY == dpiScale,
                        $"The row test must use real WPF DPI: expected={dpiScale}, actual={VisualTreeHelper.GetDpi(image).DpiScaleY}.");
                    Rect bounds = image.TransformToAncestor(taskbar).TransformBounds(new Rect(image.RenderSize));
                    double rowTop = Math.Round(bounds.Top / 40) * 40;
                    if (Math.Abs(bounds.Width - 32) > 0.01 || Math.Abs(bounds.Height - 32) > 0.01 || Math.Abs(bounds.Top - rowTop - 4) * scale * dpiScale > 0.51)
                        errors.Add($"rows={rows}/dpi={dpiScale}/scale={scale}/{image.Name}: icon={bounds}, expected=32x32 at row+4");
                    for (DependencyObject ancestor = image; ancestor != taskbar; ancestor = VisualTreeHelper.GetParent(ancestor))
                    {
                        if (ancestor is UIElement element && VisualTreeHelper.GetClip(element) is Geometry clip)
                        {
                            Rect relative = image.TransformToAncestor(element).TransformBounds(new Rect(image.RenderSize));
                            if (!clip.Bounds.Contains(relative)) errors.Add($"rows={rows}/scale={scale}: an ancestor clips the icon: {relative} vs {clip.Bounds}");
                        }
                    }
                }
                foreach (Button button in Descendants(tasks).OfType<Button>().Where(button => button.Name == "AppButton"))
                {
                    if (Math.Abs(button.ActualHeight - 40) > 0.01)
                        errors.Add($"rows={rows}/scale={scale}: task button height={button.ActualHeight}, expected=40");
                }
                string path = Path.Combine(root, "artifacts", "smoke", $"net{Environment.Version.Major}", $"win7-mixed-taskbar-rows{rows}-dpi{dpiScale * 100:0}-scale{scale * 100:0}.png");
                SaveVisual(scene, (int)Math.Ceiling(scene.DesiredSize.Width * dpiScale), (int)Math.Ceiling(scene.DesiredSize.Height * dpiScale), path, 96 * dpiScale);
                host.Content = null;
            }
            Console.WriteLine("  Checked 20 mixed taskbars with natural row sizing, real 100-200% DPI and separate taskbar scaling.");
            Assert(errors.Count == 0, $"Mixed taskbar geometry failed:\n{string.Join("\n", errors.Take(12))}");
        }
        finally
        {
            host.Content = null;
            host.Close();
            app.Resources.MergedDictionaries.Remove(theme);
        }
    }

    private sealed class PinnedButtonPreview
    {
        public BitmapSource LargeIcon { get; init; }
        public BitmapSource SmallIcon { get; init; }
        public string DisplayName => "Pinned visual regression";
    }
}
