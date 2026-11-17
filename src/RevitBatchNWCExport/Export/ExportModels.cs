using System;
using System.Collections.Generic;

namespace RevitBatchNWCExport.Export
{
    public sealed class ExportItem
    {
        public string Name { get; set; }
        /// <summary>Путь для открытия: RSN://… или путь к файлу .rvt.</summary>
        public string ModelPath { get; set; }
        public bool FromServer { get; set; }
    }

    public sealed class ExportOptions
    {
        public string TargetFolder { get; set; }
        public string ViewName { get; set; }
        /// <summary>true — имя вида совпадает полностью, false — содержит текст.</summary>
        public bool ViewNameExact { get; set; }
    }

    public sealed class ExportReportLine
    {
        public string Name { get; set; }
        public bool Success { get; set; }
        public string Message { get; set; }
        public TimeSpan Elapsed { get; set; }
        /// <summary>Окна и ошибки Revit, закрытые без участия пользователя.</summary>
        public List<string> SuppressedDialogs { get; } = new List<string>();
    }

    public sealed class ExportReport
    {
        public List<ExportReportLine> Lines { get; } = new List<ExportReportLine>();
        public int Total { get; set; }
        public bool Cancelled { get; set; }
    }
}
