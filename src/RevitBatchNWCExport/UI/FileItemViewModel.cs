using System;

namespace RevitBatchNWCExport.UI
{
    /// <summary>RVT-файл на вкладке «Папка с файлами».</summary>
    public sealed class FileItemViewModel : ObservableObject
    {
        private bool _isChecked;

        public FileItemViewModel(string fullPath, DateTime modifiedAt)
        {
            FullPath = fullPath;
            Name = System.IO.Path.GetFileName(fullPath);
            ModifiedText = modifiedAt.ToString("dd.MM.yyyy HH:mm");
        }

        public event EventHandler CheckedChanged;

        public string FullPath { get; }
        public string Name { get; }
        public string ModifiedText { get; }

        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (SetProperty(ref _isChecked, value)) CheckedChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
}
