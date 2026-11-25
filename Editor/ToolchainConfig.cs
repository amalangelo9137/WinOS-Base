namespace WinOS_Editor.Build;

public static class ToolchainConfig
{
    public const string Target = "x86_64-unknown-none-elf";

    public static readonly string[] CompileFlags =
    [
        $"--target={Target}",
        "-ffreestanding",
        "-fPIC",
        "-fno-builtin",
        "-fno-stack-protector",
        "-fno-exceptions",
        "-fno-rtti",
        "-fno-threadsafe-statics",
        "-fno-use-cxa-atexit",
        "-fno-unwind-tables",
        "-fno-asynchronous-unwind-tables",
        "-fvisibility=hidden",
        "-mno-red-zone",
        "-Wall",
        "-Wextra"
    ];

    public static readonly string[] LinkFlags =
    [
        $"--target={Target}",
        "-fuse-ld=lld",
        "-nostdlib",
        "-shared",
        "-Wl,--build-id=none",
        "-Wl,-z,noexecstack"
    ];
}