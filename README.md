<div align="center">

# 📋 JIE 剪切板

**一款轻量、高效、安全的 Windows 剪贴板增强工具**

帮你便捷管理剪贴板历史内容，支持加密保护，提升办公效率！

[![Stars](https://img.shields.io/github/stars/nljie1103/JIE-Clipboard?style=for-the-badge&logo=github&color=f4c542)](https://github.com/nljie1103/JIE-Clipboard/stargazers)
[![Forks](https://img.shields.io/github/forks/nljie1103/JIE-Clipboard?style=for-the-badge&logo=github&color=4493f8)](https://github.com/nljie1103/JIE-Clipboard/network/members)
[![Release](https://img.shields.io/github/v/release/nljie1103/JIE-Clipboard?style=for-the-badge&logo=rocket&color=3fb950)](https://github.com/nljie1103/JIE-Clipboard/releases)
[![License](https://img.shields.io/github/license/nljie1103/JIE-Clipboard?style=for-the-badge&color=9e6a03)](LICENSE)

</div>

---

## ✨ 功能特性

| 功能 | 说明 |
|:---|:---|
| 🚀 **轻量便携** | 单 EXE 文件直接运行，无需安装，无广告 |
| 📝 **多格式支持** | 文本、富文本、图片、文件、视频、文件夹 |
| 🔒 **AES-256 加密** | PBKDF2 密码派生（100,000 次迭代），支持自定义加密提示文字 |
| 💾 **持久化加密存储** | 按类型（图片/文件/视频/文件夹）DPAPI 加密本地缓存 |
| ⌨️ **全局快捷键** | 自定义快捷键，随时唤醒（默认 `Ctrl+1`） |
| 🎨 **主题切换** | 浅色 / 深色 / 跟随系统，支持自定义主题色和字体 |
| 🛡️ **安全防护** | 密码错误指数级锁定、防时间篡改、可选超限自动销毁 |
| 📌 **智能管理** | 置顶、去重、过期自动清理、复制次数限制、类型过滤 |
| 📦 **导出导入** | 加密备份（JIEEXP 格式），支持导入配置和记录 |

---

## 📥 快速开始

### 直接下载使用

1. 前往 [Releases](https://github.com/nljie1103/JIE-Clipboard/releases) 页面
2. 下载最新版本的 `JIE剪切板.exe`
3. 双击运行即可

> **运行要求：** Windows 10/11 64 位，无需安装 .NET 运行时（已内置打包）

### 基本使用

- **唤醒窗口：** 按 `Ctrl+1`（可自定义）
- **复制粘贴：** 点击记录自动复制并粘贴到上一个窗口
- **托盘操作：** 左键单击托盘图标显示窗口，右键打开菜单
- **关闭窗口：** 点击关闭按钮会最小化到托盘（右键托盘→退出 才是真正退出）

---

## 🖼️ 界面预览

![界面预览](screenshot.png)

---

## 🏗️ 从源码构建

### 环境要求

| 依赖 | 版本 |
|:---|:---|
| .NET SDK | 8.0 或更高 |
| 操作系统 | Windows 10/11 x64 |
| IDE（可选） | Visual Studio 2022 / VS Code / Rider |

### 构建步骤

```bash
# 1. 克隆仓库
git clone https://github.com/nljie1103/JIE-Clipboard.git

# 2. 进入目录
cd JIE-Clipboard

# 3. 发布为独立单文件 exe
dotnet publish -c Release
```

构建产物位于 `bin/Release/win-x64/publish/`：

```
publish/
├── JIE剪切板.exe    ← 独立运行的单文件程序（约 71MB，内含 .NET 运行时）
├── icon.ico          ← 应用图标
└── icon.png          ← 高清托盘图标
```

> **提示：** 如果构建失败提示 `Access Denied`，请先关闭正在运行的 JIE剪切板.exe

### 创建桌面快捷方式（可选）

在项目目录下执行 PowerShell：

```powershell
$ws = New-Object -ComObject WScript.Shell
$s = $ws.CreateShortcut("$([Environment]::GetFolderPath('Desktop'))\JIE剪切板.lnk")
$s.TargetPath = "$PWD\bin\Release\win-x64\publish\JIE剪切板.exe"
$s.WorkingDirectory = "$PWD\bin\Release\win-x64\publish"
$s.IconLocation = "$PWD\bin\Release\win-x64\publish\JIE剪切板.exe,0"
$s.Save()
```

---

## 📁 项目结构

```
JIE-Clipboard/
├── Program.cs                 # 程序入口（单实例检测 + 全局异常处理）
├── MainForm.cs                # 主窗口（导航布局、托盘、剪贴板监听、快捷键）
├── icon.ico / icon.png        # 应用图标资源
│
├── Models/                    # 数据模型
│   ├── AppConfig.cs           #   应用配置（类型过滤、持久化加密、安全策略等）
│   └── ClipboardRecord.cs     #   剪贴板记录（6 种内容类型 + 加密提示文字）
│
├── Services/                  # 核心服务
│   ├── ClipboardService.cs    #   剪贴板读写 + 内容预览
│   ├── EncryptionService.cs   #   AES-256-CBC 加密（PBKDF2 100K 迭代）
│   ├── FileService.cs         #   数据持久化（DPAPI 加密存储 + 文件/文件夹加密）
│   ├── HotkeyService.cs       #   全局快捷键（Win32 API）
│   ├── LogService.cs          #   日志记录
│   └── ThemeService.cs        #   主题管理（浅色/深色/跟随系统）
│
├── Controls/                  # 自定义控件
│   ├── NavigationListBox.cs   #   导航列表（GDI+ 自绘）
│   ├── RecordListPanel.cs     #   记录列表（虚拟滚动 + 加密缩略图）
│   └── ToggleSwitch.cs        #   开关控件
│
├── Dialogs/                   # 对话框
│   ├── EditRecordDialog.cs    #   编辑记录（加密/解密、提示文字、安全设置）
│   └── PasswordDialog.cs      #   密码输入
│
├── Pages/                     # 设置页面
│   ├── AllRecordsPage.cs      #   全部记录（搜索、筛选、批量操作）
│   ├── GeneralSettingsPage.cs #   通用设置（类型过滤、扩展名、持久化加密）
│   ├── HotkeyPage.cs          #   快捷键设置
│   ├── AppearancePage.cs      #   外观主题（主题色、字体、深浅模式）
│   ├── SecurityPage.cs        #   安全防护（锁定策略、自动销毁）
│   ├── ExportImportPage.cs    #   导出导入（加密备份 JIEEXP 格式）
│   └── AboutPage.cs           #   关于
│
└── Native/
    └── Win32Api.cs            # Windows API 声明（P/Invoke）
```

---

## 🔧 技术栈

| 技术 | 用途 |
|:---|:---|
| .NET 8.0 LTS | 运行时框架 |
| WinForms | UI 框架 |
| C# 12 | 编程语言 |
| System.Security.Cryptography | AES-256 加密 + PBKDF2 + DPAPI |
| Win32 API (P/Invoke) | 剪贴板监听、全局快捷键、输入模拟 |
| System.Text.Json | 数据序列化 |
| System.IO.Compression | 文件夹压缩存储 |

---

## 💾 数据存储

| 文件 | 路径 | 说明 |
|:---|:---|:---|
| config.json | `%AppData%\JIE剪切板\` | 应用配置 |
| records.dat | `%AppData%\JIE剪切板\`（可自定义） | 剪贴板记录（DPAPI 加密） |
| Images/ | `%AppData%\JIE剪切板\`（可自定义） | 图片文件（可选 DPAPI 加密 .enc） |
| Files/ | `%AppData%\JIE剪切板\`（可自定义） | 持久化文件/文件夹（DPAPI 加密 .enc） |
| Logs/ | `%AppData%\JIE剪切板\` | 日志（自动清理 7 天前） |

> 可在「通用设置」中更改 records、images 和 files 的存储位置

---

## 📄 许可证

本项目采用 [MIT](LICENSE) 许可证开源。

---

## 🙏 致谢

- [.NET](https://dotnet.microsoft.com/) — 开源跨平台框架
- 感谢所有 Star、Fork 和反馈的用户

---

<div align="center">

Made with ❤️ by [nljie1103](https://github.com/nljie1103)

**如果觉得好用，请给个 ⭐ Star 支持一下！**

</div>