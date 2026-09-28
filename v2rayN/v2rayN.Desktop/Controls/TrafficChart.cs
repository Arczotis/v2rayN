namespace v2rayN.Desktop.Controls;

/// <summary>
/// Two-series area sparkline for live upload/download rates. Both series share one
/// vertical scale so their heights stay comparable.
/// </summary>
public class TrafficChart : Control
{
    public static readonly StyledProperty<IBrush?> UpBrushProperty =
        AvaloniaProperty.Register<TrafficChart, IBrush?>(nameof(UpBrush));

    public static readonly StyledProperty<IBrush?> DownBrushProperty =
        AvaloniaProperty.Register<TrafficChart, IBrush?>(nameof(DownBrush));

    public static readonly StyledProperty<IBrush?> GridBrushProperty =
        AvaloniaProperty.Register<TrafficChart, IBrush?>(nameof(GridBrush));

    private IReadOnlyList<double> _up = [];
    private IReadOnlyList<double> _down = [];
    private int _capacity = 60;

    static TrafficChart()
    {
        AffectsRender<TrafficChart>(UpBrushProperty, DownBrushProperty, GridBrushProperty);
    }

    public IBrush? UpBrush
    {
        get => GetValue(UpBrushProperty);
        set => SetValue(UpBrushProperty, value);
    }

    public IBrush? DownBrush
    {
        get => GetValue(DownBrushProperty);
        set => SetValue(DownBrushProperty, value);
    }

    public IBrush? GridBrush
    {
        get => GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public void SetData(IReadOnlyList<double> up, IReadOnlyList<double> down, int capacity)
    {
        _up = up;
        _down = down;
        _capacity = Math.Max(2, capacity);
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0)
        {
            return;
        }

        if (GridBrush is { } grid)
        {
            var gridPen = new Pen(grid, 1, new DashStyle([3, 4], 0));
            for (var i = 1; i <= 3; i++)
            {
                var y = Math.Round(h * i / 4) + 0.5;
                context.DrawLine(gridPen, new Point(0, y), new Point(w, y));
            }
        }

        var max = Math.Max(_up.DefaultIfEmpty(0).Max(), _down.DefaultIfEmpty(0).Max());
        // Keep a floor so idle noise doesn't get stretched to full height.
        max = Math.Max(max * 1.15, 8);

        DrawSeries(context, _down, DownBrush, max, w, h);
        DrawSeries(context, _up, UpBrush, max, w, h);
    }

    private void DrawSeries(DrawingContext context, IReadOnlyList<double> data, IBrush? brush, double max, double w, double h)
    {
        if (brush is not ISolidColorBrush solid || data.Count < 2)
        {
            return;
        }

        var step = w / (_capacity - 1);
        var x0 = w - (step * (data.Count - 1));
        var points = new Point[data.Count];
        for (var i = 0; i < data.Count; i++)
        {
            points[i] = new Point(x0 + (step * i), h - 2 - ((h - 4) * data[i] / max));
        }

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(points[0], false);
            for (var i = 1; i < points.Length; i++)
            {
                // Midpoint quadratic smoothing: cheap and never overshoots below zero.
                var mid = new Point((points[i - 1].X + points[i].X) / 2, (points[i - 1].Y + points[i].Y) / 2);
                g.QuadraticBezierTo(points[i - 1], mid);
            }
            g.LineTo(points[^1]);
            g.EndFigure(false);
        }

        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(new Point(points[0].X, h), true);
            g.LineTo(points[0]);
            for (var i = 1; i < points.Length; i++)
            {
                var mid = new Point((points[i - 1].X + points[i].X) / 2, (points[i - 1].Y + points[i].Y) / 2);
                g.QuadraticBezierTo(points[i - 1], mid);
            }
            g.LineTo(points[^1]);
            g.LineTo(new Point(points[^1].X, h));
            g.EndFigure(true);
        }

        var c = solid.Color;
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            [
                new GradientStop(Color.FromArgb(70, c.R, c.G, c.B), 0),
                new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1),
            ],
        };

        context.DrawGeometry(fill, null, area);
        context.DrawGeometry(null, new Pen(brush, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), line);
    }
}
