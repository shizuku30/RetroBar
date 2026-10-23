using RetroBar.Extensions;
using RetroBar.Utilities;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TrayIcon = ManagedShell.WindowsTray.NotifyIcon;

namespace RetroBar.Controls
{
    public sealed class ThemedNotifyIconImage : Image
    {
        public static readonly DependencyProperty TrayIconProperty = DependencyProperty.Register(nameof(TrayIcon), typeof(TrayIcon), typeof(ThemedNotifyIconImage), new PropertyMetadata(null, IconChanged));
        public static readonly DependencyProperty PreviewInversionProperty = DependencyProperty.Register(nameof(PreviewInversion), typeof(bool), typeof(ThemedNotifyIconImage), new PropertyMetadata(false, AppearancePropertyChanged));
        private static readonly DependencyPropertyKey IsThemedPropertyKey = DependencyProperty.RegisterReadOnly(nameof(IsThemed), typeof(bool), typeof(ThemedNotifyIconImage), new PropertyMetadata(false));
        public static readonly DependencyProperty IsThemedProperty = IsThemedPropertyKey.DependencyProperty;
        private TrayIconThemeManager _manager;
        private bool _attached;

        public TrayIcon TrayIcon { get => (TrayIcon)GetValue(TrayIconProperty); set => SetValue(TrayIconProperty, value); }
        public bool PreviewInversion { get => (bool)GetValue(PreviewInversionProperty); set => SetValue(PreviewInversionProperty, value); }
        public bool IsThemed => (bool)GetValue(IsThemedProperty);

        public ThemedNotifyIconImage()
        {
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private static void IconChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            ThemedNotifyIconImage image = (ThemedNotifyIconImage)sender;
            if (image._attached)
            {
                if (e.OldValue is TrayIcon previous) previous.PropertyChanged -= image.ModelChanged;
                if (e.NewValue is TrayIcon current) current.PropertyChanged += image.ModelChanged;
            }
            image.RefreshAppearance();
        }

        private static void AppearancePropertyChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e) => ((ThemedNotifyIconImage)sender).RefreshAppearance();

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_attached) return;
            _attached = true;
            _manager = TrayIconThemeManager.Current;
            if (_manager != null) _manager.AppearanceChanged += ManagerChanged;
            if (TrayIcon != null) TrayIcon.PropertyChanged += ModelChanged;
            RefreshAppearance();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (!_attached) return;
            if (TrayIcon != null) TrayIcon.PropertyChanged -= ModelChanged;
            if (_manager != null) _manager.AppearanceChanged -= ManagerChanged;
            _manager = null;
            _attached = false;
        }

        private void ModelChanged(object sender, PropertyChangedEventArgs e)
        {
            if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(TrayIcon.Icon) || e.PropertyName == nameof(TrayIcon.GUID))
                RefreshAppearance();
        }

        private void ManagerChanged(object sender, EventArgs e) => RefreshAppearance();

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            RefreshAppearance();
        }

        public void RefreshAppearance()
        {
            if (!Dispatcher.CheckAccess())
            {
                if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(RefreshAppearance));
                return;
            }
            TrayIconThemeManager manager = _manager ?? TrayIconThemeManager.Current;
            int pixels = (int)Math.Ceiling(16 * Settings.Instance.TaskbarScale * VisualTreeHelper.GetDpi(this).DpiScaleX);
            ImageSource themed = TrayIcon == null ? null : manager?.GetImage(TrayIcon.GUID, pixels);
            SetValue(IsThemedPropertyKey, themed != null);
            Source = themed ?? TrayIcon?.Icon;
            if (IsThemed)
            {
                Effect = null;
                RenderOptions.SetBitmapScalingMode(this, ThemedSystemIcons.IsClassic(manager.Theme) ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
                return;
            }
            ClearValue(RenderOptions.BitmapScalingModeProperty);
            bool invert = TrayIcon?.CanInvert() == true && (PreviewInversion ||
                Settings.Instance.InvertIconsMode != InvertIconsOption.Never &&
                (Settings.Instance.InvertIconsMode == InvertIconsOption.Always || (Application.Current?.TryFindResource("InvertSystemNotifyIcons") as bool? ?? false)));
            Effect = invert ? Effect as InvertEffect ?? new InvertEffect() : null;
        }
    }
}
