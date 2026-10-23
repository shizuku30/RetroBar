using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace RetroBar.Utilities
{
    public sealed class TrayIconThemeManager : IDisposable
    {
        public const string ResourceKey = "TrayIconThemeManager";
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly VolumeMonitor _volume;
        private bool _disposed;
        public NetworkMonitor Network { get; }
        public PowerState Power { get; private set; }
        public TrayIconTheme Theme { get; private set; }
        public event EventHandler AppearanceChanged;

        public TrayIconThemeManager()
        {
            Network = new NetworkMonitor();
            _volume = new VolumeMonitor();
            Network.StateChanged += StateChanged;
            _volume.StateChanged += StateChanged;
            SystemEvents.PowerModeChanged += PowerModeChanged;
            Settings.Instance.PropertyChanged += SettingsChanged;
            Power = PowerState.Read();
            UpdateTheme();
        }

        public static TrayIconThemeManager Current => Application.Current?.TryFindResource(ResourceKey) as TrayIconThemeManager;

        public ImageSource GetImage(Guid guid, int pixelSize)
        {
            if (_disposed || !Settings.Instance.UseThemeSystemIcons || !ThemedSystemIcons.Supports(guid)) return null;
            string state = ThemedSystemIcons.GetStateKey(Theme, guid, Network.State, _volume.State, Power);
            return ThemedSystemIcons.GetImage(Theme, state, pixelSize);
        }

        private void UpdateTheme()
        {
            // Only inspect the active theme, not resources left by a previously selected dictionary.
            ResourceDictionary active = Application.Current?.Resources.MergedDictionaries.LastOrDefault(dictionary =>
                dictionary.Source?.OriginalString.Replace('\\', '/').Contains("Themes/", StringComparison.OrdinalIgnoreCase) == true);
            string overrideTheme = active?.Contains("SystemNotifyIconTheme") == true ? active["SystemNotifyIconTheme"] as string : null;
            bool glass = active?.Contains("AllowsTransparency") == true && active["AllowsTransparency"] is bool transparent && transparent;
            Theme = ThemedSystemIcons.ResolveTheme(Settings.Instance.Theme, overrideTheme, glass);
        }

        private void StateChanged(object sender, EventArgs e) => AppearanceChanged?.Invoke(this, EventArgs.Empty);

        private void SettingsChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Settings.Theme)) UpdateTheme();
            if (e.PropertyName == nameof(Settings.Theme) || e.PropertyName == nameof(Settings.UseThemeSystemIcons) ||
                e.PropertyName == nameof(Settings.TaskbarScale) || e.PropertyName == nameof(Settings.InvertIconsMode) ||
                e.PropertyName == nameof(Settings.InvertNotifyIcons))
                AppearanceChanged?.Invoke(this, EventArgs.Empty);
        }

        private void PowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (_disposed || _dispatcher.HasShutdownStarted) return;
            _dispatcher.BeginInvoke(new Action(() =>
            {
                if (_disposed) return;
                PowerState state = PowerState.Read();
                if (state == Power) return;
                Power = state;
                AppearanceChanged?.Invoke(this, EventArgs.Empty);
            }));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Settings.Instance.PropertyChanged -= SettingsChanged;
            SystemEvents.PowerModeChanged -= PowerModeChanged;
            Network.StateChanged -= StateChanged;
            _volume.StateChanged -= StateChanged;
            Network.Dispose();
            _volume.Dispose();
        }
    }
}
