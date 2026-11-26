using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskMate;

internal static class FocusDialChecks
{
    [StructLayout(LayoutKind.Sequential)] struct XY { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out XY point);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(XY point);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
    static Studio app;
    static XY latest;

    static void Pump(int ms)
    {
        var frame=new DispatcherFrame();
        var timer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(ms)};
        timer.Tick+=delegate {timer.Stop();frame.Continue=false;};
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    static IEnumerable<T> Elements<T>(DependencyObject root) where T:FrameworkElement
    {
        var match=root as T;
        if(match!=null && match.IsVisible)yield return match;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var item in Elements<T>(VisualTreeHelper.GetChild(root,i))) yield return item;
    }
    static RangeBase Dial()
    {
        return Elements<RangeBase>(app.Desk).Single(x=>AutomationProperties.GetAutomationId(x)=="FocusDurationDial");
    }
    static void Click(string name)
    {
        var button=Elements<Button>(app.Desk).Single(b=>AutomationProperties.GetName(b)==name);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,button));
        Pump(80);
    }
    static void Check(bool valid,string text)
    {
        if(!valid)throw new Exception(text);
        Console.WriteLine("PASS "+text);
    }
    static void Screenshot(string path)
    {
        var root=(FrameworkElement)app.Desk.Content;
        root.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);
        bitmap.Render(root);
        var image=new PngBitmapEncoder();image.Frames.Add(BitmapFrame.Create(bitmap));
        using(var stream=File.Create(path))image.Save(stream);
    }
    static void PointAt(RangeBase dial,double angle)
    {
        double radius=dial.ActualWidth*.406;
        var screen=dial.PointToScreen(new Point(dial.ActualWidth/2+radius*Math.Sin(angle*Math.PI/180),
            dial.ActualHeight/2-radius*Math.Cos(angle*Math.PI/180)));
        var target=new XY{X=(int)Math.Round(screen.X),Y=(int)Math.Round(screen.Y)};
        if(WindowFromPoint(target)!=new WindowInteropHelper(app.Desk).Handle)throw new Exception("Dial sample is obstructed; stopping before input");
        if(!SetCursorPos(target.X,target.Y))throw new Exception("Cannot move test cursor");
        latest=target;Pump(40);
        if(WindowFromPoint(target)!=new WindowInteropHelper(app.Desk).Handle)throw new Exception("Dial no longer owns the input target");
    }
    static void PhysicalTurns(RangeBase dial)
    {
        XY original;GetCursorPos(out original);
        bool down=false;
        try
        {
            dial.Value=59;
            int angle=354;
            PointAt(dial,angle);
            mouse_event(2,0,0,0,UIntPtr.Zero);down=true;Pump(80);
            Check(dial.IsMouseCaptured,"pressing the ring captures the pointer");
            PointAt(dial,angle+=18);
            Check(dial.Value==62,"clockwise drag crosses twelve o'clock from 59 to 62 minutes");
            PointAt(dial,angle-=30);
            Check(dial.Value==57,"reverse drag crosses the seam without a jump");
            for(int i=0;i<14;i++) PointAt(dial,angle+=60);
            Check(dial.Value==180,"continuous multi-turn adjustment clamps at 180 minutes");
            PointAt(dial,angle-=6);
            Check(dial.Value==179,"reversing away from the upper limit responds immediately");
            mouse_event(4,0,0,0,UIntPtr.Zero);down=false;Pump(60);
            Check(!dial.IsMouseCaptured,"releasing the dial releases pointer capture");
            mouse_event(0x800,0,0,120,UIntPtr.Zero);Pump(60);
            Check(dial.Value==180,"mouse wheel changes duration by one minute");
        }
        finally
        {
            if(down)mouse_event(4,0,0,0,UIntPtr.Zero);
            XY after;if(GetCursorPos(out after) && after.X==latest.X && after.Y==latest.Y)SetCursorPos(original.X,original.Y);
        }
    }
    [STAThread] public static int Main(string[] args)
    {
        try
        {
            string output=Path.GetFullPath(args[0]);Directory.CreateDirectory(output);
            string data=Path.Combine(output,"state-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(data);
            Environment.SetEnvironmentVariable("DESKATELIER_DATA",data);
            app=new Studio(data);app.Data.HideFullscreen=false;app.StartStudio(null);app.Open("focus");Pump(150);
            var dial=Dial();
            var provider=(IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement(dial).GetPattern(PatternInterface.RangeValue);
            Check(provider.Minimum==1 && provider.Maximum==180 && provider.Value==25,"fresh dial exposes the 1–180 minute range and starts at 25");
            var labels=Elements<TextBlock>(app.Desk).Select(t=>t.Text.Trim()).ToArray();
            Check(!Elements<TextBox>(app.Desk).Any() && !Elements<Expander>(app.Desk).Any()
                && !labels.Any(s=>s=="专注记录" || s.StartsWith("15 分钟") || s.StartsWith("25 分钟") || s.StartsWith("45 分钟"))
                && Elements<Button>(app.Desk).Count()==3,
                "focus page contains only back, close and start buttons and no text field, presets or records");
            Screenshot(Path.Combine(output,"focus-dial.png"));
            if(args.Contains("--pointer"))PhysicalTurns(dial);
            provider.SetValue(126);
            Check(Elements<TextBlock>(app.Desk).Any(t=>t.Text=="126:00"),"accessible range changes the three-digit clock");
            app.Desk.Close();app.Open("focus");Pump(80);
            Check(Dial().Value==126,"reopening the card keeps the chosen duration");
            Click("开始专注");
            Check(app.FocusActive && app.Data.PlannedMinutes==126 && !Dial().IsEnabled,"start locks duration for the active session");
            Click("暂停");int paused=app.RemainingSeconds;
            Check(app.FocusPaused && !Dial().IsEnabled,"pausing preserves the locked dial");
            Screenshot(Path.Combine(output,"focus-paused.png"));
            app.Desk.Close();Pump(80);app.Open("focus");Pump(100);
            Check(app.FocusPaused && app.RemainingSeconds==paused,"closing a paused card preserves its exact remaining time");
            Click("继续专注");
            Check(app.FocusActive && !app.FocusPaused && app.Data.PlannedMinutes==126,"continue resumes the same session");
            app.CancelFocus();app.Desk.Tick();Pump(80);
            Check(!app.FocusActive && Dial().IsEnabled,"ending the session restores the editable dial");
            return 0;
        }
        catch(Exception ex) {Console.Error.WriteLine(ex);return 1;}
        finally {if(app!=null)app.Quit();}
    }
}
