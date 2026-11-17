using System.Windows;
using System.Windows.Interop;

namespace RevitBatchNWCExport.UI
{
    public partial class ExportWindow : Window
    {
        public ExportWindow(ExportWindowViewModel viewModel)
        {
            InitializeComponent();
            ViewModel = viewModel;
            DataContext = viewModel;
        }

        public ExportWindowViewModel ViewModel { get; }

        private void BrowseSource_Click(object sender, RoutedEventArgs e)
        {
            var folder = FolderPicker.Pick(new WindowInteropHelper(this).Handle, "Папка с моделями RVT", ViewModel.SourceFolder);
            if (folder != null) ViewModel.SourceFolder = folder;
        }

        private void BrowseTarget_Click(object sender, RoutedEventArgs e)
        {
            var folder = FolderPicker.Pick(new WindowInteropHelper(this).Handle, "Каталог для NWC", ViewModel.TargetFolder);
            if (folder != null) ViewModel.TargetFolder = folder;
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            string error = ViewModel.Validate();
            if (error != null)
            {
                MessageBox.Show(this, error, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            DialogResult = true;
        }
    }
}
