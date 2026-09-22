// MainExeSha256 — 披露包占位模板（零值）。
//
// 说明：主程序内嵌的「导出源码」披露包（CpqSystemTool.csproj → GenerateSourcePackage）
// 以本文件（MainExeSha.zero.cs）的内容替换实值文件 MainExeSha.generated.cs，
// 因此内嵌包里的 MainExeSha256 恒为 64 个 0 —— 主程序构建与发版时置入的实值解耦，
// 披露包也永远不会成为过期快照。
//
// 发版构建外壳时，tools/cpq-shell/MainExeSha.generated.cs 中的常量在编译前
// 由发版流程置为「拟嵌入主 exe 的 SHA256」（build_shell.bat 同时编译
// Program.cs + MainExeSha.generated.cs）。见 README「SHA 生成文件」节。
namespace CpqShell
{
    public static class MainExeSha
    {
        public const string MainExeSha256 = "0000000000000000000000000000000000000000000000000000000000000000";
    }
}
