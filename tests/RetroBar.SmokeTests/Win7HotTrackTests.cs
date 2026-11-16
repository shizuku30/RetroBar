using ManagedShell.WindowsTasks;
using RetroBar.Controls;
using RetroBar.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

internal static partial class SystemIconTests
{
    public static void CheckWin7IconColors()
    {
        Color red = Color.FromRgb(224, 32, 32), blue = Color.FromRgb(32, 32, 224);
        Assert(Win7IconColor.GetDominantColor(ColorIcon(Enumerable.Repeat(Colors.White, 1024).ToArray())) == Win7IconColor.DefaultColor, "Gray pixels must fall back to Aero GLOWCOLOR.");
        Assert(Win7IconColor.GetDominantColor(ColorIcon(Enumerable.Repeat(Color.FromRgb(100, 100, 148), 1024).ToArray())) == Win7IconColor.DefaultColor, "A saturation span of 48 must be excluded.");
        Color threshold = Color.FromRgb(100, 100, 149);
        Assert(Win7IconColor.GetDominantColor(ColorIcon(Enumerable.Repeat(threshold, 1024).ToArray())) == threshold, "A saturation span of 49 must be included.");
        Color[] majority = Enumerable.Repeat(red, 600).Concat(Enumerable.Repeat(blue, 300)).Concat(Enumerable.Repeat(Colors.White, 124)).ToArray();
        Assert(Win7IconColor.GetDominantColor(ColorIcon(majority)) == red, "The largest RGB bucket must win, not the overall image average.");
        Color[] tie = Enumerable.Repeat(red, 512).Concat(Enumerable.Repeat(blue, 512)).ToArray();
        Assert(Win7IconColor.GetDominantColor(ColorIcon(tie)) == blue, "Native ties must keep the first RGB bucket.");
        Color[] alpha = Enumerable.Repeat(Color.FromArgb(128, 255, 0, 0), 20)
            .Concat(Enumerable.Repeat(Colors.Blue, 10)).Concat(Enumerable.Repeat(Color.FromArgb(0, 0, 255, 0), 994)).ToArray();
        Assert(Win7IconColor.GetDominantColor(ColorIcon(alpha)) == Colors.Red, "Native sampling must unpremultiply, ignore transparent pixels, and not alpha-weight eligible pixels.");
        Color[] diffuse = (from r in Enumerable.Range(0, 3)
                           from g in Enumerable.Range(0, 3)
                           from b in Enumerable.Range(0, 3)
                           select Color.FromRgb((byte)(r * 86 + (r == g && g == b ? 60 : 20)), (byte)(g * 86), (byte)(b * 86)))
            .Concat(Enumerable.Repeat(Colors.Transparent, 997)).ToArray();
        Assert(Win7IconColor.GetDominantColor(ColorIcon(diffuse)) == Win7IconColor.DefaultColor, "No bucket reaches the native 7 percent dominance threshold.");
    }

    public static void CheckWin7HotTracking(Application app, string root)
    {
        using IsolatedSettings settings = new IsolatedSettings();
        settings.Set("Theme", "Windows7");
        ResourceDictionary theme = new ResourceDictionary { Source = new Uri(Path.Combine(root, "RetroBar", "Themes", "Windows7.xaml")) };
        app.Resources.MergedDictionaries.Add(theme);
        AlignmentWindow host = new AlignmentWindow { Orientation = Orientation.Horizontal, Rows = 1 };
        try
        {
            Color[] colors = { Color.FromRgb(220, 48, 36), Color.FromRgb(224, 184, 32), Color.FromRgb(32, 112, 224), Colors.Gray };
            BitmapSource[] icons = colors.Select(PreviewGlyph).ToArray();
            StackPanel page = new StackPanel();
            List<(Button Button, FrameworkElement Fade, TaskButtonHotTrack Glow, double X)> effects = new List<(Button, FrameworkElement, TaskButtonHotTrack, double)>();
            foreach (double x in new[] { 7.0, 29.0, 51.0 })
            {
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromRgb(54, 150, 188)) };
                for (int i = 0; i < 6; i++)
                {
                    FrameworkElement control = i < 4
                        ? new RetroBar.Controls.TaskButton { Width = 60, DataContext = new TaskButtonPreview { Icon = icons[i], State = ApplicationWindow.WindowState.Inactive } }
                        : new ToolbarButton { DataContext = new PinnedButtonPreview { LargeIcon = icons[i - 4], SmallIcon = icons[i - 4] } };
                    row.Children.Add(control);
                }
                page.Children.Add(row);
                host.Content = page;
                page.Measure(new Size(1000, 300));
                page.Arrange(new Rect(page.DesiredSize));
                page.UpdateLayout();
                foreach (Button button in Descendants(row).OfType<Button>())
                {
                    TaskButtonHotTrack glow = button.Template.FindName("HotTrack", button) as TaskButtonHotTrack;
                    FrameworkElement fade = glow ?? (FrameworkElement)button.Template.FindName("HoverFade", button);
                    Assert(fade != null && !fade.IsHitTestVisible, "The hover fade is missing or intercepts native button input.");
                    if (button.Content is Image)
                        Assert(!Descendants(button).OfType<TaskButtonHotTrack>().Any(), "Closed shortcuts must not sample icon colors or paint a pointer-following glow.");
                    else
                        Assert(glow != null, "Running windows must retain icon-colored pointer tracking.");
                    SetInputState(button, typeof(UIElement), "IsMouseOverPropertyKey", true);
                    fade.BeginAnimation(UIElement.OpacityProperty, null);
                    fade.Opacity = 1;
                    if (glow != null) glow.PointerPosition = new Point(x, 35);
                    effects.Add((button, fade, glow, x));
                }
                page.Children.Insert(page.Children.Count - 1, new TextBlock
                {
                    Text = x < 20 ? "Pointer: left" : x > 40 ? "Pointer: right" : "Pointer: center",
                    Height = 16, Foreground = Brushes.Black, Background = Brushes.White
                });
            }
            page.Measure(new Size(1000, 300));
            page.Arrange(new Rect(page.DesiredSize));
            page.UpdateLayout();
            foreach (double dpiScale in new[] { 1.0, 1.25, 1.5, 2.0 })
            {
                VisualTreeHelper.SetRootDpi(page, new DpiScale(dpiScale, dpiScale));
                page.Measure(new Size(1000, 300));
                page.Arrange(new Rect(page.DesiredSize));
                page.UpdateLayout();
                SaveVisual(page, (int)Math.Ceiling(page.DesiredSize.Width * dpiScale), (int)Math.Ceiling(page.DesiredSize.Height * dpiScale),
                    Path.Combine(root, "artifacts", "smoke", $"net{Environment.Version.Major}", $"win7-hottrack-follow-{dpiScale * 100:0}.png"), 96 * dpiScale);
            }

            byte[] redShortcut = ReadButtonFramePixels(effects[^2].Button);
            byte[] yellowShortcut = ReadButtonFramePixels(effects[^1].Button);
            int backgroundPixel = (30 * 58 + 4) * 4;
            Assert(redShortcut.Skip(backgroundPixel).Take(4).SequenceEqual(yellowShortcut.Skip(backgroundPixel).Take(4)),
                "Closed shortcut backgrounds must not change with the icon's dominant color.");
            var pinned = effects.Last();
            TaskButtonChrome chrome = (TaskButtonChrome)pinned.Button.Template.FindName("ButtonChrome", pinned.Button);
            Assert(chrome.Source.PixelWidth == 60 && chrome.Source.PixelHeight == 40, "Closed pinned states must use the full native 60x40 frame, not the cropped tray effect.");
            Assert(ReferenceEquals(chrome.Source, app.FindResource("Win7_NotRunningPointerOverImage")) && chrome.Opacity == 1, "The closed pinned hover image is not displayed.");
            SetInputState(pinned.Button, typeof(ButtonBase), "IsPressedPropertyKey", true);
            Assert(ReferenceEquals(chrome.Source, app.FindResource("Win7_NotRunningPressedImage")), "Closed pinned pressed feedback must use native state 10.");
            SetInputState(pinned.Button, typeof(ButtonBase), "IsPressedPropertyKey", false);
            SetInputState(pinned.Button, typeof(UIElement), "IsMouseOverPropertyKey", false);
            pinned.Fade.BeginAnimation(UIElement.OpacityProperty, null);
            pinned.Fade.Opacity = 0;
            Assert(chrome.Opacity == 0, "A closed pinned icon must have no frame when not hovered.");

            TaskButtonHotTrack probe = new TaskButtonHotTrack { Icon = icons[0] };
            byte[] left = RenderGlow(probe, new Point(7, 39));
            byte[] right = RenderGlow(probe, new Point(51, 39));
            int leftPixel = (34 * 58 + 8) * 4, rightPixel = (34 * 58 + 49) * 4;
            Assert(left[leftPixel + 3] > right[leftPixel + 3] && right[rightPixel + 3] > left[rightPixel + 3], "The glow does not follow horizontal pointer movement.");
            Assert(left[leftPixel + 2] > left[leftPixel] && left[leftPixel + 2] > left[leftPixel + 1], "The red application's glow lost its dominant color.");
            Assert(Enumerable.Range(0, 58).All(column => left[column * 4 + 3] == 0), "The glow escapes the task-button bounds.");
            probe.IconOnly = false;
            byte[] labeled = RenderGlow(probe, new Point(7, 39));
            Assert(left[leftPixel + 1] > labeled[leftPixel + 1], "Icon-only mode must use the native brighter inner core.");
            WriteableBitmap mutable = new WriteableBitmap(icons[0]);
            probe.Icon = mutable;
            byte[] replacement = new byte[32 * 32 * 4];
            icons[2].CopyPixels(replacement, 32 * 4, 0);
            mutable.WritePixels(new Int32Rect(0, 0, 32, 32), replacement, 32 * 4, 0);
            Assert(probe.DominantColor == Win7IconColor.GetDominantColor(mutable), "Changing the displayed icon's pixels did not refresh its glow color.");
            probe.Orientation = Orientation.Vertical;
            byte[] top = RenderGlow(probe, new Point(29, 7));
            byte[] bottom = RenderGlow(probe, new Point(29, 33));
            int topPixel = (8 * 58 + 29) * 4, bottomPixel = (31 * 58 + 29) * 4;
            Assert(top[topPixel + 3] > bottom[topPixel + 3] && bottom[bottomPixel + 3] > top[bottomPixel + 3], "The glow does not follow vertical pointer movement.");

            // 直接驱动真实模板控件的加载/卸载和鼠标路由，不移动用户的系统鼠标。
            TaskButtonHotTrack listener = effects[0].Glow;
            Button owner = effects[0].Button;
            SetInputState(owner, typeof(UIElement), "IsMouseOverPropertyKey", false);
            listener.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            listener.PointerPosition = new Point(double.NaN, double.NaN);
            owner.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent });
            Assert(!double.IsNaN(listener.PointerPosition.X), "The effect did not subscribe to the button's actual mouse movement.");
            listener.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            listener.PointerPosition = new Point(17, 23);
            owner.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseMoveEvent });
            Assert(listener.PointerPosition == new Point(17, 23), "An obsolete effect still handles input after unloading.");
        }
        finally
        {
            host.Content = null;
            host.Close();
            app.Resources.MergedDictionaries.Remove(theme);
        }
    }

    private static byte[] ReadButtonFramePixels(Button button)
    {
        RenderTargetBitmap bitmap = new RenderTargetBitmap(58, 40, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(button);
        byte[] pixels = new byte[58 * 40 * 4];
        bitmap.CopyPixels(pixels, 58 * 4, 0);
        return pixels;
    }

    private static BitmapSource PreviewGlyph(Color color)
    {
        DrawingVisual visual = new DrawingVisual();
        using (DrawingContext context = visual.RenderOpen())
        {
            context.DrawRoundedRectangle(new SolidColorBrush(color), new Pen(Brushes.White, 0.75), new Rect(6, 4, 21, 25), 2, 2);
            context.DrawRectangle(Brushes.White, null, new Rect(11, 10, 11, 2));
            context.DrawRectangle(Brushes.White, null, new Rect(11, 15, 8, 2));
        }
        RenderTargetBitmap bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] RenderGlow(TaskButtonHotTrack glow, Point pointer)
    {
        glow.PointerPosition = pointer;
        glow.Measure(new Size(58, 40));
        glow.Arrange(new Rect(0, 0, 58, 40));
        RenderTargetBitmap bitmap = new RenderTargetBitmap(58, 40, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(glow);
        byte[] pixels = new byte[58 * 40 * 4];
        bitmap.CopyPixels(pixels, 58 * 4, 0);
        return pixels;
    }

    private static BitmapSource ColorIcon(Color[] colors)
    {
        byte[] bytes = new byte[32 * 32 * 4];
        for (int i = 0; i < colors.Length; i++)
        {
            bytes[i * 4] = colors[i].B;
            bytes[i * 4 + 1] = colors[i].G;
            bytes[i * 4 + 2] = colors[i].R;
            bytes[i * 4 + 3] = colors[i].A;
        }
        BitmapSource source = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, bytes, 32 * 4);
        source.Freeze();
        return source;
    }
}
