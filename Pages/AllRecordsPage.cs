using JIE剪切板.Controls;
using JIE剪切板.Dialogs;
using JIE剪切板.Models;
using JIE剪切板.Services;

namespace JIE剪切板.Pages;

public class AllRecordsPage : UserControl
{
    private TextBox _searchBox = null!;
    private Panel _buttonPanel = null!;
    private RecordListPanel _recordList = null!;
    private Panel _statsBar = null!;


    private readonly MainForm _mainForm;
    private string _searchText = "";

    public AllRecordsPage(MainForm mainForm)
    {
        _mainForm = mainForm;
        Dock = DockStyle.Fill;
        BackColor = ThemeService.WindowBackground;
        InitializeControls();
    }

    private void InitializeControls()
    {
        // Stats bar (bottom, fixed)
        _statsBar = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = DpiHelper.Scale(36),
            BackColor = ThemeService.StatsBarBackground,
            Padding = new Padding(DpiHelper.Scale(15), 0, DpiHelper.Scale(15), 0)
        };
        _statsBar.Paint += StatsBar_Paint;

        // Search panel (top)
        var searchPanel = new Panel { Dock = DockStyle.Top, Height = DpiHelper.Scale(50), Padding = new Padding(DpiHelper.Scale(15), DpiHelper.Scale(10), DpiHelper.Scale(15), DpiHelper.Scale(5)) };
        _searchBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font(ThemeService.GlobalFont.FontFamily, 10f),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = ThemeService.IsDarkMode ? Color.FromArgb(50, 50, 50) : Color.White,
            ForeColor = ThemeService.TextColor
        };
        _searchBox.GotFocus += (_, _) => { if (_searchBox.Text == "搜索剪贴板记录") { _searchBox.Text = ""; _searchBox.ForeColor = ThemeService.TextColor; } };
        _searchBox.LostFocus += (_, _) => { if (string.IsNullOrEmpty(_searchBox.Text)) { _searchBox.Text = "搜索剪贴板记录"; _searchBox.ForeColor = ThemeService.SecondaryTextColor; } };
        _searchBox.Text = "搜索剪贴板记录";
        _searchBox.ForeColor = ThemeService.SecondaryTextColor;
        _searchBox.TextChanged += SearchBox_TextChanged;
        searchPanel.Controls.Add(_searchBox);

        // Button panel
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

        // Record list
        _recordList = new RecordListPanel { Dock = DockStyle.Fill };
        _recordList.RecordClicked += RecordList_RecordClicked;
        _recordList.RecordRightClicked += RecordList_RecordRightClicked;

        Controls.Add(_recordList);
        Controls.Add(_buttonPanel);
        Controls.Add(searchPanel);
        Controls.Add(_statsBar);
    }

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

    public void RefreshRecords()
    {
        try
        {
            var records = _mainForm.Records;
            var filtered = ApplySearch(records);

            // Sort: pinned first, then by time desc
            filtered = filtered.OrderByDescending(r => r.IsPinned).ThenByDescending(r => r.CreateTime).ToList();
            _recordList.SetRecords(filtered);
            UpdateStats();
        }
        catch (Exception ex)
        {
            LogService.Log("Failed to refresh records", ex);
        }
    }

    private List<ClipboardRecord> ApplySearch(List<ClipboardRecord> records)
    {
        if (string.IsNullOrEmpty(_searchText) || _searchText == "搜索剪贴板记录")
            return records.ToList();

        var keyword = _searchText.ToLower();
        var result = new List<ClipboardRecord>();

        foreach (var record in records)
        {
            if (record.IsEncrypted)
            {
                // Search encrypted records
                if ("加密内容".Contains(keyword))
                {
                    result.Add(record);
                    continue;
                }

                if (_mainForm.Config.AllowSearchEncryptedContent)
                {
                    // Temp decrypt for search (risky, user confirmed)
                    // We cannot decrypt without password, so encrypted search is limited
                    // to matching the literal "[加密内容]" text
                }
                continue;
            }

            // Search unencrypted records
            var preview = ClipboardService.GetContentPreview(record, 500).ToLower();
            var timeStr = record.CreateTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm").ToLower();
            if (preview.Contains(keyword) || timeStr.Contains(keyword))
                result.Add(record);
        }
        return result;
    }

    private void SearchBox_TextChanged(object? sender, EventArgs e)
    {
        _searchText = _searchBox.Text;
        RefreshRecords();
    }

    private void RecordList_RecordClicked(object? sender, ClipboardRecord record)
    {
        try
        {
            if (record.IsEncrypted)
            {
                HandleEncryptedRecordClick(record);
                return;
            }

            // Check copy count limit
            if (record.MaxCopyCount > 0 && record.CurrentCopyCount >= record.MaxCopyCount)
            {
                MessageBox.Show(this, "该记录已达到最大复制次数限制。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            // Write to clipboard and auto-paste
            _mainForm.CopyAndPasteRecord(record);
        }
        catch (Exception ex)
        {
            LogService.Log("Record click handler failed", ex);
        }
    }

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

        // Step 1: Check lock state with time tamper detection
        if (record.LockUntil.HasValue && record.LockUntil.Value > DateTime.Now)
        {
            // Time tamper check
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

        // Auto-release expired lock
        if (record.LockUntil.HasValue && record.LockUntil.Value <= DateTime.Now)
        {
            record.PasswordFailCount = 0;
            record.LockUntil = null;
        }

        // Step 2: Show password dialog
        using var dialog = new PasswordDialog();
        dialog.TopMost = true;
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        // Step 3: Verify password
        if (EncryptionService.VerifyPassword(record, dialog.Password))
        {
            // Correct: reset counters
            record.PasswordFailCount = 0;
            record.CumulativeLockCount = 0;
            record.LockUntil = null;
            record.LastKnownSystemTime = null;
            _mainForm.SaveData();

            // Decrypt and paste
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
            // Wrong password
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
                    int lockMinutes = baseLock * (1 << (record.CumulativeLockCount - 1)); // Double each time
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

    private void RecordList_RecordRightClicked(object? sender, (ClipboardRecord record, Point location) args)
    {
        var (record, location) = args;
        var menu = new ContextMenuStrip();

        var menuEdit = new ToolStripMenuItem("编辑记录");
        menuEdit.Click += (_, _) => EditRecord(record);

        var menuDelete = new ToolStripMenuItem("删除本条");
        menuDelete.Click += (_, _) =>
        {
            if (MessageBox.Show(this, "确定要删除这条记录吗？", "确认删除",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                _mainForm.DeleteRecord(record);
                RefreshRecords();
            }
        };

        var menuPin = new ToolStripMenuItem(record.IsPinned ? "取消置顶" : "置顶");
        menuPin.Click += (_, _) =>
        {
            record.IsPinned = !record.IsPinned;
            _mainForm.SaveData();
            RefreshRecords();
        };

        var menuCopy = new ToolStripMenuItem("复制本条");
        menuCopy.Click += (_, _) => RecordList_RecordClicked(sender, record);

        menu.Items.AddRange(new ToolStripItem[] { menuEdit, menuDelete, menuPin, menuCopy });
        menu.Show(_recordList, location);
    }

    private void EditRecord(ClipboardRecord record)
    {
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
        }

        using var editDialog = new EditRecordDialog(record, _mainForm.Config);
        editDialog.TopMost = true;
        if (editDialog.ShowDialog(this) == DialogResult.OK)
        {
            _mainForm.SaveData();
            RefreshRecords();
        }
    }

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

    private void StatsBar_Paint(object? sender, PaintEventArgs e)
    {
        try
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(ThemeService.StatsBarBackground);

            // Top border line
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
