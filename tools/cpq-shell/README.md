# cpq-shell — 系统清理与优化工具 · 智能外壳（.NET Framework 4.8）

单一 exe 交付形态：检测本机 .NET 10 桌面运行时 → 缺则双选项（自动安装 / 官方下载页）
→ 把内嵌主程序解到 `%LOCALAPPDATA%\CpqSystemTool\cache\main` 并启动（不在外壳目录留文件）。

## 文件

| 文件 | 说明 |
|---|---|
| `Program.cs` | 外壳全部源码（约 500 行，WinForms，.NET Framework 4.8 / C# 5 语法，x64） |
| `build_shell.bat` | 用系统 `csc`（`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`）编译 + 嵌入 payload |

| `MainExeSha.generated.cs` | **发版生成文件**：主 exe 的 SHA256 常量（编译前由发版流程置入实值；勿手改语义） |
| `MainExeSha.zero.cs` | 披露包占位模板（64 个 0）：主程序内嵌源码包用它替换实值文件，见下 |

## SHA 生成文件（MainExeSha256 的存放约定）

主 exe 的 SHA256 是**发版时刻**才确定的值，因此单独放在 `MainExeSha.generated.cs`（发版生成文件），
`Program.cs` 中的 `Consts.MainExeSha256` 直接引用它。这样：

- **主程序构建**（`GenerateSourcePackage`）内嵌本目录源码时，用 `MainExeSha.zero.cs`（零值模板）
  替换实值文件 —— 主 exe 的哈希与发版时置入的 SHA 解耦，内嵌源码包永远不是过期快照；
- **外壳编译**（`build_shell.bat`）同时编译 `Program.cs` + 实值 `MainExeSha.generated.cs`，
  发版流程在编译前把实值置为「拟嵌入主 exe 的 SHA256」。

> 导出源码（用户侧）拿到的是零值占位版本；要重编外壳，把 `MainExeSha.generated.cs` 的常量
> 改为对应主 exe 的实值即可（见「构建步骤」）。
## 构建步骤

1. **主程序**（.NET 10 单文件）：

   ```
   dotnet publish src/CpqSystemTool/CpqSystemTool.csproj -c Release -r win-x64 ^
     --self-contained false -p:PublishSingleFile=true -p:ContinuousCI=true
   ```

2. **staging payload**：把 publish 目录的 `系统清理与优化工具.exe` 改名拷到
   `D:\cpq-builds\shell-payload\cpq_main_v122.exe`（名字保持 `cpq_main_v122`，缓存文件名不变），
   同目录放 `WebView2Loader.dll`。

3. **更新常量**（`Program.cs` → `Consts`）：
   - `MainExeSha256` = 新主程序 SHA256（钉死）
   - `LoaderSha256` = WebView2Loader.dll SHA256（一般不变）
   - `RuntimeR2Url` / `RuntimeOfficialUrl` / `RuntimeSha256` = 运行时镜像常量（.NET 10 补丁升级时同步，见设计文档 §8）

4. **编译**：

   ```
   cmd /c build_shell.bat
   ```

   注意：从 Git-bash / MSYS 直接调用 `csc /nologo …` 会被路径转换吃掉开关（`/nologo` →
   `C:/Program Files/Git/nologo`），须 `MSYS_NO_PATHCONV=1` 或在纯 cmd 下运行。

5. 产物：`bin\cpq-shell.exe`（约 10.4MB，含内嵌主程序 + Loader）。SHA256 记入发布台账
   （R2 上传对象 `系统清理与优化工具_vX.XX.exe` 即此产物；GitHub 资产为英文命名副本）。

## 关键约束（勿改）

- **`UseShellExecute=true`**：主程序 manifest=requireAdministrator，外壳非提权，
  启动主程序必须走系统 UAC。而 .NET Framework 该模式下**禁止**
  `ProcessStartInfo.EnvironmentVariables`（ArgumentException，#182 教训）
  → 数据根只能走命令行参数 `--cpq-data-root "<外壳目录>\cpq-tool"`。
- **SHA 钉死**：主程序 / Loader / 运行时镜像三处均校验，改主程序必改 `MainExeSha256`。
- **进度条**：下载期真实百分比；安装期 Marquee 滚动（Burn 静默安装无内部进度可查）；
  完成切回 Continuous + 100%。所有跨线程 UI 更新走 `Ui()`/`BeginInvoke` try/catch。
- 外壳自身只依赖 .NET Framework 4.8（Win10 1809+/Win11 内置），csc 直接编译，
  无 NuGet / MSBuild 依赖。
