using System.Drawing.Drawing2D;
using AITeam.Services;

namespace AITeam;

/// <summary>
/// 任務階段進度條：一排圓點代表各階段，已完成打勾、進行中實心放大、未開始空心，
/// 圓點下方是階段名稱。階段清單依任務種類（查詢／修改）決定。
/// </summary>
public sealed class TaskStageStrip : Control
{
    private static readonly Color RailColor = Color.FromArgb(190, 199, 210);
    private static readonly Color DoneColor = Color.FromArgb(46, 160, 92);
    private static readonly Color RunningColor = Color.FromArgb(214, 158, 46);
    private static readonly Color WaitingColor = Color.FromArgb(43, 108, 176);
    private static readonly Color PendingText = Color.FromArgb(104, 113, 123);
    private static readonly Color CurrentText = Color.FromArgb(34, 40, 49);

    private const int NodeSize = 15;
    private const int NodeTop = 4;

    private IReadOnlyList<TaskStage> _stages = Array.Empty<TaskStage>();
    private int _currentIndex = -1;
    private bool _waiting;
    private bool _allDone;

    public TaskStageStrip()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        Height = 46;
        Font = new Font("Microsoft JhengHei UI", 8.5F);
    }

    public void ShowStages(IReadOnlyList<TaskStage> stages)
    {
        _stages = stages;
        _currentIndex = -1;
        _waiting = false;
        _allDone = false;
        Invalidate();
    }

    public void SetCurrent(TaskStage stage, bool waitingForUser)
    {
        var index = -1;
        for (var i = 0; i < _stages.Count; i++)
            if (_stages[i] == stage) { index = i; break; }

        // 階段只會往前走：CI 失敗退回修正時仍留在同一個階段，不讓格子倒退跳動。
        if (index >= _currentIndex) _currentIndex = index;
        _waiting = waitingForUser;
        _allDone = false;
        Invalidate();
    }

    public void MarkAllDone()
    {
        _allDone = true;
        _waiting = false;
        Invalidate();
    }

    public void Clear()
    {
        _stages = Array.Empty<TaskStage>();
        _currentIndex = -1;
        _waiting = false;
        _allDone = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_stages.Count == 0) return;

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var slot = (float)ClientSize.Width / _stages.Count;
        var centerY = NodeTop + NodeSize / 2f;

        for (var i = 0; i < _stages.Count; i++)
        {
            var centerX = slot * i + slot / 2f;
            var done = _allDone || i < _currentIndex;
            var current = !_allDone && i == _currentIndex;

            if (i > 0)
            {
                var previousX = slot * (i - 1) + slot / 2f;
                using var railPen = new Pen(done || current ? DoneColor : RailColor, 2f);
                e.Graphics.DrawLine(railPen, previousX + NodeSize / 2f + 2, centerY, centerX - NodeSize / 2f - 2, centerY);
            }

            var node = new RectangleF(centerX - NodeSize / 2f, NodeTop, NodeSize, NodeSize);
            if (done)
            {
                using var fill = new SolidBrush(DoneColor);
                e.Graphics.FillEllipse(fill, node);
                DrawCheck(e.Graphics, node);
            }
            else if (current)
            {
                var accent = _waiting ? WaitingColor : RunningColor;
                using var halo = new SolidBrush(Color.FromArgb(48, accent));
                e.Graphics.FillEllipse(halo, RectangleF.Inflate(node, 4, 4));
                using var fill = new SolidBrush(accent);
                e.Graphics.FillEllipse(fill, node);
            }
            else
            {
                using var back = new SolidBrush(BackColor);
                e.Graphics.FillEllipse(back, node);
                using var pen = new Pen(RailColor, 2f);
                e.Graphics.DrawEllipse(pen, RectangleF.Inflate(node, -1, -1));
            }

            var label = TaskStages.DisplayName(_stages[i]);
            using var font = current ? new Font(Font, FontStyle.Bold) : new Font(Font, FontStyle.Regular);
            var slotRect = new Rectangle((int)(slot * i), NodeTop + NodeSize + 5, (int)slot, 20);
            TextRenderer.DrawText(
                e.Graphics,
                label,
                font,
                slotRect,
                current ? CurrentText : PendingText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    private static void DrawCheck(Graphics g, RectangleF node)
    {
        using var pen = new Pen(Color.White, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var left = new PointF(node.Left + node.Width * 0.28f, node.Top + node.Height * 0.52f);
        var middle = new PointF(node.Left + node.Width * 0.44f, node.Top + node.Height * 0.68f);
        var right = new PointF(node.Left + node.Width * 0.74f, node.Top + node.Height * 0.34f);
        g.DrawLines(pen, new[] { left, middle, right });
    }
}
