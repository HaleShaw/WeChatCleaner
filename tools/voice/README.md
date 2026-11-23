# 内置语音转换器

`silk-to-mp3.exe` 是单独运行的 32 位 Windows 命令行程序。它用 Skype SILK SDK 解码标准 SILK v3 和带腾讯前缀的 SILK v3，再用 LAME 编码为单声道 MP3。C# 主程序将它作为资源嵌入，只在需要转换 SILK 时运行。参数依次为源文件和新输出文件，转换器不会覆盖已有文件。

## 源码和许可证

- [Skype SILK SDK](https://github.com/kn007/silk-v3-decoder/tree/507be6bca8ce1fb977a061481f1d79e8c610e309/silk)：`sources/silk-sdk.zip` 收录这个提交的 `silk` 源码和上游仓库许可证。SDK 的许可声明见 [SILK-NOTICE.txt](SILK-NOTICE.txt)。
- [LAME 3.100](https://sourceforge.net/projects/lame/files/lame/3.100/)：源码包为 `sources/lame-3.100.tar.gz`，许可条款见 [LAME-COPYING.txt](LAME-COPYING.txt)。
- 入口源码 `silk-to-mp3.c` 和 LAME 编译配置 `config.h` 也在此目录。随附源码和脚本可用于重新构建，包括替换所链接的 LAME 版本。

## 重新构建

在有 PowerShell、`tar.exe` 和 [Zig 0.14.1](https://ziglang.org/download/) 的 Windows 开发机上，从仓库根目录运行：

```powershell
.\tools\voice\build.ps1 -Zig 'C:\Tools\zig-windows\zig.exe'
```

脚本校验两个源码压缩包，生成 `tools\voice\silk-to-mp3.exe`。也可用 `-Output` 指定独立输出。重建主程序后会嵌入新的转换器。目标架构为 Windows x86，可用于 32 位系统及支持 x86 进程的 64 位 Windows。Windows 7 SP1 需要 Universal CRT（KB2999226）。
