using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitBatchNWCExport.Export;
using RevitBatchNWCExport.RevitServer;
using RevitBatchNWCExport.Settings;
using RevitBatchNWCExport.UI;

namespace RevitBatchNWCExport.Commands
{
    [Transaction(TransactionMode.Manual)]
    public class BatchExportCommand : IExternalCommand
    {
        private const string Caption = "NWC Экспорт";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            string version = uiApp.Application.VersionNumber;
            IntPtr owner = uiApp.MainWindowHandle;

            if (!OptionalFunctionalityUtils.IsNavisworksExporterAvailable())
            {
                TaskDialog.Show(Caption, "Не установлен экспортёр Navisworks (Navisworks NWC Export Utility) для Revit " + version + ".");
                return Result.Failed;
            }

            var settings = ExportSettings.Load();

            // 1. Дерево Revit Server
            var hosts = RevitServerConfig.GetServers(version);
            var nodes = new System.Collections.Generic.List<TreeNodeViewModel>();
            string serverStatus;
            if (hosts.Count == 0)
            {
                serverStatus = "Серверы Revit Server " + version + " не настроены: нет списка в " + RevitServerConfig.RsnPath(version);
            }
            else
            {
                var loading = new TreeLoadingWindow(hosts, version);
                new WindowInteropHelper(loading).Owner = owner;
                loading.ShowDialog();
                nodes = loading.Nodes;
                serverStatus = loading.Status;
            }

            // 2. Выбор моделей и параметров
            var viewModel = new ExportWindowViewModel(settings, nodes, serverStatus);
            var window = new ExportWindow(viewModel);
            new WindowInteropHelper(window).Owner = owner;
            bool? ok = window.ShowDialog();
            viewModel.ToSettings().Save();
            if (ok != true) return Result.Cancelled;

            // 3. Экспорт
            var items = viewModel.GetSelectedItems();
            var options = viewModel.GetOptions();
            var report = new BatchExporter(uiApp, options).Run(items);
            ShowReport(report, options.TargetFolder);
            return Result.Succeeded;
        }

        private static void ShowReport(ExportReport report, string targetFolder)
        {
            int succeeded = report.Lines.Count(l => l.Success);
            int failed = report.Lines.Count(l => !l.Success);
            int skipped = report.Total - report.Lines.Count;

            var details = new StringBuilder();
            foreach (var line in report.Lines)
            {
                details.AppendLine($"{(line.Success ? "✓" : "✗")} {line.Name} — {line.Message} ({FormatElapsed(line.Elapsed)})");
                foreach (var entry in line.SuppressedDialogs) details.AppendLine("      · " + entry);
            }

            var content = new StringBuilder();
            if (failed > 0) content.AppendLine($"С ошибками: {failed}");
            if (skipped > 0) content.AppendLine($"Не обработано после остановки: {skipped}");
            content.Append("Каталог: " + targetFolder);

            var dialog = new TaskDialog(Caption)
            {
                MainInstruction = $"Экспортировано в NWC: {succeeded} из {report.Total}" + (report.Cancelled ? " (остановлено)" : ""),
                MainContent = content.ToString(),
                ExpandedContent = details.ToString(),
                MainIcon = failed > 0 ? TaskDialogIcon.TaskDialogIconWarning : TaskDialogIcon.TaskDialogIconInformation,
                CommonButtons = TaskDialogCommonButtons.Close
            };
            if (succeeded > 0) dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Открыть каталог с NWC");

            if (dialog.Show() == TaskDialogResult.CommandLink1)
            {
                Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "\"" + targetFolder + "\"", UseShellExecute = true });
            }
        }

        private static string FormatElapsed(TimeSpan t) =>
            t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} мин {t.Seconds} с" : $"{t.Seconds} с";
    }
}
