using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DeskMate;
using Forms = System.Windows.Forms;

internal static partial class DismissalChecks
{
    [StructLayout(LayoutKind.Sequential)] struct XY { public int X, Y; }
    [DllImport("user32.dll")] static extern bool GetCursorPos(out XY point);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(XY point);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr info);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd, StringBuilder title, int length);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder title, int length);
    static int failures;
    static XY last, saved;
    static Studio app;

    [STAThread] public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--target")
        {
            int clicks=0;
            var form=new Forms.Form { Text="Dismissal target 0", TopMost=true, ShowInTaskbar=false,
                FormBorderStyle=Forms.FormBorderStyle.FixedToolWindow, StartPosition=Forms.FormStartPosition.Manual,
                Bounds=new System.Drawing.Rectangle(48,48,250,130) };
            form.MouseDown+=delegate { form.Text="Dismissal target "+(++clicks); };
            InstallTestDragSource(form,Path.GetDirectoryName(args[1]));
            form.Shown+=delegate { File.WriteAllText(args[1],form.Handle.ToInt64().ToString()); };
            Forms.Application.Run(form);
            return 0;
        }
        GetCursorPos(out saved);
        var folder=Path.GetFullPath(Path.Combine(args[0],"state-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("DESKATELIER_DATA",folder);
        var ready=Path.Combine(folder,"fixture.txt");
        using(var target=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--target \""+ready+"\"") {UseShellExecute=false,CreateNoWindow=true}))
        {
            try
            {
                var startup=Stopwatch.StartNew();
                do { Pump(50); } while(!File.Exists(ready)&&!target.HasExited&&startup.ElapsedMilliseconds<5000);
                IntPtr outside=File.Exists(ready)?new IntPtr(Int64.Parse(File.ReadAllText(ready))):IntPtr.Zero;
                if(outside==IntPtr.Zero)throw new InvalidOperationException("No test target");
                app=new Studio(folder);app.StartStudio(null);Pump(300);
                if(args.Contains("--desktop-only")) { CheckDesktop();return failures==0?0:1; }
                Click(outside,new Point(110,110),false);
                var menu=OpenMenu();
                int before=Count(outside);
                Click(outside,new Point(110,110),false);
                Check(!menu.IsOpen,"left click on an already-active outside window closes pet menu");
                Check(Count(outside)==before+1,"outside window receives that first click");
                menu.IsOpen=false;

                app.Open("notes");Pump(180);
                var box=Find<TextBox>(app.Desk);
                ClickElement(box);
                box.Text="leave my draft here";
                Check(app.Desk.IsVisible && box.IsKeyboardFocusWithin,"inside editor keeps card and input focus");
                var combo=Find<ComboBox>(app.Desk);
                combo.IsDropDownOpen=true;Pump(100);
                var option=(ComboBoxItem)combo.ItemContainerGenerator.ContainerFromIndex(2);
                ClickElement(option);
                Check(app.Desk.IsVisible && combo.SelectedIndex==2,"popup option is inside the card interaction");
                combo.IsDropDownOpen=true;Pump(80);
                before=Count(outside);
                Click(outside,new Point(110,110),false);
                Check(!app.Desk.IsVisible && !combo.IsDropDownOpen,"click outside a topmost note card with its popup open dismisses both");
                Check(Count(outside)==before+1 && GetForegroundWindow()==outside,"outside window receives focus and its first click");
                app.Open("notes");Pump(120);
                Check(Find<TextBox>(app.Desk).Text=="leave my draft here","reopening preserves the unfinished note");
                CheckOwnedPicker(outside);
                menu=OpenMenu();
                Check(!app.Desk.IsVisible,"right-clicking the pet dismisses the previous tool before choosing another");
                var gift=menu.Items.OfType<MenuItem>().Single(i=>Object.Equals(i.Header,"礼物"));
                ClickElement(gift);
                Check(app.Desk.IsVisible && app.Desk.CurrentTool=="gift","first-level gift button opens its tool");
                Click(outside,new Point(110,110),true);
                Check(!app.Desk.IsVisible,"outside right click dismisses the tool");

                app.Open("focus");Pump(100);
                app.StartFocus(25);
                // Some programmatic opens never acquire the foreground. Simulate
                // that state using only our own separate test window.
                SetForegroundWindow(outside);Pump(120);
                Check(GetForegroundWindow()==outside && app.Desk.IsVisible,"external process already has keyboard focus while tool is open");
                before=Count(outside);
                Click(outside,new Point(110,110),false,2);
                Check(!app.Desk.IsVisible && Count(outside)==before+2,"two immediate clicks reach the outside window and dismiss the card");
                Check(app.FocusActive && app.RemainingSeconds>1400,"dismissing the timer preserves the running focus period");
                CheckAttachments(outside,folder);
                if(args.Contains("--desktop"))CheckDesktop();
            }
            catch(Exception e){ failures++;Console.WriteLine("FAIL "+e); }
            finally
            {
                if(app!=null)app.Quit();
                if(!target.HasExited){if(File.Exists(ready))PostMessage(new IntPtr(Int64.Parse(File.ReadAllText(ready))),0x10,IntPtr.Zero,IntPtr.Zero);if(!target.WaitForExit(2000))target.Kill();}
                XY p;if(GetCursorPos(out p)&&p.X==last.X&&p.Y==last.Y)SetCursorPos(saved.X,saved.Y);
            }
        }
        return failures==0?0:1;
    }
    static void CheckOwnedPicker(IntPtr outside)
    {
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(80) };
        var watch=Stopwatch.StartNew();
        bool observed=false;
        IntPtr owner=new WindowInteropHelper(app.Desk).Handle;
        timer.Tick+=delegate {
            IntPtr active=GetForegroundWindow();
            var kind=new StringBuilder(100);GetClassName(active,kind,kind.Capacity);
            if(kind.ToString()=="#32770" && GetWindow(active,4)==owner)
            {
                timer.Stop();
                Click(outside,new Point(110,110),false);
                observed=app.Desk.IsVisible;
                PostMessage(active,0x10,IntPtr.Zero,IntPtr.Zero);
            }
            else if(watch.ElapsedMilliseconds>5000) { timer.Stop(); throw new InvalidOperationException("Test picker did not become foreground"); }
        };
        timer.Start();
        new Microsoft.Win32.OpenFileDialog {Title="Dismissal QA"}.ShowDialog(app.Desk);
        Pump(120);
        Check(observed && app.Desk.IsVisible,"outside click during native file picker preserves the dialog owner and returning card");
    }
    static void CheckDesktop()
    {
        app.Open("settings");Pump(140);
        uint shellProcess;GetWindowThreadProcessId(GetShellWindow(),out shellProcess);
        foreach(var screen in Forms.Screen.AllScreens){
            var area=screen.WorkingArea;
            for(int y=area.Top+55;y<area.Bottom-30;y+=110)
            for(int x=area.Left+55;x<area.Right-30;x+=120)
            {
                var point=new XY{X=x,Y=y};var hit=WindowFromPoint(point);
                uint owner;GetWindowThreadProcessId(hit,out owner);
                if(shellProcess==0||owner!=shellProcess)continue;
                var root=GetAncestor(hit,2);string shell=ClassOf(root);
                if(shell!="WorkerW"&&shell!="Progman")continue;
                if(hit!=root){
                    if(ClassOf(hit)!="SysListView32")continue;
                    // Leave real desktop icons and any existing selection alone.
                    if(SendMessage(hit,0x1032,IntPtr.Zero,IntPtr.Zero)!=IntPtr.Zero)continue;
                    var element=AutomationElement.FromPoint(new Point(x,y));
                    if(element==null||element.Current.ControlType!=ControlType.List)continue;
                }
                Click(hit,new Point(x,y),false,1,true);
                Check(!app.Desk.IsVisible,"left click on confirmed empty Explorer desktop background dismisses settings");
                return;
            }
        }
        Console.WriteLine("SKIP real desktop: no unobstructed, unselected Explorer list background was available");
    }
    static string ClassOf(IntPtr window)
    {
        var value=new StringBuilder(64);GetClassName(window,value,value.Capacity);return value.ToString();
    }
    static ContextMenu OpenMenu()
    {
        var location=ExposedPoint(app.Pet.LiveSprite);
        Click(new WindowInteropHelper(app.Pet).Handle,location,true);
        var menu=(ContextMenu)typeof(PetWindow).GetField("quickMenu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(app.Pet);
        if(menu==null||!menu.IsOpen)throw new InvalidOperationException("Pet right click did not open menu");
        return menu;
    }
    static int Count(IntPtr hwnd)
    {
        var title=new StringBuilder(64);GetWindowText(hwnd,title,title.Capacity);
        return Int32.Parse(title.ToString().Split(' ').Last());
    }
    static T Find<T>(DependencyObject root,Func<T,bool> matchElement=null) where T:FrameworkElement
    {
        var queue=new Queue<DependencyObject>();queue.Enqueue(root);
        while(queue.Count>0)
        {
            var item=queue.Dequeue();
            var match=item as T;if(match!=null&&match.IsVisible&&(matchElement==null||matchElement(match)))return match;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++)queue.Enqueue(VisualTreeHelper.GetChild(item,i));
        }
        throw new InvalidOperationException("No visible "+typeof(T).Name);
    }
    static void ClickElement(FrameworkElement element)
    {
        var source=(HwndSource)PresentationSource.FromVisual(element);
        var point=element.PointToScreen(new Point(element.ActualWidth/2,element.ActualHeight/2));
        Click(source.Handle,point,false);
    }
    static void Click(IntPtr expected,Point location,bool right,int count=1,bool exactOnly=false)
    {
        last=new XY { X=(int)location.X,Y=(int)location.Y };
        if(!SetCursorPos(last.X,last.Y))throw new InvalidOperationException("Test cursor unavailable");
        Pump(55);
        var hit=WindowFromPoint(last);
        if(hit!=expected && (exactOnly || GetAncestor(hit,2)!=expected))throw new InvalidOperationException("Test point is not on the expected test window");
        for(int i=0;i<count;i++){
            mouse_event(right?8u:2u,0,0,0,UIntPtr.Zero);
            mouse_event(right?16u:4u,0,0,0,UIntPtr.Zero);
        }
        Pump(180);
    }
    static void Check(bool condition,string message){if(!condition)failures++;Console.WriteLine((condition?"PASS ":"FAIL ")+message);}
    static void Pump(int ms)
    {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};
        timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
}
