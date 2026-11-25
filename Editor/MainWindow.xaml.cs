using Microsoft.Win32;
using System.IO;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Text.Json;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using WinOS_Editor.toolchains;
using WinOS_Editor.Analysis;
using WinOS_Editor.Build;

namespace WinOS_Editor
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private FrameworkProject? CurrentProject; // currently loaded project
        private string? CurrentProjectPath; // obvious lol

        private readonly DocMgr _documentManager = new();

        private readonly ToolchainManager _toolchainManager = new();

        private ICodeAnalyzer? _codeAnalyzer;

        private readonly Dictionary<string, List<Symbol>> _fileSymbols =
            new(StringComparer.OrdinalIgnoreCase);

        private double _editorFontSize = 14;
        private static readonly string SettingsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinOS-Editor");
        private static readonly string SettingsPath = Path.Combine(SettingsDirectory, "settings.json");
        private string _lastSearchText = "";

        public MainWindow()
        {
            InitializeComponent();
            LoadEditorSettings();

            _toolchainManager.DetectLLVM();

            if (_toolchainManager.IsAvailable && _toolchainManager.LLVMPath is string llvmPath)
                _codeAnalyzer = new ClangCodeAnalyzer(llvmPath);

            if (App.FilePath != string.Empty)
                OpenProject(App.FilePath);
        }

        // Props set;
        public void FrameworkNameSet(string content)
        {
            if (CurrentProject != null)
                CurrentProject.Name = content;
        }

        public void FrameworkVersionSet(string content)
        {
            if (CurrentProject != null)
                CurrentProject.Version = content;
        }

        public void FrameworkDescriptionSet(string content)
        {
            if (CurrentProject != null)
                CurrentProject.Description = content;
        }

        /// <summary>
        /// Shhows the Framework metadata window.
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="e">eventargs</param>
        private void ShowFrameworkMetadata(object sender, RoutedEventArgs? e)
        {
            FMeta window = new() { Owner = this };

            if (CurrentProject != null)
            {
                window.FrameworkNameSet(CurrentProject.Name);
                window.FrameworkVersionSet(CurrentProject.Version);
                window.FrameworkDescriptionSet(CurrentProject.Description);
            }

            window.ShowDialog();
        }

        private bool PrepareForProjectSwitch()
        {
            if (CurrentProject == null)
                return true;

            foreach (TabItem tab in EditorTabs.Items
                         .Cast<TabItem>().ToList())
            {
                if (tab.Tag is Document document)
                {
                    if (!ConfirmDocumentClose(document))
                        return false;
                }
            }

            if (!SaveProject())
                return false;

            // Clear the previous project's workspace
            EditorTabs.Items.Clear();
            _documentManager.Clear();
            ProjectTree.Items.Clear();
            FWTree.Items.Clear();
            _fileSymbols.Clear();

            return true;
        }

        /// <summary>
        /// Creates a new project.
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="e">eventargs</param>

        private void NewProject(object? sender, RoutedEventArgs? e)
        {
            // Safely close the previous project
            if (!PrepareForProjectSwitch()) return;

            // Create and save the new project
            CurrentProject = new FrameworkProject();
            CurrentProjectPath = string.Empty;

            if (!SaveProject())
            {
                CurrentProject = null;
                CurrentProjectPath = null;
                return;
            }

            // Generate demo.cpp from the template
            string projectDirectory =
                Path.GetDirectoryName(CurrentProjectPath)!;

            string templatePath =
                Path.Combine(AppContext.BaseDirectory,
                             "Templates", "demo.cpp");

            string demoPath =
                Path.Combine(projectDirectory, "demo.cpp");

            try
            {
                File.Copy(templatePath, demoPath);

                CurrentProject.SourceFiles.Add("demo.cpp");

                RefreshProjectTree();
                OpenDocumentInEditor(demoPath);

                SaveProject();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to create starter source file:\n{ex.Message}",
                    "New Project",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            ShowFrameworkMetadata(this, null);
        }

        /// <summary>
        /// Opens a project from path (file picker)
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="e">eventargs</param>
        // overload 1 : OG
        public void OpenProject(string projectPath)
        {
            string path;

            if (projectPath != string.Empty)
            {
                path = projectPath;
            }
            else
            {
                // setup file picker
                OpenFileDialog openFileDialog = new()
                {
                    Title = "Open WinOS Framework Project",
                    Filter = "WinDroid Framework Project (*.wdfp)|*.wdfp",
                    DefaultExt = ".wdfp",
                    AddExtension = true,
                };

                // open window and return if closed without picking
                if (openFileDialog.ShowDialog(this) == false) return;
                else if (openFileDialog.FileName == string.Empty) // return if empty path
                {
                    MessageBox.Show("Path cannot be empty!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                path = openFileDialog.FileName;
            }

            if (!PrepareForProjectSwitch()) return;

            // temporary, so the current project wont get pre cooked
            FrameworkProject? tmp = ProjectManager.Load(path);
            if (tmp != null)
            {
                // write current project and its path
                CurrentProjectPath = path;
                CurrentProject = tmp;
                _ = AnalyzeProjectAsync();

                // rebuild project hierarchy
                RefreshProjectTree();

                // restore previously open documents
                string projectDirectory = Path.GetDirectoryName(CurrentProjectPath)!;

                foreach (string relativePath in CurrentProject.OpenDocuments)
                {
                    string fullPath = Path.GetFullPath(
                        Path.Combine(projectDirectory, relativePath)
                    );

                    if (File.Exists(fullPath))
                        OpenDocumentInEditor(fullPath);
                }

                // restore previously active document
                if (!string.IsNullOrEmpty(CurrentProject.ActiveDocumentPath))
                {
                    string activePath = Path.GetFullPath(
                        Path.Combine(projectDirectory, CurrentProject.ActiveDocumentPath)
                    );

                    foreach (TabItem tab in EditorTabs.Items)
                    {
                        if (tab.Tag is Document document &&
                            Path.GetFullPath(document.FilePath) == activePath)
                        {
                            EditorTabs.SelectedItem = tab;
                            break;
                        }
                    }
                }
            }
        }

        // overload 2 : for button
        private void OpenProject(object? sender, RoutedEventArgs e)
        {
            OpenProject(string.Empty);
        }

        /// <summary>
        /// Saves the project. Opens file picker if necessary.
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="e">eventargs</param>
        // overload 1 : returns bool
        private bool SaveProject()
        {
            // js save to existing if path is already there
            if (!string.IsNullOrEmpty(CurrentProjectPath) && CurrentProject != null)
            {
                UpdateWorkspaceState();
                return ProjectManager.Save(CurrentProject, CurrentProjectPath);
            }
            // else if no path but project is present
            else
            {
                {
                    // setup file picker
                    SaveFileDialog saveFileDialog = new()
                    {
                        Title = "Save WinOS Framework Project",
                        Filter = "WinDroid Framework Project (*.wdfp)|*.wdfp",
                        DefaultExt = ".wdfp",
                        AddExtension = true,
                    };

                    // open window and return if closed without picking
                    if (saveFileDialog.ShowDialog(this) == false) return false;
                    else if (saveFileDialog.FileName == string.Empty) // return if empty path
                    {
                        MessageBox.Show("Path cannot be empty!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }

                    UpdateWorkspaceState();
                    if (ProjectManager.Save(CurrentProject, saveFileDialog.FileName))
                    {
                        CurrentProjectPath = saveFileDialog.FileName;
                        return true;
                    }

                    return false;
                }
            }
        }

        // overload 2 : returns nothing, for button
        private void SaveProject(object? sender, RoutedEventArgs? e) { SaveProject(); }

        private void Exit(object sender, RoutedEventArgs? e)
        {
            Application.Current.Shutdown();
        }

        private void SaveplusExit(object sender, RoutedEventArgs e)
        {
            try
            {
                _documentManager.SaveAllDocuments();

                foreach (TabItem tab in EditorTabs.Items)
                {
                    if (tab.Tag is Document document)
                        UpdateTabHeader(tab, document);
                }

                if (SaveProject())
                    Exit(this, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Save + Exit Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // tree double click

        private void FWTreeDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (FWTree.SelectedItem is not TreeViewItem item)
                return;

            if (item.Tag is Symbol symbol)
                NavigateToSymbol(symbol);
        }

        private void ProjectTreeDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (ProjectTree.SelectedItem is not TreeViewItem item)
                return;

            if (item.Tag is string filePath)
            {
                if (File.Exists(filePath))
                    OpenDocumentInEditor(filePath);

                return;
            }

            if (item.Tag is Symbol symbol)
                NavigateToSymbol(symbol);
        }

        private void UpdateTabHeader(TabItem tab, Document document)
        {
            if (tab.Header is DockPanel header &&
                header.Children.OfType<TextBlock>().FirstOrDefault() is TextBlock title)
            {
                title.Text = document.FileName + (document.IsModified ? "*" : "");
            }
        }

        private void UpdateEditorStatus()
        {
            WorkspaceStatusText.Text = CurrentProject?.Name ?? "No project";

            if (EditorTabs.SelectedItem is not TabItem tab ||
                tab.Content is not TextEditor editor)
            {
                CaretStatusText.Text = "";
                return;
            }

            int line = editor.Document.GetLineByOffset(editor.TextArea.Caret.Offset).LineNumber - 1;
            int lineStart = editor.Document.GetLineByNumber(line + 1).Offset;
            int column = editor.TextArea.Caret.Offset - lineStart;

            CaretStatusText.Text = $"Ln {line + 1}, Col {column + 1}";
        }

        private void CreateEditorTab(Document document)
        {
            TextEditor editor = new()
            {
                Name = "Code",
                Text = document.Content,
                FontSize = _editorFontSize,
                WordWrap = true,
                ShowLineNumbers = true,
                Margin = new(24, 4, 4, 4),
                Background = SystemColors.DesktopBrush,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                BorderBrush = SystemColors.AccentColorDark2Brush,
                Cursor = Cursors.IBeam,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            };

            editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("C++");
            editor.PreviewMouseWheel += (_, e) =>
            {
                if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
                    return;

                _editorFontSize = Math.Clamp(_editorFontSize + (e.Delta > 0 ? 1 : -1), 8, 32);
                ApplyEditorZoom();
                SaveEditorSettings();
                e.Handled = true;
            };

            TextBlock title = new()
            {
                Text = document.FileName,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 2, 0)
            };

            Button closeButton = new()
            {
                Content = "×",
                Tag = document,
                Width = 18,
                Height = 18,
                Padding = new Thickness(0),
                Margin = new Thickness(3, 0, 0, 0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Focusable = false,
                ToolTip = "Close tab"
            };

            DockPanel header = new() { LastChildFill = true };
            DockPanel.SetDock(closeButton, Dock.Right);
            header.Children.Add(closeButton);
            header.Children.Add(title);

            TabItem tab = new()
            {
                Header = header,
                Content = editor,
                Tag = document
            };

            closeButton.Click += (_, _) => CloseTab(tab);

            editor.TextChanged += (_, _) =>
            {
                document.Content = editor.Text;
                document.IsModified = true;
                UpdateTabHeader(tab, document);
                UpdateEditorStatus();
            };

            editor.TextArea.SelectionChanged += (_, _) => UpdateEditorStatus();

            // Pair brackets and quotes, wrapping any selected text.
            editor.PreviewTextInput += (_, e) =>
            {
                if (e.Text.Length != 1)
                    return;

                char typed = e.Text[0];
                char closing = typed switch
                {
                    '(' => ')',
                    '[' => ']',
                    '{' => '}',
                    '"' => '"',
                    '\'' => '\'',
                    _ => '\0'
                };

                if (closing != '\0')
                {
                    int selectionStart = editor.SelectionStart;
                    string selected = editor.SelectedText;

                    editor.SelectedText = typed + selected + closing;
                    editor.TextArea.Caret.Offset = selectionStart + 1 + selected.Length;
                    e.Handled = true;
                    return;
                }

                // Move over an existing closing character instead of duplicating it.
                if (typed is ')' or ']' or '}' or '"' or '\'')
                {
                    int caret = editor.TextArea.Caret.Offset;
                    if (caret < editor.Text.Length && editor.Text[caret] == typed)
                    {
                        editor.TextArea.Caret.Offset++;
                        e.Handled = true;
                    }
                }
            };

            editor.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    int caret = editor.SelectionStart;
                    string textBeforeCaret = editor.Text[..caret];
                    int lineStart = textBeforeCaret.LastIndexOf('\n') + 1;
                    string currentLine = textBeforeCaret[lineStart..];

                    string indentation = new(
                        currentLine.TakeWhile(c => c is ' ' or '\t').ToArray());

                    bool afterOpeningBrace = currentLine.TrimEnd().EndsWith('{');
                    bool beforeClosingBrace =
                        editor.SelectionLength == 0 &&
                        caret < editor.Text.Length &&
                        editor.Text[caret] == '}';

                    const string indentUnit = "    ";

                    if (afterOpeningBrace && beforeClosingBrace)
                    {
                        string newline = Environment.NewLine;
                        editor.SelectedText =
                            newline + indentation + indentUnit +
                            newline + indentation;

                        editor.TextArea.Caret.Offset =
                            caret + newline.Length +
                            indentation.Length + indentUnit.Length;

                        e.Handled = true;
                        return;
                    }

                    string nextIndentation = afterOpeningBrace
                        ? indentation + indentUnit
                        : indentation;

                    string lineBreak = Environment.NewLine;
                    editor.SelectedText = lineBreak + nextIndentation;
                    editor.TextArea.Caret.Offset = caret + lineBreak.Length + nextIndentation.Length;
                    e.Handled = true;
                    return;
                }

                if (e.Key == Key.Tab)
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
                    {
                        int caret = editor.TextArea.Caret.Offset;
                        int line = editor.Document.GetLineByOffset(caret).LineNumber - 1;
                        int lineStart = editor.Document.GetLineByNumber(line + 1).Offset;
                        int removeCount = 0;

                        while (removeCount < 4 &&
                               lineStart + removeCount < editor.Text.Length &&
                               editor.Text[lineStart + removeCount] == ' ')
                        {
                            removeCount++;
                        }

                        if (removeCount == 0 &&
                            lineStart < editor.Text.Length &&
                            editor.Text[lineStart] == '\t')
                        {
                            removeCount = 1;
                        }

                        if (removeCount > 0)
                        {
                            editor.Select(lineStart, removeCount);
                            editor.SelectedText = "";
                            editor.TextArea.Caret.Offset = Math.Max(lineStart, caret - removeCount);
                        }
                    }
                    else
                    {
                        editor.SelectedText = "    ";
                    }

                    e.Handled = true;
                    return;
                }

                if (e.Key != Key.Back || editor.SelectionLength != 0)
                    return;

                int backspaceCaret = editor.TextArea.Caret.Offset;
                if (backspaceCaret <= 0 || backspaceCaret >= editor.Text.Length)
                    return;

                char before = editor.Text[backspaceCaret - 1];
                char after = editor.Text[backspaceCaret];

                bool matchingPair =
                    (before == '(' && after == ')') ||
                    (before == '[' && after == ']') ||
                    (before == '{' && after == '}') ||
                    (before == '"' && after == '"') ||
                    (before == '\'' && after == '\'');

                if (matchingPair)
                {
                    editor.Select(backspaceCaret - 1, 2);
                    editor.SelectedText = "";
                    e.Handled = true;
                }
            };

            EditorTabs.Items.Add(tab);
            EditorTabs.SelectedItem = tab;
            editor.Focus();
            UpdateEditorStatus();
        }

        private void CloseTab(TabItem tab)
        {
            if (tab.Tag is Document document)
            {
                if (!ConfirmDocumentClose(document))
                    return;

                _documentManager.CloseDocument(document);
            }

            EditorTabs.Items.Remove(tab);
            UpdateEditorStatus();
        }

        private void MainWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            bool control = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

            if (!control)
                return;

            if (e.Key == Key.S)
            {
                if (shift)
                    SaveAllDocumentsToolbarClick(this, new RoutedEventArgs());
                else
                    SaveActiveDocument();

                e.Handled = true;
            }
            else if (e.Key == Key.W)
            {
                CloseActiveTabToolbarClick(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.Tab && EditorTabs.Items.Count > 1)
            {
                int direction = shift ? -1 : 1;
                int count = EditorTabs.Items.Count;
                int next = (EditorTabs.SelectedIndex + direction + count) % count;
                EditorTabs.SelectedIndex = next;
                e.Handled = true;
            }
            else if (e.Key == Key.F)
            {
                ShowFindDialog(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.H)
            {
                ShowReplaceDialog(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key == Key.G)
            {
                ShowGoToLineDialog(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key is Key.OemPlus or Key.Add)
            {
                ZoomIn(this, new RoutedEventArgs());
                e.Handled = true;
            }
            else if (e.Key is Key.OemMinus or Key.Subtract)
            {
                ZoomOut(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        private void MainWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            foreach (TabItem tab in EditorTabs.Items.Cast<TabItem>().ToList())
            {
                if (tab.Tag is Document document && !ConfirmDocumentClose(document))
                {
                    e.Cancel = true;
                    return;
                }
            }

            if (CurrentProject != null && !SaveProject())
                e.Cancel = true;
        }

        private void OpenDocumentInEditor(string filePath)
        {
            Document document = _documentManager.OpenDocument(filePath);

            // Check whether this document already has a tab.
            foreach (TabItem tab in EditorTabs.Items)
            {
                if (tab.Tag == document)
                {
                    EditorTabs.SelectedItem = tab;
                    return;
                }
            }

            // No existing tab: create one.
            CreateEditorTab(document);
        }

        private void SaveActiveDocument()
        {
            if (_documentManager.ActiveDocument is not Document document)
                return;

            try
            {
                _documentManager.SaveDocument(document);

                if (EditorTabs.SelectedItem is TabItem tab)
                    UpdateTabHeader(tab, document);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Save Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        // editor tab delta
        private void EditorTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EditorTabs.SelectedItem is TabItem tab &&
                tab.Tag is Document document)
            {
                _documentManager.SetActiveDocument(document);
            }

            UpdateEditorStatus();
        }

        // editor toolbar button click functions
        private void SaveActiveDocumentToolbarClick(object sender, RoutedEventArgs e) { SaveActiveDocument(); }

        private void SaveAllDocumentsToolbarClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _documentManager.SaveAllDocuments();

                foreach (TabItem tab in EditorTabs.Items)
                {
                    if (tab.Tag is Document document)
                        UpdateTabHeader(tab, document);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Save All Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseActiveTabToolbarClick(object sender, RoutedEventArgs e)
        {
            if (EditorTabs.SelectedItem is TabItem tab)
                CloseTab(tab);
        }

        private void CloseAllTabsToolbarClick(object sender, RoutedEventArgs e)
        {
            List<TabItem> tabs = EditorTabs.Items.Cast<TabItem>().ToList();

            // Ask about every modified document before removing any tabs.
            foreach (TabItem tab in tabs)
            {
                if (tab.Tag is Document document && !ConfirmDocumentClose(document))
                    return;
            }

            foreach (TabItem tab in tabs)
            {
                if (tab.Tag is Document document)
                    _documentManager.CloseDocument(document);

                EditorTabs.Items.Remove(tab);
            }

            UpdateEditorStatus();
        }

        private bool ConfirmDocumentClose(Document document)
        {
            if (!document.IsModified)
                return true;

            MessageBoxResult result = MessageBox.Show(
                $"Save changes to {document.FileName}?",
                "Unsaved Changes",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Cancel)
                return false;

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _documentManager.SaveDocument(document);

                    TabItem? documentTab = EditorTabs.Items
                        .Cast<TabItem>()
                        .FirstOrDefault(t => t.Tag == document);

                    if (documentTab != null)
                        UpdateTabHeader(documentTab, document);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Save Failed",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return false;
                }
            }

            return true;
        }

        private void FWSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshFrameworkTree();
        }

        private void ProjectSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshProjectTree();
        }

        private static TreeViewItem? CreateFilteredSymbolTreeItem(
            Symbol symbol, string filter)
        {
            TreeViewItem item = new()
            {
                Foreground = SystemColors.AccentColorLight3Brush,
                Header = $"{symbol.Kind}: {symbol.Name}",
                Tag = symbol
            };

            bool selfMatches = string.IsNullOrWhiteSpace(filter) ||
                symbol.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                symbol.Kind.ToString().Contains(filter, StringComparison.OrdinalIgnoreCase);

            foreach (Symbol child in symbol.Children)
            {
                TreeViewItem? childItem = CreateFilteredSymbolTreeItem(child, filter);
                if (childItem != null)
                    item.Items.Add(childItem);
            }

            return selfMatches || item.Items.Count > 0 ? item : null;
        }

        private void RefreshProjectTree()
        {
            ProjectTree.Items.Clear();

            if (CurrentProject == null ||
                string.IsNullOrEmpty(CurrentProjectPath))
                return;

            string projectDirectory = Path.GetDirectoryName(CurrentProjectPath)!;
            string filter = ProjectSearch.Text.Trim();

            foreach (string source in CurrentProject.SourceFiles)
            {
                string fullPath = Path.GetFullPath(
                    Path.Combine(projectDirectory, source));
                string fileName = Path.GetFileName(source);
                bool fileMatches = string.IsNullOrWhiteSpace(filter) ||
                    fileName.Contains(filter, StringComparison.OrdinalIgnoreCase);

                TreeViewItem fileItem = new()
                {
                    Foreground = SystemColors.AccentColorLight3Brush,
                    Header = fileName,
                    Tag = fullPath
                };

                if (_fileSymbols.TryGetValue(fullPath, out List<Symbol>? symbols))
                {
                    foreach (Symbol symbol in symbols)
                    {
                        TreeViewItem? symbolItem = fileMatches
                            ? CreateSymbolTreeItem(symbol)
                            : CreateFilteredSymbolTreeItem(symbol, filter);

                        if (symbolItem != null)
                            fileItem.Items.Add(symbolItem);
                    }
                }

                if (fileMatches || fileItem.Items.Count > 0)
                    ProjectTree.Items.Add(fileItem);
            }
        }

        private void RefreshFrameworkTree()
        {
            FWTree.Items.Clear();

            List<Symbol> mergedSymbols = [];

            foreach (List<Symbol> symbols in _fileSymbols.Values)
                MergeSymbolLists(mergedSymbols, symbols);

            string filter = FWSearch.Text.Trim();
            foreach (Symbol symbol in mergedSymbols)
            {
                TreeViewItem? item = CreateFilteredSymbolTreeItem(symbol, filter);
                if (item != null)
                    FWTree.Items.Add(item);
            }
        }

        private static void MergeSymbolLists(
            List<Symbol> target,
            List<Symbol> incoming)
        {
            foreach (Symbol symbol in incoming)
            {
                bool canMerge =
                    symbol.Name.Length > 0 &&
                    symbol.Kind is
                        SymbolKind.Namespace or
                        SymbolKind.Class or
                        SymbolKind.Struct or
                        SymbolKind.Union or
                        SymbolKind.Enum;

                if (canMerge)
                {
                    Symbol? existing = target.FirstOrDefault(s =>
                        s.Kind == symbol.Kind &&
                        s.Name == symbol.Name);

                    if (existing == null)
                    {
                        target.Add(CloneSymbol(symbol));
                    }
                    else
                    {
                        MergeSymbolLists(existing.Children, symbol.Children);
                    }

                    continue;
                }

                if (IsCallable(symbol))
                {
                    Symbol? existing = target.FirstOrDefault(s =>
                        IsCallable(s) &&
                        SameCallable(s, symbol));

                    if (existing == null)
                    {
                        target.Add(CloneSymbol(symbol));
                    }
                    else
                    {
                        MergeCallable(existing, symbol);
                    }

                    continue;
                }

                target.Add(CloneSymbol(symbol));
            }
        }

        private static bool IsCallable(Symbol symbol)
        {
            return symbol.Kind is
                SymbolKind.Function or
                SymbolKind.Method or
                SymbolKind.Constructor or
                SymbolKind.Destructor;
        }

        private static bool SameCallable(Symbol a, Symbol b)
        {
            if (a.Kind != b.Kind || a.Name != b.Name)
                return false;

            // Mangled names distinguish overloads and method qualifiers.
            if (!string.IsNullOrEmpty(a.MangledName) &&
                !string.IsNullOrEmpty(b.MangledName))
            {
                return a.MangledName == b.MangledName;
            }

            // Use the analyzer's signature when both are available.
            if (!string.IsNullOrEmpty(a.Signature) &&
                !string.IsNullOrEmpty(b.Signature))
            {
                return a.Signature == b.Signature;
            }

            // Fallback: compare parameter types.
            if (a.Parameters.Count != b.Parameters.Count)
                return false;

            for (int i = 0; i < a.Parameters.Count; i++)
            {
                if (a.Parameters[i].Type != b.Parameters[i].Type)
                    return false;
            }

            return true;
        }

        private static void MergeCallable(Symbol existing, Symbol incoming)
        {
            // Keep useful documentation from either declaration.
            if (string.IsNullOrWhiteSpace(existing.Summary))
                existing.Summary = incoming.Summary;

            if (string.IsNullOrWhiteSpace(existing.ReturnType))
                existing.ReturnType = incoming.ReturnType;

            if (string.IsNullOrWhiteSpace(existing.Type))
                existing.Type = incoming.Type;

            // Prefer the definition's location for navigation.
            if (incoming.IsDefinition && !existing.IsDefinition)
            {
                existing.FilePath = incoming.FilePath;
                existing.Line = incoming.Line;
                existing.Column = incoming.Column;
                existing.IsDefinition = true;
            }

            if (existing.ClangId == null)
                existing.ClangId = incoming.ClangId;

            if (existing.PreviousDeclId == null)
                existing.PreviousDeclId = incoming.PreviousDeclId;

            if (existing.MangledName == null)
                existing.MangledName = incoming.MangledName;

            if (existing.Signature == null)
                existing.Signature = incoming.Signature;

            foreach (string id in incoming.ClangIds)
            {
                if (!existing.ClangIds.Contains(id))
                    existing.ClangIds.Add(id);
            }
        }

        private static Symbol CloneSymbol(Symbol source)
        {
            Symbol clone = new()
            {
                Name = source.Name,
                Kind = source.Kind,
                Summary = source.Summary,
                Type = source.Type,
                ReturnType = source.ReturnType,
                FilePath = source.FilePath,
                Line = source.Line,
                Column = source.Column,
                IsDefinition = source.IsDefinition,
                ClangId = source.ClangId,
                PreviousDeclId = source.PreviousDeclId,
                MangledName = source.MangledName,
                Signature = source.Signature,
                Parameters = source.Parameters
                    .Select(p => new SymbolParameter
                    {
                        Name = p.Name,
                        Type = p.Type,
                        DefaultValue = p.DefaultValue
                    })
                    .ToList(),
                ClangIds = [.. source.ClangIds]
            };

            foreach (Symbol child in source.Children)
            {
                clone.Children.Add(CloneSymbol(child));
            }

            return clone;
        }

        private TreeViewItem CreateSymbolTreeItem(Symbol symbol)
        {
            TreeViewItem item = new()
            {
                Foreground = SystemColors.AccentColorLight3Brush,
                Header = $"{symbol.Kind}: {symbol.Name}",
                Tag = symbol
            };

            foreach (Symbol child in symbol.Children)
            {
                item.Items.Add(CreateSymbolTreeItem(child));
            }

            return item;
        }

        // project hierarchy > add
        private void AddExistingSource(object sender, RoutedEventArgs e)
        {
            if (CurrentProject == null || string.IsNullOrEmpty(CurrentProjectPath))
            {
                MessageBox.Show("Open or create a framework project first.");
                return;
            }

            OpenFileDialog dialog = new()
            {
                Title = "Add Existing Source File",
                Filter = "C++ Source Files (*.cpp)|*.cpp|Assembly Files (*.asm)|*.asm|All Files (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() != true)
                return;

            foreach (string file in dialog.FileNames)
            {
                string projectDirectory = Path.GetDirectoryName(CurrentProjectPath)!;
                string relativePath = Path.GetRelativePath(projectDirectory, file);

                if (!CurrentProject.SourceFiles.Contains(relativePath))
                    CurrentProject.SourceFiles.Add(relativePath);
            }

            RefreshProjectTree();
            _ = AnalyzeProjectAsync();
        }

        private void UpdateWorkspaceState()
        {
            if (CurrentProject == null ||
                string.IsNullOrEmpty(CurrentProjectPath))
                return;

            string projectDirectory =
                Path.GetDirectoryName(CurrentProjectPath)!;

            CurrentProject.OpenDocuments.Clear();

            foreach (TabItem tab in EditorTabs.Items)
            {
                if (tab.Tag is Document document)
                {
                    string relativePath = Path.GetRelativePath(
                        projectDirectory,
                        document.FilePath
                    );

                    CurrentProject.OpenDocuments.Add(relativePath);
                }
            }

            if (EditorTabs.SelectedItem is TabItem activeTab &&
                activeTab.Tag is Document activeDocument)
            {
                CurrentProject.ActiveDocumentPath = Path.GetRelativePath(
                    projectDirectory,
                    activeDocument.FilePath
                );
            }
            else
            {
                CurrentProject.ActiveDocumentPath = null;
            }
        }

        private TextEditor? ActiveEditor =>
            EditorTabs.SelectedItem is TabItem tab ? tab.Content as TextEditor : null;

        private string? PromptForText(string title, string prompt, string initialValue = "")
        {
            Window dialog = new()
            {
                Title = title,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false
            };
            StackPanel panel = new() { Margin = new Thickness(14), MinWidth = 330 };
            panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(0, 0, 0, 8) });
            TextBox input = new() { Text = initialValue, MinWidth = 300, Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(input);
            StackPanel buttons = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Button accept = new() { Content = "OK", MinWidth = 72, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            Button cancel = new() { Content = "Cancel", MinWidth = 72, IsCancel = true };
            accept.Click += (_, _) => dialog.DialogResult = true;
            buttons.Children.Add(accept);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);
            dialog.Content = panel;
            dialog.Loaded += (_, _) => { input.Focus(); input.SelectAll(); };
            return dialog.ShowDialog() == true ? input.Text : null;
        }

        private void ShowFindDialog(object sender, RoutedEventArgs e)
        {
            if (ActiveEditor == null) return;
            string? query = PromptForText("Find", "Find:", _lastSearchText);
            if (string.IsNullOrEmpty(query)) return;
            _lastSearchText = query;
            FindNext(query);
        }

        private void FindNext(string query)
        {
            TextEditor? editor = ActiveEditor;
            if (editor == null || string.IsNullOrEmpty(query)) return;
            int start = Math.Min(editor.SelectionStart + editor.SelectionLength, editor.Text.Length);
            int index = editor.Text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
            if (index < 0) index = editor.Text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                MessageBox.Show("Could not find '" + query + "'.", "Find",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            editor.Focus();
            editor.Select(index, query.Length);
            editor.ScrollToLine(editor.Document.GetLineByOffset(index).LineNumber);
        }

        private void ShowReplaceDialog(object sender, RoutedEventArgs e)
        {
            TextEditor? editor = ActiveEditor;
            if (editor == null) return;
            string? find = PromptForText("Replace", "Find:", _lastSearchText);
            if (string.IsNullOrEmpty(find)) return;
            string? replacement = PromptForText("Replace", "Replace with:", "");
            if (replacement == null) return;
            _lastSearchText = find;
            editor.Text = editor.Text.Replace(find, replacement, StringComparison.OrdinalIgnoreCase);
            editor.Focus();
        }

        private void ShowGoToLineDialog(object sender, RoutedEventArgs e)
        {
            TextEditor? editor = ActiveEditor;
            if (editor == null) return;
            string? value = PromptForText("Go To Line", "Line number:");
            if (!int.TryParse(value, out int requestedLine) || requestedLine < 1) return;
            int line = Math.Clamp(requestedLine - 1, 0, editor.Document.LineCount - 1);
            editor.Focus();
            editor.TextArea.Caret.Offset = editor.Document.GetLineByNumber(line + 1).Offset;
            editor.ScrollToLine(line + 1);
        }


        private void LoadEditorSettings()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return;

                using JsonDocument settings = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (settings.RootElement.TryGetProperty("editorFontSize", out JsonElement value) &&
                    value.TryGetDouble(out double fontSize))
                {
                    _editorFontSize = Math.Clamp(fontSize, 8, 32);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not load editor settings: {ex.Message}");
                _editorFontSize = 14;
            }
        }

        private void SaveEditorSettings()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                string json = JsonSerializer.Serialize(new { editorFontSize = _editorFontSize },
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not save editor settings: {ex.Message}");
            }
        }

        private void ZoomIn(object sender, RoutedEventArgs e)
        {
            _editorFontSize = Math.Min(32, _editorFontSize + 1);
            ApplyEditorZoom();
            SaveEditorSettings();
        }

        private void ZoomOut(object sender, RoutedEventArgs e)
        {
            _editorFontSize = Math.Max(8, _editorFontSize - 1);
            ApplyEditorZoom();
            SaveEditorSettings();
        }

        private void ResetZoom(object sender, RoutedEventArgs e)
        {
            _editorFontSize = 14;
            ApplyEditorZoom();
            SaveEditorSettings();
        }

        private void ApplyEditorZoom()
        {
            foreach (TabItem tab in EditorTabs.Items)
                if (tab.Content is TextEditor editor) editor.FontSize = _editorFontSize;
        }

        private async void CheckForErrorsClick(object sender, RoutedEventArgs e)
        {
            if (CurrentProject == null || string.IsNullOrEmpty(CurrentProjectPath))
            {
                MessageBox.Show("Open a framework project first.");
                return;
            }
            if (!_toolchainManager.IsAvailable)
            {
                MessageBox.Show("LLVM/Clang was not found. Install LLVM and ensure clang++.exe is on PATH.");
                return;
            }

            DiagnosticsOutput.Clear();
            DiagnosticsOutput.AppendText("Checking project sources...\n");
            string directory = Path.GetDirectoryName(CurrentProjectPath)!;
            int failures = 0;
            foreach (string relative in CurrentProject.SourceFiles)
            {
                string extension = Path.GetExtension(relative).ToLowerInvariant();
                if (extension is not (".cpp" or ".cc" or ".cxx" or ".c")) continue;
                string source = Path.GetFullPath(Path.Combine(directory, relative));
                if (!File.Exists(source)) continue;
                try
                {
                    var result = await _toolchainManager.RunClangAsync(source);
                    string output = (result.Errors + Environment.NewLine + result.Output).Trim();
                    if (!string.IsNullOrWhiteSpace(output)) DiagnosticsOutput.AppendText(output + Environment.NewLine);
                    if (result.ExitCode != 0) failures++;
                }
                catch (Exception ex)
                {
                    failures++;
                    DiagnosticsOutput.AppendText(source + ": " + ex.Message + "\n");
                }
            }
            DiagnosticsOutput.AppendText(failures == 0
                ? "\nCheck complete: no compiler errors reported."
                : "\nCheck complete: " + failures + " source file(s) reported errors.");
        }

        private async void BuildProjectClick(object sender, RoutedEventArgs e)
        {
            if (CurrentProject == null ||
                string.IsNullOrEmpty(CurrentProjectPath))
            {
                MessageBox.Show("Open a framework project first.");
                return;
            }

            if (!_toolchainManager.IsAvailable ||
                string.IsNullOrEmpty(_toolchainManager.LLVMPath))
            {
                MessageBox.Show(
                    "LLVM/Clang was not found. Install LLVM and ensure clang++.exe is available.");
                return;
            }

            BuildOutput.Clear();

            string projectDirectory =
                Path.GetDirectoryName(CurrentProjectPath)!;

            var buildManager = new BuildManager(
                _toolchainManager.LLVMPath);

            try
            {
                bool success = await buildManager.BuildAsync(
                    CurrentProject,
                    projectDirectory,
                    message => BuildOutput.AppendText(message + Environment.NewLine));

                BuildOutput.AppendText(
                    success
                        ? Environment.NewLine + "Build completed successfully."
                        : Environment.NewLine + "Build failed.");
            }
            catch (Exception ex)
            {
                BuildOutput.AppendText(
                    Environment.NewLine + "[BUILD ERROR] " + ex.Message);
            }
        }

        private async Task TestClangAsync()
        {
            if (!_toolchainManager.IsAvailable)
            {
                MessageBox.Show("LLVM is not available.");
                return;
            }

            if (CurrentProject == null || CurrentProjectPath == null)
            {
                MessageBox.Show("Open a framework project first.");
                return;
            }

            string sourceFile = Path.Combine(CurrentProjectPath, "demo.cpp");

            if (!File.Exists(sourceFile))
            {
                MessageBox.Show("demo.cpp was not found.");
                return;
            }

            var result = await _toolchainManager.RunClangAsync(sourceFile);

            MessageBox.Show(
                $"Exit code: {result.ExitCode}\n\n" +
                $"Output:\n{result.Output}\n\n" +
                $"Errors:\n{result.Errors}"
            );
        }

        private async Task AnalyzeProjectAsync()
        {
            if (_codeAnalyzer == null || CurrentProject == null ||
                string.IsNullOrEmpty(CurrentProjectPath))
                return;

            _fileSymbols.Clear();

            string? projectDirectory = Path.GetDirectoryName(CurrentProjectPath);

            if (projectDirectory == null)
                return;

            foreach (string source in CurrentProject.SourceFiles)
            {
                string extension = Path.GetExtension(source).ToLowerInvariant();

                // ClangCodeAnalyzer currently analyzes C++ files.
                if (extension is not (".cpp" or ".cc" or ".cxx"))
                    continue;

                string fullPath = Path.GetFullPath(
                    Path.Combine(projectDirectory, source));

                if (!File.Exists(fullPath))
                    continue;

                try
                {
                    List<Symbol> symbols =
                        await _codeAnalyzer.AnalyzeFileAsync(fullPath);

                    _fileSymbols[fullPath] = symbols;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"Analysis failed for {fullPath}: {ex.Message}");
                }
            }

            RefreshProjectTree();
            RefreshFrameworkTree();
        }

        private void NavigateToSymbol(Symbol symbol)
        {
            if (string.IsNullOrEmpty(symbol.FilePath) ||
                !File.Exists(symbol.FilePath))
                return;

            OpenDocumentInEditor(symbol.FilePath);

            // Find the editor belonging to the opened document.
            if (EditorTabs.SelectedItem is not TabItem tab ||
                tab.Content is not TextEditor editor)
                return;

            string[] lines = editor.Text.Split('\n');

            int line = Math.Clamp(symbol.Line - 1, 0, lines.Length - 1);
            int column = Math.Clamp(symbol.Column - 1, 0, lines[line].Length);

            int offset = 0;

            for (int i = 0; i < line; i++)
                offset += lines[i].Length + 1;

            offset += column;

            editor.Focus();
            editor.TextArea.Caret.Offset = Math.Min(offset, editor.Text.Length);
            editor.ScrollToLine(line + 1);
        }
    }
}