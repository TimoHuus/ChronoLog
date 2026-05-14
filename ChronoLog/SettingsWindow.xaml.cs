using System.Windows;

namespace ChronoLog
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void WipeDatabase_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                "Are you absolutely sure you want to wipe the database?\n\nThis will delete all logs and custom contexts permanently. The application will close immediately to ensure a clean slate.",
                "FACTORY RESET",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                // Destroy and rebuild the database files
                DatabaseHelper.WipeDatabase();

                // Safely kill the application to clear the RAM
                System.Windows.Application.Current.Shutdown();
            }
        }
    }
}