using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using DeskMate;
using ComData = System.Runtime.InteropServices.ComTypes.IDataObject;
using Forms = System.Windows.Forms;

public class StickyExternalDragChecks
{
    // A private Windows desktop has native windows and OLE, but never receives hardware input.
    // Supply the decoded raw-mouse flags and coordinates at the production dispatch boundary.
    // The drop itself traverses the real registered WPF IDropTarget from a separate process.
    [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IDropTarget
    {
        [PreserveSig] int DragEnter([MarshalAs(UnmanagedType.Interface)] ComData data, int keys, long point, ref int effect);
        [PreserveSig] int DragOver(int keys, long point, ref int effect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int Drop([MarshalAs(UnmanagedType.Interface)] ComData data, int keys, long point, ref int effect);
    }
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem
    {
        void BindToHandler(IntPtr context,ref Guid handler,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out ComData data);
    }
    [DllImport("shell32.dll", CharSet=CharSet.Unicode, PreserveSig=false)]
    static extern void SHCreateItemFromParsingName(string path,IntPtr context,ref Guid iid,out IShellItem item);
    static ComData ShellFileData(string path)
    {
        Guid iid=typeof(IShellItem).GUID, handler=new Guid("B8C0BD9F-ED24-455C-83E6-D5390C4FE8C4");
        IShellItem item;
        SHCreateItemFromParsingName(path,IntPtr.Zero,ref iid,out item);
        try
        {
            iid=typeof(ComData).GUID;
            ComData data;item.BindToHandler(IntPtr.Zero,ref handler,ref iid,out data);
            return data;
        }
        finally{Marshal.ReleaseComObject(item);}
    }
    // WPF registers a managed drop target that must execute on its owning dispatcher.
    // Use the standard OLE STA marshaler when passing that interface to the fixture.
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class WindowDropTarget : StandardOleMarshalObject, IDropTarget
    {
        readonly object inner;
        readonly Type contract;
        public WindowDropTarget(IntPtr native)
        {
            inner=Marshal.GetObjectForIUnknown(native);
            contract=inner.GetType().GetInterfaces().Single(i=>i.GUID==DropTargetId);
        }
        int Forward(string method,object[] args,ref int effect)
        {
            int result=(int)contract.GetMethod(method).Invoke(inner,args);
            effect=(int)args[args.Length-1];return result;
        }
        public int DragEnter(ComData data,int keys,long point,ref int effect){return Forward("OleDragEnter",new object[]{data,keys,point,effect},ref effect);}
        public int DragOver(int keys,long point,ref int effect){return Forward("OleDragOver",new object[]{keys,point,effect},ref effect);}
        public int DragLeave(){return (int)contract.GetMethod("OleDragLeave").Invoke(inner,null);}
        public int Drop(ComData data,int keys,long point,ref int effect){return Forward("OleDrop",new object[]{data,keys,point,effect},ref effect);}
    }

    [StructLayout(LayoutKind.Sequential)] struct XY
    {
        public int X, Y;
        public XY(int x, int y) { X=x; Y=y; }
        public long Packed { get { return ((long)Y<<32) | (uint)X; } }
    }
    [StructLayout(LayoutKind.Sequential)] struct Bounds { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Bounds rect);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(XY point);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, int message, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr GetProp(IntPtr hwnd, string name);
    [DllImport("oleacc.dll")] static extern IntPtr LresultFromObject(ref Guid iid, IntPtr w, IntPtr unknown);
    [DllImport("oleacc.dll")] static extern int ObjectFromLresult(IntPtr cookie, ref Guid iid, IntPtr w, out IntPtr unknown);
    const int EnterCommand=0x8101, DropCommand=0x8102, LeaveCommand=0x8103;
    static readonly Guid DropTargetId=typeof(IDropTarget).GUID;
    static string folder;
    static IntPtr source, paper;
    static Studio app;
    static XY origin, destination;
    static MethodInfo gesture;
    static int failures;

    sealed class DragSource : Forms.Form
    {
        IDropTarget target;
        ComData data;
        XY point;
        IntPtr expected;
        protected override void OnShown(EventArgs args)
        {
            base.OnShown(args);
            File.WriteAllText(Path.Combine(folder,"ready"),Handle.ToString());
        }
        protected override void WndProc(ref Forms.Message msg)
        {
            if(msg.Msg!=EnterCommand && msg.Msg!=DropCommand && msg.Msg!=LeaveCommand){base.WndProc(ref msg);return;}
            try
            {
                int effect=5; // DROPEFFECT_COPY | DROPEFFECT_LINK, as for a file link.
                if(msg.Msg==EnterCommand)
                {
                    var fields=File.ReadAllLines(Path.Combine(folder,"request"));
                    expected=new IntPtr(long.Parse(fields[0]));
                    point=new XY(int.Parse(fields[1]),int.Parse(fields[2]));
                    if(WindowFromPoint(point)!=expected)throw new Exception("The external source hit another window at drag enter");
                    Guid iid=DropTargetId;
                    IntPtr unknown;
                    Marshal.ThrowExceptionForHR(ObjectFromLresult(msg.WParam,ref iid,IntPtr.Zero,out unknown));
                    try{target=(IDropTarget)Marshal.GetObjectForIUnknown(unknown);}finally{Marshal.Release(unknown);}
                    data=ShellFileData(fields[3]);
                    Marshal.ThrowExceptionForHR(target.DragEnter(data,1,point.Packed,ref effect));
                    effect=5;
                    Marshal.ThrowExceptionForHR(target.DragOver(1,point.Packed,ref effect));
                }
                else
                {
                    if(msg.Msg==LeaveCommand) Marshal.ThrowExceptionForHR(target.DragLeave());
                    else
                    {
                        if(WindowFromPoint(point)!=expected)throw new Exception("The note is no longer hittable at OLE Drop");
                        Marshal.ThrowExceptionForHR(target.Drop(data,0,point.Packed,ref effect));
                    }
                    Marshal.ReleaseComObject(target);target=null;Marshal.ReleaseComObject(data);data=null;
                }
                Acknowledge(effect.ToString());
            }
            catch(Exception ex){ Acknowledge(ex.ToString()); }
        }
        void Acknowledge(string value)
        {
            File.WriteAllText(Path.Combine(folder,"reply.tmp"),value);
            File.Move(Path.Combine(folder,"reply.tmp"),Path.Combine(folder,"reply"));
        }
        protected override void OnMouseDown(Forms.MouseEventArgs args)
        {
            base.OnMouseDown(args);
            File.AppendAllText(Path.Combine(folder,"presses"),"1");
        }
    }

    [STAThread] public static int Main(string[] args)
    {
        folder=Path.GetFullPath(args[0]);
        if(args.Length>1 && args[1]=="--source")
        {
            Forms.Application.OleRequired();
            Forms.Application.Run(new DragSource{Text="External file source",ShowInTaskbar=false});
            return 0;
        }
        Directory.CreateDirectory(folder);
        Environment.SetEnvironmentVariable("DESKATELIER_DATA",folder);
        using(var child=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"\""+folder+"\" --source") {UseShellExecute=false,CreateNoWindow=true}))
        {
            try
            {
                WaitFor(()=>File.Exists(Path.Combine(folder,"ready")),"source window");
                source=new IntPtr(long.Parse(File.ReadAllText(Path.Combine(folder,"ready"))));
                app=new Studio(folder);app.StartStudio(null);
                gesture=typeof(PetWindow).GetMethod("HandleFlyoutGesture",BindingFlags.Instance|BindingFlags.NonPublic);
                if(gesture==null)throw new Exception("Missing decoded raw mouse dispatch");
                TestDrop(false,true);
                TestDrop(true,false);
                TestDrop(false,false);
                TestDrop(false,true,true);
                TestOtherGestures();
            }
            catch(Exception ex){Console.Error.WriteLine(ex);failures++;}
            finally
            {
                if(app!=null) app.Quit();
                if(!child.HasExited)
                {
                    if(source!=IntPtr.Zero)PostMessage(source,0x10,IntPtr.Zero,IntPtr.Zero);
                    if(!child.WaitForExit(2000))child.Kill();
                }
            }
        }
        return failures==0?0:1;
    }

    static void SetUp(bool pinned)
    {
        if(app.NotesDesk!=null && app.NotesDesk.IsVisible)app.NotesDesk.Close();
        app.Data.StickyPinned=pinned;
        app.Open("notes");Pump(90);
        paper=new WindowInteropHelper(app.NotesDesk).Handle;
        Bounds box;
        if(!GetWindowRect(paper,out box))throw new Exception("Cannot measure the note");
        var screen=Forms.Screen.FromHandle(paper).Bounds;
        destination=new XY((box.Left+box.Right)/2,(box.Top+box.Bottom)/2);
        int x=box.Left-screen.Left>60?box.Left-30:box.Right+30;
        origin=new XY(x,destination.Y);
        // Model a File Explorer window covering the paper as soon as it receives focus.
        int left=Math.Min(x-14,box.Left), right=Math.Max(x+14,box.Right);
        if(!SetWindowPos(source,IntPtr.Zero,left,box.Top-10,right-left,box.Bottom-box.Top+20,0x50))
            throw new Exception("Cannot position the test window");
        if(!pinned && !SetWindowPos(paper,source,0,0,0,0,0x13))
            throw new Exception("Cannot place the ordinary note behind the source");
        Pump(70);
        if(WindowFromPoint(origin)!=source)throw new Exception("External press is outside the source window");
        if(WindowFromPoint(destination)!=(pinned?paper:source))throw new Exception("Incorrect starting Z order: hit="+WindowFromPoint(destination)+", paper="+paper+", source="+source+", paper topmost="+IsTopmost()+", point="+destination.X+","+destination.Y);
    }

    static void TestDrop(bool pinned,bool releaseFirst,bool swapped=false)
    {
        SetUp(pinned);
        ushort down=(ushort)(swapped?4:1),up=(ushort)(swapped?8:2);
        string name=(swapped?"swapped-primary":pinned?"pinned":releaseFirst?"release-first":"drop-first")+".txt";
        string file=Path.Combine(folder,name);File.WriteAllText(file,name);
        int activations=0;
        EventHandler activated=delegate{activations++;};
        app.NotesDesk.Activated+=activated;
        try
        {
            Dispatch(down,origin,swapped);
            if(!Check(app.NotesDesk.IsVisible,name+" remains visible at external press"))return;
            Dispatch(0,new XY(origin.X,origin.Y+18),swapped);
            Dispatch(0,destination,swapped);
            if(!Check(WindowFromPoint(destination)==paper,name+" is reachable above the covering window"))return;
            Check(activations==0,name+" raises no additional Activated event while exposing the target");
            Check(BeginOleDrag(file)==4,name+" accepts a CF_HDROP link in the editor");
            if(releaseFirst)Dispatch(up,destination,swapped);
            Check(Request(DropCommand,IntPtr.Zero)==4,name+" reports a link to the source process");
            if(!releaseFirst)Dispatch(up,destination,swapped);
            Check(app.NotesDesk.IsVisible && app.Data.StickyPinned==pinned && IsTopmost()==pinned,name+" restores the original pin and window style");
            Check(app.Data.Notes.Any(n=>n.AttachmentPaths.Contains(file)) && File.ReadAllText(file)==name,name+" attaches the source path and preserves its contents");
            if(!pinned)
            {
                if(!releaseFirst)
                {
                    Dispatch(down,origin,swapped);
                    Dispatch(0,new XY(origin.X,origin.Y+18),swapped);
                    Dispatch(up,new XY(origin.X,origin.Y+18),swapped);
                    Check(app.NotesDesk.IsVisible && !IsTopmost(),"a second drag can start and finish immediately after the drop");
                }
                Check(WindowFromPoint(origin)==source,name+" leaves the external window reachable for the next click");
                Dispatch(down,origin,swapped);
                Check(app.NotesDesk.IsVisible,name+" waits for the primary release before dismissing");
                Dispatch(up,origin,swapped);
                Check(!app.NotesDesk.IsVisible,name+" dismisses on the next ordinary outside click");
            }
        }
        finally{app.NotesDesk.Activated-=activated;}
    }

    static int BeginOleDrag(string file)
    {
        var target=GetProp(paper,"OleDropTargetInterface");
        if(target==IntPtr.Zero)throw new Exception("WPF window did not register its OLE target");
        Guid iid=DropTargetId;
        IntPtr marshaler=Marshal.GetComInterfaceForObject(new WindowDropTarget(target),typeof(IDropTarget));
        IntPtr cookie;
        try{cookie=LresultFromObject(ref iid,IntPtr.Zero,marshaler);}finally{Marshal.Release(marshaler);}
        if(cookie.ToInt64()<=0)throw new Exception("OLE could not marshal the drop target");
        File.WriteAllLines(Path.Combine(folder,"request"),new[]{paper.ToString(),destination.X.ToString(),destination.Y.ToString(),file});
        return Request(EnterCommand,cookie);
    }

    static void TestOtherGestures()
    {
        SetUp(false);
        Dispatch(1,origin);
        SendMessage(source,0x201,new IntPtr(1),new IntPtr(0x00140014));
        Dispatch(0,new XY(origin.X+1,origin.Y));
        Dispatch(2,new XY(origin.X+1,origin.Y));
        SendMessage(source,0x202,IntPtr.Zero,new IntPtr(0x00140014));
        Pump(100);
        Check(!app.NotesDesk.IsVisible && File.ReadAllText(Path.Combine(folder,"presses"))=="1","outside click with one pixel of motion dismisses and reaches its source once");
        SetUp(false);
        Dispatch(1,origin);
        Dispatch(0,new XY(origin.X,origin.Y+18));
        Dispatch(2,new XY(origin.X,origin.Y+25));
        Pump(150);
        Check(app.NotesDesk.IsVisible && !IsTopmost() && !app.Data.StickyPinned,"drag released away from the note leaves it available to retry");
        SetUp(false);
        Dispatch(1,origin);
        Dispatch(0,destination);
        if(app.NotesDesk.IsVisible && WindowFromPoint(destination)==paper)
        {
            string file=Path.Combine(folder,"cancelled.txt");File.WriteAllText(file,"cancel");
            Check(BeginOleDrag(file)==4,"cancelled drag reached the real drop target");
            Request(LeaveCommand,IntPtr.Zero);
            Dispatch(2,origin);Pump(150);
            Check(app.NotesDesk.IsVisible && !IsTopmost() && !app.Data.Notes.Any(n=>n.AttachmentPaths.Contains(file)),
                "OLE DragLeave followed by release preserves the note without adding a file");
        }
        SetUp(false);
        Dispatch(4,origin);
        Check(!app.NotesDesk.IsVisible,"outside right press dismisses the unpinned note");
        SetUp(false);
        Dispatch(16,origin);
        Check(!app.NotesDesk.IsVisible,"outside middle press dismisses the unpinned note");
        SetUp(false);
        Dispatch(1,origin,true);
        Check(!app.NotesDesk.IsVisible,"swapped physical left is secondary and dismisses on press");
        SetUp(false);
        Dispatch(1,origin);Dispatch(0,new XY(origin.X,origin.Y+18));
        Check(IsTopmost(),"drag is raised before the input session is interrupted");
        // Deliver the normal session-lock notification only to this private test window.
        SendMessage(new WindowInteropHelper(app.Pet).Handle,0x02b1,new IntPtr(7),IntPtr.Zero);
        Check(app.NotesDesk.IsVisible && !IsTopmost() && !app.Data.StickyPinned,"session interruption restores the unpinned style despite a missing mouse release");
        Dispatch(1,origin);Dispatch(2,origin);
        Check(!app.NotesDesk.IsVisible,"the first ordinary click after a session interruption dismisses the note");
        app.Open("focus");Pump(80);
        Bounds external;
        if(!GetWindowRect(source,out external))throw new Exception("Cannot measure the external window");
        var outside=new XY(external.Left+12,external.Top+12);
        if(WindowFromPoint(outside)!=source)
        {
            // Keep the source test point away from the anchored focus card.
            var screen=Forms.Screen.FromHandle(source).WorkingArea;
            SetWindowPos(source,IntPtr.Zero,screen.Left+20,screen.Top+20,160,100,0x50);
            outside=new XY(screen.Left+60,screen.Top+65);
        }
        if(WindowFromPoint(outside)!=source || !app.Desk.IsVisible)throw new Exception("Missing outside point for the focus tool");
        Dispatch(1,outside);
        Check(!app.Desk.IsVisible,"the focus tool still dismisses immediately on an outside press");
    }
    static bool IsTopmost(){return (GetWindowLong(paper,-20)&8)!=0;}
    static void Dispatch(ushort buttons,XY point,bool swapped=false)
    {
        // The diagnostic 1.8.0 build exposes the original three-argument mouse dispatch.
        var args=gesture.GetParameters().Length==3?new object[]{buttons,point.X,point.Y}:new object[]{buttons,point.X,point.Y,swapped};
        gesture.Invoke(app.Pet,args);
    }
    static bool Check(bool value,string description)
    {
        if(!value)failures++;
        Console.WriteLine((value?"PASS ":"FAIL ")+description);return value;
    }
    static int Request(int command,IntPtr cookie)
    {
        string reply=Path.Combine(folder,"reply");
        if(!PostMessage(source,command,cookie,IntPtr.Zero))throw new Exception("External source could not receive OLE command");
        WaitFor(()=>File.Exists(reply),"external OLE response");
        string value=File.ReadAllText(reply);File.Delete(reply);
        int effect;
        if(!int.TryParse(value,out effect))throw new Exception("External OLE call failed: "+value);
        return effect;
    }
    static void WaitFor(Func<bool> predicate,string description)
    {
        var timer=Stopwatch.StartNew();
        while(!predicate()){if(timer.ElapsedMilliseconds>5000)throw new TimeoutException(description);Pump(10);}
    }
    static void Pump(int ms)
    {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(ms)};
        timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
}
