using RetroBar.Utilities;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RetroBar.Controls
{
    public sealed class TaskButtonHotTrack : FrameworkElement
    {
        public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(ImageSource), typeof(TaskButtonHotTrack),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, IconChanged));
        public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(TaskButtonHotTrack),
            new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty PointerPositionProperty = DependencyProperty.Register(nameof(PointerPosition), typeof(Point), typeof(TaskButtonHotTrack),
            new FrameworkPropertyMetadata(new Point(double.NaN, double.NaN), FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty IconOnlyProperty = DependencyProperty.Register(nameof(IconOnly), typeof(bool), typeof(TaskButtonHotTrack),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender, (sender, _) => ((TaskButtonHotTrack)sender).BuildRamp()));

        private ButtonBase _owner;
        private Color _color = Win7IconColor.DefaultColor;
        private readonly byte[] _ramp = new byte[100 * 4];
        private WriteableBitmap _bitmap;
        private byte[] _pixels;

        public ImageSource Icon { get => (ImageSource)GetValue(IconProperty); set => SetValue(IconProperty, value); }
        public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
        public Point PointerPosition { get => (Point)GetValue(PointerPositionProperty); set => SetValue(PointerPositionProperty, value); }
        public bool IconOnly { get => (bool)GetValue(IconOnlyProperty); set => SetValue(IconOnlyProperty, value); }
        public Color DominantColor => _color;

        public TaskButtonHotTrack()
        {
            IsHitTestVisible = false;
            BuildRamp();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private static void IconChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            TaskButtonHotTrack glow = (TaskButtonHotTrack)sender;
            glow._color = Win7IconColor.GetDominantColor((ImageSource)e.NewValue);
            glow.BuildRamp();
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            DetachOwner();
            _owner = TemplatedParent as ButtonBase;
            if (_owner == null) return;
            _owner.MouseEnter += PointerMoved;
            _owner.MouseMove += PointerMoved;
            if (_owner.IsMouseOver) PointerPosition = Mouse.GetPosition(_owner);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e) => DetachOwner();

        private void DetachOwner()
        {
            if (_owner == null) return;
            _owner.MouseEnter -= PointerMoved;
            _owner.MouseMove -= PointerMoved;
            _owner = null;
        }

        private void PointerMoved(object sender, MouseEventArgs e) => PointerPosition = e.GetPosition(_owner);

        private void BuildRamp()
        {
            // Explorer!DrawRadialGradient 的 100 级余弦衰减；中心 30% 在图标模式混入 75% 白色，有标题时为 50%。
            for (int i = 0; i < 100; i++)
            {
                float cosine = MathF.Cos((float)Math.PI * i / 100);
                int alpha = i <= 50 ? 64 - (int)(-191 * cosine) : (int)((cosine + 1) * 64);
                int r = _color.R, g = _color.G, b = _color.B;
                if (i < 30)
                {
                    int white = IconOnly ? 3 : 1, divisor = IconOnly ? 4 : 2;
                    r += (255 - r) * white / divisor * (30 - i) / 30;
                    g += (255 - g) * white / divisor * (30 - i) / 30;
                    b += (255 - b) * white / divisor * (30 - i) / 30;
                }
                _ramp[i * 4] = (byte)(b * alpha / 255);
                _ramp[i * 4 + 1] = (byte)(g * alpha / 255);
                _ramp[i * 4 + 2] = (byte)(r * alpha / 255);
                _ramp[i * 4 + 3] = (byte)alpha;
            }
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            _bitmap = null;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            if (RenderSize.Width <= 0 || RenderSize.Height <= 0) return;
            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            int width = Math.Max(1, (int)Math.Ceiling(RenderSize.Width * dpi.DpiScaleX));
            int height = Math.Max(1, (int)Math.Ceiling(RenderSize.Height * dpi.DpiScaleY));
            if (_bitmap == null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
            {
                _bitmap = new WriteableBitmap(width, height, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32, null);
                _pixels = new byte[width * height * 4];
            }
            Array.Clear(_pixels, 0, _pixels.Length);
            Point pointer = PointerPosition;
            bool horizontal = Orientation == Orientation.Horizontal;
            double centerX = horizontal && !double.IsNaN(pointer.X) ? Math.Clamp(pointer.X, 0, RenderSize.Width) * dpi.DpiScaleX : width / 2.0;
            double centerY = horizontal ? height - dpi.DpiScaleY
                : !double.IsNaN(pointer.Y) ? Math.Clamp(pointer.Y, 0, RenderSize.Height) * dpi.DpiScaleY : height / 2.0;
            // 原版最小宽度：32px 图标 + 两侧 (3px sizing margin + 1px) + 9px 组合端帽。
            double radiusX = 7 * Math.Max(RenderSize.Width, 49) / 4 * dpi.DpiScaleX;
            double radiusY = 7 * Math.Max(RenderSize.Height, 49) / 4 * dpi.DpiScaleY;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                if (x < dpi.DpiScaleX || x >= width - dpi.DpiScaleX || y < dpi.DpiScaleY || y >= height - dpi.DpiScaleY) continue;
                // 3px 框体内使用主光晕，框体边缘用 1.5 倍半径的外光晕；都限制在按钮内。
                bool outer = x < 3 * dpi.DpiScaleX || x >= width - 3 * dpi.DpiScaleX || y < 3 * dpi.DpiScaleY || y >= height - 3 * dpi.DpiScaleY;
                double expansion = outer ? 1.5 : 1;
                double dx = (x - centerX) / (radiusX * expansion);
                double dy = (y - centerY) / (radiusY * expansion);
                int index = (int)(Math.Sqrt(dx * dx + dy * dy) * 100);
                if (index >= 100) continue;
                int source = index * 4, destination = (y * width + x) * 4;
                _pixels[destination] = _ramp[source];
                _pixels[destination + 1] = _ramp[source + 1];
                _pixels[destination + 2] = _ramp[source + 2];
                _pixels[destination + 3] = _ramp[source + 3];
            }
            _bitmap.WritePixels(new Int32Rect(0, 0, width, height), _pixels, width * 4, 0);
            context.DrawImage(_bitmap, new Rect(RenderSize));
        }
    }
}
