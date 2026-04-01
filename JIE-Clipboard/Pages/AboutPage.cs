using JIE剪切板.Services;
using System.Diagnostics;

namespace JIE剪切板.Pages;

/// <summary>
/// “关于”页面。
/// 显示应用信息：名称、版本、描述、功能特性、技术栈、作者信息、官网/开源链接。
/// </summary>
public class AboutPage : UserControl
{
    public AboutPage()
    {
        Dock = DockStyle.Fill;
        BackColor = ThemeService.WindowBackground;
        InitializeControls();
    }

    private void InitializeControls()
    {
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(DpiHelper.Scale(50), DpiHelper.Scale(40), DpiHelper.Scale(50), DpiHelper.Scale(40)),
            AutoScroll = true
        };

        // 应用名称
        var nameLabel = new Label
        {
            Text = "JIE 剪切板",
            Font = new Font(ThemeService.GlobalFont.FontFamily, 24f, FontStyle.Bold),
            ForeColor = ThemeService.ThemeColor,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 5),
            TextAlign = ContentAlignment.MiddleCenter
        };

        var versionLabel = new Label
        {
            Text = "版本 3.0.2.1",
            Font = new Font(ThemeService.GlobalFont.FontFamily, 11f),
            ForeColor = ThemeService.SecondaryTextColor,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 20)
        };

        var descLabel = new Label
        {
            Text = "一款简洁高效的 Windows 剪贴板管理工具。\n支持文本、图片、文件、视频、文件夹等多种格式，\n提供 AES-256 加密保护，确保您的敏感数据安全。",
            Font = ThemeService.GlobalFont,
            ForeColor = ThemeService.TextColor,
            AutoSize = true,
            MaximumSize = new Size(DpiHelper.Scale(500), 0),
            Margin = new Padding(0, 0, 0, DpiHelper.Scale(25))
        };

        // 功能特性分组
        var featuresGroup = new GroupBox
        {
            Text = "功能特性",
            Size = new Size(DpiHelper.Scale(500), DpiHelper.Scale(160)),
            ForeColor = ThemeService.TextColor,
            Font = ThemeService.GlobalFont,
            Margin = new Padding(0, 0, 0, 20)
        };
        var featuresLabel = new Label
        {
            Text = "✓ 支持文本/富文本/图片/文件/视频/文件夹\n" +
                   "✓ AES-256-CBC 加密保护敏感内容\n" +
                   "✓ PBKDF2 密码派生（100,000次迭代）\n" +
                   "✓ 全局快捷键快速唤醒\n" +
                   "✓ 浅色/深色/跟随系统主题\n" +
                   "✓ 数据导出/导入备份\n" +
                   "✓ 单文件便携发布",
            ForeColor = ThemeService.TextColor,
            AutoSize = true,
            Location = new Point(DpiHelper.Scale(15), DpiHelper.Scale(25)),
            MaximumSize = new Size(DpiHelper.Scale(470), 0)
        };
        featuresGroup.Controls.Add(featuresLabel);

        // 技术栈分组
        var techGroup = new GroupBox
        {
            Text = "技术栈",
            Size = new Size(DpiHelper.Scale(500), DpiHelper.Scale(80)),
            ForeColor = ThemeService.TextColor,
            Font = ThemeService.GlobalFont,
            Margin = new Padding(0, 0, 0, 20)
        };
        var techLabel = new Label
        {
            Text = ".NET 8.0 LTS  |  WinForms  |  C# 12  |  System.Security.Cryptography  |  Win32 API",
            ForeColor = ThemeService.SecondaryTextColor,
            AutoSize = true,
            Location = new Point(DpiHelper.Scale(15), DpiHelper.Scale(30)),
            MaximumSize = new Size(DpiHelper.Scale(470), 0)
        };
        techGroup.Controls.Add(techLabel);

        // 作者信息
        Font? iconFont = null;
        try { iconFont = new Font("Segoe MDL2 Assets", 12f); } catch { }

        var authorPanel = CreateInfoRow(iconFont, "\uE77B", "牛连杰", null, null);
        var websitePanel = CreateInfoRow(iconFont, "\uE774", "官网：", "www.jiuliu.org", "https://www.jiuliu.org");
        var githubPanel = CreateInfoRow(iconFont, "\uEC7A", "开源地址：", "github.com/nljie1103/JIE-Clipboard", "https://github.com/nljie1103/JIE-Clipboard");

        var copyrightLabel = new Label
        {
            Text = $"© {DateTime.Now.Year} JIE剪切板. All rights reserved.",
            ForeColor = ThemeService.SecondaryTextColor,
            AutoSize = true,
            Margin = new Padding(0, 15, 0, 0)
        };

        layout.Controls.AddRange(new Control[] {
            nameLabel, versionLabel, descLabel,
            featuresGroup, techGroup,
            authorPanel, websitePanel, githubPanel,
            copyrightLabel
        });

        Controls.Add(layout);
    }

    /// <summary>创建一行信息（图标 + 文本 + 可选超链接）</summary>
    private Panel CreateInfoRow(Font? iconFont, string iconChar, string text, string? linkText, string? url)
    {
        var panel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 3, 0, 3)
        };

        var iconLabel = new Label
        {
            Text = iconChar,
            Font = iconFont ?? ThemeService.GlobalFont,
            ForeColor = ThemeService.ThemeColor,
            AutoSize = true,
            Margin = new Padding(0, 0, 8, 0)
        };
        panel.Controls.Add(iconLabel);

        var label = new Label
        {
            Text = text,
            Font = ThemeService.GlobalFont,
            ForeColor = ThemeService.TextColor,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 0)
        };
        panel.Controls.Add(label);

        if (linkText != null && url != null)
        {
            var link = new LinkLabel
            {
                Text = linkText,
                Font = ThemeService.GlobalFont,
                AutoSize = true,
                LinkColor = ThemeService.ThemeColor,
                ActiveLinkColor = ThemeService.ThemeColor,
                VisitedLinkColor = ThemeService.ThemeColor,
                Margin = new Padding(0, 0, 0, 0)
            };
            link.LinkClicked += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch { }
            };
            panel.Controls.Add(link);
        }

        return panel;
    }
}
