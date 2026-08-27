using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using FfxTool.Core;

namespace FfxTool.Gui
{
    /// <summary>Common surface MainWindow uses to drive the active page.</summary>
    public interface ISection
    {
        void OpenFile();
        void OnShown();
    }

    public partial class MainWindow : Window
    {
        // ---------- true rounded window region (Win7-safe) ----------
        // The HWND itself is always a rectangle; no amount of WPF-side
        // clipping changes what the OS paints AROUND our content, so on a
        // patterned desktop the square corner pixels used to peek out past
        // the rounded silhouette (the artifact from the latest screenshots).
        // SetWindowRgn makes the window genuinely non-rectangular at the OS
        // level — the standard skinned-app technique of that era — while the
        // WPF-side rim + clip keep drawing the anti-aliased edge just inside
        // it. The region is re-applied on every size/state change and is
        // removed entirely while maximized. Best-effort by design: if it ever
        // fails, the window simply falls back to square corners.
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);

        // GetWindowLong/SetWindowLong need the *Ptr variants on x64; every
        // 64-bit Windows still exports the old names, so fall back to them.
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern IntPtr GetWindowLong32(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern IntPtr SetWindowLong32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        private static IntPtr GetWindowExStyle(IntPtr hwnd)
        {
            const int GWL_EXSTYLE = -20;
            return IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, GWL_EXSTYLE)
                                    : GetWindowLong32(hwnd, GWL_EXSTYLE);
        }

        private static IntPtr SetWindowExStyle(IntPtr hwnd, IntPtr style)
        {
            const int GWL_EXSTYLE = -20;
            return IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, GWL_EXSTYLE, style)
                                    : SetWindowLong32(hwnd, GWL_EXSTYLE, style);
        }

        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_WINDOWPOSCHANGED = 0x0047;
        private const int WM_ENTERSIZEMOVE = 0x0231;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private const uint SWP_NOSIZE = 0x0001;
        private const int WS_EX_COMPOSITED = 0x02000000;

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        private HwndSource _source;
        // last size the rounded region was applied for — SetWindowRgn costs
        // a full-window invalidate, so skip redundant re-cuts
        private int _lastRgnW = -1, _lastRgnH = -1;
        // true while the user is dragging the window border (between
        // WM_ENTERSIZEMOVE and WM_EXITSIZEMOVE) — inside that loop the
        // region is re-cut WITHOUT forcing a repaint, see WndProc
        private bool _liveResize;
        // live-resize freeze frame: true while the drag shows the captured
        // bitmap and the content tree is collapsed (EngageResizeFreeze)
        private bool _freezeActive;

        private IntPtr _hwnd;
        private readonly PluginProfile _profile;
        private readonly ConvertPage _convert;
        private readonly ListerPage _lister;
        private readonly ProfilePage _profilePage;
        private readonly SettingsPage _settings;

        public MainWindow()
        {
            InitializeComponent();
            LoadWindowBounds();

            // taskbar / Alt-Tab icon (title bar is custom chrome, so this is
            // where the brand actually shows). Cosmetic — never block startup.
            try
            {
                Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico"));
            }
            catch { /* missing asset falls back to the exe icon */ }

            _profile = PluginProfile.Load();

            _lister = new ListerPage(_profile);
            _profilePage = new ProfilePage(_profile, OnProfileChanged);
            _convert = new ConvertPage(_profile);
            _settings = new SettingsPage(_profilePage);

            // Convert-first: it's the main thing this tool does.
            Rail.AddItem("Convert", "SwapHoriz", "Convert Preset · Ctrl+1");
            Rail.AddItem("Effect Lister", "List", "Effect Lister · Ctrl+2");
            Rail.AddItem("Settings", "Settings", "Settings · Ctrl+3");
            Rail.SelectionChanged += i => ShowSection(i);
            // the + is the upload button: jump to Convert and pick a file
            Rail.FabClicked += () =>
            {
                Rail.Select(0);
                _convert.OpenFile();
            };

            MinBtn.Click += (s, e) => WindowState = WindowState.Minimized;
            MaxBtn.Click += (s, e) => ToggleMaximize();
            CloseBtn.Click += (s, e) => Close();
            StateChanged += (s, e) => ApplyChromeState();
            // keep the rounded silhouette glued to the window while resizing
            WindowRoot.SizeChanged += (s, e) => UpdateWindowClip();
            // DPI can flip at runtime (drag between monitors); both the clip
            // and the device-pixel region must follow it
            DpiChanged += (s, e) => UpdateWindowClip();
            // cache the HWND for the region calls as soon as it exists, and
            // re-apply once the first render has settled — the chrome worker
            // and the DWM both touch window geometry during startup, so the
            // last writer (us) has to come after them
            SourceInitialized += (s, e) =>
            {
                _hwnd = new WindowInteropHelper(this).Handle;
                _source = HwndSource.FromHwnd(_hwnd);
                _source?.AddHook(WndProc);
                EnableComposited();
            };
            ContentRendered += (s, e) => UpdateWindowClip();
            // inactive windows dim their title, like a native caption does;
            // activation is also a cheap defensive moment to re-assert the
            // rounded region in case anything in the system cleared it
            Activated += (s, e) =>
            {
                TitleBrand.Opacity = 1;
                UpdateWindowClip();
            };
            Deactivated += (s, e) => TitleBrand.Opacity = 0.55;

            ShowSection(0);
            ApplyChromeState(); // first paint: rim + rounded corners
        }

        private ISection ActiveSection()
        {
            switch (Rail.SelectedIndex)
            {
                case 0: return _convert;
                case 1: return _lister;
                default: return null;
            }
        }

        private void ShowSection(int index)
        {
            switch (index)
            {
                case 0: PageHost.Content = _convert; break;
                case 1: PageHost.Content = _lister; break;
                case 2: PageHost.Content = _settings; break;
            }
            AnimateSectionIn();
            (PageHost.Content as ISection)?.OnShown();
        }

        /// <summary>
        /// Quiet fade + 12px rise on every section switch. Deliberately
        /// subtle (~1/5 s, decelerating) so it reads as material settling,
        /// not as an animation showcase.
        /// </summary>
        private void AnimateSectionIn()
        {
            if (PageHost.Content is UIElement page)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var slide = new TranslateTransform(0, 12);
                page.RenderTransform = slide;
                var dur = TimeSpan.FromMilliseconds(220);
                page.BeginAnimation(UIElement.OpacityProperty,
                    new DoubleAnimation(0, 1, dur) { EasingFunction = ease });
                slide.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(12, 0, dur) { EasingFunction = ease });
            }
        }

        private void OnProfileChanged()
        {
            _convert.OnProfileChanged();
            _lister.OnProfileChanged();
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            base.OnPreviewKeyDown(e);
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
            {
                ActiveSection()?.OpenFile();
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key >= Key.D1 && e.Key <= Key.D3)
            {
                int index = (int)e.Key - (int)Key.D1;
                Rail.SelectWithoutNotify(index);
                ShowSection(index);
                e.Handled = true;
            }
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized; // bounds handled by ApplyChromeState
        }

        /// <summary>
        /// Keeps the custom window overlay correct in every state. Windows
        /// maximizes a borderless-chrome window a few pixels BEYOND the work
        /// area on each side (the invisible resize border hangs off-screen),
        /// so while maximized we square the corners, drop the rim and pad
        /// the content back into the visible area — otherwise the outer 8px
        /// of the app silently disappear at the screen edges. Windowed: an
        /// anti-aliased 1px Outline rim traces the rounded silhouette over
        /// every child (zero transparency, Win7-safe).
        /// </summary>
        private void ApplyChromeState()
        {
            // a state flip (maximize / restore / aero-snap) ends any frozen
            // drag — the live tree must be back before paddings change
            if (WindowState != WindowState.Normal && _freezeActive)
                DisengageResizeFreeze();
            bool max = WindowState == WindowState.Maximized;
            WindowRoot.CornerRadius = max ? new CornerRadius(0) : new CornerRadius(8);
            WindowRoot.Padding = max ? new Thickness(8) : new Thickness(0);
            WindowRim.Visibility = max ? Visibility.Collapsed : Visibility.Visible;
            WindowRim.CornerRadius = max ? new CornerRadius(0) : new CornerRadius(7.5);
            MaxIcon.IconName = max ? "Restore" : "Maximize";
            MaxBtn.ToolTip = max ? "Restore" : "Maximize";
            UpdateWindowClip();
        }

        /// <summary>
        /// Border.CornerRadius rounds the window's own background but not its
        /// children — the nav rail and the page surfaces are rectangles, and
        /// unclipped they would poke square corners through the rounded
        /// silhouette. The clip cuts the whole subtree to match; the rim
        /// overlay then strokes the same curve anti-aliased on top, and the
        /// OS-level window region (ApplyWindowRegion) removes the corner
        /// pixels the HWND would otherwise paint past it. Maximized windows
        /// go back to a full square.
        /// </summary>
        private void UpdateWindowClip()
        {
            bool max = WindowState == WindowState.Maximized;
            if (max)
            {
                WindowRoot.Clip = null;
            }
            else
            {
                WindowRoot.Clip = new RectangleGeometry(
                    new Rect(0, 0, WindowRoot.ActualWidth, WindowRoot.ActualHeight), 8, 8);
            }
            ApplyWindowRegion(max);
        }

        private void ApplyWindowRegion(bool maximized)
        {
            try
            {
                IntPtr hwnd = _hwnd != IntPtr.Zero ? _hwnd : new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return; // window not created yet

                if (maximized)
                {
                    SetWindowRgn(hwnd, IntPtr.Zero, true); // square again
                    _lastRgnW = -1; // force a fresh cut on the next restore
                    return;
                }

                // device pixels, not DIUs
                var source = PresentationSource.FromVisual(this);
                var dpi = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
                int w = (int)Math.Ceiling(ActualWidth * dpi.M11);
                int h = (int)Math.Ceiling(ActualHeight * dpi.M22);
                ApplyWindowRegionSize(w, h);
            }
            catch
            {
                // cosmetic — worst case the corners stay square
            }
        }

        /// <summary>
        /// Cuts the rounded region to an explicit DEVICE-pixel size. Called
        /// from both the WPF layout path (above) and the WndProc resize path
        /// (below); the size dedupe keeps whichever path runs first from
        /// paying for SetWindowRgn twice. `redraw` is FALSE inside a live
        /// border drag — see WndProc for why that makes resizing smooth.
        /// </summary>
        private void ApplyWindowRegionSize(int w, int h, bool redraw = true)
        {
            try
            {
                IntPtr hwnd = _hwnd != IntPtr.Zero ? _hwnd : new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;
                if (w == _lastRgnW && h == _lastRgnH) return; // already cut to this size
                _lastRgnW = w; _lastRgnH = h;

                var source = PresentationSource.FromVisual(this);
                var dpi = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
                int diameter = Math.Max(2, (int)Math.Round(8 * dpi.M11) * 2); // ellipse size, not radius
                IntPtr rgn = CreateRoundRectRgn(0, 0, w + 1, h + 1, diameter, diameter);
                // ownership: after a successful SetWindowRgn the system owns
                // (and frees) the region — deliberately not deleted here
                SetWindowRgn(hwnd, rgn, redraw);
            }
            catch
            {
                // cosmetic — worst case the corners stay square
            }
        }

        /// <summary>
        /// Live-resize "freeze frame" — the decisive Win7 smoothness
        /// measure. Weak-GPU (or plain software-rendered) Win7 machines
        /// repaint the whole window for every pixel of a border drag, and
        /// a content-heavy page like the Lister turns that into visible
        /// rubber-banding. So the moment a drag produces its first real
        /// size change, the last fully rendered frame is captured into a
        /// frozen bitmap, the real content tree is COLLAPSED (no layout,
        /// no render cost at all) and the bitmap is stretched to follow
        /// the window — the same "stretch the last frame" behaviour the
        /// DWM itself uses for glass frames. One snapshot per drag, zero
        /// per-pixel work; WM_EXITSIZEMOVE brings the live tree back for
        /// one final exact layout (and the Lister's graph redraws through
        /// its coalescing timer). If the snapshot fails for any reason
        /// the content is restored immediately — the worst case is the
        /// previous per-pixel behaviour, never a blank window.
        /// </summary>
        private void EngageResizeFreeze()
        {
            if (_freezeActive || WindowRoot == null) return;
            try
            {
                if (WindowRoot.ActualWidth < 2 || WindowRoot.ActualHeight < 2) return;
                var source = PresentationSource.FromVisual(this);
                var dpi = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
                int pw = Math.Max(1, (int)Math.Ceiling(WindowRoot.ActualWidth * dpi.M11));
                int ph = Math.Max(1, (int)Math.Ceiling(WindowRoot.ActualHeight * dpi.M22));
                var rtb = new RenderTargetBitmap(pw, ph, 96 * dpi.M11, 96 * dpi.M22, PixelFormats.Pbgra32);
                rtb.Render(WindowRoot);
                rtb.Freeze();
                FreezeLayer.Source = rtb;
                FreezeLayer.Visibility = Visibility.Visible;
                ChromeGrid.Visibility = Visibility.Collapsed;
                BodyGrid.Visibility = Visibility.Collapsed;
                _freezeActive = true;
            }
            catch
            {
                DisengageResizeFreeze(); // never trade content for a failed snapshot
            }
        }

        /// <summary>Ends the freeze frame and restores the live tree.</summary>
        private void DisengageResizeFreeze()
        {
            _freezeActive = false;
            if (FreezeLayer != null)
            {
                FreezeLayer.Source = null;
                FreezeLayer.Visibility = Visibility.Collapsed;
            }
            if (ChromeGrid != null) ChromeGrid.Visibility = Visibility.Visible;
            if (BodyGrid != null) BodyGrid.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Kills the "window goes black while resizing" artifact on Win7 —
        /// and, since the region went in, makes the resize SMOOTH. Stack of
        /// era-correct measures:
        ///
        /// 1. WM_WINDOWPOSCHANGED — re-cut the rounded region to the NEW
        ///    size the instant the OS applies it. Waiting for WPF's
        ///    SizeChanged left a 1-2 frame gap where the window was bigger
        ///    than its region, and pixels outside a window region render
        ///    black — that gap WAS the flash. WINDOWPOS.cx/cy arrive in
        ///    device pixels, so this path needs no DPI conversion.
        ///    INSIDE a live border drag the re-cut passes fRedraw=FALSE:
        ///    SetWindowRgn with TRUE forces a full-window invalidate EVERY
        ///    step, on top of the repaints the resize loop does anyway —
        ///    that double invalidation was the visible jank. With FALSE the
        ///    region still clips correctly (no black corners, the mask is
        ///    applied immediately) and the sizing loop's own WM_PAINT for
        ///    the newly exposed areas repaints everything; a final forced
        ///    cut happens on WM_EXITSIZEMOVE for a clean settle.
        /// 2. WM_ERASEBKGND — answer "erased" without touching anything.
        ///    The old pixels stay on screen until WPF paints the new frame
        ///    instead of flashing an empty background between frames.
        /// 3. WS_EX_COMPOSITED — bottom-up double-buffered painting of the
        ///    whole window subtree; silences child-repaint flicker on
        ///    DWM-off (Win7 Basic) machines where there is no compositor
        ///    to hide intermediate frames.
        /// </summary>
        private void EnableComposited()
        {
            try
            {
                if (_hwnd == IntPtr.Zero) return;
                IntPtr ex = GetWindowExStyle(_hwnd);
                if (((long)ex & WS_EX_COMPOSITED) == 0)
                    SetWindowExStyle(_hwnd, (IntPtr)((long)ex | WS_EX_COMPOSITED));
            }
            catch { /* cosmetic */ }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_ERASEBKGND)
            {
                handled = true;
                return new IntPtr(1); // "already erased" — keeps old pixels
            }

            if (msg == WM_ENTERSIZEMOVE)
            {
                _liveResize = true; // border drag / resize loop begins
            }
            else if (msg == WM_EXITSIZEMOVE)
            {
                _liveResize = false;
                DisengageResizeFreeze(); // live tree back for the final exact layout
                // settle: one forced repaint with the region already synced
                // (the dedupe skips the cut if the size did not change)
                if (WindowState != WindowState.Minimized && WindowState != WindowState.Maximized)
                {
                    var source = PresentationSource.FromVisual(this);
                    var dpi = source?.CompositionTarget?.TransformToDevice ?? Matrix.Identity;
                    ApplyWindowRegionSize(
                        (int)Math.Ceiling(ActualWidth * dpi.M11),
                        (int)Math.Ceiling(ActualHeight * dpi.M22), true);
                }
            }
            else if (msg == WM_WINDOWPOSCHANGED)
            {
                // fire-and-forget safety: never fight the genie animation
                if (WindowState != WindowState.Minimized)
                {
                    var wp = (WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(WINDOWPOS));
                    if ((wp.flags & SWP_NOSIZE) == 0 && wp.cx > 0 && wp.cy > 0)
                    {
                        bool max = WindowState == WindowState.Maximized;
                        if (max)
                        {
                            if (_lastRgnW != -1)
                            {
                                try { SetWindowRgn(hwnd, IntPtr.Zero, true); } catch { }
                                _lastRgnW = -1;
                            }
                        }
                        else
                        {
                            // first real size change of a border drag: switch
                            // to the freeze frame so the drag stays smooth
                            if (_liveResize && WindowState == WindowState.Normal)
                                EngageResizeFreeze();
                            // inside the drag: keep the mask in sync but let
                            // the sizing loop's own paints cover the pixels
                            ApplyWindowRegionSize(wp.cx, wp.cy, !_liveResize);
                        }
                    }
                }
            }

            return IntPtr.Zero;
        }

        // ---------- window bounds persistence (window.json) ----------
        [DataContract(Namespace = "")]
        private class StoredBounds
        {
            [DataMember] public double X;
            [DataMember] public double Y;
            [DataMember] public double Width;
            [DataMember] public double Height;
            [DataMember] public bool Maximized;
        }

        private string BoundsPath()
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "FFXCompatibilityTool");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "window.json");
        }

        private void LoadWindowBounds()
        {
            try
            {
                if (!File.Exists(BoundsPath())) return;
                StoredBounds b;
                var serializer = new DataContractJsonSerializer(typeof(StoredBounds));
                using (var fs = File.OpenRead(BoundsPath()))
                    b = serializer.ReadObject(fs) as StoredBounds;
                if (b == null) return;

                if (b.Width >= MinWidth && b.Height >= MinHeight)
                {
                    Width = b.Width;
                    Height = b.Height;
                }
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = b.X;
                Top = b.Y;
                if (b.Maximized) WindowState = WindowState.Maximized;
                // make sure a saved off-screen position can't strand the window
                if (Left < -Width + 100 || Left > SystemParameters.VirtualScreenWidth - 100)
                    WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            catch { /* defaults */ }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                var b = new StoredBounds
                {
                    X = RestoreBounds.Left,
                    Y = RestoreBounds.Top,
                    Width = RestoreBounds.Width,
                    Height = RestoreBounds.Height,
                    Maximized = WindowState == WindowState.Maximized
                };
                var serializer = new DataContractJsonSerializer(typeof(StoredBounds));
                using (var fs = File.Create(BoundsPath()))
                    serializer.WriteObject(fs, b);
            }
            catch { /* best-effort */ }
            base.OnClosing(e);
        }
    }
}
