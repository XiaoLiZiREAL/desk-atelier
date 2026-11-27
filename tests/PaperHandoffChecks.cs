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
        editor.Text="晚点提醒我浇花";Click(grip);
        var choose=Part<Button>(page,"ConfirmPaperTime");
        var clockBox=Part<TextBox>(page,"PaperClock");
        clockBox.Text="25:78";Click(choose);
        Check(!other.HeldByPet && choose.IsVisible,"ambiguous writing waits and invalid clock cannot arm an alarm");
        Click(Part<Button>(page,"CancelPaperTime"));
        Check(!other.HeldByPet && other.Text==editor.Text,"cancel leaves the user's original paper untouched");
        Click(Part<Button>(page,"SetPaperTime"));
        DateTime chosen=DateTime.Today.AddDays(2).AddHours(16).AddMinutes(35);
        Part<TextBox>(page,"PaperDate").Text=chosen.ToString("yyyy-MM-dd");
        Part<TextBox>(page,"PaperClock").Text="16:35";Click(Part<Button>(page,"ConfirmPaperTime"));
        long fixedTime=chosen.ToUniversalTime().Ticks;
        Check(other.HeldByPet && other.DueUtc==fixedTime && Store.Load().Notes.Any(n=>n.DueUtc==fixedTime),
            "manual date is attached to the original sheet and persisted");
        editor.Text="晚点提醒我浇客厅的花";Click(grip);
        Check(other.DueUtc==fixedTime,"body edits and a second handoff preserve a chosen deadline");
        editor.Text="一小时后浇花";
        Check(other.DueUtc> DateTime.UtcNow.AddMinutes(59).Ticks && other.DueUtc<fixedTime,"new written instruction replaces the chosen time");
        Click(Part<Button>(page,"SetPaperTime"));Click(Part<Button>(page,"KeepPaperOnly"));
        Check(other.HeldByPet && other.DueUtc==0,"keep-only retracts the deadline");
        other.HeldByPet=false;other.Done=false;other.PaperTimeKey=null;
        other.DueUtc=DateTime.UtcNow.AddDays(1).Date.AddHours(7).AddSeconds(43).Ticks;
        long originalQuickDrop=other.DueUtc;Click(Part<Button>(page,"SetPaperTime"));
        Click(Part<Button>(page,"ConfirmPaperTime"));
        Check(other.DueUtc==originalQuickDrop,"an existing quick-drop reminder keeps its original minute and seconds when confirmed");
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
        app.Data.Notes.Add(new NoteItem{Text="每周五下午四点交周报",HeldByPet=true,PaperRepeatKey="32/960",PaperTimeKey="repeat:32/960",
            AttachmentPaths=new List<string>{Path.Combine(app.UserFolder,"周报.xlsx")},DueUtc=now});
        app.Save();
    }
    static void VerifyRestart()
    {
        var receive=Part<Button>(app.Pet,"ReceivePaper");
        Check(receive.IsVisible && app.Data.Notes.Count(n=>n.HeldByPet && n.Notified)==3,
            "new process restores a pending slip and catches up an overdue sheet");
        Click(receive);Pump();
        Check(Part<TextBox>(app.NotesDesk,"StickyEditor").Text=="上次还没收的便签" &&
            AutomationProperties.GetName(Part<Grid>(app.NotesDesk,"StickyPaper"))=="红色便签" && receive.IsVisible,
            "oldest pending paper opens with its original tint and the next slip awaits collection");
        Click(receive);Pump();
        Check(Part<TextBox>(app.NotesDesk,"StickyEditor").Text=="关机期间到点" &&
            app.Data.Notes.Count(n=>n.Done)==2,"both recovered single-use sheets are acknowledged exactly once");
        var weekly=app.Data.Notes.Single(n=>n.PaperRepeatKey!=null);
        Check(weekly.AttachmentPaths.Count==1 && Part<Button>(app.Pet,"OpenReturnedPaperFile").IsVisible,
            "an overdue weekly note and its attached path survive process restart");
        Click(receive);app.TickTools();Pump();
        Check(!receive.IsVisible && weekly.HeldByPet && !weekly.Notified && weekly.DueUtc>DateTime.UtcNow.Ticks &&
            app.Data.Notes.Count==3,"weekly recovery advances one original paper without replaying missed reminders");
    }
    static void DropFiles(TextBox editor,params string[] paths)
    {
        var data=new DataObject(DataFormats.FileDrop,paths);
        var ctor=typeof(DragEventArgs).GetConstructors(Hidden).Single();
        var args=(DragEventArgs)ctor.Invoke(new object[]{data,DragDropKeyStates.None,DragDropEffects.Copy,editor,new Point(10,10)});
        args.RoutedEvent=DragDrop.PreviewDropEvent;editor.RaiseEvent(args);Pump();
        Check(args.Handled,"the sticky sheet handles Explorer file drops over the writing area");
    }
    static void WaitForFile(string path)
    {
        var clock=System.Diagnostics.Stopwatch.StartNew();
        while(!File.Exists(path) && clock.ElapsedMilliseconds<7000){Pump();Thread.Sleep(50);}
        Check(File.Exists(path),"clicking the attachment launches its original Windows file");
        File.Delete(path);
    }
    static void WeeklyAttachment()
    {
        app.Open("notes");Pump();
        var page=app.NotesDesk;var editor=Part<TextBox>(page,"StickyEditor");
        var grip=Part<Button>(page,"GivePaper");
        for(int i=0;grip.IsEnabled && i<=app.Data.Notes.Count;i++)Click(Part<Button>(page,"StickyFold"));
        Check(!grip.IsEnabled,"weekly attachment test starts on a fresh sheet");
        string path=Path.Combine(app.UserFolder,"周报.xlsx"),marker=Path.Combine(app.UserFolder,"paper-opened.txt");
        File.WriteAllText(path,"attachment fixture");
        string probe=typeof(PaperHandoffChecks).Assembly.Location;
        Environment.SetEnvironmentVariable("DESKATELIER_TEST_FILE_OPENED",marker);
        DropFiles(editor,path,probe,path);
        var note=app.Data.Notes.Single(n=>n.AttachmentPaths.Count==2);
        Check(note.Text=="" && grip.IsEnabled && Store.Load().Notes.Any(n=>n.AttachmentPaths.SequenceEqual(note.AttachmentPaths)),
            "an attachment-only draft saves paths once in drop order");
        editor.Text="每周五下午四点交周报";Click(grip);
        long first=note.DueUtc;
        Check(first>DateTime.UtcNow.Ticks && note.PaperRepeatKey=="32/960" && Store.Load().Notes.Any(n=>n.DueUtc==first && n.PaperRepeatKey=="32/960"),
            "handing over the original paper schedules and saves the weekly rule");
        editor.Text="每周五下午四点交财务周报";
        Check(note.DueUtc==first,"editing the report description keeps its weekly occurrence");
        note.DueUtc=DateTime.UtcNow.AddDays(-35).Ticks;app.TickTools();app.TickTools();Pump();
        var open=Part<Button>(app.Pet,"OpenReturnedPaperFile");
        Check(open.IsVisible && AutomationProperties.GetName(open).Contains("周报.xlsx"),"the overdue return slip offers the attached report");
        File.Move(path,path+".moved");Click(open);Pump();
        Check(note.Notified && open.IsVisible && editor.Text==note.Text,"missing original reopens its paper and preserves the pending reminder");
        Click(Part<Button>(page,"DetachPaperFile"));
        Check(File.Exists(path+".moved") && note.AttachmentPaths.SequenceEqual(new[]{probe}),"unlinking a moved file leaves the original alone");
        Click(Part<Button>(page,"OpenPaperFile"));WaitForFile(marker);
        Check(note.Notified,"opening a reference on the sheet does not acknowledge the waiting reminder");
        Click(open);WaitForFile(marker);
        Check(!note.Notified && !note.Done && !open.IsVisible && note.DueUtc==first && note.AttachmentPaths.Single()==probe,
            "one click opens the returned file, skips missed weeks, and keeps the original sheet");
        note.DueUtc=DateTime.UtcNow.AddSeconds(-1).Ticks;app.TickTools();Click(Part<Button>(app.Pet,"SnoozePaper"));
        long snooze=note.DueUtc;Click(Part<Button>(page,"SetPaperTime"));Click(Part<Button>(page,"ConfirmPaperTime"));
        Check(note.DueUtc==snooze && note.PaperRepeatKey=="32/960","confirming a snoozed occurrence preserves the weekly wall clock");
        Click(Part<Button>(page,"SetPaperTime"));Part<RadioButton>(page,"PaperRepeat1").IsChecked=true;Click(Part<Button>(page,"ConfirmPaperTime"));
        Check(note.PaperRepeatKey.StartsWith("127/"),"changing frequency in the clock picker arms the new cycle");
        Click(Part<Button>(page,"SetPaperTime"));Click(Part<Button>(page,"KeepPaperOnly"));
        Check(note.PaperRepeatKey==null && note.DueUtc==0,"keep-only clears future cycles and retains the file");
        editor.Text="";Click(Part<Button>(page,"DetachPaperFile"));
        Check(!app.Data.Notes.Contains(note) && File.Exists(probe),"removing the last file from an empty draft releases its slot");
        Environment.SetEnvironmentVariable("DESKATELIER_TEST_FILE_OPENED",null);
        page.Close();
    }
    [STAThread] static int Main(string[] args)
    {
        if(args.Length==0){
            // When opened as the controlled attachment fixture, leave a receipt in the isolated test profile.
            string marker=Environment.GetEnvironmentVariable("DESKATELIER_TEST_FILE_OPENED");
            if(marker==null)return 2;
            File.WriteAllText(marker,"opened");return 0;
        }
        string folder=Path.GetFullPath(args[0]);
        Directory.CreateDirectory(folder);Environment.SetEnvironmentVariable("DESKATELIER_DATA",folder);
        try{
            TimeCases();app=new Studio(folder);app.Data.PetWelcomeSeen=true;app.Data.HideFullscreen=false;
            app.Data.SingAlongEnabled=false;app.StartStudio(null);
            if(args.Contains("--restart"))VerifyRestart();
            else{
                Exercise();WeeklyAttachment();
                if(args.Contains("--pointer"))DragOriginal();
                PrepareRestart();
            }
            return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(app!=null)app.Quit();}
    }
}
