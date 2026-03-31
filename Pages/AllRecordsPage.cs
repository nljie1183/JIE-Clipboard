using JIE剪切板.Controls;
using JIE剪切板.Dialogs;
using JIE剪切板.Models;
using JIE剪切板.Services;

namespace JIE剪切板.Pages;

/// <summary>
/// “全部记录”页面。
/// 显示所有剪贴板记录，支持：
/// - 搜索过滤（带防抖动）
/// - 批量操作（清除全部/按日期清除/清除非固定）
/// - 点击复制到剪贴板 + 自动粘贴
/// - 右键菜单（编辑/删除/置顶/复制）
/// - 加密记录的密码验证、锁定、自动删除逻辑
/// - 底部状态栏显示统计信息
/// </summary>
public class AllRecordsPage : UserControl
{
    private TextBox _searchBox = null!;              // 搜索输入框
    private Panel _buttonPanel = null!;              // 操作按钮区域
    private RecordListPanel _recordList = null!;     // 记录列表控件
    private Panel _statsBar = null!;                 // 底部统计栏


    private readonly MainForm _mainForm;
    private string _searchText = "";                 // 当前搜索关键词
    private System.Windows.Forms.Timer? _searchDebounceTimer; // 搜索防抖动计时器
    private ContextMenuStrip? _recordContextMenu;    // 右键上下文菜单
    private ClipboardRecord? _contextMenuRecord;     // 当前右键点击的记录

    public AllRecordsPage(MainForm mainForm)
    {
        _mainForm = mainForm;
        Dock = DockStyle.Fill;
        BackColor = ThemeService.WindowBackground;
        InitializeControls();
    }

    /// <summary>初始化页面 UI：搜索栏、操作按钮、记录列表、状态栏、右键菜单</summary>
    private void InitializeControls()
    {
        // 统计栏（底部固定）
        _statsBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = DpiHelper.Scale(36),
            BackColor = ThemeService.StatsBarBackground,
            Padding = new Padding(DpiHelper.Scale(15), 0, DpiHelper.Scale(15), 0)
        };
        _statsBar.Paint += StatsBar_Paint;

        // 搜索面板（顶部）
        var searchPanel = new Panel { Dock = DockStyle.Top, Height = DpiHelper.Scale(50), Padding = new Padding(DpiHelper.Scale(15), DpiHelper.Scale(10), DpiHelper.Scale(15), DpiHelper.Scale(5)) };
        _searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 10f),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ThemeService.IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.White,
            ForeColor = ThemeService.TextColor,
            PlaceholderText = "搜索剪贴板记录"
        };
        _searchBox.TextChanged += SearchBox_TextChanged;
        searchPanel.Controls.Add(_searchBox);

        // 操作按钮区
        _buttonPanel = new Panel { Dock = DockStyle.Top, Height = DpiHelper.Scale(45), Padding = new Padding(DpiHelper.Scale(15), DpiHelper.Scale(5), DpiHelper.Scale(15), DpiHelper.Scale(5)) };
        var btnClearAll = CreateButton("清除全部记录", Color.FromArgb(220, 53, 69));
        btnClearAll.Click += BtnClearAll_Click;
        var btnClearBefore = CreateButton("清除指定日期前记录", ThemeService.ThemeColor);
        btnClearBefore.Click += BtnClearBefore_Click;
        var btnClearAfter = CreateButton("清除指定日期后记录", ThemeService.ThemeColor);
        btnClearAfter.Click += BtnClearAfter_Click;
        var btnClearUnpinned = CreateButton("清除非固定记录", ThemeService.ThemeColor);
        btnClearUnpinned.Click += BtnClearUnpinned_Click;

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        flow.Controls.AddRange(new Control[] { btnClearAll, btnClearBefore, btnClearAfter, btnClearUnpinned });
        _buttonPanel.Controls.Add(flow);

        // 记录列表控件
        _recordList = new RecordListPanel { Dock = DockStyle.Fill };
        _recordList.RecordClicked += RecordList_RecordClicked;
        _recordList.RecordRightClicked += RecordList_RecordRightClicked;

        // 搜索防抖计时器
        _searchDebounceTimer = new System.Windows.Forms.Timer { Interval = 300 };
        _searchDebounceTimer.Tick += (_, _) => { _searchDebounceTimer.Stop(); RefreshRecords(); };

        // 可复用的右键菜单
        _recordContextMenu = new ContextMenuStrip();
        var menuEdit = new ToolStripMenuItem("编辑记录");
        menuEdit.Click += (_, _) => { if (_contextMenuRecord != null) EditRecord(_contextMenuRecord); };
        var menuDelete = new ToolStripMenuItem("删除本条");
        menuDelete.Click += (_, _) =>
        {
            if (_contextMenuRecord != null && MessageBox.Show(this, "确定要删除这条记录吗？", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _mainForm.DeleteRecord(_contextMenuRecord);
                RefreshRecords();
            }
        };
        var menuPin = new ToolStripMenuItem("置顶");
        menuPin.Click += (_, _) =>
        {
            if (_contextMenuRecord != null)
            {
                _contextMenuRecord.IsPinned = !_contextMenuRecord.IsPinned;
                _mainForm.SaveData();
                RefreshRecords();
            }
        };
        var menuCopy = new ToolStripMenuItem("复制本条");
        menuCopy.Click += (_, _) => { if (_contextMenuRecord != null) RecordList_RecordClicked(null, _contextMenuRecord); };
        _recordContextMenu.Items.AddRange(new ToolStripItem[] { menuEdit, menuDelete, menuPin, menuCopy });

        Controls.Add(_recordList);
        Controls.Add(_buttonPanel);
        Controls.Add(searchPanel);
        Controls.Add(_statsBar);
    }

    /// <summary>创建统一样式的操作按钮</summary>
    private Button CreateButton(string text, Color borderColor)
    {
        var btn = new Button
        {
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Size = new Size(DpiHelper.Scale(140), DpiHelper.Scale(30)),
            Margin = new Padding(0, 0, DpiHelper.Scale(10), 0),
            Font = new Font(ThemeService.GlobalFont.FontFamily, 8.5f),
            ForeColor = borderColor,
            BackColor = ThemeService.WindowBackground
        };
        btn.FlatAppearance.BorderColor = borderColor;
        btn.FlatAppearance.BorderSize = 1;
        return btn;
    }

    /// <summary>刷新记录列表：应用搜索过滤，按置顶+时间排序，更新状态栏</summary>
    public void RefreshRecords()
    {
        try
        {
            var records = _mainForm.Records;
            var filtered = ApplySearch(records);

            // 排序：置顶在前，然后按时间倒序
            filtered = filtered.OrderByDescending(r => r.IsPinned).ThenByDescending(r => r.CreateTime).ToList();
            _recordList.SetRecords(filtered);
            UpdateStats();
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to refresh records", ex);
        }
    }

    /// <summary>搜索过滤逻辑：匹配预览文本和时间，加密记录仅匹配“加密内容”关键词</summary>
    private List<ClipboardRecord> ApplySearch(List<ClipboardRecord> records)
    {
        if (string.IsNullOrEmpty(_searchText))
            return records.ToList();

        var keyword = _searchText.ToLower();
        var result = new List<ClipboardRecord>();

        foreach (var record in records)
        {
            if (record.IsEncrypted)
            {
                // 搜索加密记录：仅匹配"加密内容"关键词
                if ("加密内容".Contains(keyword))
                {
                    result.Add(record);
                    continue;
                }

                if (_mainForm.Config.AllowSearchEncryptedContent)
                {
                    // 搜索加密记录的可用明文信息：预览文本（含加密提示）和时间
                    var encPreview = ClipboardService.GetContentPreview(record, 500).ToLower();
                    var encTime = record.CreateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm").ToLower();
                    if (encPreview.Contains(keyword) || encTime.Contains(keyword))
                    {
                        result.Add(record);
                    }
                }
                continue;
            }

            // 搜索非加密记录：匹配预览文本和时间
            var preview = ClipboardService.GetContentPreview(record, 500).ToLower();
            var timeStr = record.CreateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm").ToLower();
            if (preview.Contains(keyword) || timeStr.Contains(keyword))
                result.Add(record);
        }
        return result;
    }

    /// <summary>搜索框文本变化：启动防抖动计时器（300ms 后刷新）</summary>
    private void SearchBox_TextChanged(object? sender, EventArgs e)
    {
        _searchText = _searchBox.Text;
        _searchDebounceTimer?.Stop();
        _searchDebounceTimer?.Start();
    }

    /// <summary>左键点击记录：复制到剪贴板并自动粘贴（加密记录需输入密码）</summary>
    private void RecordList_RecordClicked(object? sender, ClipboardRecord record)
    {
        try
        {
            if (record.IsEncrypted)
            {
                HandleEncryptedRecordClick(record);
                return;
            }

            // 检查是否超过最大复制次数限制
            if (record.MaxCopyCount > 0 && record.CurrentCopyCount >= record.MaxCopyCount)
            {
                MessageBox.Show(this, "该记录已达到最大复制次数限制。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // 写入剪贴板并自动粘贴
            _mainForm.CopyAndPasteRecord(record);
        }
        catch (Exception ex)
        {
            LogService.Log("Record click handler failed", ex);
        }
    }

    /// <summary>
    /// 处理加密记录的点击逻辑：
    /// 1. 检查锁定状态 + 时间篡改检测
    /// 2. 弹出密码输入框
    /// 3. 验证密码 → 成功则解密粘贴；失败则计数、锁定或删除
    /// </summary>
    private void HandleEncryptedRecordClick(ClipboardRecord record)
    {
        var maxAttempts = record.UseGlobalSecuritySettings
            ? _mainForm.Config.DefaultMaxPasswordAttempts
            : record.MaxPasswordAttempts;
        var baseLock = record.UseGlobalSecuritySettings
            ? _mainForm.Config.DefaultBaseLockMinutes
            : record.BaseLockMinutes;
        var autoDelete = record.UseGlobalSecuritySettings
            ? _mainForm.Config.DefaultAutoDeleteOnExceed
            : record.AutoDeleteOnExceed;

        // 步骤一：检查锁定状态（含时间篡改检测）
        if (record.LockUntil.HasValue && record.LockUntil.Value > DateTime.Now)
        {
            // 时间篡改检测：系统时间被回拨超过1分钟则拒绝解锁
            if (record.LastKnownSystemTime.HasValue && DateTime.Now < record.LastKnownSystemTime.Value.AddMinutes(-1))
            {
                MessageBox.Show(this, "检测到系统时间可能被修改，锁定状态不会被解除。",
                    "安全提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var remaining = record.LockUntil.Value - DateTime.Now;
            MessageBox.Show(this, $"该记录已被锁定，剩余解禁时间：{FormatTimeSpan(remaining)}",
                "记录已锁定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // 自动释放已过期的锁定
        if (record.LockUntil.HasValue && record.LockUntil.Value <= DateTime.Now)
        {
            record.PasswordFailCount = 0;
            record.LockUntil = null;
        }

        // 步骤二：弹出密码输入对话框
        using var dialog = new PasswordDialog();
        dialog.TopMost = true;
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        // 步骤三：验证密码
        if (EncryptionService.VerifyPassword(record, dialog.Password))
        {
            // 密码正确：重置所有安全计数器
            record.PasswordFailCount = 0;
            record.CumulativeLockCount = 0;
            record.LockUntil = null;
            record.LastKnownSystemTime = null;
            _mainForm.SaveData();

            // 解密内容并粘贴到剪贴板
            var result = EncryptionService.DecryptRecord(record, dialog.Password);
            if (result.HasValue)
            {
                ClipboardService.WriteToClipboard(record, result.Value.content);
                _mainForm.HideAndPaste();
            }
            else
            {
                MessageBox.Show(this, "该加密记录已损坏，无法解密。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        else
        {
            // 密码错误：累加失败计数
            record.PasswordFailCount++;
            record.LastKnownSystemTime = DateTime.Now;
            _mainForm.SaveData();

            if (maxAttempts > 0 && record.PasswordFailCount >= maxAttempts)
            {
                if (autoDelete)
                {
                    _mainForm.DeleteRecord(record);
                    RefreshRecords();
                    MessageBox.Show(this, "已达到最大错误次数，该加密记录已自动删除。",
                        "记录删除", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    record.CumulativeLockCount++;
                    int lockMinutes = baseLock * (1 << (record.CumulativeLockCount - 1)); // 每次锁定时长翻倍
                    record.LockUntil = DateTime.Now.AddMinutes(lockMinutes);
                    record.PasswordFailCount = 0;
                    _mainForm.SaveData();
                    MessageBox.Show(this, $"已达到最大错误次数，该记录已被锁定{FormatTimeSpan(TimeSpan.FromMinutes(lockMinutes))}。",
                        "记录锁定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                int remaining = maxAttempts > 0 ? maxAttempts - record.PasswordFailCount : -1;
                string msg = remaining > 0
                    ? $"密码错误，剩余可尝试次数：{remaining}次"
                    : "密码错误";
                MessageBox.Show(this, msg, "密码错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _mainForm.SaveData();
            }
        }
    }

    /// <summary>右键点击记录：显示上下文菜单，动态更新置顶菜单文本</summary>
    private void RecordList_RecordRightClicked(object? sender, (ClipboardRecord record, Point location) args)
    {
        _contextMenuRecord = args.record;
        // 动态更新置顶/取消置顶菜单文本
        if (_recordContextMenu != null && _recordContextMenu.Items.Count > 2
            && _recordContextMenu.Items[2] is ToolStripMenuItem pinItem)
        {
            pinItem.Text = args.record.IsPinned ? "取消置顶" : "置顶";
        }
        _recordContextMenu?.Show(_recordList, args.location);
    }

    /// <summary>编辑记录：加密记录需先验证密码，然后打开编辑对话框</summary>
    private void EditRecord(ClipboardRecord record)
    {
        string? decryptedContent = null;
        ClipboardContentType? decryptedType = null;
        string? password = null;

        if (record.IsEncrypted)
        {
            using var pwDialog = new PasswordDialog();
            pwDialog.TopMost = true;
            if (pwDialog.ShowDialog(this) != DialogResult.OK) return;
            if (!EncryptionService.VerifyPassword(record, pwDialog.Password))
            {
                record.PasswordFailCount++;
                record.LastKnownSystemTime = DateTime.Now;
                _mainForm.SaveData();
                MessageBox.Show(this, "密码错误，无法编辑。", "密码错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 解密内容用于编辑器中显示
            password = pwDialog.Password;
            var result = EncryptionService.DecryptRecord(record, password);
            if (result.HasValue)
            {
                decryptedContent = result.Value.content;
                decryptedType = result.Value.type;
            }
        }

        using var editDialog = new EditRecordDialog(record, _mainForm.Config,
            decryptedContent, decryptedType, password);
        editDialog.TopMost = true;
        if (editDialog.ShowDialog(this) == DialogResult.OK)
        {
            _mainForm.SaveData();
            RefreshRecords();
        }
    }

    /// <summary>清除全部记录（需确认）</summary>
    private void BtnClearAll_Click(object? sender, EventArgs e)
    {
        if (MessageBox.Show(this, "确定要清除全部记录吗？此操作不可撤销。", "确认清除",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            _mainForm.ClearAllRecords();
            RefreshRecords();
        }
    }

    private void BtnClearBefore_Click(object? sender, EventArgs e)
    {
        ShowDateClearDialog(true);
    }

    private void BtnClearAfter_Click(object? sender, EventArgs e)
    {
        ShowDateClearDialog(false);
    }

    /// <summary>显示按日期清除的对话框（支持清除指定日期前/后的记录）</summary>
    private void ShowDateClearDialog(bool isBefore)
    {
        using var form = new Form
        {
            Text = isBefore ? "清除指定日期前记录" : "清除指定日期后记录",
            Size = new Size(350, 200),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            TopMost = true
        };

        var dtp = new DateTimePicker { Location = new Point(20, 20), Size = new Size(290, 25), Format = DateTimePickerFormat.Short };
        var chkPinned = new CheckBox { Text = "同时删除置顶记录", Location = new Point(20, 60), AutoSize = true };
        var btnOk = new Button { Text = "确认", Location = new Point(130, 110), DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "取消", Location = new Point(220, 110), DialogResult = DialogResult.Cancel };
        form.Controls.AddRange(new Control[] { dtp, chkPinned, btnOk, btnCancel });
        form.AcceptButton = btnOk;
        form.CancelButton = btnCancel;

        if (form.ShowDialog(this) == DialogResult.OK)
        {
            var date = dtp.Value.Date;
            var deletePinned = chkPinned.Checked;
            var toRemove = _mainForm.Records.Where(r =>
            {
                if (!deletePinned && r.IsPinned) return false;
                var local = r.CreateTime.ToLocalTime();
                return isBefore ? local < date : local > date.AddDays(1);
            }).ToList();

            foreach (var r in toRemove)
                _mainForm.DeleteRecord(r);
            RefreshRecords();
            MessageBox.Show(this, $"已清除 {toRemove.Count} 条记录。", "清除完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    /// <summary>清除所有非固定记录（需确认）</summary>
    private void BtnClearUnpinned_Click(object? sender, EventArgs e)
    {
        if (MessageBox.Show(this, "确定要清除所有非固定记录吗？", "确认清除",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            var toRemove = _mainForm.Records.Where(r => !r.IsPinned).ToList();
            foreach (var r in toRemove)
                _mainForm.DeleteRecord(r);
            RefreshRecords();
        }
    }

    private void UpdateStats()
    {
        _statsBar.Invalidate();
    }

    /// <summary>自绘状态栏：显示全部记录数、置顶数、加密数、搜索结果数</summary>
    private void StatsBar_Paint(object? sender, PaintEventArgs e)
    {
        try
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(ThemeService.StatsBarBackground);

            // 顶部分界线
            using var borderPen = new Pen(ThemeService.BorderColor, 1);
            g.DrawLine(borderPen, 0, 0, _statsBar.Width, 0);

            var font = ThemeService.GlobalFont;
            using var boldFont = new Font(font, FontStyle.Bold);
            using var grayBrush = new SolidBrush(ThemeService.SecondaryTextColor);
            using var themeBrush = new SolidBrush(ThemeService.ThemeColor);

            int x = DpiHelper.Scale(15);
            var records = _mainForm.Records;

            x = DrawStat(g, "全部记录：", records.Count.ToString(), "条", x, font, boldFont, grayBrush, themeBrush);
            x = DrawStat(g, "置顶记录：", records.Count(r => r.IsPinned).ToString(), "条", x + DpiHelper.Scale(30), font, boldFont, grayBrush, themeBrush);
            x = DrawStat(g, "加密记录：", records.Count(r => r.IsEncrypted).ToString(), "条", x + DpiHelper.Scale(30), font, boldFont, grayBrush, themeBrush);

            if (!string.IsNullOrEmpty(_searchText) && _searchText != "搜索剪贴板记录")
            {
                var filtered = ApplySearch(records);
                DrawStat(g, "搜索结果：", filtered.Count.ToString(), "条", x + DpiHelper.Scale(30), font, boldFont, grayBrush, themeBrush);
            }
        }
        catch { }
    }

    /// <summary>绘制单个统计项（标签+数字+后缀），返回最终 X 坐标</summary>
    private int DrawStat(Graphics g, string label, string number, string suffix, int x, Font font, Font boldFont, Brush grayBrush, Brush themeBrush)
    {
        int y = (_statsBar.Height - (int)font.GetHeight()) / 2;
        var labelSize = g.MeasureString(label, font);
        g.DrawString(label, font, grayBrush, x, y);
        x += (int)labelSize.Width;
        var numSize = g.MeasureString(number, boldFont);
        g.DrawString(number, boldFont, themeBrush, x, y);
        x += (int)numSize.Width;
        var suffixSize = g.MeasureString(suffix, font);
        g.DrawString(suffix, font, grayBrush, x, y);
        return x + (int)suffixSize.Width;
    }

    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}小时{ts.Minutes}分钟";
        return $"{(int)ts.TotalMinutes}分钟";
    }
}
