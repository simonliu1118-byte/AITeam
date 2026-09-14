using System.Diagnostics;
using AITeam.Services;

namespace AITeam;

/// <summary>
/// 幫一個唯讀的 RichTextBox 把 AI 回覆裡的參照收起來：畫面上只留檔名，
/// 完整路徑改用 tooltip 顯示，點一下則直接開啟該檔案所在的資料夾。
/// </summary>
public sealed class LinkFoldingLog
{
    private static readonly Color LinkColor = Color.FromArgb(43, 108, 176);

    private readonly RichTextBox _box;
    private readonly ToolTip _tip = new()
    {
        InitialDelay = 220,
        ReshowDelay = 120,
        AutoPopDelay = 20000,
        ShowAlways = true
    };

    private readonly List<LogLinkSpan> _links = new();
    private int _hovered = -1;

    public LinkFoldingLog(RichTextBox box)
    {
        _box = box;
        _box.DetectUrls = false;
        _box.MouseMove += OnMouseMove;
        _box.MouseLeave += (_, _) => ClearHover();
        _box.MouseClick += OnMouseClick;
    }

    public void Clear()
    {
        _links.Clear();
        ClearHover();
        _box.Clear();
    }

    public void SetText(string text)
    {
        Clear();
        Append(text);
    }

    /// <summary>附加一段文字；裡面的 Markdown 參照會被折成檔名。</summary>
    public void Append(string text)
    {
        // RichTextBox 內部一律用 \n 當換行，先正規化，折疊算出來的位置才會跟實際字元位置一致。
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var folded = LogLinks.Fold(normalized);

        var baseOffset = _box.TextLength;
        _box.SelectionStart = baseOffset;
        _box.SelectionLength = 0;
        _box.SelectionColor = _box.ForeColor;
        _box.AppendText(folded.Text);

        foreach (var link in folded.Links)
        {
            var span = new LogLinkSpan(baseOffset + link.Start, link.Length, link.Target);
            _links.Add(span);
            _box.Select(span.Start, span.Length);
            _box.SelectionColor = LinkColor;
        }

        _box.SelectionStart = _box.TextLength;
        _box.SelectionLength = 0;
        _box.SelectionColor = _box.ForeColor;
    }

    /// <summary>發言者那一行：粗體＋強調色，讓使用者一眼就掃得到是誰在講。</summary>
    public void AppendHeading(string text)
    {
        var start = _box.TextLength;
        _box.SelectionStart = start;
        _box.SelectionLength = 0;
        _box.AppendText(text);

        _box.Select(start, _box.TextLength - start);
        _box.SelectionColor = LinkColor;
        _box.SelectionFont = new Font(_box.Font, FontStyle.Bold);

        ResetCaretStyle();
    }

    /// <summary>一條淡淡的分隔線，標示這個人講完了。</summary>
    public void AppendDivider(int width = 60)
    {
        var start = _box.TextLength;
        _box.SelectionStart = start;
        _box.SelectionLength = 0;
        _box.AppendText(new string('─', width) + "\n");

        _box.Select(start, _box.TextLength - start);
        _box.SelectionColor = Color.FromArgb(214, 221, 229);

        ResetCaretStyle();
    }

    /// <summary>記住目前的長度，之後可以把從這裡開始的內容整段換掉。</summary>
    public int Mark() => _box.TextLength;

    /// <summary>把 Mark() 之後追加的內容整段移除——用來把「正在發言…」換成真正的發言。</summary>
    public void TruncateTo(int offset)
    {
        if (offset < 0 || offset >= _box.TextLength) return;

        // 唯讀的 RichTextBox 會直接忽略 SelectedText 的指派，刪不掉任何東西——
        // 這正是「正在發言…」沒被換掉、跟真正的發言疊在一起的原因。
        // 暫時解除唯讀，刪完再設回去。
        var wasReadOnly = _box.ReadOnly;
        _box.ReadOnly = false;
        try
        {
            _box.Select(offset, _box.TextLength - offset);
            _box.SelectedText = string.Empty;
        }
        finally
        {
            _box.ReadOnly = wasReadOnly;
        }

        _links.RemoveAll(link => link.Start >= offset);
        ResetCaretStyle();
    }

    private void ResetCaretStyle()
    {
        _box.SelectionStart = _box.TextLength;
        _box.SelectionLength = 0;
        _box.SelectionColor = _box.ForeColor;
        _box.SelectionFont = _box.Font;
    }

    public void ScrollToTop()
    {
        _box.SelectionStart = 0;
        _box.SelectionLength = 0;
        _box.ScrollToCaret();
    }

    public void ScrollToEnd()
    {
        _box.SelectionStart = _box.TextLength;
        _box.SelectionLength = 0;
        _box.ScrollToCaret();
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        var index = FindLink(e.Location);
        if (index == _hovered) return;

        _hovered = index;
        if (index < 0)
        {
            _box.Cursor = Cursors.IBeam;
            _tip.Hide(_box);
            return;
        }

        _box.Cursor = Cursors.Hand;
        _tip.Show(_links[index].Target + "\n（點一下可開啟所在資料夾）", _box, e.X + 14, e.Y + 18);
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var index = FindLink(e.Location);
        if (index < 0) return;

        Reveal(_links[index].Target);
    }

    private int FindLink(Point location)
    {
        if (_links.Count == 0) return -1;

        var charIndex = _box.GetCharIndexFromPosition(location);
        // GetCharIndexFromPosition 在空白處會回傳最靠近的字元，所以要確認滑鼠真的落在那個字上。
        var charBounds = _box.GetPositionFromCharIndex(charIndex);
        if (location.X < charBounds.X - 2) return -1;

        for (var i = 0; i < _links.Count; i++)
            if (_links[i].Contains(charIndex)) return i;

        return -1;
    }

    private void ClearHover()
    {
        _hovered = -1;
        _box.Cursor = Cursors.IBeam;
        _tip.Hide(_box);
    }

    /// <summary>在檔案總管裡開啟並選取這個檔案；不是本機路徑就交給系統預設處理方式。</summary>
    private static void Reveal(string target)
    {
        try
        {
            if (File.Exists(target))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
                return;
            }

            if (Directory.Exists(target))
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
                return;
            }

            // 檔案已經不在了（例如暫存工作區被清掉）就只告訴使用者原本的位置，不要丟例外。
            MessageBox.Show($"找不到這個位置：\r\n{target}", "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"開啟失敗：{ex.Message}", "AITeam", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
