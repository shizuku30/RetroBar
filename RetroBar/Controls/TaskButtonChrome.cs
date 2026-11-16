using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RetroBar.Controls
{
    public sealed class TaskButtonChrome : FrameworkElement
    {
        public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(BitmapSource), typeof(TaskButtonChrome),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, SourceChanged));

        private BitmapSource[,] _slices;

        public BitmapSource Source
        {
            get => (BitmapSource)GetValue(SourceProperty);
            set => SetValue(SourceProperty, value);
        }

        private static void SourceChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
        {
            TaskButtonChrome chrome = (TaskButtonChrome)sender;
            chrome._slices = null;
            if (!(e.NewValue is BitmapSource source)) return;

            // Win7 按钮图片四周的 3px 包含圆角、1px 黑边和内侧亮线，不能随按钮整体拉伸。
            int[] x = { 0, 3, source.PixelWidth - 3, source.PixelWidth };
            int[] y = { 0, 3, source.PixelHeight - 3, source.PixelHeight };
            chrome._slices = new BitmapSource[3, 3];
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
            {
                CroppedBitmap slice = new CroppedBitmap(source, new Int32Rect(x[column], y[row], x[column + 1] - x[column], y[row + 1] - y[row]));
                if (slice.CanFreeze) slice.Freeze();
                chrome._slices[row, column] = slice;
            }
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            if (_slices == null || RenderSize.Width <= 0 || RenderSize.Height <= 0) return;

            double horizontalEdge = Math.Min(3, RenderSize.Width / 2);
            double verticalEdge = Math.Min(3, RenderSize.Height / 2);
            double[] x = { 0, horizontalEdge, RenderSize.Width - horizontalEdge, RenderSize.Width };
            double[] y = { 0, verticalEdge, RenderSize.Height - verticalEdge, RenderSize.Height };
            drawingContext.PushGuidelineSet(new GuidelineSet(x, y));
            for (int row = 0; row < 3; row++)
            for (int column = 0; column < 3; column++)
            {
                Rect destination = new Rect(x[column], y[row], x[column + 1] - x[column], y[row + 1] - y[row]);
                drawingContext.DrawImage(_slices[row, column], destination);
            }
            drawingContext.Pop();
        }
    }
}
