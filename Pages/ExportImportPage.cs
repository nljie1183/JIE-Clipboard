using JIE剪切板.Models;
using JIE剪切板.Services;

namespace JIE剪切板.Pages;

public class ExportImportPage : UserControl
{
    private readonly MainForm _mainForm;
    private CheckBox _chkExportRecords = null!, _chkExportConfig = null!;
    private Label _lblStatus = null!;

    public ExportImportPage(MainForm mainForm)
    {
        _mainForm = mainForm;
        Dock = DockStyle.Fill;
        BackColor = ThemeService.WindowBackground;
        Padding = new Padding(DpiHelper.Scale(30), DpiHelper.Scale(20), DpiHelper.Scale(30), DpiHelper.Scale(20));
        InitializeControls();
    }

    private void InitializeControls()
    {
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            AutoSize = true,
            WrapContents = false
        };

        // Title
        layout.Controls.Add(new Label
        {
            Text = "导出导入",
            Font = new Font(ThemeService.GlobalFont.FontFamily, 14f, FontStyle.Bold),
            ForeColor = ThemeService.TextColor,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 5)
        });

        layout.Controls.Add(new Label
        {
            Text = "导出或导入剪贴板数据和配置",
            ForeColor = ThemeService.SecondaryTextColor,
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 20)
        });

        // Export section
        var exportGroup = new GroupBox
        {
            Text = "导出",
            Size = new Size(DpiHelper.Scale(500), DpiHelper.Scale(180)),
            ForeColor = ThemeService.TextColor,
            Font = ThemeService.GlobalFont,
            Margin = new Padding(0, 0, 0, DpiHelper.Scale(15))
        };

        _chkExportRecords = new CheckBox
        {
            Text = "导出剪贴板记录",
            Checked = true,
            AutoSize = true,
            Location = DpiHelper.Scale(new Point(20, 30)),
            ForeColor = ThemeService.TextColor
        };

        _chkExportConfig = new CheckBox
        {
            Text = "导出应用设置",
            Checked = true,
            AutoSize = true,
            Location = DpiHelper.Scale(new Point(20, 60)),
            ForeColor = ThemeService.TextColor
        };

        var exportNote = new Label
        {
            Text = "注意：加密记录将以密文形式导出，导入后仍需密码解密。\n图片文件不包含在导出中，仅导出路径引用。",
            ForeColor = ThemeService.SecondaryTextColor,
            AutoSize = true,
            MaximumSize = new Size(DpiHelper.Scale(450), 0),
            Location = DpiHelper.Scale(new Point(20, 90))
        };

        var btnExport = new Button
        {
            Text = "选择位置并导出",
            FlatStyle = FlatStyle.Flat,
            Size = DpiHelper.Scale(new Size(140, 35)),
            Location = DpiHelper.Scale(new Point(20, 135)),
            BackColor = ThemeService.ThemeColor,
            ForeColor = Color.White
        };
        btnExport.FlatAppearance.BorderSize = 0;
        btnExport.Click += BtnExport_Click;

        exportGroup.Controls.AddRange(new Control[] { _chkExportRecords, _chkExportConfig, exportNote, btnExport });
        layout.Controls.Add(exportGroup);

        // Import section
        var importGroup = new GroupBox
        {
            Text = "导入",
            Size = new Size(DpiHelper.Scale(500), DpiHelper.Scale(180)),
            ForeColor = ThemeService.TextColor,
            Font = ThemeService.GlobalFont,
            Margin = new Padding(0, 0, 0, DpiHelper.Scale(15))
        };

        var importDesc = new Label
        {
            Text = "从备份文件恢复数据。导入时可以选择：\n• 合并模式：保留现有数据，合并导入的新记录\n• 覆盖模式：清除现有数据，完全使用导入的数据",
            ForeColor = ThemeService.TextColor,
            AutoSize = true,
            MaximumSize = new Size(DpiHelper.Scale(450), 0),
            Location = DpiHelper.Scale(new Point(20, 30))
        };

        var btnImportMerge = new Button
        {
            Text = "合并导入",
            FlatStyle = FlatStyle.Flat,
            Size = DpiHelper.Scale(new Size(100, 35)),
            Location = DpiHelper.Scale(new Point(20, 120)),
            ForeColor = ThemeService.ThemeColor,
            BackColor = ThemeService.WindowBackground
        };
        btnImportMerge.FlatAppearance.BorderColor = ThemeService.ThemeColor;
        btnImportMerge.Click += (_, _) => DoImport(false);

        var btnImportOverwrite = new Button
        {
            Text = "覆盖导入",
            FlatStyle = FlatStyle.Flat,
            Size = DpiHelper.Scale(new Size(100, 35)),
            Location = DpiHelper.Scale(new Point(130, 120)),
            ForeColor = Color.FromArgb(220, 53, 69),
            BackColor = ThemeService.WindowBackground
        };
        btnImportOverwrite.FlatAppearance.BorderColor = Color.FromArgb(220, 53, 69);
        btnImportOverwrite.Click += (_, _) => DoImport(true);

        importGroup.Controls.AddRange(new Control[] { importDesc, btnImportMerge, btnImportOverwrite });
        layout.Controls.Add(importGroup);

        // Status
        _lblStatus = new Label
        {
            Text = "",
            AutoSize = true,
            MaximumSize = new Size(500, 0),
            Margin = new Padding(0, 5, 0, 0)
        };
        layout.Controls.Add(_lblStatus);

        Controls.Add(layout);
    }

    private void BtnExport_Click(object? sender, EventArgs e)
    {
        try
        {
            if (!_chkExportRecords.Checked && !_chkExportConfig.Checked)
            {
                SetStatus("请至少选择一项导出内容", Color.Red);
                return;
            }

            using var dialog = new SaveFileDialog
            {
                Title = "选择导出位置",
                Filter = "JSON文件|*.json|所有文件|*.*",
                FileName = $"JIE剪切板_备份_{DateTime.Now:yyyyMMdd_HHmmss}.json",
                DefaultExt = ".json"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            var records = _chkExportRecords.Checked ? _mainForm.Records : null;
            var config = _chkExportConfig.Checked ? _mainForm.Config : null;

            var error = FileService.ExportRecords(records ?? new List<ClipboardRecord>(), config, dialog.FileName);
            if (string.IsNullOrEmpty(error))
            {
                int count = records?.Count ?? 0;
                SetStatus($"导出成功！共导出 {count} 条记录" + (_chkExportConfig.Checked ? " + 应用设置" : ""), Color.Green);
            }
            else
            {
                SetStatus(error, Color.Red);
            }
        }
        catch (Exception ex)
        {
            LogService.Log("Export button handler failed", ex);
            SetStatus($"导出失败: {ex.Message}", Color.Red);
        }
    }

    private void DoImport(bool overwrite)
    {
        try
        {
            using var dialog = new OpenFileDialog
            {
                Title = "选择备份文件",
                Filter = "JSON文件|*.json|所有文件|*.*"
            };

            if (dialog.ShowDialog(this) != DialogResult.OK) return;

            if (overwrite)
            {
                var confirm = MessageBox.Show(this,
                    "覆盖导入将清除所有现有数据！此操作不可撤销。\n\n确定要继续吗？",
                    "确认覆盖导入",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (confirm != DialogResult.Yes) return;
            }

            var (records, config, error) = FileService.ImportRecords(dialog.FileName);
            if (!string.IsNullOrEmpty(error))
            {
                SetStatus(error, Color.Red);
                return;
            }

            int importedCount = 0;

            if (records != null && records.Count > 0)
            {
                if (overwrite)
                {
                    _mainForm.Records.Clear();
                    _mainForm.Records.AddRange(records);
                    importedCount = records.Count;
                }
                else
                {
                    // Merge: skip duplicates by ID
                    var existingIds = _mainForm.Records.Select(r => r.Id).ToHashSet();
                    foreach (var record in records)
                    {
                        if (!existingIds.Contains(record.Id))
                        {
                            _mainForm.Records.Add(record);
                            importedCount++;
                        }
                    }
                }
            }

            if (config != null && (overwrite || records == null))
            {
                // Apply imported config
                _mainForm.ApplyImportedConfig(config);
            }

            _mainForm.SaveData();
            _mainForm.RefreshCurrentPage();

            string mode = overwrite ? "覆盖" : "合并";
            SetStatus($"{mode}导入成功！共导入 {importedCount} 条新记录", Color.Green);
        }
        catch (Exception ex)
        {
            LogService.Log("Import handler failed", ex);
            SetStatus($"导入失败: {ex.Message}", Color.Red);
        }
    }

    private void SetStatus(string message, Color color)
    {
        _lblStatus.Text = message;
        _lblStatus.ForeColor = color;
    }
}
