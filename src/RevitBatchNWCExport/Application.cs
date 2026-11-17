using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using RevitBatchNWCExport.Commands;

namespace RevitBatchNWCExport
{
    public class Application : IExternalApplication
    {
        public const string RibbonTabName = "roshior-a";
        public const string HelpUrl = "https://github.com/roshior-a/RevitBatchNWCExport/";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(RibbonTabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // Вкладка уже создана другим плагином roshior-a
            }

            RibbonPanel panel = application.CreateRibbonPanel(RibbonTabName, "Экспорт");

            var buttonData = new PushButtonData(
                nameof(BatchExportCommand),
                "NWC Экспорт",
                typeof(BatchExportCommand).Assembly.Location,
                typeof(BatchExportCommand).FullName)
            {
                ToolTip = "Пакетный экспорт моделей в NWC.",
                LongDescription = "Экспорт выбранных моделей с Revit Server или из папки в NWC "
                                  + "с указанного 3D-вида.",
                Image = CreateIcon(16),
                LargeImage = CreateIcon(32)
            };
            buttonData.SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, HelpUrl));
            panel.AddItem(buttonData);

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application) => Result.Succeeded;

        /// <summary>Значок кнопки рисуется в коде: синий квадрат со стрелкой экспорта.</summary>
        private static BitmapSource CreateIcon(int size)
        {
            double s = size;
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                var background = new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0));
                dc.DrawRoundedRectangle(background, null, new Rect(s * 0.06, s * 0.06, s * 0.88, s * 0.88), s * 0.16, s * 0.16);

                var pen = new Pen(Brushes.White, s * 0.11)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round
                };
                var arrow = new StreamGeometry();
                using (var g = arrow.Open())
                {
                    // стрелка вниз
                    g.BeginFigure(new Point(s * 0.5, s * 0.22), false, false);
                    g.LineTo(new Point(s * 0.5, s * 0.6), true, true);
                    g.BeginFigure(new Point(s * 0.33, s * 0.45), false, false);
                    g.PolyLineTo(new[] { new Point(s * 0.5, s * 0.62), new Point(s * 0.67, s * 0.45) }, true, true);
                    // лоток
                    g.BeginFigure(new Point(s * 0.26, s * 0.68), false, false);
                    g.PolyLineTo(new[] { new Point(s * 0.26, s * 0.76), new Point(s * 0.74, s * 0.76), new Point(s * 0.74, s * 0.68) }, true, true);
                }
                dc.DrawGeometry(null, pen, arrow);
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
