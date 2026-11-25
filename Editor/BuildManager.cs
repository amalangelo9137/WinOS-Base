using System.Diagnostics;
using System.IO;

namespace WinOS_Editor.Build;

public sealed class BuildManager
{
    private readonly string _llvmPath;

    public BuildManager(string llvmPath)
    {
        _llvmPath = llvmPath;
    }

    public async Task<bool> BuildAsync(
        FrameworkProject project,
        string projectDirectory,
        Action<string> log)
    {
        string clang = Path.Combine(_llvmPath, "clang++.exe");
        string linker = Path.Combine(_llvmPath, "ld.lld.exe");

        if (!File.Exists(clang))
        {
            log("[ERROR] clang++.exe was not found.");
            return false;
        }

        if (!File.Exists(linker))
        {
            log("[ERROR] ld.lld.exe was not found.");
            log("Install LLVM with LLD enabled before building.");
            return false;
        }

        string buildDirectory = Path.Combine(projectDirectory, "Build");
        string objectDirectory = Path.Combine(buildDirectory, "obj");

        Directory.CreateDirectory(objectDirectory);

        log("WinOS Framework Build");
        log($"Target: {ToolchainConfig.Target}");
        log("Mode: Freestanding C++");
        log("");

        var objectFiles = new List<string>();
        int sourceIndex = 0;
        int failures = 0;

        foreach (string relativePath in project.SourceFiles)
        {
            string extension = Path.GetExtension(relativePath).ToLowerInvariant();

            if (extension is ".asm")
            {
                log($"[SKIP] {relativePath} — assembly support is not implemented yet.");
                continue;
            }

            if (extension is not (".cpp" or ".cc" or ".cxx" or ".c"))
                continue;

            string sourcePath = Path.GetFullPath(
                Path.Combine(projectDirectory, relativePath));

            if (!File.Exists(sourcePath))
            {
                log($"[MISSING] {relativePath}");
                failures++;
                continue;
            }

            string objectPath = Path.Combine(
                objectDirectory,
                $"source_{sourceIndex++:D3}.o");

            var arguments = new List<string>();

            arguments.AddRange(ToolchainConfig.CompileFlags);

            arguments.Add(
                extension == ".c" ? "-std=c17" : "-std=c++20");

            arguments.Add("-c");
            arguments.Add(sourcePath);
            arguments.Add("-o");
            arguments.Add(objectPath);

            log($"[COMPILE] {relativePath}");

            var result = await RunProcessAsync(
                clang,
                arguments,
                projectDirectory);

            LogProcessResult(result, log);

            if (result.ExitCode != 0)
            {
                log($"[FAILED] {relativePath}");
                failures++;
                continue;
            }

            objectFiles.Add(objectPath);
            log($"[OK] {relativePath}");
        }

        if (failures > 0)
        {
            log("");
            log($"Build stopped: {failures} source file(s) failed.");
            return false;
        }

        if (objectFiles.Count == 0)
        {
            log("[ERROR] No supported source files were found.");
            return false;
        }

        string frameworkName = SanitizeFileName(project.Name);
        string outputPath = Path.Combine(
            buildDirectory,
            frameworkName + ".elf");

        var linkArguments = new List<string>
{
    "-shared",
    "--build-id=none",
    "-z",
    "noexecstack",
    "-o",
    outputPath
};

        linkArguments.AddRange(objectFiles);

        log("");
        log("[LINK] Creating ELF64 shared object...");

        var linkResult = await RunProcessAsync(
            linker,
            linkArguments,
            projectDirectory);

        LogProcessResult(linkResult, log);

        if (linkResult.ExitCode != 0)
        {
            log("[FAILED] Linking.");
            return false;
        }

        log("");
        log($"[SUCCESS] Framework built: {outputPath}");
        log("Output format: ELF64 shared object (ET_DYN)");
        log("Packaging into .wdll is the next stage.");

        return true;
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string executable,
        IEnumerable<string> arguments,
        string workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };

        process.Start();

        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        return new ProcessResult(
            process.ExitCode,
            await stdout,
            await stderr);
    }

    private static void LogProcessResult(
        ProcessResult result,
        Action<string> log)
    {
        if (!string.IsNullOrWhiteSpace(result.StandardError))
            log(result.StandardError.Trim());

        if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            log(result.StandardOutput.Trim());
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');

        return string.IsNullOrWhiteSpace(name)
            ? "WinOSFramework"
            : name;
    }

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}