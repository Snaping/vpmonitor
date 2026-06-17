using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VPMonitor.UI;

public class WaveformChart : Control
{
    private readonly Queue<double> _dataPoints = new(60);
    private double _maxValue = 100;
    private double _minValue = 0;
    private string _title = string.Empty;
    private string _unit = string.Empty;
    private Color _lineColor = Color.FromArgb(33, 150, 243);
    private Color _fillColor = Color.FromArgb(50, 33, 150, 243);
    private Color _gridColor = Color.FromArgb(30, 200, 200, 200);
    private bool _showGrid = true;
    private double _currentValue;
    private double _warningThreshold = double.NaN;
    private Color _warningColor = Color.FromArgb(255, 193, 7);
    private bool _isWarning;

    [Category("Appearance")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string Title
    {
        get => _title;
        set { _title = value; Invalidate(); }
    }

    [Category("Appearance")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public string Unit
    {
        get => _unit;
        set { _unit = value; Invalidate(); }
    }

    [Category("Appearance")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public Color LineColor
    {
        get => _lineColor;
        set { _lineColor = value; _fillColor = Color.FromArgb(50, _lineColor); Invalidate(); }
    }

    [Category("Appearance")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public Color FillColor
    {
        get => _fillColor;
        set { _fillColor = value; Invalidate(); }
    }

    [Category("Behavior")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public double MaxValue
    {
        get => _maxValue;
        set { _maxValue = value; Invalidate(); }
    }

    [Category("Behavior")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public double MinValue
    {
        get => _minValue;
        set { _minValue = value; Invalidate(); }
    }

    [Category("Behavior")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public bool ShowGrid
    {
        get => _showGrid;
        set { _showGrid = value; Invalidate(); }
    }

    [Category("Behavior")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public double WarningThreshold
    {
        get => _warningThreshold;
        set { _warningThreshold = value; Invalidate(); }
    }

    [Category("Behavior")]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
    public Color WarningColor
    {
        get => _warningColor;
        set { _warningColor = value; Invalidate(); }
    }

    [Browsable(false)]
    public double CurrentValue => _currentValue;

    [Browsable(false)]
    public bool IsWarning => _isWarning;

    public WaveformChart()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);
    }

    public void AddDataPoint(double value)
    {
        _currentValue = value;

        if (_dataPoints.Count >= 60)
            _dataPoints.Dequeue();
        _dataPoints.Enqueue(value);

        _isWarning = !double.IsNaN(_warningThreshold) && value >= _warningThreshold;

        Invalidate();
    }

    public void Clear()
    {
        _dataPoints.Clear();
        _currentValue = 0;
        _isWarning = false;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var rect = ClientRectangle;
        var padding = new Padding(40, 35, 20, 25);
        var chartArea = new Rectangle(
            padding.Left,
            padding.Top,
            rect.Width - padding.Left - padding.Right,
            rect.Height - padding.Top - padding.Bottom
        );

        g.Clear(BackColor);

        if (_showGrid)
            DrawGrid(g, chartArea);

        DrawAxes(g, chartArea);

        if (_dataPoints.Count > 0)
            DrawWaveform(g, chartArea);

        DrawTitleAndValue(g, rect);
    }

    private void DrawGrid(Graphics g, Rectangle chartArea)
    {
        using var pen = new Pen(_gridColor, 1);

        for (int i = 0; i <= 4; i++)
        {
            float y = chartArea.Top + (chartArea.Height * i / 4f);
            g.DrawLine(pen, chartArea.Left, y, chartArea.Right, y);
        }

        for (int i = 0; i <= 6; i++)
        {
            float x = chartArea.Left + (chartArea.Width * i / 6f);
            g.DrawLine(pen, x, chartArea.Top, x, chartArea.Bottom);
        }

        if (!double.IsNaN(_warningThreshold) && _warningThreshold > _minValue && _warningThreshold < _maxValue)
        {
            using var warningPen = new Pen(_warningColor, 1) { DashStyle = DashStyle.Dash };
            float y = chartArea.Bottom - (float)((_warningThreshold - _minValue) / (_maxValue - _minValue) * chartArea.Height);
            g.DrawLine(warningPen, chartArea.Left, y, chartArea.Right, y);
        }
    }

    private void DrawAxes(Graphics g, Rectangle chartArea)
    {
        using var pen = new Pen(Color.FromArgb(80, 200, 200, 200), 1);
        g.DrawLine(pen, chartArea.Left, chartArea.Top, chartArea.Left, chartArea.Bottom);
        g.DrawLine(pen, chartArea.Left, chartArea.Bottom, chartArea.Right, chartArea.Bottom);

        using var textBrush = new SolidBrush(Color.FromArgb(180, 200, 200, 200));
        using var stringFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };

        for (int i = 0; i <= 4; i++)
        {
            float y = chartArea.Bottom - (chartArea.Height * i / 4f);
            double value = _minValue + (_maxValue - _minValue) * i / 4;
            g.DrawString(value.ToString("0"), Font, textBrush, chartArea.Left - 5, y, stringFormat);
        }

        stringFormat.Alignment = StringAlignment.Center;
        stringFormat.LineAlignment = StringAlignment.Near;
        for (int i = 0; i <= 6; i++)
        {
            float x = chartArea.Left + (chartArea.Width * i / 6f);
            int seconds = 60 - i * 10;
            g.DrawString(seconds == 60 ? "0s" : (60 - seconds).ToString() + "s", Font, textBrush, x, chartArea.Bottom + 5, stringFormat);
        }

        stringFormat.Alignment = StringAlignment.Near;
        stringFormat.LineAlignment = StringAlignment.Center;
        using var timeBrush = new SolidBrush(Color.FromArgb(150, 200, 200, 200));
        g.DrawString("-60s", Font, timeBrush, chartArea.Left, chartArea.Bottom + 5, stringFormat);
        stringFormat.Alignment = StringAlignment.Far;
        g.DrawString("now", Font, timeBrush, chartArea.Right, chartArea.Bottom + 5, stringFormat);
    }

    private void DrawWaveform(Graphics g, Rectangle chartArea)
    {
        var points = new List<PointF>();
        var dataArray = _dataPoints.ToArray();

        for (int i = 0; i < dataArray.Length; i++)
        {
            double normalizedValue = (dataArray[i] - _minValue) / (_maxValue - _minValue);
            normalizedValue = Math.Max(0, Math.Min(1, normalizedValue));

            float x = chartArea.Left + (chartArea.Width * i / 59f);
            float y = chartArea.Bottom - (float)(normalizedValue * chartArea.Height);

            points.Add(new PointF(x, y));
        }

        if (points.Count >= 2)
        {
            var path = new GraphicsPath();
            path.AddLines(points.ToArray());

            path.AddLine(points[^1].X, chartArea.Bottom, points[0].X, chartArea.Bottom);
            path.CloseFigure();

            using var fillBrush = new SolidBrush(_isWarning ? Color.FromArgb(80, _warningColor) : _fillColor);
            g.FillPath(fillBrush, path);

            using var linePen = new Pen(_isWarning ? _warningColor : _lineColor, 2);
            g.DrawLines(linePen, points.ToArray());

            using var dotBrush = new SolidBrush(_isWarning ? _warningColor : _lineColor);
            g.FillEllipse(dotBrush, points[^1].X - 4, points[^1].Y - 4, 8, 8);
        }
    }

    private void DrawTitleAndValue(Graphics g, Rectangle rect)
    {
        using var titleBrush = new SolidBrush(ForeColor);
        using var valueBrush = new SolidBrush(_isWarning ? _warningColor : _lineColor);
        using var titleFont = new Font(Font.FontFamily, 10f, FontStyle.Bold);
        using var valueFont = new Font(Font.FontFamily, 14f, FontStyle.Bold);

        g.DrawString(_title, titleFont, titleBrush, 5, 5);

        var valueText = _currentValue.ToString("0.0") + " " + _unit;
        var valueSize = g.MeasureString(valueText, valueFont);
        g.DrawString(valueText, valueFont, valueBrush, rect.Width - valueSize.Width - 5, 2);
    }
}
