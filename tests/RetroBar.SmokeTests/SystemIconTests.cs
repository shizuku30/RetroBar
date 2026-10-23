using ManagedShell.WindowsTray;
using RetroBar.Controls;
using RetroBar.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using NotifyIcon = ManagedShell.WindowsTray.NotifyIcon;

internal static class SystemIconTests
{
    private static readonly Guid Network = new Guid(NotificationArea.NETWORK_GUID);
    private static readonly Guid Volume = new Guid(NotificationArea.VOLUME_GUID);
    private static readonly Guid Power = new Guid(NotificationArea.POWER_GUID);
    private static readonly Guid Hardware = new Guid(NotificationArea.HARDWARE_GUID);
    private static readonly NetworkState Online = new NetworkState(NetworkConnectionType.Ethernet, NetworkConnectivity.Internet, 0, null);
    private static readonly VolumeState Audible = new VolumeState(true, true, false, 0.5f);
    private static readonly PowerState Battery = new PowerState(true, true, false, false, 50);

    public static void CheckThemeMapping(string root)
    {
        var expected = new Dictionary<string, TrayIconTheme>
        {
            ["System"] = TrayIconTheme.Win98, ["System XP"] = TrayIconTheme.WinXP, ["System Vista"] = TrayIconTheme.Vista,
            ["Windows 95-98"] = TrayIconTheme.Win98, ["Windows Me"] = TrayIconTheme.Win2000,
            ["Windows 2000"] = TrayIconTheme.Win2000, ["Watercolor"] = TrayIconTheme.WinXP, ["Windows7"] = TrayIconTheme.Win7
        };
        foreach (string file in Directory.GetFiles(Path.Combine(root, "RetroBar", "Themes"), "*.xaml"))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            TrayIconTheme value = expected.TryGetValue(name, out var known) ? known : name.Contains("XP") ? TrayIconTheme.WinXP : TrayIconTheme.Vista;
            Assert(ThemedSystemIcons.ResolveTheme(name) == value, $"Wrong icon theme: {name}");
        }
        foreach (string old in new[] { "Windows 1.0", "Windows 2.0", "Windows 3.0", "Windows 3.11", "Windows 95", "Windows 98 SE", "Windows NT 4.0", "Chicago" })
            Assert(ThemedSystemIcons.ResolveTheme(old, glass: true) == TrayIconTheme.Win98, $"Pre-98 theme used newer icons: {old}");
        Assert(ThemedSystemIcons.ResolveTheme("Custom", "WinXP") == TrayIconTheme.WinXP, "Explicit custom theme override failed.");
        Assert(ThemedSystemIcons.ResolveTheme("Custom glass", glass: true) == TrayIconTheme.Vista, "Glass fallback failed.");
        Assert(ThemedSystemIcons.ResolveTheme("Modern") == TrayIconTheme.Win7, "Modern-style fallback failed.");
    }

    public static void CheckAssets()
    {
        int count = 0;
        foreach (TrayIconTheme theme in Enum.GetValues<TrayIconTheme>())
        foreach (string state in ThemedSystemIcons.GetStates(theme))
        foreach (int pixels in new[] { 16, 20, 24, 32, 40, 48, 64 })
        {
            BitmapSource source = ThemedSystemIcons.GetImage(theme, state, pixels) as BitmapSource;
            Assert(source != null && source.IsFrozen && source.PixelWidth > 0 && source.PixelHeight > 0, $"Missing embedded asset: {theme}/{state}@{pixels}");
            FormatConvertedBitmap rgba = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            byte[] data = new byte[rgba.PixelWidth * rgba.PixelHeight * 4];
            rgba.CopyPixels(data, rgba.PixelWidth * 4, 0);
            Assert(data.Where((_, index) => index % 4 == 3).Any(alpha => alpha > 0), $"Empty asset: {theme}/{state}");
            count++;
        }
        Assert(ThemedSystemIcons.GetImage(TrayIconTheme.Win98, "not-a-state") == null, "Unknown asset should fall back.");
        Console.WriteLine($"  Checked {count} themed state/size combinations.");
    }

    public static void CheckStates()
    {
        foreach (TrayIconTheme theme in Enum.GetValues<TrayIconTheme>())
        {
            string Key(Guid id, NetworkState n = null, VolumeState v = null, PowerState p = null) => ThemedSystemIcons.GetStateKey(theme, id, n ?? Online, v ?? Audible, p ?? Battery);
            void Exists(string state) => Assert(state != null && ThemedSystemIcons.GetImage(theme, state) != null, $"Unmapped state: {theme}/{state}");
            foreach (float level in new[] { 0, 0.01f, 0.33f, 0.34f, 0.66f, 0.67f, 1f })
                Exists(Key(Volume, v: new VolumeState(true, true, false, level)));
            Assert(Key(Volume, v: Audible with { Muted = true }) == "VolumeMuted", "Mute must override volume level.");
            Assert(Key(Volume, v: Audible with { HasDevice = false }) == "VolumeUnavailable", "Missing device was not represented.");
            Assert(Key(Volume, v: VolumeState.Unknown) == null, "Unknown audio state should retain the native icon.");
            Assert(Key(Volume, v: Audible with { Level = float.NaN }) == null, "Invalid audio state should retain the native icon.");
            foreach (NetworkConnectivity connectivity in Enum.GetValues<NetworkConnectivity>())
            foreach (NetworkConnectionType type in Enum.GetValues<NetworkConnectionType>())
            foreach (int bars in new[] { 0, 1, 2, 3, 4 })
                Exists(Key(Network, n: new NetworkState(type, connectivity, bars, null)));
            Assert(Key(Network, n: new NetworkState(NetworkConnectionType.WiFi, NetworkConnectivity.Internet, 0, null)) == "NetworkOnline", "Unknown Wi-Fi signal must not show full bars.");
            foreach (int percent in new[] { -1, 0, 1, 5, 6, 19, 20, 21, 33, 50, 66, 95, 96, 100 })
            foreach (bool ac in new[] { false, true })
            foreach (bool charging in new[] { false, true })
                Exists(Key(Power, p: new PowerState(true, true, ac, charging, percent)));
            Exists(Key(Power, p: new PowerState(true, false, true, false, -1)));
            Exists(Key(Power, p: new PowerState(true, false, false, false, -1)));
            Assert(Key(Power, p: PowerState.Unknown) == null, "Unknown power state should retain the native icon.");
            Exists(Key(Hardware));
            Assert(Key(Guid.NewGuid()) == null, "A third-party GUID was replaced.");
        }
    }

    public static void CheckLiveMonitors()
    {
        using VolumeMonitor volume = new VolumeMonitor();
        volume.Refresh();
        Assert(volume.State.IsKnown, "Core Audio status could not be read.");
        Assert(!volume.State.HasDevice || volume.State.Level >= 0 && volume.State.Level <= 1, "Invalid live volume level.");
        Assert(PowerState.Read().IsKnown, "Power status could not be read.");
        Assert(Marshal.SizeOf<VolumeMonitor.PropertyKey>() == 20, "Wrong Core Audio property-key ABI.");
        Type status = typeof(PowerState).GetNestedType("SYSTEM_POWER_STATUS", BindingFlags.NonPublic);
        Assert(Marshal.SizeOf(status) == 12, "Wrong power-status ABI.");
        for (int i = 0; i < 10; i++)
        {
            using VolumeMonitor disposable = new VolumeMonitor();
            disposable.Refresh();
            disposable.Dispose();
            disposable.Refresh();
        }
        using NetworkMonitor shared = new NetworkMonitor();
        object icon = RuntimeHelpers.GetUninitializedObject(typeof(NetworkNotifyIcon));
        typeof(NetworkNotifyIcon).GetField("_networkMonitor", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(icon, shared);
        ((NetworkNotifyIcon)icon).Dispose();
        Assert(!(bool)typeof(NetworkMonitor).GetField("_isDisposed", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(shared), "The synthetic icon disposed the shared network monitor.");
        Console.WriteLine($"  Core Audio known; output device present: {volume.State.HasDevice}; monitor disposal passed.");
    }

    public static void CheckViewUpdates(Application app)
    {
        using IsolatedSettings settings = new IsolatedSettings();
        using TrayIconThemeManager manager = new TrayIconThemeManager();
        app.Resources[TrayIconThemeManager.ResourceKey] = manager;
        try
        {
            DrawingImage original = new DrawingImage(new GeometryDrawing(Brushes.Red, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
            original.Freeze();
            NotifyIcon model = new NotifyIcon(null, new IntPtr(42)) { GUID = Hardware, Icon = original, Title = "Native tooltip", CallbackMessage = 123, Version = 4 };
            ThemedNotifyIconImage image = new ThemedNotifyIconImage { TrayIcon = model };
            image.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            Assert(image.IsThemed && image.Source != original && image.Effect == null, "System icon was not replaced or was inverted.");
            ImageSource win98 = image.Source;
            settings.Set("Theme", "Windows7");
            Assert(image.IsThemed && image.Source != win98, "Theme change did not refresh the image.");
            Assert(model.Icon == original && model.HWnd == new IntPtr(42) && model.CallbackMessage == 123 && model.Version == 4 && model.Title == "Native tooltip", "Replacement mutated native callback data.");
            settings.Set("UseThemeSystemIcons", false);
            Assert(!image.IsThemed && image.Source == original, "Disabling themed icons did not restore the original.");
            settings.Set("UseThemeSystemIcons", true);
            image.TrayIcon = new NotifyIcon(null) { GUID = Guid.NewGuid(), Icon = original, Path = @"C:\Windows\System32\SndVolSSO.dll" };
            Assert(!image.IsThemed && image.Source == original, "Third-party icon was matched by filename.");
            DrawingImage update = new DrawingImage(new GeometryDrawing(Brushes.Green, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
            update.Freeze();
            image.TrayIcon.Icon = update;
            Assert(image.Source == update, "Native icon update was lost.");
            image.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent));
            image.TrayIcon.Icon = original;
            Assert(image.Source == update, "Unloaded image remained subscribed to its model.");
        }
        finally { app.Resources.Remove(TrayIconThemeManager.ResourceKey); }
    }

    public static void CheckWin7Hover(Application app, string root)
    {
        string path = Path.Combine(root, "RetroBar", "Themes", "Windows7.xaml");
        XDocument document = XDocument.Load(path);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XElement[] templates = document.Descendants(presentation + "ControlTemplate")
            .Where(element => (string)element.Attribute("TargetType") == "{x:Type controls:NotifyIcon}").ToArray();
        Assert(templates.Length == 2, "The tray and overflow icon templates must both be checked.");
        Assert(templates.All(template => !template.Descendants(presentation + "ToggleButton").Any()), "A pin button still overlays a tray icon.");
        Assert(!document.Descendants().Attributes().Any(attribute => attribute.Value.Contains("PinToggle")), "A pin-button animation or setter remains.");

        using IsolatedSettings settings = new IsolatedSettings();
        settings.Set("Theme", "Windows7");
        ResourceDictionary theme = new ResourceDictionary { Source = new Uri(path) };
        app.Resources.MergedDictionaries.Add(theme);
        using TrayIconThemeManager manager = new TrayIconThemeManager();
        app.Resources[TrayIconThemeManager.ResourceKey] = manager;
        try
        {
            Style trayStyle = (Style)app.FindResource(typeof(RetroBar.Controls.NotifyIcon));
            RetroBar.Controls.NotifyIconList list = new RetroBar.Controls.NotifyIconList
            {
                Style = (Style)app.FindResource(typeof(RetroBar.Controls.NotifyIconList))
            };
            list.ApplyTemplate();
            Popup popup = list.Template.FindName("OverflowPopup", list) as Popup;
            Assert(popup != null && !popup.IsOpen, "The overflow template was not loaded without opening a window.");
            Style popupStyle = ((popup.Child as Border)?.Child as Border)?.Resources["PopupNotifyIcon"] as Style;
            Assert(popupStyle != null, "The overflow icon style is missing.");

            StackPanel samples = new StackPanel { Background = new SolidColorBrush(Color.FromRgb(49, 65, 70)) };
            foreach (bool overflow in new[] { false, true })
            {
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal, Height = 52 };
                row.Children.Add(new TextBlock { Text = overflow ? "Overflow" : "Tray", Width = 76, Margin = new Thickness(8, 18, 0, 0), Foreground = Brushes.White });
                foreach (bool hover in new[] { false, true })
                foreach (Guid guid in new[] { Hardware, Network })
                {
                    NotifyIcon model = new NotifyIcon(null) { GUID = guid, Title = "Hover regression" };
                    RetroBar.Controls.NotifyIcon icon = new RetroBar.Controls.NotifyIcon
                    {
                        Style = overflow ? popupStyle : trayStyle, DataContext = model,
                        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
                    };
                    Border cell = new Border { Child = icon, Width = 48, Height = 48 };
                    row.Children.Add(cell);
                    cell.Measure(new Size(48, 48));
                    cell.Arrange(new Rect(0, 0, 48, 48));
                    icon.ApplyTemplate();
                    Assert(icon.Template.FindName("PinToggle", icon) == null && !Descendants(icon).OfType<ToggleButton>().Any(), "An icon still contains a hover button.");
                    if (overflow)
                    {
                        Border frame = icon.Template.FindName("HoverFrame", icon) as Border;
                        Assert(frame != null, "The normal overflow hover frame was lost.");
                        if (hover)
                        {
                            frame.Background = new SolidColorBrush(Color.FromArgb(38, 110, 165, 210));
                            frame.BorderBrush = new SolidColorBrush(Color.FromArgb(102, 110, 165, 210));
                        }
                    }
                    else
                    {
                        Image highlight = icon.Template.FindName("HoverTileImg", icon) as Image;
                        Assert(highlight != null && !highlight.IsHitTestVisible, "The non-interactive tray hover highlight was lost.");
                        ThemedNotifyIconImage glyph = Descendants(icon).OfType<ThemedNotifyIconImage>().Single();
                        Point glyphCenter = glyph.TransformToAncestor(icon).Transform(new Point(glyph.ActualWidth / 2, glyph.ActualHeight / 2));
                        Point highlightCenter = highlight.TransformToAncestor(icon).Transform(new Point(highlight.ActualWidth / 2, highlight.ActualHeight / 2));
                        Assert((glyphCenter - highlightCenter).Length < 0.01,
                            $"Tray hover is not centered: glyph={glyphCenter}, highlight={highlightCenter}");
                        if (hover) highlight.Opacity = 1;
                    }
                    Assert(!model.IsPinned, "Rendering a hover changed notification visibility.");
                }
                samples.Children.Add(row);
            }
            samples.Measure(new Size(320, 120));
            samples.Arrange(new Rect(samples.DesiredSize));
            samples.UpdateLayout();
            SaveVisual(samples, (int)Math.Ceiling(samples.DesiredSize.Width), (int)Math.Ceiling(samples.DesiredSize.Height),
                Path.Combine(root, "artifacts", "smoke", $"net{Environment.Version.Major}", "win7-tray-hover.png"));
        }
        finally
        {
            app.Resources.Remove(TrayIconThemeManager.ResourceKey);
            app.Resources.MergedDictionaries.Remove(theme);
        }
    }

    public static void CheckSettingsLayout(Application app, string root)
    {
        using IsolatedSettings settings = new IsolatedSettings();
        app.Resources["SmokeSettings"] = settings.Instance;
        try
        {
            RenderSettingsLayout(root, "notification-settings-en.png");
            ResourceDictionary chinese = new ResourceDictionary
            {
                Source = new Uri(Path.Combine(root, "RetroBar", "Languages", "\u4e2d\u6587(\u7b80\u4f53).xaml"))
            };
            app.Resources.MergedDictionaries.Add(chinese);
            try { RenderSettingsLayout(root, "notification-settings-zh.png"); }
            finally { app.Resources.MergedDictionaries.Remove(chinese); }
        }
        finally { app.Resources.Remove("SmokeSettings"); }
    }

    public static void CheckWin7Alignment(Application app, string root)
    {
        using IsolatedSettings settings = new IsolatedSettings();
        settings.Set("Theme", "Windows7");
        ResourceDictionary theme = new ResourceDictionary { Source = new Uri(Path.Combine(root, "RetroBar", "Themes", "Windows7.xaml")) };
        app.Resources.MergedDictionaries.Add(theme);
        using TrayIconThemeManager manager = new TrayIconThemeManager();
        app.Resources[TrayIconThemeManager.ResourceKey] = manager;
        int scenarios = 0;
        try
        {
            Style trayStyle = (Style)app.FindResource(typeof(RetroBar.Controls.NotifyIcon));
            RetroBar.Controls.NotifyIconList list = new RetroBar.Controls.NotifyIconList { Style = (Style)app.FindResource(typeof(RetroBar.Controls.NotifyIconList)) };
            list.ApplyTemplate();
            Popup popup = (Popup)list.Template.FindName("OverflowPopup", list);
            Style popupStyle = (Style)((Border)((Border)popup.Child).Child).Resources["PopupNotifyIcon"];
            foreach (Orientation orientation in new[] { Orientation.Horizontal, Orientation.Vertical })
            foreach (FlowDirection flow in new[] { FlowDirection.LeftToRight, FlowDirection.RightToLeft })
            foreach (int rows in new[] { 1, 2 })
            foreach (double scale in new[] { 1.0, 1.25, 1.5, 1.75, 2.0 })
            foreach (bool enabled in new[] { false, true })
            {
                settings.Set("TaskbarScale", scale);
                settings.Set("UseThemeSystemIcons", enabled);
                AlignmentWindow window = new AlignmentWindow { Orientation = orientation, Rows = rows, FlowDirection = flow };
                StackPanel panel = new StackPanel { Orientation = orientation, LayoutTransform = new ScaleTransform(scale, scale) };
                Border scene = new Border { Child = panel, Padding = new Thickness(20), Background = new SolidColorBrush(Color.FromRgb(49, 65, 70)) };
                window.Content = scene;
                var icons = new List<(RetroBar.Controls.NotifyIcon Icon, bool Overflow)>();
                try
                {
                    foreach (bool overflow in new[] { false, true })
                    foreach (Guid guid in new[] { Volume, Network })
                    {
                        NotifyIcon model = new NotifyIcon(null)
                        {
                            GUID = guid, Icon = ThemedSystemIcons.GetImage(TrayIconTheme.Win7, guid == Volume ? "VolumeHigh" : "NetworkOnline")
                        };
                        RetroBar.Controls.NotifyIcon icon = new RetroBar.Controls.NotifyIcon
                        {
                            DataContext = model, Style = overflow ? popupStyle : trayStyle,
                            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
                        };
                        panel.Children.Add(new Border { Width = 50, Height = 50, Child = icon });
                        icons.Add((icon, overflow));
                    }
                    window.ApplyTemplate();
                    scene.Measure(new Size(1000, 1000));
                    scene.Arrange(new Rect(scene.DesiredSize));
                    scene.UpdateLayout();
                    foreach (var sample in icons)
                    {
                        ThemedNotifyIconImage glyph = Descendants(sample.Icon).OfType<ThemedNotifyIconImage>().Single();
                        FrameworkElement chrome = sample.Icon.Template.FindName(sample.Overflow ? "HoverFrame" : "HoverTileImg", sample.Icon) as FrameworkElement;
                        Point glyphCenter = glyph.TransformToAncestor(scene).Transform(new Point(glyph.ActualWidth / 2, glyph.ActualHeight / 2));
                        Point chromeCenter = chrome.TransformToAncestor(scene).Transform(new Point(chrome.ActualWidth / 2, chrome.ActualHeight / 2));
                        string context = $"{orientation}/{flow}/rows={rows}/scale={scale}/enabled={enabled}/overflow={sample.Overflow}";
                        Assert((glyphCenter - chromeCenter).Length < 0.01, $"Hover alignment error ({context}): {glyphCenter} vs {chromeCenter}");
                        Assert(glyph.Margin == new Thickness(0), $"Image still carries layout spacing ({context}).");
                        if (sample.Overflow)
                        {
                            ((Border)chrome).Background = new SolidColorBrush(Color.FromArgb(38, 110, 165, 210));
                            ((Border)chrome).BorderBrush = new SolidColorBrush(Color.FromArgb(102, 110, 165, 210));
                        }
                        else
                        {
                            Thickness spacing = orientation == Orientation.Horizontal ? new Thickness(0, 4, 9, 1) : new Thickness(1, 1, 2, 1);
                            Assert(sample.Icon.Margin == spacing, $"Tray spacing changed ({context}).");
                            Assert(Math.Abs(chrome.Height - (orientation == Orientation.Horizontal ? 40 : 27)) < 0.01, $"Wrong orientation hover height ({context}).");
                            ((Image)chrome).Opacity = 1;
                        }
                    }
                    scenarios++;
                    if (enabled && rows == 1 && flow == FlowDirection.LeftToRight)
                    {
                        scene.UpdateLayout();
                        string filename = $"win7-alignment-{orientation}-{scale * 100:0}.png";
                        SaveVisual(scene, (int)Math.Ceiling(scene.DesiredSize.Width), (int)Math.Ceiling(scene.DesiredSize.Height),
                            Path.Combine(root, "artifacts", "smoke", $"net{Environment.Version.Major}", filename));
                    }
                }
                finally { window.Content = null; window.Close(); }
            }
            Console.WriteLine($"  Checked {scenarios} layout scenarios and {scenarios * 4} icon/highlight centers.");
        }
        finally
        {
            app.Resources.Remove(TrayIconThemeManager.ResourceKey);
            app.Resources.MergedDictionaries.Remove(theme);
        }
    }

    private sealed class AlignmentWindow : Window
    {
        public Orientation Orientation { get; set; }
        public int Rows { get; set; }
    }

    private static void RenderSettingsLayout(string root, string filename)
    {
        XDocument document = XDocument.Load(Path.Combine(root, "RetroBar", "PropertiesWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        document.Root.Attribute(x + "Class").Remove();
        foreach (XElement element in document.Descendants())
        {
            string ns = element.Name.NamespaceName;
            if (ns.StartsWith("clr-namespace:") && !ns.Contains(";assembly="))
                element.Name = XName.Get(element.Name.LocalName, ns + ";assembly=RetroBar");
        }
        var events = new HashSet<string> { "Closing", "Loaded", "ContentRendered", "Checked", "Unchecked", "SelectionChanged", "Click", "RequestNavigate" };
        foreach (XAttribute attribute in document.Descendants().Attributes().ToList())
        {
            if (events.Contains(attribute.Name.LocalName) && attribute.Name.NamespaceName.Length == 0) attribute.Remove();
            else if (attribute.IsNamespaceDeclaration && attribute.Value.StartsWith("clr-namespace:") && !attribute.Value.Contains(";assembly="))
                attribute.Value += ";assembly=RetroBar";
            else if (!attribute.IsNamespaceDeclaration)
                attribute.Value = attribute.Value.Replace("{x:Static Settings:Settings.Instance}", "{StaticResource SmokeSettings}");
        }
        ParserContext context = new ParserContext { BaseUri = new Uri("pack://application:,,,/RetroBar;component/") };
        Window window = (Window)XamlReader.Parse(document.ToString(), context);
        FrameworkElement content = (FrameworkElement)window.Content;
        content.Measure(new Size(440, 1100));
        content.Arrange(new Rect(0, 0, 440, content.DesiredSize.Height));
        TabControl tabs = Descendants(content).OfType<TabControl>().First();
        tabs.SelectedIndex = 1;
        content.Measure(new Size(440, 1100));
        content.Arrange(new Rect(0, 0, 440, content.DesiredSize.Height));
        content.UpdateLayout();
        CheckBox toggle = Descendants(content).OfType<CheckBox>().FirstOrDefault(box => BindingOperations.GetBinding(box, System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty)?.Path.Path == "UseThemeSystemIcons");
        Assert(toggle != null && toggle.ActualHeight > 0, "The notification-area setting is missing or clipped.");
        Point position = toggle.TranslatePoint(new Point(0, 0), content);
        Assert(position.X >= 0 && position.Y >= 0 && position.X + toggle.ActualWidth <= content.ActualWidth &&
            position.Y + toggle.ActualHeight <= content.ActualHeight, "The themed-icon checkbox overflows its dialog.");
        object settings = Application.Current.FindResource("SmokeSettings");
        PropertyInfo property = settings.GetType().GetProperty("UseThemeSystemIcons");
        toggle.IsChecked = false;
        Assert(!(bool)property.GetValue(settings), "Checkbox binding did not update the setting immediately.");
        string json = JsonSerializer.Serialize(settings, settings.GetType());
        object restored = JsonSerializer.Deserialize(json, settings.GetType());
        Assert(!(bool)property.GetValue(restored), "The checkbox setting did not survive serialization.");
        toggle.IsChecked = true;
        Assert((bool)property.GetValue(settings), "Checkbox could not enable themed icons again.");
        string dir = Path.Combine(root, "artifacts", "smoke", $"net{Environment.Version.Major}");
        SaveVisual(content, 440, (int)Math.Ceiling(content.RenderSize.Height), Path.Combine(dir, filename));
        window.Content = null;
        window.Close();
    }

    public static void RenderPreviews(string root)
    {
        string[] states = { "VolumeMuted", "VolumeLow", "VolumeHigh", "NetworkOnline", "NetworkDisconnected", "NetworkWiFi1", "NetworkWiFi4", "NetworkWiFiLimited2", "PowerLow", "PowerCharging", "Hardware" };
        foreach (double scale in new[] { 1.0, 1.5, 2.0 })
        {
            StackPanel panel = new StackPanel { LayoutTransform = new ScaleTransform(scale, scale) };
            foreach (TrayIconTheme theme in Enum.GetValues<TrayIconTheme>())
            {
                DockPanel row = new DockPanel { Height = 42, Width = 520, Background = new SolidColorBrush(theme == TrayIconTheme.WinXP ? Color.FromRgb(24, 78, 170) : theme >= TrayIconTheme.Vista ? Color.FromRgb(48, 65, 70) : Color.FromRgb(212, 208, 200)) };
                row.Children.Add(new TextBlock { Text = theme.ToString(), Width = 100, Margin = new Thickness(8, 10, 0, 0), Foreground = theme >= TrayIconTheme.WinXP ? Brushes.White : Brushes.Black, FontSize = 12 });
                foreach (string state in states)
                {
                    string key = state;
                    if (!ThemedSystemIcons.IsClassic(theme) && (key == "PowerLow" || key == "PowerCharging")) key += "1";
                    Image image = new Image { Source = ThemedSystemIcons.GetImage(theme, key, (int)(16 * scale)), Width = 16, Height = 16, Margin = new Thickness(8, 0, 8, 0), Stretch = Stretch.Uniform };
                    RenderOptions.SetBitmapScalingMode(image, ThemedSystemIcons.IsClassic(theme) ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
                    row.Children.Add(image);
                }
                panel.Children.Add(row);
            }
            panel.Measure(new Size(2000, 1000));
            panel.Arrange(new Rect(panel.DesiredSize));
            panel.UpdateLayout();
            string path = Path.Combine(root, "artifacts", "smoke", $"net{Environment.Version.Major}", $"themed-tray-{scale * 100:0}.png");
            SaveVisual(panel, (int)Math.Ceiling(panel.DesiredSize.Width), (int)Math.Ceiling(panel.DesiredSize.Height), path);
        }
    }

    private static void SaveVisual(Visual visual, int width, int height, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        RenderTargetBitmap bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (DependencyObject nested in Descendants(child)) yield return nested;
        }
    }

    private sealed class IsolatedSettings : IDisposable
    {
        private readonly Type _type = typeof(VolumeMonitor).Assembly.GetType("RetroBar.Utilities.Settings");
        private readonly object _oldInstance;
        private readonly object _oldInitializing;
        public object Instance { get; }
        public IsolatedSettings()
        {
            // Keep all setting changes inside this test process, with persistence disabled.
            RuntimeHelpers.RunClassConstructor(_type.TypeHandle);
            _oldInstance = _type.GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            _oldInitializing = _type.GetField("_isInitializing", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            Instance = Activator.CreateInstance(_type);
            _type.GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, Instance);
            _type.GetField("_isInitializing", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, true);
        }
        public void Set(string name, object value) => _type.GetProperty(name).SetValue(Instance, value);
        public void Dispose()
        {
            _type.GetField("instance", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _oldInstance);
            _type.GetField("_isInitializing", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _oldInitializing);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
