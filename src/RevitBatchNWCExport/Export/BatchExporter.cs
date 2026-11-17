using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;

using Autodesk.Revit.UI;

using RevitBatchNWCExport.UI;

namespace RevitBatchNWCExport.Export
{
    /// <summary>
    /// Пакетный экспорт: каждая модель открывается отсоединённой (без изменений в хранилище),
    /// экспортируется с найденного 3D-вида в NWC и закрывается без сохранения.
    /// </summary>
    public sealed class BatchExporter
    {
        private readonly UIApplication _uiApp;
        private readonly ExportOptions _options;

        public BatchExporter(UIApplication uiApp, ExportOptions options)
        {
            _uiApp = uiApp;
            _options = options;
        }

        public ExportReport Run(System.Collections.Generic.IList<ExportItem> items)
        {
            var report = new ExportReport { Total = items.Count };

            using (var progress = ExportProgress.Start(items.Count))
            using (var suppressor = new DialogSuppressor(_uiApp))
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (progress.StopRequested)
                    {
                        report.Cancelled = true;
                        break;
                    }
                    var item = items[i];
                    progress.Report(i, $"{i + 1}/{items.Count}: {item.Name}");
                    var line = ExportOne(item);
                    line.SuppressedDialogs.AddRange(suppressor.TakeJournal());
                    report.Lines.Add(line);
                    progress.Report(i + 1, "");
                }
            }
            return report;
        }

        private ExportReportLine ExportOne(ExportItem item)
        {
            var timer = Stopwatch.StartNew();
            var line = new ExportReportLine { Name = item.Name };
            string nwcName = Path.GetFileNameWithoutExtension(item.Name);
            string target = Path.Combine(_options.TargetFolder, nwcName + ".nwc");
            Document doc = null;
            try
            {
                if (File.Exists(target) && IsFileInUse(target))
                {
                    line.Message = "NWC занят другим приложением (например, Navisworks)";
                    return line;
                }

                doc = Open(item);

                var view = FindView(doc);
                if (view == null)
                {
                    line.Message = _options.ViewNameExact
                        ? $"нет 3D-вида «{_options.ViewName}»"
                        : $"нет 3D-вида, содержащего «{_options.ViewName}»";
                    return line;
                }

                var nwOptions = new NavisworksExportOptions
                {
                    ExportScope = NavisworksExportScope.View,
                    ViewId = view.Id,
                    ConvertElementProperties = true,
                    ExportLinks = false,
                    ConvertLinkedCADFormats = false,
                    DivideFileIntoLevels = false,
                    FindMissingMaterials = false
                };
                doc.Export(_options.TargetFolder, nwcName, nwOptions);

                line.Success = true;
                line.Message = $"вид «{view.Name}»";
            }
            catch (Autodesk.Revit.Exceptions.FileNotFoundException)
            {
                line.Message = "модель не найдена";
            }
            catch (Exception ex)
            {
                line.Message = ex.Message;
            }
            finally
            {
                if (doc != null)
                {
                    try
                    {
                        if (doc.IsValidObject) doc.Close(false);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine("RevitBatchNWCExport: не удалось закрыть " + item.Name + ": " + ex.Message);
                    }
                }
                line.Elapsed = timer.Elapsed;
            }
            return line;
        }

        private Document Open(ExportItem item)
        {
            var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(item.ModelPath);
            var openOptions = new OpenOptions();
            if (item.FromServer || IsWorkshared(item.ModelPath))
            {
                openOptions.DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets;
                openOptions.SetOpenWorksetsConfiguration(new WorksetConfiguration(WorksetConfigurationOption.OpenAllWorksets));
            }
            return _uiApp.Application.OpenDocumentFile(modelPath, openOptions);
        }

        private static bool IsWorkshared(string path)
        {
            try
            {
                return BasicFileInfo.Extract(path).IsWorkshared;
            }
            catch (Exception)
            {
                return false; // ошибку покажет само открытие
            }
        }

        private View3D FindView(Document doc)
        {
            string name = _options.ViewName;
            return new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .Where(v => !v.IsTemplate)
                .Where(v => _options.ViewNameExact
                    ? string.Equals(v.Name, name, StringComparison.Ordinal)
                    : v.Name.IndexOf(name, StringComparison.CurrentCultureIgnoreCase) >= 0)
                .OrderBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
                .FirstOrDefault();
        }

        private static bool IsFileInUse(string path)
        {
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
                return false;
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
        }
    }
}
