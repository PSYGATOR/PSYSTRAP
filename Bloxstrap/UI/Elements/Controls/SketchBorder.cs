using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Bloxstrap.UI.Elements.Controls
{
    /// <summary>
    /// PSYstrap: a border that looks drawn with a marker, like the logo. The outline wobbles a little and is drawn twice
    /// (a main pass and a lighter second pass), and the fill can be the logo's green hatching.
    ///
    /// With Roughness 0 it is a plain rounded rectangle, which is what the non-PSY themes use, so the same markup works
    /// in every theme. The wobble is seeded per control, so it doesn't change shape every time it redraws.
    /// </summary>
    public class SketchBorder : Decorator
    {
        #region Properties
        public static readonly DependencyProperty BackgroundProperty = DependencyProperty.Register(
            nameof(Background), typeof(Brush), typeof(SketchBorder), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.Register(
            nameof(BorderBrush), typeof(Brush), typeof(SketchBorder), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty BorderThicknessProperty = DependencyProperty.Register(
            nameof(BorderThickness), typeof(double), typeof(SketchBorder), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
            nameof(CornerRadius), typeof(double), typeof(SketchBorder), new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
            nameof(Padding), typeof(Thickness), typeof(SketchBorder), new FrameworkPropertyMetadata(new Thickness(0), FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty RoughnessProperty = DependencyProperty.Register(
            nameof(Roughness), typeof(double), typeof(SketchBorder), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty HatchedProperty = DependencyProperty.Register(
            nameof(Hatched), typeof(bool), typeof(SketchBorder), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SeedProperty = DependencyProperty.Register(
            nameof(Seed), typeof(int), typeof(SketchBorder), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush? Background { get => (Brush?)GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
        public Brush? BorderBrush { get => (Brush?)GetValue(BorderBrushProperty); set => SetValue(BorderBrushProperty, value); }
        public double BorderThickness { get => (double)GetValue(BorderThicknessProperty); set => SetValue(BorderThicknessProperty, value); }
        public double CornerRadius { get => (double)GetValue(CornerRadiusProperty); set => SetValue(CornerRadiusProperty, value); }
        public Thickness Padding { get => (Thickness)GetValue(PaddingProperty); set => SetValue(PaddingProperty, value); }

        /// <summary>How far the outline wanders, in pixels. 0 = a clean rounded rectangle.</summary>
        public double Roughness { get => (double)GetValue(RoughnessProperty); set => SetValue(RoughnessProperty, value); }

        /// <summary>Fill with the logo's green marker hatching instead of Background (only when Roughness > 0).</summary>
        public bool Hatched { get => (bool)GetValue(HatchedProperty); set => SetValue(HatchedProperty, value); }

        /// <summary>Picks the wobble. 0 = a random one per control (kept for the control's lifetime).</summary>
        public int Seed { get => (int)GetValue(SeedProperty); set => SetValue(SeedProperty, value); }
        #endregion

        private readonly int _randomSeed = Random.Shared.Next(1, int.MaxValue);

        /// <summary>The logo's green marker hatching, for anything that wants it.</summary>
        public static readonly Brush HatchBrush = CreateHatchBrush();

        private static Brush CreateHatchBrush()
        {
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x34, 0xD7, 0x26)), null, new RectangleGeometry(new Rect(0, 0, 12, 12))));

            var bright = new Pen(new SolidColorBrush(Color.FromRgb(0x3C, 0xF0, 0x2A)), 4.2);
            var dark = new Pen(new SolidColorBrush(Color.FromRgb(0x27, 0xB5, 0x2B)), 1.4);

            // diagonal strokes, continued across the tile edges so the pattern joins up
            foreach (var (pen, offset) in new[] { (bright, 0.0), (dark, 6.0) })
            {
                for (int k = -1; k <= 1; k++)
                {
                    double shift = k * 12 + offset;
                    group.Children.Add(new GeometryDrawing(null, pen, new LineGeometry(new Point(shift - 2, 14), new Point(shift + 14, -2))));
                }
            }

            group.Freeze();

            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 12, 12),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };

            brush.Freeze();
            return brush;
        }

        #region Layout
        protected override Size MeasureOverride(Size constraint)
        {
            var padding = Padding;
            double extraW = padding.Left + padding.Right + BorderThickness * 2;
            double extraH = padding.Top + padding.Bottom + BorderThickness * 2;

            if (Child is null)
                return new Size(extraW, extraH);

            Child.Measure(new Size(Math.Max(0, constraint.Width - extraW), Math.Max(0, constraint.Height - extraH)));
            return new Size(Child.DesiredSize.Width + extraW, Child.DesiredSize.Height + extraH);
        }

        protected override Size ArrangeOverride(Size arrangeSize)
        {
            if (Child is not null)
            {
                var padding = Padding;
                double b = BorderThickness;
                Child.Arrange(new Rect(
                    padding.Left + b,
                    padding.Top + b,
                    Math.Max(0, arrangeSize.Width - padding.Left - padding.Right - b * 2),
                    Math.Max(0, arrangeSize.Height - padding.Top - padding.Bottom - b * 2)));
            }

            return arrangeSize;
        }
        #endregion

        #region Drawing
        protected override void OnRender(DrawingContext dc)
        {
            double thickness = BorderThickness;
            var rect = new Rect(RenderSize);

            if (rect.Width < 2 || rect.Height < 2)
                return;

            rect.Inflate(-thickness / 2, -thickness / 2);

            double radius = Math.Min(CornerRadius, Math.Min(rect.Width, rect.Height) / 2);
            double roughness = Roughness;
            Brush? fill = Hatched && roughness > 0 ? HatchBrush : Background;
            Pen? pen = BorderBrush is null || thickness <= 0 ? null : new Pen(BorderBrush, thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

            if (roughness <= 0)
            {
                dc.DrawRoundedRectangle(fill, pen, rect, radius, radius);
                return;
            }

            int seed = Seed != 0 ? Seed : _randomSeed;

            dc.DrawGeometry(fill, null, RoughRect(rect, radius, roughness * 0.6, seed, closed: true));

            if (pen is null)
                return;

            // main pass, then a thinner, lighter second pass that doesn't quite line up: that's what makes it look drawn
            dc.DrawGeometry(null, pen, RoughRect(rect, radius, roughness, seed, closed: false));

            var second = new Pen(BorderBrush, Math.Max(1, thickness * 0.55)) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.PushOpacity(0.75);
            dc.DrawGeometry(null, second, RoughRect(rect, radius * 1.15, roughness * 1.3, seed * 7 + 3, closed: false));
            dc.Pop();
        }

        /// <summary>
        /// A rounded rectangle walked as points, each nudged sideways a little, joined with smooth curves. When not
        /// closed the line overshoots its start a bit, like a marker stroke that doesn't stop exactly where it began.
        /// </summary>
        private static Geometry RoughRect(Rect rect, double radius, double roughness, int seed, bool closed)
        {
            var random = new Random(seed);
            double Jitter() => (random.NextDouble() * 2 - 1) * roughness;

            // corners get slightly different radii, like a hand-drawn box
            double[] radii = Enumerable.Range(0, 4).Select(_ => Math.Max(1, radius * (0.75 + random.NextDouble() * 0.6))).ToArray();
            radii = radii.Select(r => Math.Min(r, Math.Min(rect.Width, rect.Height) / 2)).ToArray();

            var points = new List<Point>();
            const double step = 22;

            void Side(Point from, Point to, Vector normal)
            {
                double length = (to - from).Length;
                int count = Math.Max(1, (int)(length / step));

                for (int i = 0; i < count; i++)
                {
                    var p = from + (to - from) * (i / (double)count);
                    points.Add(p + normal * Jitter());
                }
            }

            void Corner(Point center, double r, double startDeg)
            {
                for (int i = 0; i < 4; i++)
                {
                    double angle = (startDeg + i * 22.5) * Math.PI / 180;
                    points.Add(new Point(center.X + Math.Cos(angle) * r + Jitter() * 0.5, center.Y + Math.Sin(angle) * r + Jitter() * 0.5));
                }
            }

            double l = rect.Left, t = rect.Top, r2 = rect.Right, b = rect.Bottom;

            Side(new Point(l + radii[0], t), new Point(r2 - radii[1], t), new Vector(0, 1));
            Corner(new Point(r2 - radii[1], t + radii[1]), radii[1], 270);
            Side(new Point(r2, t + radii[1]), new Point(r2, b - radii[2]), new Vector(1, 0));
            Corner(new Point(r2 - radii[2], b - radii[2]), radii[2], 0);
            Side(new Point(r2 - radii[2], b), new Point(l + radii[3], b), new Vector(0, 1));
            Corner(new Point(l + radii[3], b - radii[3]), radii[3], 90);
            Side(new Point(l, b - radii[3]), new Point(l, t + radii[0]), new Vector(1, 0));
            Corner(new Point(l + radii[0], t + radii[0]), radii[0], 180);

            if (!closed && points.Count > 2)
            {
                // overshoot past the start
                points.Add(points[0]);
                points.Add(points[1] + new Vector(Jitter(), Jitter()));
            }

            var figure = new PathFigure { StartPoint = Mid(points[^1], points[0]), IsClosed = closed, IsFilled = closed };
            var segment = new PolyQuadraticBezierSegment();

            int last = closed ? points.Count : points.Count - 1;

            for (int i = 0; i < last; i++)
            {
                var current = points[i];
                var next = points[(i + 1) % points.Count];
                segment.Points.Add(current);
                segment.Points.Add(Mid(current, next));
            }

            figure.Segments.Add(segment);

            var geometry = new PathGeometry(new[] { figure });
            geometry.Freeze();
            return geometry;
        }

        private static Point Mid(Point a, Point b) => new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
        #endregion
    }
}
