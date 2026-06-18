using System.ComponentModel;

namespace VPMonitor.Controls;

public class WaveformChart : Control
{
    private readonly Queue<double> _dataPoints = new();
    private readonly int _maxDataPoints = 60;
    private readonly object _lockObject = new();
    private Pen _linePen = Pens.Blue;
    private Pen _gridPen = new(Color.FromArgb(50, Color.Gray));
    private Brush _fillBrush = new SolidBrush(Color.FromArgb(30, Color.Blue));
    private Brush _textBrush = Brushes.Black;
    private StringFormat _stringFormat = new();
    private double _maxValue = 100.0;
    private double _minValue = 0.0;
    private string _title = string.Empty;
    private string _unit = string.Empty;
    private bool _showGrid = true;
    private int _gridLines = 5;
    private Font _titleFont = new("Arial", 10, FontStyle.Bold);
    private Font _valueFont = new("Arial", 9);

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color LineColor
    {
        get => _linePen.Color;
        set
        {
            _linePen = new Pen(value, 2);
            _fillBrush = new SolidBrush(Color.FromArgb(30, value));
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double MaxValue
    {
        get => _maxValue;
        set
        {
            _maxValue = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double MinValue
    {
        get => _minValue;
        set
        {
            _minValue = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _title;
        set
        {
            _title = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Unit
    {
        get => _unit;
        set
        {
            _unit = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            _showGrid = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int GridLines
    {
        get => _gridLines;
        set
        {
            _gridLines = value;
            Invalidate();
        }
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double CurrentValue { get; private set; }

    public WaveformChart()
    {
        DoubleBuffered = true;
        _stringFormat.Alignment = StringAlignment.Far;
        _stringFormat.LineAlignment = StringAlignment.Center;
        BackColor = Color.White;
    }

    public void AddDataPoint(double value)
    {
        lock (_lockObject)
        {
            CurrentValue = value;
            _dataPoints.Enqueue(value);
            if (_dataPoints.Count > _maxDataPoints)
            {
                _dataPoints.Dequeue();
            }
        }
        Invalidate();
    }

    public void ClearData()
    {
        lock (_lockObject)
        {
            _dataPoints.Clear();
            CurrentValue = 0;
        }
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var chartRect = new Rectangle(
            Padding.Left + 50,
            Padding.Top + 25,
            Width - Padding.Horizontal - 60,
            Height - Padding.Vertical - 50);

        if (chartRect.Width <= 0 || chartRect.Height <= 0) return;

        if (_showGrid)
        {
            DrawGrid(g, chartRect);
        }

        DrawTitle(g);

        double[] data;
        lock (_lockObject)
        {
            data = _dataPoints.ToArray();
        }

        if (data.Length > 0)
        {
            DrawWaveform(g, chartRect, data);
            DrawCurrentValue(g, chartRect);
        }

        DrawAxes(g, chartRect);
    }

    private void DrawGrid(Graphics g, Rectangle chartRect)
    {
        for (int i = 0; i <= _gridLines; i++)
        {
            var y = chartRect.Top + (chartRect.Height * i / _gridLines);
            g.DrawLine(_gridPen, chartRect.Left, y, chartRect.Right, y);

            var value = _maxValue - ((_maxValue - _minValue) * i / _gridLines);
            var text = value.ToString("F1");
            var textSize = g.MeasureString(text, _valueFont);
            g.DrawString(text, _valueFont, _textBrush,
                chartRect.Left - textSize.Width - 5,
                y - textSize.Height / 2);
        }

        for (int i = 0; i <= 6; i++)
        {
            var x = chartRect.Left + (chartRect.Width * i / 6);
            g.DrawLine(_gridPen, x, chartRect.Top, x, chartRect.Bottom);

            var seconds = (6 - i) * 10;
            var text = seconds == 0 ? "Now" : $"-{seconds}s";
            var textSize = g.MeasureString(text, _valueFont);
            g.DrawString(text, _valueFont, _textBrush,
                x - textSize.Width / 2,
                chartRect.Bottom + 5);
        }
    }

    private void DrawTitle(Graphics g)
    {
        if (!string.IsNullOrEmpty(_title))
        {
            var textSize = g.MeasureString(_title, _titleFont);
            g.DrawString(_title, _titleFont, _textBrush,
                (Width - textSize.Width) / 2, 5);
        }
    }

    private void DrawWaveform(Graphics g, Rectangle chartRect, double[] data)
    {
        if (data.Length < 2) return;

        var range = _maxValue - _minValue;
        if (range <= 0) range = 1;

        var points = new List<PointF>();
        var stepX = (float)chartRect.Width / (_maxDataPoints - 1);

        for (int i = 0; i < data.Length; i++)
        {
            var x = chartRect.Right - (data.Length - 1 - i) * stepX;
            var normalizedValue = (data[i] - _minValue) / range;
            normalizedValue = Math.Max(0, Math.Min(1, normalizedValue));
            var y = chartRect.Bottom - (float)(normalizedValue * chartRect.Height);
            points.Add(new PointF(x, y));
        }

        var fillPoints = new List<PointF>(points)
        {
            new PointF(points[points.Count - 1].X, chartRect.Bottom),
            new PointF(points[0].X, chartRect.Bottom)
        };

        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddPolygon(fillPoints.ToArray());
        g.FillPath(_fillBrush, path);

        if (points.Count >= 2)
        {
            g.DrawLines(_linePen, points.ToArray());
        }

        if (points.Count > 0)
        {
            var lastPoint = points[points.Count - 1];
            using var dotBrush = new SolidBrush(_linePen.Color);
            g.FillEllipse(dotBrush, lastPoint.X - 4, lastPoint.Y - 4, 8, 8);
            g.DrawEllipse(Pens.White, lastPoint.X - 4, lastPoint.Y - 4, 8, 8);
        }
    }

    private void DrawCurrentValue(Graphics g, Rectangle chartRect)
    {
        var valueText = $"{CurrentValue:F2} {_unit}";
        var textSize = g.MeasureString(valueText, _titleFont);

        var bgRect = new RectangleF(
            chartRect.Right - textSize.Width - 10,
            chartRect.Top + 5,
            textSize.Width + 10,
            textSize.Height + 4);

        using var bgBrush = new SolidBrush(Color.FromArgb(200, Color.White));
        g.FillRoundedRectangle(bgBrush, bgRect, 3);
        g.DrawRectangle(Pens.LightGray, bgRect.X, bgRect.Y, bgRect.Width, bgRect.Height);

        g.DrawString(valueText, _titleFont, _linePen.Brush,
            bgRect.X + 5, bgRect.Y + 2);
    }

    private void DrawAxes(Graphics g, Rectangle chartRect)
    {
        using var axisPen = new Pen(Color.DarkGray, 2);
        g.DrawLine(axisPen, chartRect.Left, chartRect.Top, chartRect.Left, chartRect.Bottom);
        g.DrawLine(axisPen, chartRect.Left, chartRect.Bottom, chartRect.Right, chartRect.Bottom);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _linePen?.Dispose();
            _gridPen?.Dispose();
            _fillBrush?.Dispose();
            _textBrush?.Dispose();
            _stringFormat?.Dispose();
            _titleFont?.Dispose();
            _valueFont?.Dispose();
        }
        base.Dispose(disposing);
    }
}

public static class GraphicsExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, RectangleF rect, float radius)
    {
        using var path = new System.Drawing.Drawing2D.GraphicsPath();
        path.AddArc(rect.X, rect.Y, radius, radius, 180, 90);
        path.AddArc(rect.Right - radius, rect.Y, radius, radius, 270, 90);
        path.AddArc(rect.Right - radius, rect.Bottom - radius, radius, radius, 0, 90);
        path.AddArc(rect.X, rect.Bottom - radius, radius, radius, 90, 90);
        path.CloseAllFigures();
        g.FillPath(brush, path);
    }
}
