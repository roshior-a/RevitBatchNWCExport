using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RevitBatchNWCExport.UI
{
    public enum TreeNodeKind
    {
        Server,
        Folder,
        Model
    }

    /// <summary>
    /// Узел дерева Revit Server. Галочка папки ставит/снимает галочки всех вложенных моделей;
    /// если внутри отмечена только часть, у папки неопределённое состояние.
    /// </summary>
    public sealed class TreeNodeViewModel : ObservableObject
    {
        private bool? _isChecked = false;
        private bool _isExpanded;
        private DateTime? _modifiedAt;
        private string _error;

        private TreeNodeViewModel(TreeNodeKind kind, string name, string modelPath, TreeNodeViewModel parent)
        {
            Kind = kind;
            Name = name;
            ModelPath = modelPath;
            Parent = parent;
        }

        public static TreeNodeViewModel CreateServer(string host) =>
            new TreeNodeViewModel(TreeNodeKind.Server, host, null, null) { _isExpanded = true };

        public static TreeNodeViewModel CreateFolder(string name, TreeNodeViewModel parent) =>
            new TreeNodeViewModel(TreeNodeKind.Folder, name, null, parent);

        public static TreeNodeViewModel CreateModel(string name, string modelPath, TreeNodeViewModel parent) =>
            new TreeNodeViewModel(TreeNodeKind.Model, name, modelPath, parent);

        /// <summary>Вызывается при любом изменении галочки внутри дерева.</summary>
        public event EventHandler CheckedChanged;

        public TreeNodeKind Kind { get; }
        public string Name { get; }
        /// <summary>Путь для открытия: RSN://сервер/папка/модель.rvt.</summary>
        public string ModelPath { get; }
        public TreeNodeViewModel Parent { get; }
        public ObservableCollection<TreeNodeViewModel> Children { get; } = new ObservableCollection<TreeNodeViewModel>();

        public bool IsModel => Kind == TreeNodeKind.Model;
        public bool IsServer => Kind == TreeNodeKind.Server;

        public bool? IsChecked
        {
            get => _isChecked;
            set => SetChecked(value, updateChildren: true, updateParent: true);
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public DateTime? ModifiedAt
        {
            get => _modifiedAt;
            set
            {
                if (SetProperty(ref _modifiedAt, value)) OnPropertyChanged(nameof(ModifiedText));
            }
        }

        public string ModifiedText => _modifiedAt.HasValue ? _modifiedAt.Value.ToString("dd.MM.yyyy HH:mm") : "";

        public string Error
        {
            get => _error;
            set
            {
                if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(_error);

        public IEnumerable<TreeNodeViewModel> Descendants() =>
            Children.SelectMany(c => new[] { c }.Concat(c.Descendants()));

        public IEnumerable<TreeNodeViewModel> CheckedModels() =>
            (IsModel ? new[] { this } : Descendants()).Where(n => n.IsModel && n.IsChecked == true);

        private void SetChecked(bool? value, bool updateChildren, bool updateParent)
        {
            if (value == _isChecked) return;
            _isChecked = value;

            if (updateChildren && value.HasValue)
                foreach (var child in Children)
                    child.SetChecked(value, true, false);

            if (updateParent) Parent?.UpdateFromChildren();

            OnPropertyChanged(nameof(IsChecked));
            if (updateParent) RaiseCheckedChanged();
        }

        private void UpdateFromChildren()
        {
            bool? state;
            if (Children.All(c => c.IsChecked == true)) state = true;
            else if (Children.All(c => c.IsChecked == false)) state = false;
            else state = null;
            SetChecked(state, updateChildren: false, updateParent: true);
        }

        private void RaiseCheckedChanged()
        {
            var root = this;
            while (root.Parent != null) root = root.Parent;
            root.CheckedChanged?.Invoke(root, EventArgs.Empty);
        }
    }
}
