using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace DeskMate
{
    public sealed partial class PetWindow : Window
    {
        private readonly Studio app;
        private readonly Image sprite = new Image();
        public Image LiveSprite { get { return sprite; } }
        private readonly TextBlock words = new TextBlock();
        private readonly Border bubble = new Border();
        private readonly Forms.NotifyIcon tray = new Forms.NotifyIcon();
        private readonly Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Random random = new Random();
        private readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background);
        private readonly Dictionary<string, BitmapSource[][]> art = new Dictionary<string, BitmapSource[][]>();
        private BitmapSource[][] frames;
        private Drawing.Icon trayIcon;
        private IntPtr handle;
        private Forms.ToolStripMenuItem visibilityItem, quietItem, topItem;
        private bool manuallyHidden, fullscreenHidden, pressing, dragging, disposed;
        private NativePoint pressPoint, pressWindow;
        private long effectUntil, speechUntil, ambientAt, shellCheckAt, pendingClickAt, nearbySince;
        private long shownSince;
        private int row, currentRow = -1, currentFrame = -1;
        private string character;
        private double scale;
        private VisitScene visitor;
        private string noticePage = "notes";
        private const int Idle = 0, Right = 1, Leftward = 2, Wave = 3, Jump = 4, Miss = 5, Wait = 6, Work = 7, Review = 8;
        private static readonly int[] Counts = { 6, 8, 8, 4, 5, 8, 6, 6, 6, 8, 8 };
        private static readonly int[] FrameMs = { 360, 100, 100, 205, 142, 170, 265, 240, 250 };

        public PetWindow(Studio app)
        {
            this.app = app;
            Title = "桌面伙伴";
            AllowsTransparency = true;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            UseLayoutRounding = true;
            var panel = new Grid();
            Content = panel;
            sprite.Stretch = Stretch.Uniform;
            sprite.HorizontalAlignment = HorizontalAlignment.Center;
            sprite.VerticalAlignment = VerticalAlignment.Bottom;
            sprite.Cursor = Cursors.Hand;
            RenderOptions.SetBitmapScalingMode(sprite, BitmapScalingMode.HighQuality);
            sprite.ToolTip = "轻点打招呼 · 拖动换位置 · 右键或双击选工具";
            ToolTipService.SetInitialShowDelay(sprite, 1000);
            panel.Children.Add(sprite);

            bubble.Background = new SolidColorBrush(Color.FromRgb(255, 252, 244));
            bubble.BorderBrush = new SolidColorBrush(Color.FromRgb(185, 172, 148));
            bubble.BorderThickness = new Thickness(1);
            bubble.Padding = new Thickness(12, 7, 12, 7);
            bubble.CornerRadius = new CornerRadius(13, 13, 13, 3);
            bubble.VerticalAlignment = VerticalAlignment.Bottom;
            bubble.HorizontalAlignment = HorizontalAlignment.Center;
            bubble.IsHitTestVisible = false;
            bubble.Visibility = Visibility.Collapsed;
            words.FontFamily = new FontFamily("Microsoft YaHei UI");
            words.FontSize = 12;
            words.Foreground = new SolidColorBrush(Color.FromRgb(47, 60, 51));
            words.TextWrapping = TextWrapping.Wrap;
            words.TextTrimming = TextTrimming.CharacterEllipsis;
            words.MaxHeight = 60;
            bubble.Child = words;
            panel.Children.Add(bubble);

            BuildMenu();
            sprite.MouseLeftButtonDown += OnPress;
            sprite.MouseMove += OnMove;
            sprite.MouseLeftButtonUp += OnRelease;
            sprite.LostMouseCapture += delegate { pressing = dragging = false; };
            sprite.MouseRightButtonUp += delegate(object sender, MouseButtonEventArgs args) {
                pendingClickAt = 0;
                OpenQuickMenu(true);
                args.Handled = true;
            };
            SourceInitialized += delegate {
                handle = new WindowInteropHelper(this).Handle;
                long style = GetStyle(handle, -20);
                SetStyle(handle, -20, (style | 0x08000000L | 0x80L) & ~0x40000L);
            };
            Loaded += delegate { PlaceSaved(); };
            InstallKaraokeTracking();
            Closed += delegate { DisposeTray(); };
            timer.Interval = TimeSpan.FromMilliseconds(80);
            timer.Tick += Tick;
            RefreshRoutinePace();
            Sync();
            tray.Visible = true;
            timer.Start();
        }

        ContextMenu quickMenu;
        static readonly string[][] MoreTools={
            new[]{"小屋","room"},new[]{"礼物","gift"},new[]{"随身背包","bag"},new[]{"生活手记","journal"}
        };
        void BuildMenu()
        {
            menu.ShowImageMargin=false;menu.Font=new Drawing.Font("Microsoft YaHei UI",9);
            AddTray("番茄钟",delegate{app.Open("focus");});
            AddTray("听音乐",delegate{app.Open("music");});
            AddTray("便签",delegate{app.Open("notes");});
            var more=new Forms.ToolStripMenuItem("更多");menu.Items.Add(more);
            foreach(var entry in MoreTools){string key=entry[1];more.DropDownItems.Add(entry[0],null,delegate{app.Open(key);});}
            menu.Items.Add(new Forms.ToolStripSeparator());
            AddTray("伙伴设置",delegate{app.Open("settings");});
            var display=new Forms.ToolStripMenuItem("桌面");menu.Items.Add(display);
            quietItem=new Forms.ToolStripMenuItem("安静模式",null,delegate{ToggleQuiet();});
            topItem=new Forms.ToolStripMenuItem("保持置顶",null,delegate{ToggleTopmost();});
            display.DropDownItems.Add(quietItem);display.DropDownItems.Add(topItem);
            display.DropDownItems.Add("回到屏幕右下角",null,delegate{Reveal();PlaceDefault();RememberPosition();});
            visibilityItem=AddTray("收起桌宠",ToggleVisibility);
            AddTray("退出",delegate{app.Quit();});
            menu.Opening+=delegate{UpdateMenu();};tray.ContextMenuStrip=menu;
            tray.MouseDoubleClick+=delegate(object sender,Forms.MouseEventArgs e){if(e.Button==Forms.MouseButtons.Left)ShowQuickMenu();};
            tray.BalloonTipClicked+=delegate{app.Open(noticePage);};
        }
        Forms.ToolStripMenuItem AddTray(string label,Action click)
        {
            var item=new Forms.ToolStripMenuItem(label);item.Click+=delegate{click();};menu.Items.Add(item);return item;
        }
        public void ShowQuickMenu()
        {
            if(disposed || app.Exiting)return;
            Reveal();
            if(!IsLoaded){Dispatcher.BeginInvoke((Action)delegate{if(!disposed)OpenQuickMenu(false);},DispatcherPriority.Loaded);return;}
            OpenQuickMenu(false);
        }
        void OpenQuickMenu(bool atPointer)
        {
            if(quickMenu!=null)quickMenu.IsOpen=false;
            quickMenu=CreateOverlayMenu();quickMenu.PlacementTarget=sprite;
            quickMenu.Placement=atPointer?System.Windows.Controls.Primitives.PlacementMode.MousePoint:System.Windows.Controls.Primitives.PlacementMode.Left;
            quickMenu.IsOpen=true;
        }
        ContextMenu CreateOverlayMenu()
        {
            var popup=new ContextMenu{FontFamily=words.FontFamily,FontSize=13,MinWidth=125};
            AddOverlay(popup,"番茄钟",delegate{app.Open("focus");});
            AddOverlay(popup,"听音乐",delegate{app.Open("music");});
            AddOverlay(popup,"便签",delegate{app.Open("notes");});
            var more=new MenuItem{Header="更多"};popup.Items.Add(more);
            foreach(var entry in MoreTools){string key=entry[1];AddOverlay(more,entry[0],delegate{app.Open(key);});}
            popup.Items.Add(new Separator());
            AddOverlay(popup,"伙伴设置",delegate{app.Open("settings");});
            AddOverlay(popup,"收起桌宠",ToggleVisibility);
            AddOverlay(popup,"退出",delegate{app.Quit();});
            return popup;
        }
        static void AddOverlay(ItemsControl owner,string label,Action action)
        {
            var item=new MenuItem{Header=label};
            string glyph=label=="番茄钟"?"\uE916":label=="听音乐"?"\uE8D6":label=="便签"?"\uE70B":label=="伙伴设置"?"\uE713":null;
            if(glyph!=null)item.Icon=Ui.Icon(glyph,13,Ui.Green);
            item.Click+=delegate{action();};owner.Items.Add(item);
        }
        private void UpdateMenu()
        {
            visibilityItem.Text = manuallyHidden || fullscreenHidden ? "显示桌宠" : "收起桌宠";
            quietItem.Checked = app.Data.Quiet;
            topItem.Checked = app.Data.AlwaysOnTop;
        }

        private void ToggleQuiet() { app.Data.Quiet = !app.Data.Quiet; app.Save(); Sync(); }
        private void ToggleTopmost() { app.Data.AlwaysOnTop = !app.Data.AlwaysOnTop; app.Save(); Sync(); }

        private void ToggleVisibility()
        {
            if (manuallyHidden || fullscreenHidden) { Reveal(); return; }
            manuallyHidden = true;
            pendingClickAt = 0;
            EndVisit();
            Hide();
        }

        public void Reveal()
        {
            manuallyHidden = fullscreenHidden = false;
            if (!IsVisible) Show();
            if (handle != IntPtr.Zero) ClampCurrent();
        }

        public void Sync()
        {
            Topmost = app.Data.AlwaysOnTop;
            var newScale = app.Data.PetScale;
            if (double.IsNaN(newScale) || double.IsInfinity(newScale)) newScale = 1;
            newScale = Math.Max(.6, Math.Min(2.0, newScale));
            if (scale != newScale)
            {
                EndVisit();
                scale = newScale;
                sprite.Width = 152 * scale;
                sprite.Height = sprite.Width * 208 / 192;
                Width = Math.Max(sprite.Width + 24, 222);
                Height = sprite.Height + 84;
                bubble.MaxWidth = Width - 14;
                bubble.Margin = new Thickness(0, 0, 0, sprite.Height - 3);
                if (handle != IntPtr.Zero) Dispatcher.BeginInvoke((Action)ClampCurrent, DispatcherPriority.Loaded);
            }
            string wanted = app.Data.Character;
            if (character != wanted)
            {
                EndVisit();
                StopPerformance();
                ReleaseCharacterActions(character);
                if (character != null && character != "rem" && character != "kurisu") art.Remove(character);
                character = wanted;
                frames = LoadArt(wanted);
                currentFrame = currentRow = -1;
                row = Idle;
                effectUntil = 0;
                bubble.Visibility = Visibility.Collapsed;
                DrawFrame(Idle, 0);
                SetTrayIcon();
            }
            tray.Text = app.CharacterName + " · 桌面伙伴";
            if (visitor != null) visitor.Layer.Topmost = Topmost;
            if (app.Data.Quiet) bubble.Visibility = Visibility.Collapsed;
            if(app.Data.Quiet){EndIndependentVisit();StopPerformance();}
            if(schedulePace!=app.Data.ActivityPace)RefreshRoutinePace();
            if (!app.Data.HideFullscreen && fullscreenHidden)
            {
                fullscreenHidden = false;
                if (!manuallyHidden) Show();
            }
            UpdateMenu();
            RefreshKaraokeOverlay();
        }

        internal BitmapSource[][] AnimationFrames(string key) { return LoadArt(key); }

        private BitmapSource[][] LoadArt(string key)
        {
            BitmapSource[][] result;
            if (art.TryGetValue(key, out result)) return result;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(app.Asset(key + ".png"), UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            int rows = bitmap.PixelHeight * 192 / (bitmap.PixelWidth / 8 * 208);
            if ((rows != 9 && rows != 11) || bitmap.PixelWidth % 8 != 0 || bitmap.PixelHeight % rows != 0)
                throw new InvalidDataException("桌宠图集尺寸不正确：" + key);
            int w = bitmap.PixelWidth / 8, h = bitmap.PixelHeight / rows;
            result = new BitmapSource[rows][];
            for (int y = 0; y < rows; y++)
            {
                result[y] = new BitmapSource[Counts[y]];
                for (int x = 0; x < result[y].Length; x++)
                {
                    var frame = new CroppedBitmap(bitmap, new Int32Rect(x * w, y * h, w, h));
                    frame.Freeze();
                    result[y][x] = frame;
                }
            }
            art[key] = result;
            return result;
        }

        private void DrawFrame(int state, int index)
        {
            if (frames == null || (currentRow == state && currentFrame == index)) return;
            currentRow = state;
            currentFrame = index;
            sprite.Source = frames[state][index];
        }

        private void SetTrayIcon()
        {
            var encode = new PngBitmapEncoder();
            encode.Frames.Add(BitmapFrame.Create(frames[Idle][0]));
            using (var stream = new MemoryStream())
            {
                encode.Save(stream);
                stream.Position = 0;
                using (var original = new Drawing.Bitmap(stream))
                using (var miniature = new Drawing.Bitmap(original, 32, 32))
                {
                    IntPtr icon = miniature.GetHicon();
                    try
                    {
                        var previous = trayIcon;
                        using (Drawing.Icon borrowed = Drawing.Icon.FromHandle(icon))
                            trayIcon = (Drawing.Icon)borrowed.Clone();
                        tray.Icon = trayIcon;
                        if (previous != null) previous.Dispose();
                    }
                    finally { DestroyIcon(icon); }
                }
            }
        }

        private void OnPress(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (e.ClickCount >= 2)
            {
                pendingClickAt = 0;
                app.Open("home");
                return;
            }
            NativeRect r;
            if (!GetCursorPos(out pressPoint) || !GetWindowRect(handle, out r)) return;
            pressWindow.X = r.Left;
            pressWindow.Y = r.Top;
            dragging = false;
            pressing = sprite.CaptureMouse();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!pressing) return;
            NativePoint p;
            if (!GetCursorPos(out p)) return;
            int dx = p.X - pressPoint.X, dy = p.Y - pressPoint.Y;
            if (!dragging && Math.Abs(dx) + Math.Abs(dy) < 6) return;
            if (!dragging) { EndVisit(); StopPerformance(); dragging = true; pendingClickAt = 0; bubble.Visibility = Visibility.Collapsed; }
            int targetRow = dx < 0 ? Leftward : Right;
            if (row != targetRow) { row = targetRow; shownSince = clock.ElapsedMilliseconds; }
            Position(pressWindow.X + dx, pressWindow.Y + dy);
        }

        private void OnRelease(object sender, MouseButtonEventArgs e)
        {
            if (!pressing) return;
            bool moved = dragging;
            pressing = dragging = false;
            sprite.ReleaseMouseCapture();
            if (moved)
            {
                ClampCurrent();
                RememberPosition();
                SetEffect(Idle, 0);
            }
            else pendingClickAt = clock.ElapsedMilliseconds + Forms.SystemInformation.DoubleClickTime;
            e.Handled = true;
        }

        private void SetEffect(int newRow, int duration)
        {
            StopPerformance();
            row = newRow;
            shownSince = clock.ElapsedMilliseconds;
            effectUntil = shownSince + duration;
            DrawFrame(row, 0);
        }

        public void React(string action, string text)
        {
            string part=action=="tea"?"tea":action=="game"?"game":action=="focus"||action=="study"||action=="work"?"focus":
                action=="rest"?"rest":null;
            if(part=="focus")EndIndependentVisit();
            if (part!=null && visitor==null && BeginPerformance(part,false))
            {
                ambientAt=NextAmbient();if(!app.Data.Quiet)Say(text);return;
            }
            int use = Wave;
            switch ((action ?? "").ToLowerInvariant())
            {
                case "focus": case "work": case "running": case "study": use = Work; break;
                case "tea": case "read": case "review": case "note": case "gift": use = Review; break;
                case "success": case "jump": case "jumping": case "win": case "finish": use = Jump; break;
                case "miss": case "lose": case "failed": use = Miss; break;
                case "wait": case "waiting": case "rest": use = Wait; break;
                case "idle": use = Idle; break;
            }
            SetEffect(use, Counts[use] * FrameMs[use] * (use == Jump || use == Miss ? 1 : 2));
            ambientAt = NextAmbient();
            if (!app.Data.Quiet) Say(text);
        }

        public void Notice(string text)
        {
            if (disposed || string.IsNullOrWhiteSpace(text)) return;
            noticePage = text.Contains("便签") ? "notes" : text.Contains("专注") ? "focus" : "home";
            tray.ShowBalloonTip(6000, app.CharacterName + "的提醒", text, Forms.ToolTipIcon.Info);
            if (IsVisible) { SetEffect(Wait, 2700); Say(text); }
        }

        private void Say(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            words.Text = text;
            speechUntil = clock.ElapsedMilliseconds + Math.Max(3400, Math.Min(8500, 1100 + text.Length * 190L));
            bubble.Visibility = Visibility.Visible;
        }

        private void Tick(object sender, EventArgs e)
        {
            long now = clock.ElapsedMilliseconds;
            if(worldTick!=0 && now-worldTick>12000){StopPerformance();EndVisit();RefreshRoutinePace();}
            worldTick=now;
            if (now >= shellCheckAt && !pressing)
            {
                shellCheckAt = now + 1450;
                bool hide = app.Data.HideFullscreen && IsOtherFullscreen();
                if (hide != fullscreenHidden)
                {
                    fullscreenHidden = hide;
                    if (!manuallyHidden) { if (hide) { EndVisit(); Hide(); } else Show(); }
                }
                if (IsVisible && !pressing) ClampCurrent();
            }
            timer.Interval = TimeSpan.FromMilliseconds(!IsVisible ? 700 : visitor != null ? 34 : 80);
            RefreshKaraokeOverlay();
            if (!IsVisible) { EndVisit(); StopPerformance();ambientAt=now+16000; nearbySince = 0; pendingClickAt = 0; return; }
            if (now >= speechUntil && bubble.Visibility == Visibility.Visible) bubble.Visibility = Visibility.Collapsed;
            if (pendingClickAt > 0 && now >= pendingClickAt)
            {
                pendingClickAt = 0;
                app.Interact("pet");
            }
            if(visitor!=null && visitor.Independent && (app.Data.Quiet || (app.Music!=null && app.Music.Playing) ||
                (app.FocusActive && !app.FocusPaused)))EndIndependentVisit();
            if (visitor != null) {UpdateVisit(now);if(visitor!=null && visitor.Phase==2 && visitor.HostTea!=null)return;}
            if(visitor==null && activePoses!=null && !dragging)
            {
                if(independentPerformance && (app.Data.Quiet || (app.Music!=null && app.Music.Playing) ||
                    (app.FocusActive && !app.FocusPaused && performance!="focus")))FinishPerformance();
                else if(AdvancePerformance(now))return;
            }
            if (visitor == null && !dragging && now >= effectUntil)
            {
                if (row != Idle) SetEffect(Idle, 0);
                if (!pressing && AnimateKaraoke(now)) return;
                if (!KaraokeVisible && now >= ambientAt && !pressing) {Ambient();if(activePoses!=null || visitor!=null)return;}
                if (row == Idle && !pressing && TryLook(now)) return;
            }
            int frame = (int)(((now - shownSince) / FrameMs[row]) % Counts[row]);
            DrawFrame(row, Math.Max(0, frame));
        }

        private bool TryLook(long now)
        {
            if (frames.Length < 11) return false;
            NativePoint p;
            if (!GetCursorPos(out p) || !sprite.IsVisible) return false;
            Point eye = sprite.PointToScreen(new Point(sprite.ActualWidth * .50, sprite.ActualHeight * .37));
            double dx = p.X - eye.X, dy = p.Y - eye.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < 24 || distance > Math.Max(105, sprite.ActualWidth * 1.45))
            { nearbySince = 0; return false; }
            if (nearbySince == 0) nearbySince = now;
            long dwell = now - nearbySince;
            if (dwell < 300 || dwell > 3300) return false;
            int angle = ((int)Math.Round(Math.Atan2(dx, -dy) * 8 / Math.PI) + 16) % 16;
            DrawFrame(9 + angle / 8, angle % 8);
            return true;
        }

        private void PlaceSaved()
        {
            double x = app.Data.PetX, y = app.Data.PetY;
            if (double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(x) || double.IsInfinity(y) ||
                Math.Abs(x) > 100000 || Math.Abs(y) > 100000) PlaceDefault();
            else { Position((int)x, (int)y); ClampCurrent(); }
        }

        private void PlaceDefault()
        {
            NativeRect r;
            if (!GetWindowRect(handle, out r)) return;
            var area = Forms.Screen.PrimaryScreen.WorkingArea;
            Position(area.Right - (r.Right - r.Left) - 26, area.Bottom - (r.Bottom - r.Top) - 14);
            ClampCurrent();
        }

        private void ClampCurrent()
        {
            if (handle == IntPtr.Zero) return;
            NativeRect r;
            if (!GetWindowRect(handle, out r)) return;
            NativeMonitor monitor = new NativeMonitor();
            monitor.Size = Marshal.SizeOf(typeof(NativeMonitor));
            if (!GetMonitorInfo(MonitorFromWindow(handle, 2), ref monitor)) return;
            int x = Math.Max(monitor.Work.Left, Math.Min(r.Left, monitor.Work.Right - (r.Right - r.Left)));
            int y = Math.Max(monitor.Work.Top, Math.Min(r.Top, monitor.Work.Bottom - (r.Bottom - r.Top)));
            if (r.Left != x || r.Top != y) Position(x, y);
        }

        private void Position(int x, int y)
        {
            if (handle != IntPtr.Zero) SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, 0x1 | 0x4 | 0x10);
        }

        private void RememberPosition()
        {
            NativeRect r;
            if (!GetWindowRect(handle, out r)) return;
            app.Data.PetX = r.Left;
            app.Data.PetY = r.Top;
            app.Save();
        }

        // This short-lived, click-through window only covers the two sprites and their walking path.
        private sealed class VisitScene
        {
            public Window Layer;
            public IntPtr Handle;
            public Image Actor;
            public BitmapSource[] HostTea,GuestTea;
            public BitmapSource[][] Art;
            public long Since;
            public bool Independent;
            public double Side;
            public int Phase = -1;
        }

        public void Visit(){StartVisit(false);}
        private void StartVisit(bool independent)
        {
            if (disposed || visitor != null) return;
            StopPerformance();
            Reveal();
            if (handle == IntPtr.Zero) return;
            NativeRect r;
            NativeMonitor mon = new NativeMonitor { Size = Marshal.SizeOf(typeof(NativeMonitor)) };
            if (!GetWindowRect(handle, out r) || !GetMonitorInfo(MonitorFromWindow(handle, 2), ref mon)) return;
            double center = (r.Left + r.Right) * .5;
            var scene = new VisitScene {
                Actor = new Image { Width = sprite.Width, Height = sprite.Height, Stretch = Stretch.Fill },
                Art = LoadArt(character == "rem" ? "kurisu" : "rem"),
                HostTea=ActionFrames(character,"tea"),GuestTea=ActionFrames(character=="rem"?"kurisu":"rem","tea"),
                Since = clock.ElapsedMilliseconds, Independent=independent,
                Side = center - mon.Work.Left > mon.Work.Right - center ? -1 : 1
            };
            var canvas = new Canvas { IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(scene.Actor, BitmapScalingMode.HighQuality);
            canvas.Children.Add(scene.Actor);
            scene.Layer = new Window {
                Width = sprite.Width * 2.8, Height = sprite.Height,
                Background = Brushes.Transparent, Content = canvas, AllowsTransparency = true,
                WindowStyle = WindowStyle.None, ShowInTaskbar = false, ShowActivated = false,
                ResizeMode = ResizeMode.NoResize, Topmost = Topmost, Focusable = false,
                IsHitTestVisible = false, WindowStartupLocation = WindowStartupLocation.Manual,
                Title = "桌边来客"
            };
            scene.Layer.SourceInitialized += delegate {
                scene.Handle = new WindowInteropHelper(scene.Layer).Handle;
                SetStyle(scene.Handle, -20, (GetStyle(scene.Handle, -20) | 0x080000A0L) & ~0x40000L);
            };
            visitor = scene;
            scene.Layer.Show();
            UpdateVisit(scene.Since);
        }

        private static double Smooth(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return t * t * (3 - 2 * t);
        }

        private void UpdateVisit(long now)
        {
            VisitScene scene = visitor;
            if (scene == null) return;
            double t = (now - scene.Since) / 1000.0;
            if (t >= 14.3) { EndVisit(); SetEffect(Idle, 0); return; }
            NativeRect host, layer;
            NativeMonitor mon = new NativeMonitor { Size = Marshal.SizeOf(typeof(NativeMonitor)) };
            if (!GetWindowRect(handle, out host) || !GetWindowRect(scene.Handle, out layer) ||
                !GetMonitorInfo(MonitorFromWindow(handle, 2), ref mon)) return;
            double pixelRatio = (host.Right - host.Left) / Width;
            if (pixelRatio <= 0) return;
            int windowW = layer.Right - layer.Left, windowH = layer.Bottom - layer.Top;
            double cx = (host.Left + host.Right) * .5;
            int x = (int)Math.Round(cx - (scene.Side < 0 ? windowW - sprite.Width * .54 * pixelRatio : sprite.Width * .54 * pixelRatio));
            x = Math.Max(mon.Work.Left, Math.Min(x, mon.Work.Right - windowW));
            int y = host.Bottom - windowH;
            if (layer.Left != x || layer.Top != y)
                SetWindowPos(scene.Handle, IntPtr.Zero, x, y, 0, 0, 0x1 | 0x4 | 0x10);
            double hostCenter = (cx - x) / pixelRatio;
            double free = scene.Side < 0 ? hostCenter : windowW / pixelRatio - hostCenter;
            double far = Math.Max(0, Math.Min(sprite.Width * 1.68, free - sprite.Width * .5));
            double near = Math.Min(sprite.Width * .62, far * .82);
            double path = t < 3 ? 1 - Smooth(t / 3) : t > 11.1 ? Smooth((t - 11.1) / 3.2) : 0;
            double guestCenter = hostCenter + scene.Side * (near + (far - near) * path);
            Canvas.SetLeft(scene.Actor, guestCenter - sprite.Width / 2);
            scene.Actor.Opacity = Math.Min(Smooth(t / .55), Smooth((14.3 - t) / .65));
            int phase = t < 3 ? 0 : t < 5 ? 1 : t < 10.2 ? 2 : t < 11.1 ? 3 : 4;
            if (phase != scene.Phase)
            {
                scene.Phase = phase;
                bool rem = character == "rem";
                switch (phase)
                {
                    case 0: SetEffect(Wait, 3200); break;
                    case 1:
                        SetEffect(Wave, 2100);
                        if (!app.Data.Quiet) Say(rem ? "红莉栖小姐，刚好泡了茶。" : character == "kurisu" ? "蕾姆？来得正好，实验告一段落了。" : "蕾姆，来窗边一起坐坐吧。");
                        break;
                    case 2: SetEffect(Review, 3300); break;
                    case 3: case 4:
                        SetEffect(Wave, 2200);
                        if (phase == 3 && !app.Data.Quiet) Say(rem ? "下次见，杯子放这里就好。" : "茶很好喝。下次换我请你。");
                        break;
                }
            }
            int guestState = phase == 0 ? (scene.Side < 0 ? Right : Leftward) : phase == 4 ?
                (scene.Side < 0 ? Leftward : Right) : phase == 2 ? Review : Wave;
            int frame = ((int)(t * 1000 / FrameMs[guestState])) % Counts[guestState];
            scene.Actor.Source = scene.Art[guestState][frame];
            if (phase == 2)
            {
                var score=ActionScore.For("tea");
                long beat=(long)((t-5)*1000);
                if(scene.HostTea!=null)ShowPose(scene.HostTea[score.At(beat)]);
                if(scene.GuestTea!=null)scene.Actor.Source=scene.GuestTea[score.At(Math.Max(0,beat-320))];
            }
        }

        private void EndVisit()
        {
            if (visitor == null) return;
            var old = visitor;
            visitor = null;
            old.Layer.Close();
            ambientAt = NextAmbient();
            bubble.Visibility = Visibility.Collapsed;
        }
        private void EndIndependentVisit()
        {
            if(visitor==null || !visitor.Independent)return;
            EndVisit();SetEffect(Idle,0);
        }

        private bool IsOtherFullscreen()
        {
            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == handle) return false;
            uint pid;
            GetWindowThreadProcessId(foreground, out pid);
            if (pid == GetCurrentProcessId()) return false;
            var name = new StringBuilder(96);
            GetClassName(foreground, name, name.Capacity);
            string type = name.ToString();
            if (type == "Progman" || type == "WorkerW" || type == "Shell_TrayWnd" || type == "Shell_SecondaryTrayWnd") return false;
            if (IsZoomed(foreground) && (GetStyle(foreground, -16) & 0x00C00000) == 0x00C00000) return false;
            NativeRect r;
            NativeMonitor monitor = new NativeMonitor();
            monitor.Size = Marshal.SizeOf(typeof(NativeMonitor));
            if (!GetWindowRect(foreground, out r) || !GetMonitorInfo(MonitorFromWindow(foreground, 2), ref monitor)) return false;
            if (MonitorFromWindow(foreground, 2) != MonitorFromWindow(handle, 2)) return false;
            return r.Left <= monitor.Bounds.Left + 1 && r.Top <= monitor.Bounds.Top + 1 &&
                   r.Right >= monitor.Bounds.Right - 1 && r.Bottom >= monitor.Bounds.Bottom - 1;
        }

        public void DisposeTray()
        {
            if (disposed) return;
            disposed = true;
            timer.Stop();
            DisposeKaraoke();
            EndVisit();
            tray.Visible = false;
            tray.Dispose();
            menu.Dispose();
            if (trayIcon != null) trayIcon.Dispose();
        }

        [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct NativeMonitor { public int Size; public NativeRect Bounds, Work; public int Flags; }
        [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int w, int h, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitor info);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("kernel32.dll")] private static extern uint GetCurrentProcessId();
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll", EntryPoint="GetWindowLongW")] private static extern int GetStyle32(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint="SetWindowLongW")] private static extern int SetStyle32(IntPtr window, int index, int value);
        [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] private static extern IntPtr GetStyle64(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetStyle64(IntPtr window, int index, IntPtr value);
        private static long GetStyle(IntPtr h, int i) { return IntPtr.Size == 8 ? GetStyle64(h, i).ToInt64() : GetStyle32(h, i); }
        private static void SetStyle(IntPtr h, int i, long v)
        { if (IntPtr.Size == 8) SetStyle64(h, i, new IntPtr(v)); else SetStyle32(h, i, (int)v); }
    }
}


