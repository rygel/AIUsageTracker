using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AIConsumptionTracker.UI.Services
{
    public static class ScreenshotService
    {
        public static void SaveScreenshot(FrameworkElement element, string filePath)
        {
            // Ensure layout is updated
            FrameworkElement target = element;
            Brush? background = null;

            if (element is Window window)
            {
                background = window.Background;
                if (window.Content is FrameworkElement content)
                {
                    target = content; // Render content instead of the Window object itself
                }
            }

            target.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            target.Arrange(new Rect(target.DesiredSize));
            target.UpdateLayout();

            int width = (int)target.ActualWidth;
            int height = (int)target.ActualHeight;

            if (width <= 0) width = (int)target.Width;
            if (height <= 0) height = (int)target.Height;
            if (width <= 0) width = (int)target.DesiredSize.Width;
            if (height <= 0) height = (int)target.DesiredSize.Height;
            
            // Final fallback
            if (width <= 0) width = 800;
            if (height <= 0) height = 600;

            RenderTargetBitmap rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            
            DrawingVisual dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                // Draw background if captured from Window
                if (background != null)
                {
                    dc.DrawRectangle(background, null, new Rect(0, 0, width, height));
                }

                VisualBrush vb = new VisualBrush(target);
                dc.DrawRectangle(vb, null, new Rect(0, 0, width, height));
            }
            rtb.Render(dv);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using (FileStream fs = File.Create(filePath))
            {
                encoder.Save(fs);
            }
        }
    }
}
