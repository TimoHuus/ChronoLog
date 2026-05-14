using System;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media.Animation;

namespace ChronoLog
{
    public partial class MainWindow : Window
    {
        // --- STATE VARIABLES ---
        private double _hiddenPosition;
        private double _visiblePosition;

        private ObservableCollection<LogEntry> _logEntries;
        private ICollectionView _feedView;
        private string _activeContext = "All";

        private ObservableCollection<ContextItem> _contextItems;

        public MainWindow()
        {
            InitializeComponent();

            // Ensure database schema exists (does not wipe existing DB)
            DatabaseHelper.InitializeDatabase();

            // Load History
            var history = DatabaseHelper.GetRecentEntries();
            _logEntries = new ObservableCollection<LogEntry>(history);
            FeedList.ItemsSource = _logEntries;

            // Load Contexts
            _contextItems = new ObservableCollection<ContextItem>();
            // Ensure reserved contexts are always on top
            _contextItems.Add(new ContextItem { Name = "All" });
            _contextItems.Add(new ContextItem { Name = "Archive" });

            foreach (var ctx in DatabaseHelper.GetContexts())
            {
                // ctx is a DatabaseHelper.ContextRecord
                // Skip reserved names to avoid duplicates
                if (ctx.Name == "All" || ctx.Name == "Archive") continue;
                _contextItems.Add(new ContextItem { Name = ctx.Name, Color = ctx.Color });
            }
            ContextList.ItemsSource = _contextItems;
            ContextList.SelectedIndex = 0; // Select "All" by default

            // Apply Filtering View
            _feedView = CollectionViewSource.GetDefaultView(_logEntries);
            _feedView.Filter = FilterFeed;

            RefreshTodoCounts();
        }

        // --- WINDOW MECHANICS ---

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;

            _visiblePosition = screenWidth - this.Width;
            _hiddenPosition = screenWidth - 10;

            this.Height = SystemParameters.PrimaryScreenHeight / 2;
            this.Top = 0;
            this.Left = _hiddenPosition;

            ScrollToBottom();
        }

        private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            DoubleAnimation slideOut = new DoubleAnimation(_visiblePosition, TimeSpan.FromSeconds(0.2));
            this.BeginAnimation(Window.LeftProperty, slideOut);
        }

        private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            DoubleAnimation slideIn = new DoubleAnimation(_hiddenPosition, TimeSpan.FromSeconds(0.2));
            slideIn.BeginTime = TimeSpan.FromMilliseconds(500);
            this.BeginAnimation(Window.LeftProperty, slideIn);
        }

        private void ScrollToBottom()
        {
            Dispatcher.InvokeAsync(() =>
            {
                if (FeedList.Items.Count > 0)
                {
                    FeedList.ScrollIntoView(FeedList.Items[FeedList.Items.Count - 1]);
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }

        // --- USER INPUT & LOGIC ---

        private void InputBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            // Hide placeholder when user begins typing
            InputPlaceholder.Visibility = string.IsNullOrEmpty(InputBox.Text) ? Visibility.Visible : Visibility.Hidden;

            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                string newNote = InputBox.Text;

                if (!string.IsNullOrWhiteSpace(newNote))
                {
                    LogEntry newLog = new LogEntry
                    {
                        Timestamp = DateTime.Now,
                        ContextName = _activeContext, // Simplified, since "All" and "Archive" lock the input box
                        Note = newNote
                    };

                    // Apply the selected context color immediately so the new entry shows colored time block
                    var ctxItem = _contextItems != null ? System.Linq.Enumerable.FirstOrDefault(_contextItems, c => c.Name == _activeContext) : null;
                    if (ctxItem != null)
                    {
                        newLog.Color = ctxItem.Color;
                    }

                    DatabaseHelper.SaveEntry(newLog);
                    _logEntries.Add(newLog);

                    if (newLog.IsTodo)
                    {
                        var ctxToUpdate = _contextItems.FirstOrDefault(c => c.Name == _activeContext);
                        if (ctxToUpdate != null)
                        {
                            ctxToUpdate.TodoCount++;
                        }
                    }

                    FeedList.ScrollIntoView(newLog);
                    InputBox.Clear();
                    InputPlaceholder.Visibility = Visibility.Visible;
                }
            }
        }

        // --- CONTEXT MANAGEMENT ---

        private void AddContext_Click(object sender, RoutedEventArgs e)
        {
            AddNewContext();
        }

        private void NewContextBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                e.Handled = true;
                AddNewContext();
            }
        }

        private void AddNewContext()
        {
            string newCtx = NewContextBox.Text.Trim();

            if (!string.IsNullOrWhiteSpace(newCtx) && newCtx != "All" && newCtx != "Archive")
            {
                string assignedColor = DatabaseHelper.AddContext(newCtx);
                _contextItems.Add(new ContextItem { Name = newCtx, Color = assignedColor });
                NewContextBox.Clear();
            }
        }

        private void DeleteContext_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string ctxName)
            {
                MessageBoxResult result = MessageBox.Show(
                    $"Are you sure you want to delete '{ctxName}'?\n\nIts log entries will be moved to 'Archive'.",
                    "Confirm Deletion",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // 1. Update the Hard Drive (SQLite)
                    DatabaseHelper.DeleteContext(ctxName);

                    // 2. NEW: Update the RAM (Our live array) so the UI knows they moved!
                    foreach (var log in _logEntries)
                    {
                        if (log.ContextName == ctxName)
                        {
                            log.ContextName = "Archive";
                            // Archive has no color
                            log.Color = null;
                        }
                    }

                    // 3. Remove the context name from the sidebar UI
                    for (int i = 0; i < _contextItems.Count; i++)
                    {
                        if (_contextItems[i].Name == ctxName)
                        {
                            _contextItems.RemoveAt(i);
                            break;
                        }
                    }

                    // 4. Snap back to "All" if we were looking at the deleted project
                    if (_activeContext == ctxName)
                    {
                        ContextList.SelectedIndex = 0;
                    }

                    // 5. Refresh the mask. It will now see the "General" tags in RAM and filter them correctly.
                    if (_feedView != null)
                    {
                        _feedView.Refresh();
                    }
                }
            }
        }

        // --- FILTERING ---

        private bool FilterFeed(object item)
        {
            LogEntry entry = item as LogEntry;

            if (_activeContext == "All")
                return entry?.ContextName != "Archive"; // Show everything EXCEPT the bin

            if (_activeContext == "Archive")
                return entry?.ContextName == "Archive"; // ONLY show the bin

            return entry?.ContextName == _activeContext;
        }

        private void ContextList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ContextList.SelectedItem is ContextItem selectedItem)
            {
                _activeContext = selectedItem.Name;

                if (_activeContext == "All" || _activeContext == "Archive")
                {
                    InputBox.IsEnabled = false;
                    InputPlaceholder.Visibility = Visibility.Visible;
                }
                else
                {
                    InputBox.IsEnabled = true;
                    InputPlaceholder.Visibility = Visibility.Hidden;
                }

                if (_feedView != null)
                {
                    _feedView.Refresh();
                    ScrollToBottom();
                }
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Create a new instance of the settings window
            SettingsWindow settings = new SettingsWindow();

            // ShowDialog pauses the main window until the settings window is closed
            settings.ShowDialog();
        }

        private void TodoCheckbox_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.DataContext is LogEntry entry)
            {
                if (entry.TryAcknowledge())
                {
                    DatabaseHelper.UpdateEntryDone(entry.Id, true);
                    cb.IsEnabled = false;

                    // Decrement the active counter
                    var ctxToUpdate = _contextItems.FirstOrDefault(c => c.Name == entry.ContextName);
                    if (ctxToUpdate != null && ctxToUpdate.TodoCount > 0)
                    {
                        ctxToUpdate.TodoCount--;
                    }
                }
            }
        }

        private void RefreshTodoCounts()
        {
            foreach (var ctx in _contextItems)
            {
                if (ctx.Name == "All" || ctx.Name == "Archive") continue;

                ctx.TodoCount = _logEntries.Count(log => log.ContextName == ctx.Name && log.IsTodo == true && log.Done != true);
            }
        }
    }

    public class ContextItem : INotifyPropertyChanged
    {
        private int _todoCount;

        public string Name { get; set; }
        public string? Color { get; set; }
        public Visibility DeleteVisibility => Name == "All" || Name == "Archive" ? Visibility.Hidden : Visibility.Visible;

        public int TodoCount
        {
            get => _todoCount;
            set
            {
                if (_todoCount != value)
                {
                    _todoCount = value;
                    OnPropertyChanged(nameof(TodoCount));
                    OnPropertyChanged(nameof(BadgeVisibility));
                }
            }
        }

        public Visibility BadgeVisibility => TodoCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}