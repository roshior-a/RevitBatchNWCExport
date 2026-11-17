using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using RevitBatchNWCExport.Export;
using RevitBatchNWCExport.Settings;

namespace RevitBatchNWCExport.UI
{
    public sealed class ExportWindowViewModel : ObservableObject
    {
        private int _selectedTab;
        private string _sourceFolder;
        private string _filesStatus;
        private string _targetFolder;
        private string _viewName;
        private bool _viewNameExact;

        public ExportWindowViewModel(ExportSettings settings, IList<TreeNodeViewModel> serverNodes, string serverStatus)
        {
            ServerNodes = new ObservableCollection<TreeNodeViewModel>(serverNodes ?? new List<TreeNodeViewModel>());
            foreach (var root in ServerNodes) root.CheckedChanged += (s, e) => OnPropertyChanged(nameof(SelectionSummary));
            ServerStatus = serverStatus;

            _selectedTab = settings.SelectedTab;
            _targetFolder = settings.TargetFolder;
            _viewName = settings.ViewName;
            _viewNameExact = settings.ViewNameExact;
            SourceFolder = settings.SourceFolder;

            CheckAllFilesCommand = new RelayCommand(() => SetAllFiles(true), () => Files.Count > 0);
            UncheckAllFilesCommand = new RelayCommand(() => SetAllFiles(false), () => Files.Count > 0);
        }

        // ---- Revit Server ----

        public ObservableCollection<TreeNodeViewModel> ServerNodes { get; }
        public string ServerStatus { get; }
        public bool HasServerStatus => !string.IsNullOrEmpty(ServerStatus);

        // ---- Папка с файлами ----

        public ObservableCollection<FileItemViewModel> Files { get; } = new ObservableCollection<FileItemViewModel>();

        public ICommand CheckAllFilesCommand { get; }
        public ICommand UncheckAllFilesCommand { get; }

        public string SourceFolder
        {
            get => _sourceFolder;
            set
            {
                if (SetProperty(ref _sourceFolder, value ?? "")) LoadFiles();
            }
        }

        public string FilesStatus
        {
            get => _filesStatus;
            private set => SetProperty(ref _filesStatus, value);
        }

        // ---- Общее ----

        /// <summary>0 — Revit Server, 1 — папка. Экспортируется то, что выбрано на активной вкладке.</summary>
        public int SelectedTab
        {
            get => _selectedTab;
            set
            {
                if (SetProperty(ref _selectedTab, value)) OnPropertyChanged(nameof(SelectionSummary));
            }
        }

        public string TargetFolder
        {
            get => _targetFolder;
            set => SetProperty(ref _targetFolder, value ?? "");
        }

        public string ViewName
        {
            get => _viewName;
            set
            {
                if (SetProperty(ref _viewName, value ?? "")) OnPropertyChanged(nameof(MatchHint));
            }
        }

        public bool ViewNameExact
        {
            get => _viewNameExact;
            set
            {
                if (!SetProperty(ref _viewNameExact, value)) return;
                OnPropertyChanged(nameof(ViewNameContains));
                OnPropertyChanged(nameof(MatchHint));
            }
        }

        public bool ViewNameContains
        {
            get => !_viewNameExact;
            set => ViewNameExact = !value;
        }

        public string MatchHint => ViewNameExact
            ? $"Экспортируется 3D-вид с именем «{ViewName}»."
            : $"Экспортируется первый по алфавиту 3D-вид, в имени которого есть «{ViewName}».";

        public string SelectionSummary
        {
            get
            {
                int count = GetSelectedItems().Count;
                string source = SelectedTab == ExportSettings.ServerTab ? "Revit Server" : "папка с файлами";
                return $"Источник: {source}. Выбрано моделей: {count}";
            }
        }

        public List<ExportItem> GetSelectedItems()
        {
            if (SelectedTab == ExportSettings.ServerTab)
            {
                return ServerNodes.SelectMany(n => n.CheckedModels())
                    .Select(n => new ExportItem { Name = n.Name, ModelPath = n.ModelPath, FromServer = true })
                    .ToList();
            }
            return Files.Where(f => f.IsChecked)
                .Select(f => new ExportItem { Name = f.Name, ModelPath = f.FullPath, FromServer = false })
                .ToList();
        }

        public ExportOptions GetOptions() => new ExportOptions
        {
            TargetFolder = TargetFolder.Trim(),
            ViewName = ViewName.Trim(),
            ViewNameExact = ViewNameExact
        };

        /// <returns>Текст ошибки или null, если всё заполнено.</returns>
        public string Validate()
        {
            if (GetSelectedItems().Count == 0)
                return SelectedTab == ExportSettings.ServerTab
                    ? "Отметьте модели в дереве Revit Server."
                    : "Отметьте файлы в папке.";
            if (string.IsNullOrWhiteSpace(TargetFolder))
                return "Укажите каталог для NWC.";
            if (!Directory.Exists(TargetFolder.Trim()))
                return "Каталог для NWC не найден:\n" + TargetFolder;
            if (string.IsNullOrWhiteSpace(ViewName))
                return "Укажите имя вида для экспорта.";
            return null;
        }

        public ExportSettings ToSettings() => new ExportSettings
        {
            SelectedTab = SelectedTab,
            SourceFolder = SourceFolder?.Trim() ?? "",
            TargetFolder = TargetFolder?.Trim() ?? "",
            ViewName = string.IsNullOrWhiteSpace(ViewName) ? ExportSettings.DefaultViewName : ViewName.Trim(),
            ViewNameExact = ViewNameExact
        };

        private void LoadFiles()
        {
            Files.Clear();
            string folder = _sourceFolder.Trim();
            if (folder.Length == 0)
            {
                FilesStatus = "Выберите папку с моделями.";
            }
            else if (!Directory.Exists(folder))
            {
                FilesStatus = "Папка не найдена.";
            }
            else
            {
                try
                {
                    // Только сама папка, без подпапок; резервные копии «*.0001.rvt» не показываем
                    var files = Directory.GetFiles(folder, "*.rvt", SearchOption.TopDirectoryOnly)
                        .Where(f => string.Equals(Path.GetExtension(f), ".rvt", StringComparison.OrdinalIgnoreCase))
                        .Where(f => !IsBackup(f))
                        .OrderBy(f => Path.GetFileName(f), StringComparer.CurrentCultureIgnoreCase);
                    foreach (var file in files)
                    {
                        var item = new FileItemViewModel(file, File.GetLastWriteTime(file));
                        item.CheckedChanged += (s, e) => OnPropertyChanged(nameof(SelectionSummary));
                        Files.Add(item);
                    }
                    FilesStatus = Files.Count == 0 ? "В папке нет файлов RVT." : $"Файлов RVT: {Files.Count}";
                }
                catch (Exception ex)
                {
                    FilesStatus = "Не удалось прочитать папку: " + ex.Message;
                }
            }
            OnPropertyChanged(nameof(SelectionSummary));
            CommandManager.InvalidateRequerySuggested();
        }

        private static bool IsBackup(string file)
        {
            // Модель.0001.rvt
            var name = Path.GetFileNameWithoutExtension(file);
            int dot = name.LastIndexOf('.');
            return dot > 0 && name.Length - dot - 1 == 4 && name.Substring(dot + 1).All(char.IsDigit);
        }

        private void SetAllFiles(bool value)
        {
            foreach (var f in Files) f.IsChecked = value;
        }
    }
}
