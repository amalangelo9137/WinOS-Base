# WinOS Development Context

Last updated: 2026-11-15

This file is the handoff for continuing WinOS development across chats. Keep it concise and update it whenever a meaningful implementation milestone or architectural decision changes.

## Sources of truth

- Repository code is authoritative for what is implemented.
- This file is authoritative for current development direction, decisions, and next steps.
- Do not assume code is tested just because it exists in the repository.
- User works locally and will explicitly say when the repository has been pushed/updated. Do not overwrite remote files based on assumed local state.

## Project overview

WinOS means **WinOS: The OS for Winners**. WinOS-Editor is the WPF-based Framework Studio used to create WinOS frameworks; it is not intended to be a universal IDE.

### Solution / subprojects

- **WinOS-EFI** — UEFI boot application.
- **WinOS-Kernel** — C++ kernel and assembly.
- **WinOS-Editor** — WPF C# framework IDE.
- **WinOS main** — solution/application-level project.
- No separate WinOS-Studio project currently exists.

When giving implementation instructions, always identify the subproject and exact file path.

## WinOS-Editor direction

- Target: WinOS frameworks.
- Primary language: C++; C for interoperability when needed; assembly for low-level operations.
- Initial CPU target: x86-64. Architecture expansion can come later.
- Clang/LLVM is the C/C++ compiler candidate and is user-installed (do not bundle it).
- Build engine is intended to be custom C#.
- Avoid adding unnecessary frameworks, toolchains, or configuration layers.
- Preserve the current editor tab styling unless explicitly asked to change it.
- User wants to understand and implement straightforward UI work themselves. Explain concepts and guide step by step instead of dumping large code changes.

## Code hierarchy requirements

Both hierarchy panels should be code-structure browsers:

- **Framework Hierarchy** — aggregate symbols across all project source files.
- **Project Hierarchy** — symbols grouped under each individual source file.
- Include namespaces, classes, structs, unions, functions, methods, constructors, destructors, variables, fields, enums, aliases, and relevant declarations.
- Symbol details should include developer-authored documentation, parameter names/types/defaults, return/type information, file path, line, and column where available.
- Documentation must come only from explicit developer-authored comments. Do not generate or infer summaries; leave missing documentation blank.
- Double-clicking a symbol should navigate to its source location in the editor.

## Current WinOS-Editor implementation

Project uses .NET 10 Windows and WPF. Relevant files:

- `Editor/MainWindow.xaml` — existing UI. Contains `FWTree`, `ProjectTree`, `FWSearch`, and `ProjectSearch`.
- `Editor/MainWindow.xaml.cs` — main window logic.
- `Editor/Analysis/Symbol.cs` — symbol model.
- `Editor/Analysis/ICodeAnalyzer.cs` — analyzer interface.
- `Editor/Analysis/ClangCodeAnalyzer.cs` — Clang JSON AST analyzer.
- `Editor/toolchains/ToolchainMgr.cs` — LLVM detection/toolchain support.
- `Editor/wdfp.cs` — framework project model.
- `Editor/Document.cs` and `Editor/DocMgr.cs` — document management.

Current analyzer interface:

```csharp
Task<List<Symbol>> AnalyzeFileAsync(string filePath);
```

The Clang analyzer currently:
- Invokes `clang++.exe` with JSON AST output, comment parsing, and syntax-only mode.
- Extracts supported declarations, parameters, types, source positions, and explicit Clang comment documentation.
- Merges some redeclarations and distinguishes overloads using signature/mangled-name information.
- Has not yet been fully validated against a representative C++ test suite or a successful local build. Treat edge cases (header/macro source attribution, templates, source ranges, definition detection, return-type parsing) as needing validation.

## Latest WinOS-Editor workspace update (2026-11-15)

Remote repository was updated directly by the assistant. The user must pull these commits before local testing. Changes have NOT been built or runtime-tested in this environment.

Commits:
- `f80c6cca644df0f5651cc422a7871a7d082c4372` — editor workspace interactions and event wiring.
- `7b02ee5caf85795f28bb98a61c853b291d418cf8` — status bar and window keyboard/closing events.

Changes in `Editor/MainWindow.xaml.cs`:
- Rebuilt `CreateEditorTab` so editor handlers are registered once, not inside `TextChanged`. The previous nesting caused handlers to accumulate on every keystroke.
- Preserved auto bracket/quote pairing, selection wrapping, closing-character skip, paired Backspace, and smart Enter.
- Added Tab indentation and Shift+Tab outdent.
- Added per-tab close buttons and shared close confirmation.
- Added Ctrl+S, Ctrl+Shift+S, Ctrl+W, Ctrl+Tab and Ctrl+Shift+Tab.
- Added caret line/column status updates.
- Added window-close confirmation for modified documents and project save.
- Fixed startup ordering so LLVM/analyzer initialization occurs before opening a project and starting analysis.
- Updated save/close tab title handling.
- Clears both hierarchies and symbol cache when switching projects.

Changes in `Editor/MainWindow.xaml`:
- Added global keyboard and window-closing event handlers.
- Added a bottom status bar showing project name and caret position.
- Reserved space for the status bar without changing the main workspace layout.

Important next action:
- User must pull the latest remote commits and build/test locally. Do not claim these changes compile until user verifies.
- Check close-button appearance, Ctrl+Tab navigation, Ctrl+S/Shift+Ctrl+S, window close prompts, smart indentation, Tab/Shift+Tab, and project switching.
- Known limitation: editor remains a WPF TextBox, not a syntax-highlighting editor. Auto-pairing is not syntax-aware and indentation is basic.
- Next workspace priorities after verification: find/replace, Go To Line, editor zoom/font controls, line numbers, syntax highlighting, and proper diagnostics/build output integration.

## Current UI / integration status

- `FWTree` and `ProjectTree` already exist in XAML.
- `ProjectTree` currently lists project source files; double-click opens a file.
- `FWTreeDoubleClick` is currently empty.
- The analyzer is **not yet connected to the UI**.
- The code editor is currently a WPF `TextBox`; line/column navigation can initially use its text and caret position.
- `RefreshProjectTree()` currently builds the source-file list.
- `TestClangAsync()` exists in `MainWindow.xaml.cs` but is not wired to a UI action.
- LLVM detection runs through the existing `ToolchainManager`.

## Immediate task: connect analyzer to hierarchy UI

Implement this incrementally, keeping existing styling:

1. Add analyzer state/initialization in `MainWindow.xaml.cs`, using the detected LLVM path from the existing toolchain manager. Inspect the current toolchain manager API rather than guessing property names.
2. Analyze each supported project source file asynchronously when a project is opened or source files change. Avoid blocking the WPF UI thread.
3. Store results by source-file path so Project Hierarchy can show each file's symbols and Framework Hierarchy can show the combined symbol tree.
4. Populate the existing `ProjectTree` and `FWTree` with nested `TreeViewItem` objects. Preserve the source-file nodes in Project Hierarchy.
5. Wire search boxes to filter their respective trees without losing the underlying analysis results.
6. Implement symbol double-click navigation using `Symbol.FilePath`, `Line`, and `Column`; keep file double-click behavior intact.

Do not start a separate build-system implementation until the analyzer/UI integration is coherent.

## Working agreement

- User prefers learning while building: explain why each change is needed and what the code does.
- Ask one focused question when a design choice is genuinely unresolved; otherwise proceed with a clear recommendation.
- Make small, coherent implementation steps and specify exact file placement.
- Do not claim compilation, runtime validation, or tests passed unless actually run.
- Do not change remote GitHub files unless the user explicitly says the repo is updated or directly asks for a remote edit.
