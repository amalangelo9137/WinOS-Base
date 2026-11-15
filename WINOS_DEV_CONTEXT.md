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
- A separate **WinOS-Studio** project currently exists under `Studio/`; it is distinct from WinOS-Editor.

When giving implementation instructions, always identify the subproject and exact file path.

## WinOS-Editor direction

- Target: WinOS frameworks.
- Primary language: C++; C for interoperability when needed; assembly for low-level operations.
- Initial CPU target: x86-64. Architecture expansion can come later.
- Clang/LLVM is the C/C++ compiler candidate and is user-installed (do not bundle it).
- Build engine is intended to be custom C#.
- Avoid adding unnecessary frameworks, toolchains, or configuration layers.
- Preserve the current editor tab styling unless explicitly asked to change it.
- User has asked the assistant to take ownership of implementation-heavy WinOS-Editor workspace changes and provide concise summaries; don't force them through every implementation detail.

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

## Latest WinOS-Editor workspace updates (2026-11-15)

The assistant has updated the remote repository directly. Pull the latest main branch before local testing. No local build or runtime test was available here.

Workspace commits:
- `f80c6cca644df0f5651cc422a7871a7d082c4372` — editor workspace interactions and event wiring.
- `7b02ee5caf85795f28bb98a61c853b291d418cf8` — status bar and window keyboard/closing events.
- `b1180b2365868f2e8cfd081d7557b1f4d7f4286b` — Find/Replace/Go To Line/zoom and diagnostics/build output UI.
- `17c1f5dc20194abcd628b9991f9e30c1a6929d52` — implement editor search, navigation, zoom, compiler diagnostics, and compile-to-object.
- `ae42b35cb5ebeb55a9facafb067277feb20793ab` — wire Build menu commands.
- `a31f00a129f8212e62a562db9faef1b02bbc3b5f` — persist zoom across tabs and avoid object output filename collisions.
- `340bfd940ec99c103b69a847df3aa830842b5124` — ignore future generated bin/obj directories.
- `60c83a4aed48c629ab9de4e4f31c69ca87efc10e` — update this development handoff.

## Repository-first coding workflow

- Before changing WinOS code, fetch the current complete contents of the target file from GitHub, plus its directly related XAML/code-behind, project file, and any referenced model/toolchain files.
- Do not patch from remembered snippets or old chat summaries. Re-read the current remote file before each update and use its latest blob SHA.
- After a change, fetch the committed files again and run static cross-checks: XAML event handlers must exist in code-behind; XAML names must be unique; method declarations must not be accidentally duplicated; nullable/invalid syntax patterns must be checked.
- Never claim a successful build unless a real build was run. If local Windows/.NET build tools are unavailable, say so and separate verified static checks from unverified compilation.
- Keep implementation changes narrow. Fix reported compile/runtime errors before adding more features.

## Current WinOS-Editor workspace status

- The analyzer is connected to both hierarchy panels. Project Hierarchy groups symbols by source file; Framework Hierarchy merges symbols across files.
- Both hierarchy search boxes filter their trees while preserving source-file grouping and parent paths.
- Symbol tree items expose available authored documentation, types, parameters, and source locations in tooltips. Double-click navigation is wired in both trees.
- The editor remains a WPF TextBox per document tab.
- Workspace features: bracket/quote pairing, paired Backspace, smart Enter indentation, Tab/Shift+Tab indentation, per-tab close buttons, modified-document prompts, save/close shortcuts, tab navigation shortcuts, caret line/column status, and project open-document restoration.
- Latest additions: Find, Replace All, Go To Line, editor zoom, Ctrl+F/Ctrl+H/Ctrl+G, Ctrl+Plus/Ctrl+Minus, compiler diagnostics, and compile-to-object build.
- Build currently compiles each C/C++ source independently to Build/obj/*.obj. It does not link or package .wdll files yet.
- LLVM is user-installed and detected through ToolchainManager; it is not bundled.
- .gitignore now ignores future bin/ and obj/ output. Previously committed generated artifacts remain tracked until explicitly removed from Git.
- Recent workspace changes have not been built or runtime-tested in this environment. Treat them as unverified until the user builds locally.
- 2026-11-15 audit found three invalid null-conditional assignments in `MainWindow.xaml.cs` (`CurrentProject?.Property = ...`) and two XAML search event handlers with no code-behind methods. Commit `c060fc1892dc4526763a9fc056559a302b9f090b` fixes the assignments, implements both missing handlers, and restores hierarchy filtering. Static cross-check now finds no missing MainWindow XAML handlers, no duplicate XAML names, and no invalid null-conditional assignments. A real WPF build is still required.

## Next workspace priorities

1. Verify the new commands and editor interactions with a local build.
2. Replace the plain WPF TextBox with a suitable editor surface before attempting robust line numbers and C++ syntax highlighting.
3. Improve compiler diagnostics by parsing Clang file/line/column output into clickable diagnostics.
4. Extend the custom build stage into deterministic object naming, linking, and eventual .wdll packaging, with framework metadata and target architecture settings.
5. Validate the analyzer against a representative C++ test suite, especially headers, templates, macros, overloads, and source attribution.
