using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Shell;

using Bloxstrap.UI.Elements.Bootstrapper.Base;

namespace Bloxstrap.UI.Elements.Bootstrapper
{
    /// <summary>
    /// PSYstrap's loading window. The logo artwork is split into its green body and its crown: the body gets coloured
    /// in by thick marker sweeps (an animated stroke used as an opacity mask), then the crown drops on with a bounce
    /// and a few sparkles pop. One loop is 5.5 seconds (Roblox usually starts in about 4). The status text and progress bar are the real ones.
    /// </summary>
    public partial class PsyDialog : IBootstrapperDialog
    {
        private static readonly TimeSpan Loop = TimeSpan.FromSeconds(5.5);

        // the sweeps, in the artwork's 500x500 space: back and forth from top to bottom, like colouring it in
        private const string SweepPath = "M 12 112 L 488 82 L 18 178 L 488 158 L 18 248 L 488 232 L 24 322 L 484 306 L 36 396 L 474 382 L 96 470 L 420 476";
        private const double SweepThickness = 96;

        // dash lengths are in multiples of the stroke thickness; the path is ~4900 px long, so 52 covers all of it
        private const double DashLength = 52;

        private readonly System.Windows.Shapes.Path _sweep;
        private bool _isClosing;
        private bool _indeterminate = true;
        private int _progressValue;
        private int _progressMaximum = 100;

        public Bloxstrap.Bootstrapper? Bootstrapper { get; set; }

        public PsyDialog()
        {
            InitializeComponent();

            Title = App.Settings.Prop.BootstrapperTitle;
            Icon = App.Settings.Prop.BootstrapperIcon.GetIcon().GetImageSource();

            // the mask: a white marker stroke drawn over a 500x500 canvas, shown through a VisualBrush
            _sweep = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(SweepPath),
                Stroke = Brushes.White,
                StrokeThickness = SweepThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeDashArray = new DoubleCollection { DashLength, 60 },
                StrokeDashOffset = DashLength
            };

            var maskCanvas = new Canvas { Width = 500, Height = 500, Background = Brushes.Transparent };
            maskCanvas.Children.Add(_sweep);

            BodyMasked.OpacityMask = new VisualBrush(maskCanvas)
            {
                Viewbox = new Rect(0, 0, 500, 500),
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.Fill
            };

            Loaded += (_, _) => StartAnimation();
            Track.SizeChanged += (_, _) => UpdateProgressBar();

            // known straight away, so the space is there from the start and the window doesn't jump when it loads
            long placeId = RobloxImages.PlaceIdFromLaunchArgs(App.LaunchSettings.RobloxLaunchArgs);

            if (placeId != 0)
            {
                GamePanel.Visibility = Visibility.Visible;
                _ = ShowGameAsync(placeId);
            }
        }

        /// <summary>The game's icon and name under the logo (and the icon on the taskbar), once they've downloaded.</summary>
        private async Task ShowGameAsync(long placeId)
        {
            try
            {
                var iconTask = RobloxImages.GameIconAsync(placeId);
                var nameTask = GameProfiles.GetGameNameAsync(placeId);

                string? iconPath = await iconTask;
                string? name = await nameTask;

                var icon = RobloxImages.Load(iconPath, 128);

                if (icon is null && name is null)
                    return;

                // the dialog may not have its dispatcher running yet when this started, so hop back explicitly
                OnUi(() =>
                {
                    if (icon is not null)
                    {
                        GameIconBrush.ImageSource = icon;
                        Icon = icon;
                    }

                    GameNameText.Text = name ?? "Roblox game";
                    GamePanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)));
                });
            }
            catch (Exception ex)
            {
                App.Logger.WriteException("PsyDialog::ShowGame", ex);
            }
        }

        #region Animation
        private void StartAnimation()
        {
            if (!SystemParameters.ClientAreaAnimation)
            {
                // reduced motion: just show the finished logo
                _sweep.StrokeDashOffset = 0;
                BodySolid.Opacity = 1;
                Crown.Opacity = 1;
                return;
            }

            var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
            var bounce = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 };

            // colour it in
            Animate(_sweep, Shape.StrokeDashOffsetProperty, ease, (0, DashLength), (0.46, 0), (1, 0));

            // then make it solid, so no gaps are left between sweeps
            Animate(BodySolid, OpacityProperty, null, (0, 0), (0.42, 0), (0.50, 1), (1, 1));

            // the crown drops on
            Animate(Crown, OpacityProperty, null, (0, 0), (0.47, 0), (0.53, 1), (1, 1));
            Animate(CrownDrop, TranslateTransform.YProperty, bounce, (0, -190), (0.47, -190), (0.56, 10), (0.62, -6), (0.68, 0), (1, 0));
            Animate(CrownTilt, RotateTransform.AngleProperty, ease, (0, -14), (0.47, -14), (0.56, 5), (0.62, -2), (0.68, 0), (1, 0));

            // sparkles when it lands
            double delay = 0;
            foreach (var spark in new[] { Spark1, Spark2, Spark3 })
            {
                Animate(spark, OpacityProperty, null, (0, 0), (0.62 + delay, 0), (0.68 + delay, 1), (0.86, 1), (0.93, 0), (1, 0));

                var scale = (ScaleTransform)spark.RenderTransform;
                var pop = new[] { (0.0, 0.2), (0.62 + delay, 0.2), (0.68 + delay, 1.15), (0.75 + delay, 1.0), (0.86, 1.0), (0.93, 0.6), (1.0, 0.6) };
                Animate(scale, ScaleTransform.ScaleXProperty, ease, pop);
                Animate(scale, ScaleTransform.ScaleYProperty, ease, pop);

                delay += 0.017;
            }

            // hold, fade out, start again
            Animate(Layer, OpacityProperty, null, (0, 1), (0.88, 1), (0.96, 0), (1, 0));
        }

        /// <summary>One looping key-frame animation; times are fractions of the loop.</summary>
        private static void Animate(Animatable target, DependencyProperty property, IEasingFunction? ease, params (double At, double Value)[] frames)
        {
            var animation = new DoubleAnimationUsingKeyFrames { Duration = Loop, RepeatBehavior = RepeatBehavior.Forever };

            foreach (var (at, value) in frames)
                animation.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(Loop.TotalMilliseconds * at)), ease));

            target.BeginAnimation(property, animation);
        }

        private static void Animate(UIElement target, DependencyProperty property, IEasingFunction? ease, params (double At, double Value)[] frames)
        {
            var animation = new DoubleAnimationUsingKeyFrames { Duration = Loop, RepeatBehavior = RepeatBehavior.Forever };

            foreach (var (at, value) in frames)
                animation.KeyFrames.Add(new EasingDoubleKeyFrame(value, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(Loop.TotalMilliseconds * at)), ease));

            target.BeginAnimation(property, animation);
        }
        #endregion

        #region Progress bar
        private void UpdateProgressBar()
        {
            double width = Track.ActualWidth;

            if (width <= 0)
                return;

            if (_indeterminate)
            {
                // a hatched chunk sliding back and forth
                Fill.Width = width * 0.3;

                var slide = new TranslateTransform();
                Fill.RenderTransform = slide;
                slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, width * 0.7, TimeSpan.FromSeconds(1.1))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
                });
            }
            else
            {
                Fill.RenderTransform = Transform.Identity;
                double fraction = _progressMaximum <= 0 ? 0 : Math.Clamp(_progressValue / (double)_progressMaximum, 0, 1);
                Fill.Width = width * fraction;
            }
        }
        #endregion

        #region IBootstrapperDialog
        private void OnUi(Action action)
        {
            if (Dispatcher.CheckAccess())
                action();
            else
                Dispatcher.Invoke(action);
        }

        public string Message
        {
            get => Dispatcher.Invoke(() => MessageText.Text);
            set => OnUi(() => MessageText.Text = value);
        }

        public ProgressBarStyle ProgressStyle
        {
            get => _indeterminate ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
            set
            {
                bool indeterminate = value == ProgressBarStyle.Marquee;

                if (indeterminate == _indeterminate)
                    return;

                _indeterminate = indeterminate;
                OnUi(UpdateProgressBar);
            }
        }

        public int ProgressMaximum
        {
            get => _progressMaximum;
            set { _progressMaximum = value; OnUi(UpdateProgressBar); }
        }

        public int ProgressValue
        {
            get => _progressValue;
            set { _progressValue = value; OnUi(UpdateProgressBar); }
        }

        public TaskbarItemProgressState TaskbarProgressState
        {
            get => Dispatcher.Invoke(() => TaskbarInfo.ProgressState);
            set => OnUi(() => TaskbarInfo.ProgressState = value);
        }

        public double TaskbarProgressValue
        {
            get => Dispatcher.Invoke(() => TaskbarInfo.ProgressValue);
            set => OnUi(() => TaskbarInfo.ProgressValue = value);
        }

        public bool CancelEnabled
        {
            get => Dispatcher.Invoke(() => CancelButton.IsEnabled);
            set => OnUi(() =>
            {
                CancelButton.IsEnabled = value;
                CancelButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            });
        }

        public void ShowBootstrapper() => ShowDialog();

        public void CloseBootstrapper()
        {
            _isClosing = true;
            Dispatcher.BeginInvoke(Close);
        }

        public void ShowSuccess(string message, Action? callback) => BaseFunctions.ShowSuccess(message, callback);
        #endregion

        private void CancelButton_Click(object sender, RoutedEventArgs e) => Bootstrapper?.Cancel();

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            if (!_isClosing)
                Bootstrapper?.Cancel();
        }
    }
}
