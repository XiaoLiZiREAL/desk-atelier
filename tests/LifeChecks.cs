using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskMate;
using Drawing=System.Drawing;

internal static class LifeChecks
{
    static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static readonly List<string> lines=new List<string>();
    static Studio app;static PetWindow pet;static Window backdrop;
    static string target,shots,state;static IntPtr petHandle,backHandle;
    [StructLayout(LayoutKind.Sequential)] struct NativeRect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] struct NativePoint{public int X,Y;}
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    [DllImport("user32.dll")]static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);

    static void Say(string message){lines.Add(message);Console.WriteLine(message);}
    static void Need(bool yes,string message){if(!yes)throw new InvalidOperationException(message);Say("PASS "+message);}
    static void Pump(int ms)
    {
        var frame=new DispatcherFrame();var timer=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(ms)};
        timer.Tick+=delegate{timer.Stop();frame.Continue=false;};timer.Start();Dispatcher.PushFrame(frame);
    }
    static void Until(Func<bool> test,int timeout,string label)
    {
        var watch=Stopwatch.StartNew();while(!test()&&watch.ElapsedMilliseconds<timeout)Pump(45);
        Need(test(),label);
    }
    static FieldInfo Field(string name){var f=typeof(PetWindow).GetField(name,Fields);if(f==null)throw new MissingFieldException(name);return f;}
    static T Read<T>(string name){return (T)Field(name).GetValue(pet);}
    static void Put(string name,object value){Field(name).SetValue(pet,value);}
    static long PetNow(){return Read<Stopwatch>("clock").ElapsedMilliseconds;}
    static void DueSoon(){Put("ambientAt",PetNow()+100);}
    static void AgeOffer(long ms){Put("performanceStart",PetNow()-ms);}
    static void ResetPose(){typeof(PetWindow).GetMethod("FinishPerformance",Fields).Invoke(pet,null);}
    static void NoTools(){Need(!app.Desk.IsVisible && !app.Windows.OfType<Window>().Any(w=>w.IsVisible&&w!=pet&&w!=backdrop),"only the pet is visible; workbench and game windows stay closed");}

    static void BackPet()
    {
        NativeRect p;GetWindowRect(petHandle,out p);
        var backdropContent=new Border{Background=new LinearGradientBrush(Color.FromRgb(230,233,219),Color.FromRgb(199,214,195),90)};
        backdrop=new Window{Title="Life QA backdrop",Content=backdropContent,Background=Brushes.Beige,Topmost=pet.Topmost,WindowStyle=WindowStyle.None,
            ResizeMode=ResizeMode.NoResize,ShowInTaskbar=false,ShowActivated=false,WindowStartupLocation=WindowStartupLocation.Manual,Width=pet.ActualWidth,Height=pet.ActualHeight,
            Left=pet.Left,Top=pet.Top,Focusable=false};
        backdrop.Show();backHandle=new WindowInteropHelper(backdrop).Handle;
        SetWindowPos(backHandle,petHandle,p.Left,p.Top,p.Right-p.Left,p.Bottom-p.Top,0x10);
        Pump(80);
    }
    static void GuardedScreenshot(string name)
    {
        NativeRect r;if(!GetWindowRect(petHandle,out r))throw new InvalidOperationException("Pet HWND missing");
        int w=r.Right-r.Left,h=r.Bottom-r.Top;
        foreach(int x in new[]{r.Left+3,r.Right-4})
        foreach(int y in new[]{r.Top+3,r.Bottom-4})
        {
            var actual=WindowFromPoint(new NativePoint{X=x,Y=y});
            if(actual!=backHandle && actual!=petHandle)throw new InvalidOperationException("Cannot capture the private test backdrop; an unrelated window overlaps the test region");
        }
        using(var bmp=new Drawing.Bitmap(w,h))using(var graphics=Drawing.Graphics.FromImage(bmp))
        {
            graphics.CopyFromScreen(r.Left,r.Top,0,0,new Drawing.Size(w,h));bmp.Save(Path.Combine(shots,name+".png"),Drawing.Imaging.ImageFormat.Png);
        }
    }
    static BitmapSource[] Poses(string character,string action)
    {
        return (BitmapSource[])typeof(PetWindow).GetMethod("ActionFrames",Fields).Invoke(pet,new object[]{character,action});
    }
    static byte[] Pixels(BitmapSource source)
    {
        var bitmap=new FormatConvertedBitmap(source,PixelFormats.Bgra32,null,0);var result=new byte[bitmap.PixelWidth*bitmap.PixelHeight*4];
        bitmap.CopyPixels(result,bitmap.PixelWidth*4,0);return result;
    }
    static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","");}
    // Compare silhouettes and opaque-pixel color after compensating for horizontal/vertical displacement.
    static double Difference(byte[] a,byte[] b,int width,int height,out double bestOutline)
    {
        bestOutline=1;double best=Double.MaxValue;var centers=new double[4];byte[][] frames={a,b};
        for(int n=0;n<2;n++){long sx=0,sy=0,npx=0;var f=frames[n];for(int y=1;y<height;y+=3)for(int x=1;x<width;x+=3)if(f[(y*width+x)*4+3]>80){sx+=x;sy+=y;npx++;}
            centers[n*2]=(double)sx/Math.Max(1,npx);centers[n*2+1]=(double)sy/Math.Max(1,npx);}
        int dx=(int)Math.Round(centers[2]-centers[0]),dy=(int)Math.Round(centers[3]-centers[1]);
        for(int oy=-6;oy<=6;oy+=2)for(int ox=-6;ox<=6;ox+=2)
        {
            double diff=0;int count=0,outline=0;
            for(int y=1;y<height;y+=3)for(int x=1;x<width;x+=3)
            {
                int k=(y*width+x)*4,xx=x+dx+ox,yy=y+dy+oy;
                int j=(xx>=0&&xx<width&&yy>=0&&yy<height)?(yy*width+xx)*4:-1;
                bool aa=a[k+3]>80,bb=j>=0&&b[j+3]>80;if(!aa&&!bb)continue;count++;
                if(aa!=bb){diff+=255;outline++;}
                else diff+=(Math.Abs(a[k]-b[j])+Math.Abs(a[k+1]-b[j+1])+Math.Abs(a[k+2]-b[j+2]))/3.0;
            }
            best=Math.Min(best,diff/Math.Max(1,count));bestOutline=Math.Min(bestOutline,(double)outline/Math.Max(1,count));
        }
        return best;
    }
    static void CheckArt(string id,bool miner)
    {
        var keys=new List<string>{"tea","game","focus","rest","song"};if(miner)keys.Add("mine");
        foreach(var key in keys)
        {
            var frames=Poses(id,key);Need(frames!=null&&frames.Length==6,id+" / "+key+" six authored frames embedded and loadable");
            var hashes=new HashSet<string>();var all=new List<byte[]>();var feet=new List<int>();
            foreach(var image in frames)
            {
                if(image.PixelWidth!=192||image.PixelHeight!=208)throw new Exception(id+"/"+key+" wrong frame dimensions");
                byte[] px=Pixels(image);int visible=0,lowest=0;
                for(int i=3;i<px.Length;i+=4)if(px[i]>36){visible++;lowest=Math.Max(lowest,(i/4)/192);}
                if(visible<1500||visible>37000)throw new Exception(id+"/"+key+" invalid alpha coverage");
                all.Add(px);hashes.Add(Hash(px));feet.Add(lowest);
            }
            double minimum=Double.MaxValue;var outlines=new List<double>();
            for(int i=0;i<6;i++){double shape;minimum=Math.Min(minimum,Difference(all[i],all[(i+1)%6],192,208,out shape));outlines.Add(shape);}
            Need(hashes.Count==6&&minimum>3.0&&outlines.Count(v=>v>.02)>=2,id+"/"+key+" poses differ after alignment; lowest pair score "+minimum.ToString("F1")+"/255, changed silhouettes "+outlines.Min().ToString("P1")+"–"+outlines.Max().ToString("P1")+", alpha baseline "+feet.Min()+"–"+feet.Max());
        }
    }
    static void ObserveFirstTea()
    {
        var start=Stopwatch.StartNew();var frames=Poses("rem","tea");var seen=new HashSet<int>();var order=new List<int>();
        bool began=false;
        while(start.Elapsed.TotalSeconds<38)
        {
            Pump(38);
            if(pet.CurrentActivity=="tea")
            {
                began=true;int index=Array.IndexOf(frames,pet.LiveSprite.Source);
                if(index<0)throw new Exception("Runtime tea source is not a packaged authored pose");
                if(order.Count==0||order[order.Count-1]!=index)order.Add(index);
                if(seen.Add(index))GuardedScreenshot("natural-tea-"+index);
            }
            else if(began)break;
        }
        Need(seen.Count==6 && order.Count>=6,"fresh idle pet, zero inputs, naturally performed full tea score in "+start.Elapsed.TotalSeconds.ToString("F1")+" s; actual indices "+String.Join(",",order));
        Need(!pet.OfferingGame && app.Data.MaidRoutine==1,"natural tea returns to idle and advances persisted sequence");
        NoTools();
    }
    static void GetGame()
    {
        // The first complete real cycle above uses the unmodified scheduler. Later branches only shorten its intermission.
        DueSoon();Until(()=>pet.CurrentActivity=="game"&&pet.OfferingGame,2000,"next scheduled activity opens an on-pet chest game");
    }
    static void ObserveGames()
    {
        int count=app.Data.GameRounds,wins=app.Data.GamesWon;GetGame();
        Until(()=>Read<Button[]>("boxButtons")[0].IsHitTestVisible,3700,"chests become clickable after the hiding action");
        GuardedScreenshot("on-pet-chests");NoTools();
        int answer=Read<int>("hiddenBox");var button=Read<Button[]>("boxButtons")[answer];
        Need(AutomationProperties.GetName(button).Contains("盒子"),"real chest UI has an accessible action");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,button));Pump(100);
        Need(!pet.OfferingGame&&app.Data.GameRounds==count+1&&app.Data.GamesWon==wins+1,"correct chest scores exactly once");
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent,button));Need(app.Data.GameRounds==count+1,"repeat click after reveal is idempotent");
        GuardedScreenshot("on-pet-reveal");Until(()=>pet.CurrentActivity!="game",3000,"game result closes itself");
        var saved=Store.Load();Need(saved.GameRounds==count+1&&saved.GamesWon==wins+1&&saved.MaidRoutine==2,"score and next routine persist to disk");
        app.Data.MaidRoutine=1;GetGame();AgeOffer(14300);Pump(180);Need(!pet.OfferingGame,"unanswered game reveals its answer");GuardedScreenshot("unanswered-reveal");
        Until(()=>pet.CurrentActivity!="game",3000,"unanswered result clears without opening a tool");
        Need(app.Data.GameRounds==count+1&&app.Data.GamesWon==wins+1,"ignoring costs no score and creates no extra round");NoTools();
    }
    static void Blockers()
    {
        app.Data.MaidRoutine=1;GetGame();app.Data.Quiet=true;pet.Sync();Pump(100);
        DueSoon();Pump(400);Need(!pet.OfferingGame&&pet.CurrentActivity!="game","quiet interrupts and suppresses invitations");
        app.Data.Quiet=false;pet.Sync();app.Data.MaidRoutine=1;GetGame();
        app.StartFocus(2);Pump(220);Need(!pet.OfferingGame,"starting focus interrupts an invitation");
        ResetPose();DueSoon();Pump(350);Need(pet.CurrentActivity=="focus"&&!pet.OfferingGame,"focus allows work gestures without new invitations");
        app.CancelFocus();ResetPose();
        app.Data.MaidRoutine=1;GetGame();
        app.Music.StartDemo();Pump(350);Need(app.Music.Playing&&!pet.OfferingGame,"playing real demo audio interrupts an invitation");
        DueSoon();Pump(350);Need(!pet.OfferingGame&&pet.CurrentActivity!="game","music suppresses additional game invitations");
        app.Music.ExitDemo();ResetPose();
        app.Data.MaidRoutine=1;GetGame();var cursor=new NativePoint();GetCursorPos(out cursor);
        NativeRect pr;GetWindowRect(petHandle,out pr);
        SetPoint("pressPoint",cursor.X-8,cursor.Y);SetPoint("pressWindow",pr.Left,pr.Top);Put("pressing",true);
        pet.LiveSprite.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseMoveEvent});
        Need(!pet.OfferingGame&&pet.CurrentActivity!="game","real mouse-move handler cancels invitation on drag without moving system pointer");
        Put("pressing",false);Put("dragging",false);SetWindowPos(petHandle,IntPtr.Zero,pr.Left,pr.Top,0,0,0x1|0x4|0x10);
        app.Data.MaidRoutine=1;GetGame();app.Data.Character="kurisu";pet.Sync();Need(!pet.OfferingGame&&pet.CurrentActivity!="game","switching character cancels the old action");
        DueSoon();Until(()=>pet.CurrentActivity=="focus",2000,"Kurisu starts her own notebook routine");
        var pose=pet.LiveSprite.Source;Need(Poses("kurisu","focus").Any(im=>Object.ReferenceEquals(im,pose)),"Kurisu displays her own focus artwork");CaptureSignature("kurisu","focus");
    }
    static void ScheduleVisit()
    {
        app.Data.Character="rem";pet.Sync();ResetPose();app.Data.MaidRoutine=4;DueSoon();
        Until(()=>pet.CurrentActivity=="visit",2200,"real scheduler starts an unattended second-pet visit");
        Need(app.Windows.OfType<Window>().Any(w=>w.IsVisible && w.Title=="桌边来客"),"guest is a rendered desktop visitor");
    }
    static void GuestGone(string reason)
    {
        Until(()=>pet.CurrentActivity!="visit" && !app.Windows.OfType<Window>().Any(w=>w.IsVisible && w.Title=="桌边来客"),1800,reason+" closes the active unattended visit and removes guest window");
    }
    static void VisitAndJournal()
    {
        ScheduleVisit();app.Data.Quiet=true;pet.Sync();GuestGone("quiet mode");app.Data.Quiet=false;pet.Sync();
        ScheduleVisit();app.Open("focus");GuestGone("opening an explicit tool");app.Desk.Hide();Pump(100);
        ScheduleVisit();app.StartFocus(2);GuestGone("active focus");app.CancelFocus();ResetPose();
        ScheduleVisit();app.Music.StartDemo();GuestGone("playing music");app.Music.ExitDemo();ResetPose();
        string marker="QA · 留下的用户手记";app.Remember(marker);
        for(int i=0;i<25;i++)app.RememberActivity("QA · 自动记录 "+i);
        app.RememberActivity("QA · 自动记录 24");
        var saved=Store.Load();
        Need(saved.Journal.Any(e=>!e.Automatic&&e.Text==marker)&&saved.Journal.Any(e=>!e.Automatic&&e.Text.Contains("猜中了")),"earlier user entries survive 25 later autonomous journal events and reload");
        Need(saved.Journal.Count(e=>e.Automatic)==18&&saved.Journal.Count(e=>e.Text=="QA · 自动记录 24")==1,"automatic journal entries cap separately and deduplicate the same daily event");
    }
    static void CaptureSignature(string character,string action)
    {
        var frames=Poses(character,action);var seen=new HashSet<int>();var order=new List<int>();var sw=Stopwatch.StartNew();
        while(pet.CurrentActivity==action && sw.Elapsed.TotalSeconds<20)
        {
            int pose=Array.IndexOf(frames,pet.LiveSprite.Source);
            if(pose<0)throw new Exception(character+" rendered unrelated artwork during "+action);
            if(order.Count==0||order[order.Count-1]!=pose)order.Add(pose);
            if(seen.Add(pose))GuardedScreenshot(character+"-"+action+"-"+pose);
            Pump(27);
        }
        Need(seen.Count==6 && pet.CurrentActivity!=action,character+" completed "+action+" and cleared its action, actual frames "+String.Join(",",order));
    }
    static void SetPoint(string key,int x,int y)
    {
        var f=Field(key);object p=Activator.CreateInstance(f.FieldType);f.FieldType.GetField("X").SetValue(p,x);f.FieldType.GetField("Y").SetValue(p,y);f.SetValue(pet,p);
    }
    [STAThread]public static int Main(string[] args)
    {
        target=Path.GetFullPath(args.Length>0?args[0]:"life-qa");shots=Path.Combine(target,"life-screens");Directory.CreateDirectory(shots);
        state=Path.Combine(target,"isolated-life-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(state);
        Environment.SetEnvironmentVariable("DESKATELIER_DATA",state);bool ok=false;
        try
        {
            Say("Executable SHA256 "+Hash(File.ReadAllBytes(typeof(Studio).Assembly.Location)));
            app=new Studio(state);app.StartStudio(null);pet=app.Pet;petHandle=new WindowInteropHelper(pet).Handle;Pump(150);
            Need(pet.IsVisible&&!app.Desk.IsVisible && app.Windows.OfType<Window>().Count(w=>w.IsVisible)==1,"default first launch shows only the desktop pet");BackPet();
            ObserveFirstTea();ObserveGames();Blockers();VisitAndJournal();CheckArt("rem",false);CheckArt("kurisu",false);
            bool pending=args.Length<2||!String.Equals(args[1],"True",StringComparison.OrdinalIgnoreCase);
            if(!pending || File.Exists(app.Asset("motion-steve-mine.png"))){CheckArt("steve",true);app.Data.Character="steve";pet.Sync();DueSoon();Until(()=>pet.CurrentActivity=="mine",2200,"Steve autonomously mines using his character-specific schedule");Need(Poses("steve","mine").Contains(pet.LiveSprite.Source),"live Steve sprite is the mining pose");CaptureSignature("steve","mine");Need(app.Data.MinerRoutine==1,"Steve mining progress persists separately from other partners");}
            else Say("Steve is not embedded in this intermediate candidate; pass -RequireSteve for release verification.");
            Say("ALL LIFE CHECKS PASSED");ok=true;
        }
        catch(Exception e){Say("FAIL "+e.GetType().Name+" "+e.Message);Say(e.StackTrace??"");}
        finally{if(backdrop!=null)backdrop.Close();if(app!=null)try{app.Quit();}catch(Exception e){Say("Cleanup "+e.Message);}File.WriteAllLines(Path.Combine(target,"life-report.txt"),lines.ToArray(),new UTF8Encoding(true));}
        return ok?0:1;
    }
}



