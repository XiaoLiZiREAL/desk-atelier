using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using DeskMate;

class PaperHandoffChecks
{
    const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
    static Studio app;
    [StructLayout(LayoutKind.Sequential)] struct Pixel{public int X,Y;public Pixel(Point p){X=(int)p.X;Y=(int)p.Y;}}
    [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Pixel point);
    [DllImport("user32.dll")]static extern bool GetCursorPos(out Pixel point);
    [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
    static void Check(bool value,string test){if(!value)throw new Exception(test);Console.WriteLine("PASS "+test);}
    static IEnumerable<DependencyObject> Tree(DependencyObject root){
        yield return root;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
            foreach(var child in Tree(VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    static T Part<T>(DependencyObject root,string id) where T:DependencyObject
    {return Tree(root).OfType<T>().Single(x=>AutomationProperties.GetAutomationId(x)==id);}
    static void Click(Button button)
    {button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));}
    static void Pump(){app.Dispatcher.Invoke(new Action(delegate{}),DispatcherPriority.Background);}
    static DateTime Parsed(string value,DateTime now)
    {
        Type type=typeof(Studio).Assembly.GetType("DeskMate.PaperTime");
        object time=type.GetMethod("Read",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{value,now});
        string error=(string)type.GetField("Error",Hidden).GetValue(time);
        if(error!=null)throw new Exception(error);
        long ticks=(long)type.GetField("Utc",Hidden).GetValue(time);
        return new DateTime(ticks,DateTimeKind.Utc).ToLocalTime();
    }
    static void TimeCases(){
        var date=new DateTime(2026,3,4,10,17,0,DateTimeKind.Local);
        Check(Parsed("下午三点拿快递",date)==date.Date.AddHours(15),"Chinese afternoon clock");
        Check(Parsed("明天下午三点半 拿快递",date)==date.Date.AddDays(1).AddHours(15.5),"tomorrow and half hour");
        Check(Parsed("09:05 提醒我带钥匙",date)==date.Date.AddDays(1).AddHours(9).AddMinutes(5),"next occurrence across midnight");
        Check(Parsed("十五分钟后再看",date)==date.AddMinutes(15),"Chinese relative time");
        Check(Parsed("晚上十二点关灯",date)==date.Date.AddDays(1),"twelve at night means the next midnight");
        Check(Parsed("明天晚上十二点关灯",date)==date.Date.AddDays(2),"midnight at the end of tomorrow");
    }
    // Optional input test. Guard both endpoints and only permit a drop over this
    // test's pet. All coordinates come from WPF PointToScreen in physical pixels.
    static void DragOriginal()
    {
        app.Open("notes");Pump();
        var page=app.NotesDesk;
        var grip=Part<Button>(page,"GivePaper");
        Part<TextBox>(page,"StickyEditor").Text="手递便签 · 实际拖放";
        var original=app.Data.Notes.Single(n=>n.Text=="手递便签 · 实际拖放");
        if(app.Data.StickyPinned)Click(Part<Button>(page,"StickyPin"));
        var sprite=app.Pet.LiveSprite;
        Pixel from=new Pixel(grip.PointToScreen(new Point(grip.ActualWidth*.5,grip.ActualHeight*.5)));
        Pixel to=new Pixel(sprite.PointToScreen(new Point(sprite.ActualWidth*.5,sprite.ActualHeight*.55)));
        var pet=new WindowInteropHelper(app.Pet).Handle;var sheet=new WindowInteropHelper(page).Handle;
        Pixel before;if(!GetCursorPos(out before) || WindowFromPoint(from)!=sheet || WindowFromPoint(to)!=pet)
            throw new Exception("Test pet or sheet is covered; cannot perform pointer test");
        QueryContinueDragEventHandler guard=delegate(object sender,QueryContinueDragEventArgs e){
            Pixel p;if(e.Action==DragAction.Drop && (!GetCursorPos(out p) || WindowFromPoint(p)!=pet))e.Action=DragAction.Cancel;
        };
        grip.QueryContinueDrag+=guard;
        Exception error=null;
        var driver=new Thread(delegate(){
            bool pressed=false;
            try{
                if(!SetCursorPos(from.X,from.Y))throw new Exception("Pointer is unavailable");
                Thread.Sleep(120);
                if(WindowFromPoint(from)!=sheet || WindowFromPoint(to)!=pet)throw new Exception("Test endpoint moved");
                mouse_event(2,0,0,0,UIntPtr.Zero);pressed=true;Thread.Sleep(100);
                for(int i=1;i<=14;i++){
                    SetCursorPos(from.X+(to.X-from.X)*i/14,from.Y+(to.Y-from.Y)*i/14);
                    Thread.Sleep(35);
                }
                Thread.Sleep(180);
            }catch(Exception e){error=e;}
            finally{if(pressed)mouse_event(4,0,0,0,UIntPtr.Zero);}
        });
        try{
            driver.Start();
            while(driver.IsAlive){Pump();Thread.Sleep(12);}
            for(int i=0;i<10;i++){Pump();Thread.Sleep(20);}
            if(error!=null)throw error;
            Check(original.HeldByPet && app.Data.Notes.Count(n=>n.Text==original.Text)==1 && !page.IsVisible,
                "physical drag from the unpinned paper to the pet delivers its original object once");
        }finally{
            grip.QueryContinueDrag-=guard;
            Pixel end;if(GetCursorPos(out end) && end.X==to.X && end.Y==to.Y)SetCursorPos(before.X,before.Y);
        }
    }
    static void Exercise()
    {
        app.Open("notes");Pump();
        var page=app.NotesDesk;
        var editor=Part<TextBox>(page,"StickyEditor");
        var grip=Part<Button>(page,"GivePaper");
        Check(!grip.IsEnabled,"blank sheet has no handoff action");
        editor.Text="10分钟后提醒我拿快递";
        var note=app.Data.Notes.Single();
        Click(Part<Button>(page,"StickyPin"));
        Click(grip);Pump();
        long deadline=note.DueUtc;
        Check(note.HeldByPet && deadline>DateTime.UtcNow.AddMinutes(9).Ticks &&
            page.IsVisible && app.Data.Notes.Count==1 && note.Text==editor.Text,"pinned sheet is stored once with its original writing");
        int originalTone=note.PaperTone;
        page.GetType().GetField("stickyTone",Hidden).SetValue(page,(originalTone+1)%3);
        Click(grip);
        Check(note.PaperTone==originalTone,"handing back a recolored page preserves the entrusted tint");
        Check(note.DueUtc==deadline,"handing the same page twice preserves the original deadline");
        editor.Text="10分钟后提醒我拿两件快递";
        Check(note.DueUtc==deadline,"editing the body preserves the deadline");
        editor.Text="20分钟后提醒我拿快递";
        Check(note.DueUtc>deadline && !note.Notified,"editing the time reschedules the same sheet");
        Click(Part<Button>(page,"StickyFold"));editor.Text="另一个页面";
        var other=app.Data.Notes.Single(n=>!Object.ReferenceEquals(n,note));
        note.DueUtc=DateTime.UtcNow.AddSeconds(-1).Ticks;
        app.TickTools();app.TickTools();Pump();
        var receive=Part<Button>(app.Pet,"ReceivePaper");
        var later=Part<Button>(app.Pet,"SnoozePaper");
        Check(note.Notified && receive.IsVisible && editor.Text==other.Text,"deadline presents the slip without replacing an open draft");
        app.Save();var stored=Store.Load().Notes.Single(n=>n.HeldByPet);
        Check(stored.Notified && !stored.Done && stored.Text==note.Text && stored.PaperTone==note.PaperTone,
            "unclaimed original and tint survive a save and reload");
        Click(later);
        Check(!note.Notified && !receive.IsVisible && note.DueUtc>DateTime.UtcNow.AddMinutes(9).Ticks,"snooze retains the same sheet for ten minutes");
        note.DueUtc=DateTime.UtcNow.AddSeconds(-1).Ticks;app.TickTools();Click(receive);Pump();
        Check(note.Done && !note.HeldByPet && !receive.IsVisible && editor.Text==note.Text,
            "collect opens and acknowledges the exact original on its own page");
        editor.Text="我的书单";Click(grip);
        Check(note.HeldByPet && !note.Done && note.DueUtc==0,"ordinary paper can be entrusted without an alarm");
        editor.Text="10分钟后喝茶";note.DueUtc=DateTime.UtcNow.AddSeconds(-2).Ticks;app.TickTools();
        Click(Part<Button>(page,"StickyTrash"));
        Check(!receive.IsVisible && !app.Data.Notes.Contains(note),"deleting a pending reminder retracts its pet attachment");
        Click(Part<Button>(page,"StickyUndo"));
        Check(receive.IsVisible && app.Data.Notes.Contains(note),"undo restores the same pending paper");
        editor.Text="普通记录";
        Check(note.DueUtc==0 && !receive.IsVisible,"removing the time retracts a pending reminder");
        other.DueUtc=DateTime.UtcNow.AddSeconds(-1).Ticks;app.TickTools();
        Check(other.Notified && receive.IsVisible,"reminders from an older installation use the return slip");
        Click(receive);Pump();
        Check(editor.Text==other.Text && app.Data.Notes.Count==2,"returned legacy reminder opens the original page without duplicates");
        var forged=new DataObject("DeskAtelier.StickyPaper","sheet");
        Check(!app.Desk.CanAcceptPetDrop(forged),"foreign drag payload cannot claim a stored page");
        page.Close();
    }
    static void PrepareRestart()
    {
        app.Data.Notes.Clear();
        long now=DateTime.UtcNow.Ticks;
        app.Data.Notes.Add(new NoteItem{Text="上次还没收的便签",HeldByPet=true,PaperTone=2,Notified=true,DueUtc=now-100000});
        app.Data.Notes.Add(new NoteItem{Text="关机期间到点",HeldByPet=true,PaperTone=1,DueUtc=now-1});
        app.Save();
    }
    static void VerifyRestart()
    {
        var receive=Part<Button>(app.Pet,"ReceivePaper");
        Check(receive.IsVisible && app.Data.Notes.Count(n=>n.HeldByPet && n.Notified)==2,
            "new process restores a pending slip and catches up an overdue sheet");
        Click(receive);Pump();
        Check(Part<TextBox>(app.NotesDesk,"StickyEditor").Text=="上次还没收的便签" &&
            AutomationProperties.GetName(Part<Grid>(app.NotesDesk,"StickyPaper"))=="红色便签" && receive.IsVisible,
            "oldest pending paper opens with its original tint and the next slip awaits collection");
        Click(receive);Pump();
        Check(Part<TextBox>(app.NotesDesk,"StickyEditor").Text=="关机期间到点" &&
            app.Data.Notes.All(n=>n.Done) && !receive.IsVisible,"both recovered sheets are acknowledged exactly once");
    }
    [STAThread] static int Main(string[] args)
    {
        string folder=Path.GetFullPath(args[0]);
        Directory.CreateDirectory(folder);Environment.SetEnvironmentVariable("DESKATELIER_DATA",folder);
        try{
            TimeCases();app=new Studio(folder);app.Data.PetWelcomeSeen=true;app.Data.HideFullscreen=false;
            app.Data.SingAlongEnabled=false;app.StartStudio(null);
            if(args.Contains("--restart"))VerifyRestart();
            else{
                Exercise();
                if(args.Contains("--pointer"))DragOriginal();
                PrepareRestart();
            }
            return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(app!=null)app.Quit();}
    }
}
