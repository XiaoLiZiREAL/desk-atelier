using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskMate
{
    public sealed partial class PetWindow
    {
        private sealed class LyricPanel
        {
            internal Window Window;
            internal Border Card;
            internal TextBlock Current, Next, Symbol;
            internal Grid Progress;
            internal ScaleTransform Fill;
            internal IntPtr Hwnd;
        }

        private LyricPanel lyrics;
        private string lyricLine="", lyricNext="";
        private double lyricFraction;
        private bool lyricPlaying, placementDirty, placingLyrics, dancing, browserTrack, showProgress;
        private int lyricRevision;
        private long singingSince;

        public string KaraokeLyric { get { return browserTrack ? "" : lyricLine; } }
        public bool ShowingBrowserTrack { get { return browserTrack && KaraokeVisible; } }
        public bool KaraokeVisible { get { return lyrics!=null && lyrics.Window.IsVisible; } }
        public bool KaraokeMoving { get { return dancing; } }

        public void SetKaraoke(string line, string next, double fraction, bool singing)
        {
            SetMusicPanel(line,next,fraction,singing,false,true);
        }

        internal void ShowBrowserTrack(string title,string artist,double fraction,bool hasTimeline)
        {
            bool hasTitle=!String.IsNullOrWhiteSpace(title);
            string detail=String.IsNullOrWhiteSpace(artist)?(hasTitle?"浏览器正在播放":""):"正在播放 · "+artist;
            SetMusicPanel(hasTitle?title:"浏览器正在播放音频",detail,fraction,true,true,hasTimeline);
        }

        private void SetMusicPanel(string line,string next,double fraction,bool singing,bool track,bool progress)
        {
            int version=Interlocked.Increment(ref lyricRevision);
            Action update=delegate
            {
                if(disposed || version!=Volatile.Read(ref lyricRevision))return;
                string current=(line??"").Trim(), following=(next??"").Trim();
                bool changed=current!=lyricLine || following!=lyricNext || track!=browserTrack || progress!=showProgress;
                bool playbackChanged=singing!=lyricPlaying;
                if(singing && (!lyricPlaying || current!=lyricLine))singingSince=clock.ElapsedMilliseconds;
                lyricLine=current;lyricNext=following;lyricPlaying=singing;browserTrack=track;showProgress=progress;
                lyricFraction=double.IsNaN(fraction)||double.IsInfinity(fraction)?0:Math.Max(0,Math.Min(1,fraction));
                if(changed)placementDirty=true;
                if(lyrics!=null)
                {
                    if(changed || playbackChanged)StyleLyricText();
                    lyrics.Fill.ScaleX=lyricFraction;
                }
                RefreshKaraokeOverlay();
            };
            if(Dispatcher.CheckAccess())update();
            else if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(update,DispatcherPriority.Normal);
        }

        public void ClearKaraoke() { SetKaraoke("","",0,false); }

        private void InstallKaraokeTracking()
        {
            LocationChanged+=delegate{MarkLyricPlacement();};
            SizeChanged+=delegate{MarkLyricPlacement();};
            IsVisibleChanged+=delegate{MarkLyricPlacement();};
            bubble.IsVisibleChanged+=delegate{RefreshKaraokeOverlay();};
        }

        private void MarkLyricPlacement()
        {
            placementDirty=true;
            RefreshKaraokeOverlay();
        }

        private static IntPtr LyricWindowHook(IntPtr hwnd,int message,IntPtr w,IntPtr l,ref bool handled)
        {
            if(message==0x84){handled=true;return new IntPtr(-1);} // transparent hit test
            if(message==0x21){handled=true;return new IntPtr(3);} // MA_NOACTIVATE
            return IntPtr.Zero;
        }

        private LyricPanel CreateLyricPanel()
        {
            var p=new LyricPanel();
            var ink=words.FontFamily;
            p.Current=new TextBlock
            {
                FontFamily=ink,FontSize=18,FontWeight=FontWeights.SemiBold,Foreground=Ui.Color(Ui.Ink),
                TextWrapping=TextWrapping.Wrap,LineStackingStrategy=LineStackingStrategy.BlockLineHeight,LineHeight=26
            };
            p.Next=new TextBlock
            {
                FontFamily=ink,FontSize=12.5,Foreground=Ui.Color(Ui.Muted),Margin=new Thickness(0,3,0,0),
                TextTrimming=TextTrimming.CharacterEllipsis,TextWrapping=TextWrapping.NoWrap
            };
            p.Symbol=Ui.Icon("\uE8D6",15,Ui.Accent);
            p.Symbol.VerticalAlignment=VerticalAlignment.Top;p.Symbol.Margin=new Thickness(0,5,10,0);
            var lines=new StackPanel();lines.Children.Add(p.Current);lines.Children.Add(p.Next);
            var layout=new Grid();layout.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            layout.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            Grid.SetColumn(lines,1);layout.Children.Add(p.Symbol);layout.Children.Add(lines);
            var progress=new Grid{Height=2,Margin=new Thickness(0,10,0,0),ClipToBounds=true,Background=Ui.Color("#E4EAF0")};
            p.Progress=progress;
            p.Fill=new ScaleTransform(0,1);
            progress.Children.Add(new Border{Background=Ui.Color(Ui.Accent),RenderTransform=p.Fill,RenderTransformOrigin=new Point(0,.5)});
            var stack=new StackPanel();stack.Children.Add(layout);stack.Children.Add(progress);
            p.Card=new Border
            {
                Child=stack,CornerRadius=new CornerRadius(11),Background=Brushes.White,
                BorderBrush=Ui.Color("#D8DFE7"),BorderThickness=new Thickness(1),Padding=new Thickness(14,10,14,9)
            };
            p.Window=new Window
            {
                Title="桌宠歌词",Owner=this,Content=p.Card,Background=Brushes.Transparent,AllowsTransparency=true,WindowStyle=WindowStyle.None,
                ShowInTaskbar=false,ShowActivated=false,Focusable=false,IsHitTestVisible=false,ResizeMode=ResizeMode.NoResize,
                WindowStartupLocation=WindowStartupLocation.Manual,Width=310,SizeToContent=SizeToContent.Height,Opacity=0,Topmost=Topmost,
                UseLayoutRounding=true,SnapsToDevicePixels=true
            };
            p.Window.SourceInitialized+=delegate
            {
                p.Hwnd=new WindowInteropHelper(p.Window).Handle;
                SetStyle(p.Hwnd,-20,(GetStyle(p.Hwnd,-20)|0x080000A0L)&~0x40000L);
                var source=HwndSource.FromHwnd(p.Hwnd);
                if(source!=null)source.AddHook(LyricWindowHook);
            };
            p.Window.SizeChanged+=delegate { if(!placingLyrics && p.Window.IsVisible)MarkLyricPlacement(); };
            return p;
        }

        private void StyleLyricText()
        {
            lyrics.Current.Text=lyricLine;
            lyrics.Current.FontSize=browserTrack?15:lyricLine.Length>120?14:lyricLine.Length>52?16:18;
            lyrics.Current.TextWrapping=browserTrack?TextWrapping.NoWrap:TextWrapping.Wrap;
            lyrics.Current.TextTrimming=browserTrack?TextTrimming.CharacterEllipsis:TextTrimming.None;
            lyrics.Current.LineHeight=lyrics.Current.FontSize+7;
            lyrics.Next.Text=lyricNext;
            lyrics.Next.Visibility=lyricNext.Length==0?Visibility.Collapsed:Visibility.Visible;
            lyrics.Progress.Visibility=showProgress?Visibility.Visible:Visibility.Collapsed;
            lyrics.Symbol.Text=lyricPlaying?"\uE8D6":"\uE769";
        }

        private void RefreshKaraokeOverlay()
        {
            bool display=!disposed && handle!=IntPtr.Zero && IsVisible && WindowState!=WindowState.Minimized &&
                !manuallyHidden && !fullscreenHidden && !app.Data.Quiet && lyricLine.Length>0 &&
                visitor==null && !PerformingPaper && !pressing && !dragging && bubble.Visibility!=Visibility.Visible;
            if(!display)
            {
                StopSongMovement();
                if(KaraokeVisible)lyrics.Window.Hide();
                return;
            }
            if(lyrics==null)
            {
                lyrics=CreateLyricPanel();StyleLyricText();lyrics.Fill.ScaleX=lyricFraction;placementDirty=true;
            }
            if(lyrics.Window.Topmost!=Topmost)lyrics.Window.Topmost=Topmost;
            if(!lyrics.Window.IsVisible)
            {
                lyrics.Window.Opacity=0;lyrics.Window.Show();placementDirty=true;
            }
            if(placementDirty)PlaceLyricPanel();
            if(!lyricPlaying || clock.ElapsedMilliseconds<effectUntil)StopSongMovement();
        }

        private void PlaceLyricPanel()
        {
            if(placingLyrics || lyrics==null || lyrics.Hwnd==IntPtr.Zero)return;
            NativeRect pet;
            var monitor=new NativeMonitor{Size=Marshal.SizeOf(typeof(NativeMonitor))};
            if(!GetWindowRect(handle,out pet)||!GetMonitorInfo(MonitorFromWindow(handle,2),ref monitor))return;
            placingLyrics=true;
            try
            {
                double ratio=(pet.Right-pet.Left)/Math.Max(1.0,ActualWidth);
                if(ratio<.4 || ratio>6)ratio=1;
                int margin=Math.Max(4,(int)Math.Ceiling(7*ratio));
                int availableW=Math.Max(1,monitor.Work.Right-monitor.Work.Left-2*margin);
                int availableH=Math.Max(1,monitor.Work.Bottom-monitor.Work.Top-2*margin);
                double maxWidth=availableW/ratio, maxHeight=availableH/ratio;
                double width=Math.Min(maxWidth,Math.Max(270,Math.Min(438,316*Math.Pow(Math.Max(.6,scale),.25)+(lyricLine.Length>42?62:0))));
                // Let WPF propagate text wrapping and next-line visibility before reading the real HWND size.
                lyrics.Window.Width=width;lyrics.Window.MaxHeight=maxHeight;lyrics.Window.UpdateLayout();
                NativeRect panel;
                if(!GetWindowRect(lyrics.Hwnd,out panel))return;
                int pw=Math.Min(availableW,panel.Right-panel.Left),ph=Math.Min(availableH,panel.Bottom-panel.Top);
                int cx=(pet.Left+pet.Right)/2;
                int visualTop=pet.Bottom-(int)Math.Ceiling(sprite.Height*ratio*.99);
                int visualHalf=(int)Math.Ceiling(sprite.Width*ratio*.37);
                int x=cx-pw/2,y=visualTop-margin-ph;
                if(y<monitor.Work.Top+margin)
                {
                    int right=cx+visualHalf+margin,left=cx-visualHalf-margin-pw;
                    if(right+pw<=monitor.Work.Right-margin) {x=right;y=visualTop+margin;}
                    else if(left>=monitor.Work.Left+margin) {x=left;y=visualTop+margin;}
                    else y=pet.Bottom+margin;
                }
                x=Math.Max(monitor.Work.Left+margin,Math.Min(x,monitor.Work.Right-margin-pw));
                y=Math.Max(monitor.Work.Top+margin,Math.Min(y,monitor.Work.Bottom-margin-ph));
                SetWindowPos(lyrics.Hwnd,IntPtr.Zero,x,y,0,0,0x1|0x4|0x10|0x200);
                lyrics.Window.Opacity=1;placementDirty=false;
            }
            finally {placingLyrics=false;}
        }

        private bool AnimateKaraoke(long now)
        {
            if(!KaraokeVisible || !lyricPlaying)return false;
            dancing=true;
            var singing=ActionFrames(character,"song");
            if(singing!=null)ShowPose(singing[ActionScore.SungPose(now-singingSince,browserTrack?0:lyricFraction)]);
            else DrawFrame(Idle,(int)((now-singingSince)/320)%Counts[Idle]);
            return true;
        }

        private void StopSongMovement()
        {
            if(!dancing)return;
            dancing=false;
            if(currentRow!=row && frames!=null)DrawFrame(row,0);
        }

        private void DisposeKaraoke()
        {
            StopSongMovement();lyricLine=lyricNext="";lyricPlaying=false;
            if(lyrics==null)return;
            var panel=lyrics;lyrics=null;
            var source=HwndSource.FromHwnd(panel.Hwnd);if(source!=null)source.RemoveHook(LyricWindowHook);
            panel.Window.Close();
        }
    }
}
