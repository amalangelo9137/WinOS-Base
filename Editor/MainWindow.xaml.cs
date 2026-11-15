using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinOS_Editor.toolchains;
using WinOS_Editor.Analysis;

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

        public MainWindow()
        {
            InitializeComponent();

            if (App.FilePath != string.Empty)
                OpenProject(App.FilePath);

            _toolchainManager.DetectLLVM();

            if (_toolchainManager.IsAvailable && _toolchainManager.LLVMPath is string llvmPath)
                _codeAnalyzer = new ClangCodeAnalyzer(llvmPath);
        }

        // Props set;
        public void FrameworkNameSet(string content)
        { CurrentProject?.Name = content; }

        public void FrameworkVersionSet(string content)
        { CurrentProject?.Version = content; }

        public void FrameworkDescriptionSet(string content)
        { CurrentProject?.Description = content; }

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
            if (SaveProject() == true) Exit(this, null);
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

        private void CreateEditorTab(Document document)
        {
            // used to filter out the TextChanged which happens
            // when the document is initially opened.
            bool initializing = true;

            // create the editor UI, mainly the textbox
            TextBox editor = new()
            {
                Name = "Code",
                Text = document.Content,
                TextWrapping = TextWrapping.Wrap,
                Margin = new(24, 4, 4, 4),
                Background = SystemColors.DesktopBrush,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)),
                BorderBrush = SystemColors.AccentColorDark2Brush,
                Cursor = Cursors.IBeam,
                AcceptsReturn = true,
                AcceptsTab = true
            };

            // create the tab for that file
            TabItem tab = new()
            {
                Header = document.FileName,
                Content = editor,
                Tag = document
            };

            editor.TextChanged += (sender, e) =>
            {
                if (initializing)
                    return; // here.

                document.Content = editor.Text;
                document.IsModified = true;

                tab.Header = document.FileName + "*"; // marker to indicate unsaved

                // Automatically insert matching brackets and quotes.
                editor.PreviewTextInput += (sender, e) =>
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

                    // Handle opening brackets and quotes.
                    if (closing != '\0')
                    {
                        int start = editor.SelectionStart;
                        string selected = editor.SelectedText;

                        editor.SelectedText = typed + selected + closing;

                        // Keep the caret inside the pair.
                        editor.CaretIndex = start + 1 + selected.Length;

                        e.Handled = true;
                        return;
                    }

                    // Skip an automatically inserted closing character.
                    if (typed is ')' or ']' or '}' or '"' or '\'')
                    {
                        int caret = editor.CaretIndex;

                        if (caret < editor.Text.Length &&
                            editor.Text[caret] == typed)
                        {
                            editor.CaretIndex++;
                            e.Handled = true;
                        }
                    }
                };

                // Delete both characters when Backspace is pressed
                // between an automatically inserted pair.
                editor.PreviewKeyDown += (sender, e) =>
                {
                    if (e.Key == Key.Enter)
                    {
                        int eCaret = editor.CaretIndex;

                        string textBeforeCaret = editor.Text[..eCaret];
                        int lineStart = textBeforeCaret.LastIndexOf('\n') + 1;

                        string currentLine = textBeforeCaret[lineStart..];

                        string indentation = new(
                            currentLine.TakeWhile(c => c is ' ' or '\t').ToArray());

                        bool afterOpeningBrace =
                            currentLine.TrimEnd().EndsWith('{');

                        bool beforeClosingBrace =
                            eCaret < editor.Text.Length &&
                            editor.Text[eCaret] == '}';

                        string unit = "    "; // Four spaces per indentation level.

                        if (afterOpeningBrace && beforeClosingBrace)
                        {
                            editor.SelectedText =
                                Environment.NewLine +
                                indentation + unit +
                                Environment.NewLine +
                                indentation;

                            editor.CaretIndex =
                                eCaret + Environment.NewLine.Length +
                                indentation.Length + unit.Length;

                            e.Handled = true;
                            return;
                        }

                        string nextIndentation = afterOpeningBrace
                            ? indentation + unit
                            : indentation;

                        editor.SelectedText =
                            Environment.NewLine + nextIndentation;

                        editor.CaretIndex =
                            eCaret + Environment.NewLine.Length +
                            nextIndentation.Length;

                        e.Handled = true;
                        return;
                    }

                    if (e.Key != Key.Back || editor.SelectionLength != 0) return;

                    int caret = editor.CaretIndex;

                    if (caret <= 0 || caret >= editor.Text.Length)
                        return;

                    char before = editor.Text[caret - 1];
                    char after = editor.Text[caret];

                    bool matchingPair =
                        (before == '(' && after == ')') ||
                        (before == '[' && after == ']') ||
                        (before == '{' && after == '}') ||
                        (before == '"' && after == '"') ||
                        (before == '\'' && after == '\'');

                    if (matchingPair)
                    {
                        editor.Select(caret - 1, 2);
                        editor.SelectedText = "";
                        e.Handled = true;
                    }
                };
            };

            initializing = false;

            // add  it to the TabControl and activate it
            EditorTabs.Items.Add(tab);
            EditorTabs.SelectedItem = tab;
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
                {
                    tab.Header = document.FileName;
                }
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
                        tab.Header = document.FileName;
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
            if (EditorTabs.SelectedItem is not TabItem tab)
                return;

            if (tab.Tag is Document document)
            {
                if (!ConfirmDocumentClose(document))
                    return;

                _documentManager.CloseDocument(document);
            }

            EditorTabs.Items.Remove(tab);
        }

        private void CloseAllTabsToolbarClick(object sender, RoutedEventArgs e)
        {
            foreach (TabItem tab in EditorTabs.Items
                 .Cast<TabItem>().ToList())
            {
                if (tab.Tag is Document document)
                {
                    if (!ConfirmDocumentClose(document))
                        return;

                    _documentManager.CloseDocument(document);
                }

                EditorTabs.Items.Remove(tab);
            }
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

        private void RefreshProjectTree()
        {
            ProjectTree.Items.Clear();

            if (CurrentProject == null ||
                string.IsNullOrEmpty(CurrentProjectPath))
                return;

            string projectDirectory = Path.GetDirectoryName(CurrentProjectPath)!;

            foreach (string source in CurrentProject.SourceFiles)
            {
                string fullPath = Path.GetFullPath(
                    Path.Combine(projectDirectory, source));

                TreeViewItem fileItem = new()
                {
                    Header = Path.GetFileName(source),
                    Tag = fullPath
                };

                if (_fileSymbols.TryGetValue(fullPath, out List<Symbol>? symbols))
                {
                    foreach (Symbol symbol in symbols)
                    {
                        fileItem.Items.Add(CreateSymbolTreeItem(symbol));
                    }
                }

                ProjectTree.Items.Add(fileItem);
            }
        }

        private void RefreshFrameworkTree()
        {
            FWTree.Items.Clear();

            List<Symbol> mergedSymbols = [];

            foreach (List<Symbol> symbols in _fileSymbols.Values)
            {
                MergeSymbolLists(mergedSymbols, symbols);
            }

            foreach (Symbol symbol in mergedSymbols)
            {
                FWTree.Items.Add(CreateSymbolTreeItem(symbol));
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
                tab.Content is not TextBox editor)
                return;

            string[] lines = editor.Text.Split('\n');

            int line = Math.Clamp(symbol.Line - 1, 0, lines.Length - 1);
            int column = Math.Clamp(symbol.Column - 1, 0, lines[line].Length);

            int offset = 0;

            for (int i = 0; i < line; i++)
                offset += lines[i].Length + 1;

            offset += column;

            editor.Focus();
            editor.CaretIndex = Math.Min(offset, editor.Text.Length);
            editor.ScrollToLine(line);
        }
    }
}