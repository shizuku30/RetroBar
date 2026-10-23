using ManagedShell.Common.Logging;
using ManagedShell.WindowsTray;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RetroBar.Utilities
{
    public enum TrayIconTheme { Win98, Win2000, WinXP, Vista, Win7 }

    public static class ThemedSystemIcons
    {
        private const string ResourceBase = "pack://application:,,,/RetroBar;component/Resources/TrayIcons/";
        private static readonly Guid NetworkGuid = new Guid(NotificationArea.NETWORK_GUID);
        private static readonly Guid VolumeGuid = new Guid(NotificationArea.VOLUME_GUID);
        private static readonly Guid PowerGuid = new Guid(NotificationArea.POWER_GUID);
        private static readonly Guid HardwareGuid = new Guid(NotificationArea.HARDWARE_GUID);
        private static readonly Lazy<Dictionary<string, Dictionary<string, Variant[]>>> Catalog = new Lazy<Dictionary<string, Dictionary<string, Variant[]>>>(LoadCatalog);
        private static readonly ConcurrentDictionary<string, BitmapSource> Images = new ConcurrentDictionary<string, BitmapSource>();
        private static readonly ConcurrentDictionary<string, byte> FailedImages = new ConcurrentDictionary<string, byte>();

        public sealed class Variant
        {
            public int Size { get; set; }
            public string Path { get; set; }
        }

        public static bool Supports(Guid guid) => guid == NetworkGuid || guid == VolumeGuid || guid == PowerGuid || guid == HardwareGuid;
        public static bool IsClassic(TrayIconTheme theme) => theme <= TrayIconTheme.WinXP;

        public static TrayIconTheme ResolveTheme(string name, string explicitTheme = null, bool glass = false)
        {
            string theme = (name ?? "").ToLowerInvariant();
            if (theme.Contains("95") || theme.Contains("98") || theme.Contains("3.1") || theme.Contains("3.0") ||
                theme.Contains("windows 1.") || theme.Contains("windows 2.") || theme.Contains("nt 3") ||
                theme.Contains("nt 4") || theme.Contains("chicago"))
                return TrayIconTheme.Win98;
            if (Enum.TryParse(explicitTheme, true, out TrayIconTheme selected) && Enum.IsDefined(typeof(TrayIconTheme), selected))
                return selected;

            // Match specific generations before generic "classic" or "aero" names.
            if (theme.Contains("windows7") || theme.Contains("windows 7") || theme.Contains("win7") || theme.Contains("seven") ||
                theme.Contains("windows 10") || theme.Contains("windows 11") || theme.Contains("modern"))
                return TrayIconTheme.Win7;
            if (theme.Contains("vista") || theme.Contains("longhorn")) return TrayIconTheme.Vista;
            if (theme.Contains("xp") || theme.Contains("watercolor") || theme.Contains("whistler") || theme.Contains("royale") || theme.Contains("zune"))
                return TrayIconTheme.WinXP;
            if (theme.Contains("2000") || theme.Contains("millennium") || theme == "windows me" || theme == "me")
                return TrayIconTheme.Win2000;
            if (theme.Contains("classic") || theme.StartsWith("system"))
                return TrayIconTheme.Win98;
            return glass || theme.Contains("aero") ? TrayIconTheme.Vista : TrayIconTheme.Win98;
        }

        public static string GetStateKey(TrayIconTheme theme, Guid guid, NetworkState network, VolumeState volume, PowerState power)
        {
            if (guid == HardwareGuid) return "Hardware";
            if (guid == VolumeGuid)
            {
                if (!volume.IsKnown) return null;
                if (!volume.HasDevice) return "VolumeUnavailable";
                if (!float.IsFinite(volume.Level)) return null;
                if (volume.Muted) return "VolumeMuted";
                if (volume.Level <= 0) return "VolumeZero";
                return volume.Level <= 0.33f ? "VolumeLow" : volume.Level <= 0.66f ? "VolumeMedium" : "VolumeHigh";
            }
            if (guid == NetworkGuid)
            {
                if (network == null) return null;
                if (network.Connectivity == NetworkConnectivity.Disconnected || network.Type == NetworkConnectionType.None) return "NetworkDisconnected";
                bool internet = network.Connectivity == NetworkConnectivity.Internet;
                if (network.Type == NetworkConnectionType.WiFi && network.SignalBars > 0)
                    return (internet ? "NetworkWiFi" : "NetworkWiFiLimited") + Math.Clamp(network.SignalBars, 1, 4);
                if (!internet) return "NetworkLimited";
                return network.Type == NetworkConnectionType.Other ? "NetworkOther" : "NetworkOnline";
            }
            if (guid != PowerGuid || !power.IsKnown) return null;
            if (!power.HasBattery) return power.OnAcPower ? "PowerAcOnly" : "PowerUnavailable";
            if (power.Percent < 0 || power.Percent > 100) return "PowerUnknown";
            if (IsClassic(theme))
            {
                if (power.OnAcPower) return power.Charging ? "PowerCharging" : "PowerAc";
                return power.Percent <= 5 ? "PowerCritical" : power.Percent <= 33 ? "PowerLow" : power.Percent <= 66 ? "PowerMedium" : "PowerFull";
            }
            // The Vista/7 small-image strip uses nine levels, with transitions at 19, 30, ... 96 percent.
            int level = Math.Clamp((power.Percent - 8) / 11, 0, 8);
            if (power.OnAcPower) return (power.Charging ? "PowerCharging" : "PowerAc") + level;
            return (power.Percent <= 5 ? "PowerCritical" : power.Percent <= 20 ? "PowerLow" : "PowerBattery") + level;
        }

        public static ImageSource GetImage(TrayIconTheme theme, string state, int pixelSize = 16)
        {
            if (state == null) return null;
            string key = theme + "/" + state;
            try
            {
                if (!Catalog.Value.TryGetValue(theme.ToString(), out var pack) || !pack.TryGetValue(state, out Variant[] variants) || variants.Length == 0)
                    return null;
                Variant selected = variants.FirstOrDefault(variant => variant.Size >= Math.Clamp(pixelSize, 1, 256)) ?? variants[variants.Length - 1];
                return Images.GetOrAdd(selected.Path, path =>
                {
                    BitmapImage image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.UriSource = new Uri(ResourceBase + path);
                    image.EndInit();
                    image.Freeze();
                    return image;
                });
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidOperationException || ex is NotSupportedException || ex is JsonException)
            {
                if (FailedImages.TryAdd(key, 0)) ShellLogger.Warning($"ThemedSystemIcons: Cannot load {key}: {ex.Message}");
                return null;
            }
        }

        public static IEnumerable<string> GetStates(TrayIconTheme theme) => Catalog.Value[theme.ToString()].Keys;

        private static Dictionary<string, Dictionary<string, Variant[]>> LoadCatalog()
        {
            using Stream stream = Application.GetResourceStream(new Uri(ResourceBase + "catalog.json"))?.Stream;
            if (stream == null) throw new IOException("The system icon catalog is missing.");
            return JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Variant[]>>>(stream);
        }
    }
}
