using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace RevitBatchNWCExport.Settings
{
    /// <summary>Настройки окна: %AppData%\RevitBatchNWCExport\settings.json.</summary>
    [DataContract]
    public sealed class ExportSettings
    {
        public const string DefaultViewName = "Navisworks";

        public const int ServerTab = 0;
        public const int FolderTab = 1;

        [DataMember(Order = 1)] public int SelectedTab { get; set; } = ServerTab;
        [DataMember(Order = 2)] public string SourceFolder { get; set; } = "";
        [DataMember(Order = 3)] public string TargetFolder { get; set; } = "";
        [DataMember(Order = 4)] public string ViewName { get; set; } = DefaultViewName;
        [DataMember(Order = 5)] public bool ViewNameExact { get; set; }

        public static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RevitBatchNWCExport", "settings.json");

        public static ExportSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    using (var stream = File.OpenRead(FilePath))
                    {
                        var settings = (ExportSettings)new DataContractJsonSerializer(typeof(ExportSettings)).ReadObject(stream);
                        return settings.Normalized();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RevitBatchNWCExport: не удалось прочитать настройки: " + ex.Message);
            }
            return new ExportSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                using (var stream = File.Create(FilePath))
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
                {
                    new DataContractJsonSerializer(typeof(ExportSettings)).WriteObject(writer, this);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RevitBatchNWCExport: не удалось сохранить настройки: " + ex.Message);
            }
        }

        // При десериализации инициализаторы свойств не выполняются
        private ExportSettings Normalized()
        {
            if (SelectedTab != ServerTab && SelectedTab != FolderTab) SelectedTab = ServerTab;
            SourceFolder = SourceFolder ?? "";
            TargetFolder = TargetFolder ?? "";
            if (string.IsNullOrWhiteSpace(ViewName)) ViewName = DefaultViewName;
            return this;
        }
    }
}
