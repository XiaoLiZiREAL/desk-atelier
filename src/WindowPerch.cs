using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DeskMate
{
    public sealed partial class PetWindow
    {
        const string LeavePerchText="离开窗口";
        IntPtr perchTarget, perchHook, perchDisplay;
        uint perchProcess, perchThread;
        int perchOffsetPixels;
        readonly PerchFocusGrace perchFocusGrace=new PerchFocusGrace(),perchRectGrace=new PerchFocusGrace();
        WindowSeatRoutine windowSeatRoutine;
        bool perchAligned, perchFallback, perchCovered, perchRefreshQueued;
#if DESKATELIER_PREVIEW
        readonly Queue<string> perchDiagnostic=new Queue<string>();
        string lastPerchDiagnostic;
#endif
        void TracePerch(string state)
        {
#if DESKATELIER_PREVIEW
            if(lastPerchDiagnostic==state)return;
            lastPerchDiagnostic=state;
            if(perchDiagnostic.Count>=40)perchDiagnostic.Dequeue();
            perchDiagnostic.Enqueue(DateTime.Now.ToString("HH:mm:ss.fff")+" "+state);
            try{System.IO.File.WriteAllLines(System.IO.Path.Combine(app.UserFolder,"window-perch.log"),perchDiagnostic);}
            catch(System.IO.IOException){}
            catch(UnauthorizedAccessException){}
#endif
        }
        BitmapSource[] perchPoses,perchStillPoses,perchLoungePoses,perchSwingPoses;
        string perchPoseArt;
        WinEventProc perchCallback;
        DispatcherTimer perchTimer;
        WindowPerchMarker perchBadge;
        System.Windows.Forms.ToolStripMenuItem leavePerchItem;

        sealed class WindowPerchMarker : FrameworkElement
        {
            protected override void OnRender(DrawingContext dc)
            {
                var pen=new Pen(Ui.Color("#668AC1"),2){DashStyle=DashStyles.Dash};
                dc.DrawLine(pen,new Point(5,Height-3),new Point(Width-5,Height-3));
            }
        }

        void InstallPerch(Grid panel)
        {
            perchBadge=new WindowPerchMarker{IsHitTestVisible=false,Width=64,Height=24,
                HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom,
                Visibility=Visibility.Collapsed};
            panel.Children.Insert(panel.Children.IndexOf(sprite),perchBadge);
            perchTimer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(50)};
            perchTimer.Tick+=delegate{UpdatePerch();};
        }

        void PerchBadge(bool show)
        {
            if(perchBadge==null)return;
            var visibility=show?Visibility.Visible:Visibility.Collapsed;
            perchBadge.Width=Math.Max(34,sprite.Width*.57);
            if(perchBadge.Visibility!=visibility)perchBadge.Visibility=visibility;
        }

        bool CacheWindowPoses()
        {
            if(perchPoseArt==currentArtId && perchPoses!=null)return true;
            var poses=LoadWindowPerchPoses(currentArtId,PerchMode.Edge);
            if(poses==null)return false;
            perchPoses=poses;
            perchStillPoses=LoadPerchAtlas("window-rest-"+currentArtId+".png",6);
            perchLoungePoses=currentArtId=="steve"?null:LoadPerchAtlas("window-lounge-"+currentArtId+".png",28);
            perchSwingPoses=currentArtId=="steve"?null:LoadPerchAtlas("window-swing-"+currentArtId+".png",WindowSeatRoutine.SwingPoseCount);
            perchPoseArt=currentArtId;
            return true;
        }

        bool TryModeAlign(PerchRect host,PerchRect own,PerchRect work,double fraction,out int x,out int y)
        {
            double pixelsPerDip=own.Height/Height;
            int gutter=PerchHeadGutter(own);
            return WindowPerchGeometry.TryAlignEdge(host,own,work,fraction,pixelsPerDip,gutter,
                own.Height-gutter,out x,out y);
        }

        bool IsPerchCandidate(IntPtr target,out uint process,out uint thread)
        {
            process=thread=0;
            if(target==IntPtr.Zero || target==handle || !IsWindow(target) || !IsWindowVisible(target))return false;
            thread=GetWindowThreadProcessId(target,out process);
            if(thread==0 || process==GetCurrentProcessId())return false;
            var name=new StringBuilder(96);
            GetClassName(target,name,name.Capacity);
            string kind=name.ToString();
            return kind!="Progman" && kind!="WorkerW" && kind!="Shell_TrayWnd" &&
                kind!="Shell_SecondaryTrayWnd" && kind!="#32768";
        }

        static PerchRect PerchBounds(NativeRect r) { return new PerchRect(r.Left,r.Top,r.Right,r.Bottom); }
        int PerchHeadGutter(PerchRect own) { return (int)Math.Floor((Height-sprite.Height)*own.Height/Height); }

        // Scope the native calls rather than changing the application's DPI mode.
        static IntPtr PerchPhysicalCoordinates()
        {
            return SetThreadDpiAwarenessContext(new IntPtr(-4));
        }
        static void RestorePerchDpi(IntPtr previous)
        {
            if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);
        }

        static bool TryReadWindowBounds(IntPtr hwnd,out PerchRect rectangle)
        {
            NativeRect frame;
            bool read=DwmPerchBounds(hwnd,9,out frame,16)==0 && frame.Right>frame.Left && frame.Bottom>frame.Top;
            rectangle=PerchBounds(frame);
            return read;
        }

        static bool ReadPerchRects(IntPtr target,IntPtr pet,int? anchorPixels,
            out PerchRect window,out PerchRect own,out PerchRect work,out IntPtr display)
        {
            window=own=work=new PerchRect();display=IntPtr.Zero;
            NativeRect nativePet;
            if(!TryReadWindowBounds(target,out window) || !GetWindowRect(pet,out nativePet))return false;
            own=PerchBounds(nativePet);
            if(own.Width<=0 || own.Height<=0)return false;
            // Pick the display containing the visual anchor, not the largest part of
            // a wide window; that part can cross the seam while the seat hasn't moved.
            // Keep X in physical pixels so a DPI resize does not change the selected display.
            int x=anchorPixels.HasValue?WindowPerchGeometry.HostAnchorX(window,anchorPixels.Value,1):
                own.Left+own.Width/2;
            display=MonitorFromPoint(new NativePoint{X=x,Y=window.Top},2);
            return TryReadPerchWork(display,out work);
        }

        static bool TryReadPerchWork(IntPtr display,out PerchRect work)
        {
            var monitor=new NativeMonitor{Size=Marshal.SizeOf(typeof(NativeMonitor))};
            work=new PerchRect();
            if(display==IntPtr.Zero || !GetMonitorInfo(display,ref monitor))return false;
            work=PerchBounds(monitor.Work);
            return work.Width>0 && work.Height>0;
        }

        bool TryAlignOnPerchDisplay(PerchRect host,PerchRect own,PerchRect work,
            ref IntPtr display,out int x,out int y)
        {
            double fraction=WindowPerchGeometry.HostFraction(host,perchOffsetPixels,1);
            if(TryModeAlign(host,own,work,fraction,out x,out y))return true;
            // During a drag across the seam, keep using the old screen until the
            // new one can fit the whole pose. Do not move through an invalid gap.
            if(perchDisplay==display || !TryReadPerchWork(perchDisplay,out work) ||
                !TryModeAlign(host,own,work,fraction,out x,out y))return false;
            display=perchDisplay;
            return true;
        }

        static bool IsPerchCloaked(IntPtr host)
        {
            int value;
            return DwmPerchFlags(host,14,out value,4)==0 && value!=0;
        }

        struct PerchDrop
        {
            internal IntPtr Target;
            internal int Offset;
            internal uint Process, Thread;
        }
        long lastDropProbe=-1000;
        bool lastDropAvailable;

        bool FindPerchDrop(out PerchDrop result)
        {
            result=new PerchDrop();
            NativeRect nativePet;
            if(!GetWindowRect(handle,out nativePet))return false;
            var own=PerchBounds(nativePet);
            if(own.Width<=0 || own.Height<=0 || Height<=0)return false;
            int dropX=own.Left+own.Width/2;
            int halo=(int)Math.Ceiling(24*own.Height/Height)+16;
            var above=new List<PerchRect>();
            var seen=new HashSet<IntPtr>();
            long nearest=long.MaxValue;
            // Scan real top-level Z order. The dragged pet is topmost but is never
            // a drop target or an obstruction. Never activate the selected host.
            for(IntPtr candidate=GetTopWindow(IntPtr.Zero);candidate!=IntPtr.Zero && seen.Count<256 && seen.Add(candidate);
                candidate=GetWindow(candidate,2))
            {
                if(candidate==handle || !IsWindowVisible(candidate) || IsIconic(candidate))continue;
                NativeRect rough;
                if(!GetWindowRect(candidate,out rough) || rough.Left>dropX+own.Width || rough.Right<=dropX-own.Width ||
                    rough.Top>own.Bottom+halo || rough.Bottom<own.Bottom-halo)continue;
                PerchRect host;
                if(IsPerchCloaked(candidate) || !TryReadWindowBounds(candidate,out host))continue;
                uint process,thread;
                double fraction;
                long distance=WindowPerchGeometry.DropDistance(host,own);
                if(distance<nearest && IsPerchCandidate(candidate,out process,out thread) &&
                    WindowPerchGeometry.TryDropFraction(host,own,own.Height/Height,out fraction))
                {
                    IntPtr display=MonitorFromPoint(new NativePoint{X=dropX,Y=host.Top},2);
                    PerchRect work;int x,y;
                    if(TryReadPerchWork(display,out work) &&
                        TryModeAlign(host,own,work,fraction,out x,out y) &&
                        WindowPerchGeometry.EdgeIsExposed(host,dropX,x+own.Width/2,above))
                    {
                        nearest=distance;
                        result=new PerchDrop{Target=candidate,Process=process,Thread=thread,Offset=x+own.Width/2-host.Left};
                        if(distance==0)break;
                    }
                }
                above.Add(host);
            }
            return result.Target!=IntPtr.Zero;
        }

        bool TryPerchDrop(bool attach)
        {
            if(!WindowPerchMotion.Supports(currentArtId) || handle==IntPtr.Zero || disposed)return false;
            long now=clock.ElapsedMilliseconds;
            if(!attach && now-lastDropProbe<35)return lastDropAvailable;
            PerchDrop drop;
            IntPtr previous=PerchPhysicalCoordinates();
            try{lastDropAvailable=FindPerchDrop(out drop);lastDropProbe=now;}
            finally{RestorePerchDpi(previous);}
            if(!lastDropAvailable)return false;
            if(!attach)return true;
            if(!CacheWindowPoses())return false;
            EndDesktopScene(false);
            TracePerch("attach");
            perchTarget=drop.Target;perchProcess=drop.Process;perchThread=drop.Thread;perchOffsetPixels=drop.Offset;
            perchFocusGrace.Reset();perchRectGrace.Reset();pendingClickAt=0;
            windowSeatRoutine=new WindowSeatRoutine(currentArtId,random.Next(),clock.ElapsedMilliseconds,perchLoungePoses!=null,perchSwingPoses!=null);
            EndVisit();ForgetPerformance();StopSongMovement();
            perchCallback=OnPerchEvent;
            // Watch only this window's process/thread and ignore all non-window events.
            perchHook=SetWinEventHook(0x8001,0x800B,IntPtr.Zero,perchCallback,perchProcess,perchThread,0);
            perchTimer.Start();
            UpdatePerch();
            return perchTarget!=IntPtr.Zero;
        }

        void OnPerchEvent(IntPtr hook,uint evt,IntPtr window,int id,int child,uint thread,uint time)
        {
            if(hook!=perchHook || window!=perchTarget || id!=0 || child!=0 || disposed)return;
            if(evt==0x8001){LeaveWindowPerch();return;}
            if(perchRefreshQueued)return;
            perchRefreshQueued=true;
            Dispatcher.BeginInvoke((Action)delegate{perchRefreshQueued=false;UpdatePerch();},DispatcherPriority.Input);
        }

        void UpdatePerch()
        {
            if(perchTarget==IntPtr.Zero || disposed || pressing || dragging)return;
            uint process;
            uint thread=GetWindowThreadProcessId(perchTarget,out process);
            if(thread!=perchThread || process!=perchProcess || thread==0 || !IsWindow(perchTarget))
            {LeaveWindowPerch();return;}
            if(manuallyHidden || fullscreenHidden){SetPerchPollInterval(150);return;}
            if(IsIconic(perchTarget) || IsPerchCloaked(perchTarget))
            {perchFocusGrace.Reset();perchRectGrace.Reset();ShowPerchFallback("host-minimized-or-cloaked");return;}
            long now=clock.ElapsedMilliseconds;
            bool aligned=false,coveredSustained=false;
            IntPtr previous=PerchPhysicalCoordinates();
            try
            {
                PerchRect host,own,work;int x,y;
                IntPtr display;
                if(IsWindowVisible(perchTarget) && ReadPerchRects(perchTarget,handle,perchOffsetPixels,
                    out host,out own,out work,out display) &&
                    TryAlignOnPerchDisplay(host,own,work,ref display,out x,out y))
                {
                    int center=x+own.Width/2;
                    PerchRect paint=WindowPerchGeometry.PaintedArea(own,x,y,PerchHeadGutter(own),
                        (int)Math.Ceiling(sprite.Width*own.Height/Height));
                    bool covered=IsWindowSeatCovered(perchTarget,host,paint,center);
                    coveredSustained=perchFocusGrace.Expired(now,!covered,75) || (covered && perchCovered);
                    aligned=!WindowPerchGeometry.NeedsMove(own.Left,own.Top,x,y,perchAligned) ||
                        SetWindowPos(handle,IntPtr.Zero,x,y,0,0,0x1|0x4|0x10);
                    if(aligned)perchDisplay=display;
                }
                else perchFocusGrace.Reset();
            }
            finally{RestorePerchDpi(previous);}
            bool badForLongEnough=perchRectGrace.Expired(now,aligned,850);
            var presentation=PerchPresentationPolicy.Resolve(aligned,false,perchAligned,badForLongEnough,coveredSustained);
            if(presentation==PerchPresentation.HoldLastPlacement){TracePerch("transient-geometry");return;}
            if(presentation==PerchPresentation.DesktopFallback){ShowPerchFallback("no-safe-layout");return;}
            if(presentation==PerchPresentation.Covered)
            {
                // Stay bound and keep the same anchor/loop. Only the display is
                // suspended; don't jump back to a saved desktop coordinate.
                perchAligned=true;perchFallback=false;perchCovered=true;
                TracePerch("covered-still-bound");
                SetPerchPollInterval(55);
                if(IsVisible)Hide();
                return;
            }
            perchCovered=false;
            TracePerch("seated");
            bool returning=!perchAligned;
            perchAligned=true;perchFallback=false;
            SetPerchPollInterval(50);
            if(!IsVisible)Show();
            Topmost=true;
            PerchBadge(false);
            if(returning){EndVisit();ForgetPerformance();StopSongMovement();}
            RenderPerch(clock.ElapsedMilliseconds);
        }

        bool IsWindowSeatCovered(IntPtr hostHandle,PerchRect host,PerchRect paint,int anchorX)
        {
            // Windows physically above the target determine occlusion. They may
            // stay above it even if focus moves to an app on another display.
            IntPtr next=GetWindow(hostHandle,3);
            for(int i=0;next!=IntPtr.Zero && next!=hostHandle && i<256;i++)
            {
                IntPtr candidate=next;
                next=GetWindow(candidate,3);
                if(next==candidate)break;
                if(candidate==handle || !IsWindowVisible(candidate) || IsIconic(candidate))continue;
                NativeRect native;
                if(!GetWindowRect(candidate,out native))continue;
                // The estimated outer rect is a fast rejection before DWM and PID checks.
                if(!WindowPerchGeometry.BlocksWindowSeat(PerchBounds(native),host,paint,anchorX))continue;
                uint pid;
                if(GetWindowThreadProcessId(candidate,out pid)==0 || pid==GetCurrentProcessId() ||
                    (GetStyle(candidate,-20)&0x20)!=0 || IsPerchCloaked(candidate))continue;
                PerchRect exact;
                if(TryReadWindowBounds(candidate,out exact) &&
                    WindowPerchGeometry.BlocksWindowSeat(exact,host,paint,anchorX))return true;
            }
            return false;
        }

        void ShowPerchFallback(string reason)
        {
            TracePerch(reason);
            perchAligned=perchCovered=false;
            bool moved=!perchFallback;
            perchFallback=true;
            SetPerchPollInterval(150);
            PerchBadge(false);
            RefreshBagAttachment();
            Topmost=app.Data.AlwaysOnTop;
            if(!IsVisible)Show();
            if(moved){PlaceSaved();SetEffect(Idle,0);}
        }

        void RenderPerch(long now)
        {
            if(!perchAligned || perchPoses==null)return;
            if(pressing || (quickMenu!=null && quickMenu.IsOpen) || menu.Visible)return;
            SeatPose pose=windowSeatRoutine==null?new SeatPose(false,0):windowSeatRoutine.At(now,app.Data.Quiet,(app.FocusActive || focusCue.Pending) && HasFocusCostume);
            BitmapSource[] atlas=pose.Lounge?perchLoungePoses:pose.Swing?perchSwingPoses:pose.Activity?perchPoses:perchStillPoses;
            BitmapSource desired=atlas==null?perchPoses[0]:atlas[pose.Frame];
            if(backpackButton.Visibility!=Visibility.Collapsed)backpackButton.Visibility=Visibility.Collapsed;
            if(bubble.Visibility!=Visibility.Collapsed)bubble.Visibility=Visibility.Collapsed;
            if(parkedProp.Visibility!=Visibility.Collapsed)parkedProp.Visibility=Visibility.Collapsed;
            if(parkedMusicGuitar.Visibility!=Visibility.Collapsed)parkedMusicGuitar.Visibility=Visibility.Collapsed;
            if(PresentFocusCompanion(now,focusCue.Started || (!pose.Activity && !pose.Lounge && !pose.Swing && pose.Frame==0)))return;
            if(!object.ReferenceEquals(sprite.Source,desired))ShowPose(desired);
        }

        void SetPerchPollInterval(int milliseconds)
        {
            if(perchTimer.Interval.TotalMilliseconds!=milliseconds)
                perchTimer.Interval=TimeSpan.FromMilliseconds(milliseconds);
        }

        void ClearWindowPerch(bool returnHome)
        {
            bool had=perchTarget!=IntPtr.Zero;
            perchTarget=perchDisplay=IntPtr.Zero;
            windowSeatRoutine=null;
            perchProcess=perchThread=0;
            perchTimer.Stop();
            perchFocusGrace.Reset();perchRectGrace.Reset();
            if(perchHook!=IntPtr.Zero){UnhookWinEvent(perchHook);perchHook=IntPtr.Zero;}
            perchCallback=null;
            perchAligned=perchFallback=perchCovered=false;
            if(had)TracePerch("detach");
            PerchBadge(false);
            if(had && !disposed)RefreshBagAttachment();
            if(!had || disposed)return;
            Topmost=app.Data.AlwaysOnTop;
            if(returnHome)
            {
                if(!manuallyHidden && !fullscreenHidden && !IsVisible)Show();
                PlaceSaved();SetEffect(Idle,0);
            }
        }

        void LeaveWindowPerch() { ClearWindowPerch(true); }
        void RequestPerchExit(){LeaveWindowPerch();}

        delegate void WinEventProc(IntPtr hook,uint evt,IntPtr window,int id,int child,uint thread,uint time);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetTopWindow(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd,uint command);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(NativePoint point,uint flags);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEventProc callback,uint pid,uint tid,uint flags);
        [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] static extern int DwmPerchBounds(IntPtr hwnd,uint attr,out NativeRect value,int size);
        [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] static extern int DwmPerchFlags(IntPtr hwnd,uint attr,out int value,int size);
    }
}
