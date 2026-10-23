using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using RetroBar.Utilities;

internal static class Program
{
    private static int failures;
    private static int tests;

    [STAThread]
    private static int Main(string[] args)
    {
        string root = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        string themePath = Path.Combine(root, "RetroBar", "Themes", "Windows7.xaml");
        Application app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources["menuTextRenderingModeConverter"] = new RetroBar.Converters.BoolToTextRenderingModeConverter();
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/RetroBar;component/Languages/English.xaml", UriKind.Relative)
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/RetroBar;component/Themes/System.xaml", UriKind.Relative)
        });

        Run("Win7 image references and image decoding", () => CheckImages(themePath));
        Run("Win7 loose XAML loading and control templates", () => CheckTheme(app, themePath, root));
        Run("Network glyph rendering without creating a tray icon", CheckNetworkGlyphs);
        Run("Network signal thresholds and adapter selection", CheckNetworkSelection);
        Run("Native network structure layouts", CheckNativeLayouts);
        Run("Live network monitor refresh and disposal (read-only)", () =>
        {
            using NetworkMonitor monitor = new NetworkMonitor();
            monitor.Refresh();
            Assert(monitor.State != null, "Network state was not initialized.");
            Console.WriteLine($"  Connection type: {monitor.State.Type}; connectivity: {monitor.State.Connectivity}");
            monitor.Dispose();
            monitor.Refresh();
        });
        Run("Every built-in theme and pre-98 fallback mapping", () => SystemIconTests.CheckThemeMapping(root));
        Run("Embedded themed-icon assets at seven pixel sizes", SystemIconTests.CheckAssets);
        Run("Network, volume, battery and unavailable state mappings", SystemIconTests.CheckStates);
        Run("Live Core Audio, power and shared-monitor ownership", SystemIconTests.CheckLiveMonitors);
        Run("Live theme toggle, third-party fallback and view lifecycle", () => SystemIconTests.CheckViewUpdates(app));
        Run("Notification-area settings layout without desktop side effects", () => SystemIconTests.CheckSettingsLayout(app, root));
        Run("Five tray styles rendered at 100/150/200 percent", () => SystemIconTests.RenderPreviews(root));

        app.Shutdown();
        Console.WriteLine($"Smoke tests: {tests - failures} passed, {failures} failed.");
        return failures == 0 ? 0 : 1;
    }

    private static void CheckImages(string themePath)
    {
        XDocument theme = XDocument.Load(themePath);
        var sources = theme.Descendants().Attributes("UriSource").Select(attribute => attribute.Value).Distinct().ToList();
        foreach (string source in sources)
        {
            string path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(themePath), source));
            Assert(File.Exists(path), $"Missing theme image: {source}");
            BitmapFrame frame = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            Assert(frame.PixelWidth > 0 && frame.PixelHeight > 0, $"Empty theme image: {source}");
        }
        Console.WriteLine($"  Decoded {sources.Count} distinct images.");
    }

    private static void CheckTheme(Application app, string themePath, string root)
    {
        ResourceDictionary theme = new ResourceDictionary { Source = new Uri(themePath) };
        app.Resources.MergedDictionaries.Add(theme);
        int styles = 0;
        foreach (DictionaryEntry entry in theme)
        {
            if (entry.Value is Style style)
            {
                style.Seal();
                styles++;
            }
            else if (entry.Value is BitmapSource bitmap)
            {
                Assert(bitmap.PixelWidth > 0, $"Cannot decode resource: {entry.Key}");
            }
        }

        foreach (Control control in new Control[] { new RetroBar.Controls.NotifyIcon(), new RetroBar.Controls.NotifyIconList() })
        {
            control.Style = (Style)app.FindResource(control.GetType());
            control.ApplyTemplate();
            control.Measure(new Size(240, 80));
            control.Arrange(new Rect(0, 0, 240, 80));
        }

        StackPanel samples = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.DimGray };
        foreach (string key in new[] { "StartButton", "TaskButton", "TaskButtonActive", "TaskButtonFlashing", "ShowDesktopButton", "TrayToggleButton", "TrayOverflowButton" })
        {
            Style style = (Style)app.FindResource(key);
            Control control = (Control)Activator.CreateInstance(style.TargetType);
            control.Style = style;
            control.Margin = new Thickness(4, 8, 4, 8);
            if (key.StartsWith("TaskButton", StringComparison.Ordinal))
            {
                control.Width = 58;
                control.Height = 40;
                ((ContentControl)control).Content = new Image
                {
                    Source = (ImageSource)app.FindResource("Win7_SearchImage"), Width = 24, Height = 24
                };
            }
            samples.Children.Add(control);
        }

        foreach (double scale in new[] { 1.0, 1.5, 2.0 })
        {
            samples.LayoutTransform = new ScaleTransform(scale, scale);
            samples.Measure(new Size(1200, 200));
            samples.Arrange(new Rect(samples.DesiredSize));
            samples.UpdateLayout();
            RenderTargetBitmap bitmap = new RenderTargetBitmap(
                (int)Math.Ceiling(samples.DesiredSize.Width), (int)Math.Ceiling(samples.DesiredSize.Height),
                96, 96, PixelFormats.Pbgra32);
            bitmap.Render(samples);
            byte[] pixels = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
            bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
            Assert(pixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha != 0), "Theme preview is empty.");
            string dir = Path.Combine(root, "artifacts", "smoke", $"net{Environment.Version.Major}");
            Directory.CreateDirectory(dir);
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream output = File.Create(Path.Combine(dir, $"win7-{scale * 100:0}.png"));
            encoder.Save(output);
        }
        app.Resources.MergedDictionaries.Remove(theme);
        Console.WriteLine($"  Loaded {theme.Count} resources, sealed {styles} styles; rendered 100/150/200% previews.");
    }

    private static void CheckNetworkGlyphs()
    {
        Typeface font = new Typeface("Segoe Fluent Icons");
        Assert(font.TryGetGlyphTypeface(out GlyphTypeface glyphs), "Network icon font is missing.");
        // Bypass the shell-owning constructor so this test never registers a real tray icon.
        object renderer = RuntimeHelpers.GetUninitializedObject(typeof(NetworkNotifyIcon));
        typeof(NetworkNotifyIcon).GetField("_glyphTypeface", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(renderer, glyphs);
        MethodInfo createImage = typeof(NetworkNotifyIcon).GetMethod("CreateIconImage", BindingFlags.NonPublic | BindingFlags.Instance);
        NetworkState[] states = Enumerable.Range(1, 4)
            .Select(bars => new NetworkState(NetworkConnectionType.WiFi, NetworkConnectivity.Internet, bars, null))
            .Concat(new[]
            {
                new NetworkState(NetworkConnectionType.WiFi, NetworkConnectivity.Limited, 2, null),
                new NetworkState(NetworkConnectionType.Ethernet, NetworkConnectivity.Internet, 0, null),
                new NetworkState(NetworkConnectionType.Ethernet, NetworkConnectivity.Limited, 0, null),
                new NetworkState(NetworkConnectionType.Other, NetworkConnectivity.Internet, 0, null),
                NetworkState.Disconnected
            }).ToArray();
        foreach (NetworkState state in states)
        {
            ImageSource source = (ImageSource)createImage.Invoke(renderer, new object[] { state });
            Image image = new Image { Source = source, Width = 32, Height = 32 };
            image.Measure(new Size(32, 32));
            image.Arrange(new Rect(0, 0, 32, 32));
            RenderTargetBitmap bitmap = new RenderTargetBitmap(32, 32, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(image);
            byte[] pixels = new byte[32 * 32 * 4];
            bitmap.CopyPixels(pixels, 32 * 4, 0);
            Assert(pixels.Where((_, index) => index % 4 == 3).Count(alpha => alpha > 0) > 8, $"Network glyph is empty: {state}");
        }
        Console.WriteLine($"  Rendered {states.Length} network states.");
    }

    private static void CheckNetworkSelection()
    {
        foreach (var sample in new[] { (0u, 1), (24u, 1), (25u, 2), (49u, 2), (50u, 3), (74u, 3), (75u, 4), (100u, 4) })
        {
            Assert(NetworkMonitor.GetSignalBars(sample.Item1) == sample.Item2, $"Wrong signal bars at {sample.Item1}%.");
        }
        NetworkAdapter wifi = new NetworkAdapter(10, Guid.NewGuid(), "Wi-Fi", NetworkConnectionType.WiFi);
        NetworkAdapter ethernet = new NetworkAdapter(20, Guid.NewGuid(), "Ethernet", NetworkConnectionType.Ethernet);
        Assert(NetworkMonitor.SelectAdapter(new[] { wifi, ethernet }, 10) == wifi, "Default route should take priority.");
        Assert(NetworkMonitor.SelectAdapter(new[] { wifi, ethernet }, 99) == ethernet, "VPN fallback should prefer Ethernet.");
        Assert(NetworkMonitor.SelectAdapter(new[] { wifi }, 99) == wifi, "Wi-Fi fallback was lost.");
        Assert(NetworkMonitor.SelectAdapter(Array.Empty<NetworkAdapter>(), 0) == null, "Disconnected state should have no adapter.");
    }

    private static void CheckNativeLayouts()
    {
        Type row = typeof(NetworkMonitor).GetNestedType("MIB_IF_ROW2", BindingFlags.NonPublic);
        Type connection = typeof(NetworkMonitor).GetNestedType("WLAN_CONNECTION_ATTRIBUTES", BindingFlags.NonPublic);
        Type association = typeof(NetworkMonitor).GetNestedType("WLAN_ASSOCIATION_ATTRIBUTES", BindingFlags.NonPublic);
        Assert(Marshal.SizeOf(row) == 1352, $"Unexpected MIB_IF_ROW2 size: {Marshal.SizeOf(row)}");
        Assert(Marshal.OffsetOf(connection, "wlanAssociationAttributes").ToInt32() == 520, "Wrong WLAN association offset.");
        Assert(Marshal.OffsetOf(association, "wlanSignalQuality").ToInt32() == 56, "Wrong WLAN signal offset.");
    }

    private static void Run(string name, Action test)
    {
        tests++;
        try
        {
            test();
            Console.WriteLine($"PASS: {name}");
        }
        catch (Exception ex)
        {
            failures++;
            Console.Error.WriteLine($"FAIL: {name}\n{ex}");
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
