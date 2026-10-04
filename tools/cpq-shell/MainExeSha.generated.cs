// Generated per release build — do not hand-edit the constant below.
//
// Value = SHA256 of the main exe actually embedded in the shipping shell.
// The release build sets it BEFORE compiling the shell (build_shell.bat compiles
// Program.cs + this file together).
//
// Disclosure package: the main exe embeds this tool's source via CpqSystemTool.csproj
// (GenerateSourcePackage). Because this file's value depends on the release build,
// the embedded copy is normalized to 64 zeros (deterministic main build, no stale
// snapshot). See tools/cpq-shell/README.md, "SHA 生成文件" section.
namespace CpqShell
{
    public static class MainExeSha
    {
        public const string MainExeSha256 = "5b4e60fdfa13404252f1b7a266e79114b4dc90387de054210bc2a2fd4946b9b2";
    }
}
