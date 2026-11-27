using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskMate;

internal static class DuetChecks
{
    static readonly BindingFlags fields=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static Studio app;
    static PetWindow pet;
    static object scene;
    static string output;
    static T Read<T>(object obj,string name){return (T)obj.GetType().GetField(name,fields).GetValue(obj);}
    static void Call(string method,params object[] args){pet.GetType().GetMethod(method,fields).Invoke(pet,args);}
    static void Need(bool good,string message){if(!good)throw new Exception(message);Console.WriteLine("PASS "+message);}
    static void Advance(int milliseconds){Call("UpdateVisit",Read<long>(scene,"Since")+milliseconds);}
    static void Flush(){Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle,new Action(delegate{}));}
    static Point Center(Image im){return new Point(Canvas.GetLeft(im)+im.Width/2,Canvas.GetTop(im)+im.Height/2);}
    static bool MatchesStep(Image im,BitmapSource expected,bool left)
    {
        if(!left)return im.Source==expected;
        var flip=im.Source as TransformedBitmap;
        return flip!=null && flip.Source==expected && flip.Transform.Value.M11<0;
    }
    static Rect VisibleInk(Image im)
    {
        var source=new FormatConvertedBitmap((BitmapSource)im.Source,PixelFormats.Bgra32,null,0);
        int w=source.PixelWidth,h=source.PixelHeight,left=w,right=-1,top=h,bottom=-1;var rgba=new byte[w*h*4];
        source.CopyPixels(rgba,w*4,0);
        for(int y=0;y<h;y++)for(int x=0;x<w;x++)if(rgba[(y*w+x)*4+3]>=128){
            left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y);
        }
        if(right<left)throw new Exception("empty actor");
        return new Rect(im.PointToScreen(new Point(im.Width*left/w,im.Height*top/h)),
            im.PointToScreen(new Point(im.Width*(right+1)/w,im.Height*(bottom+1)/h)));
    }
    static Rect WindowArea(Window window)
    {
        var root=(FrameworkElement)window.Content;
        return new Rect(root.PointToScreen(new Point()),root.PointToScreen(new Point(root.ActualWidth,root.ActualHeight)));
    }
    static void CheckSpacing(Image actor,int time)
    {
        Advance(time);pet.UpdateLayout();Read<Window>(scene,"Layer").UpdateLayout();
        var host=VisibleInk(pet.LiveSprite);var guest=VisibleInk(actor);
        Need(host.Right<=guest.Left || guest.Right<=host.Left,"both silhouettes remain visible at "+time+" ms");
    }
    static void CheckSteps(Image im,BitmapSource[] steps,int start,int duration,bool left)
    {
        Advance(start+160);bool turning=MatchesStep(im,steps[1],left);
        Advance(start+320);bool stride=MatchesStep(im,steps[3],left);
        Advance(start+duration-190);bool braking=MatchesStep(im,steps[4],left);
        Advance(start+duration-60);bool planted=MatchesStep(im,steps[5],left);
        Need(turning && stride && braking && planted,"walking turns, strides, brakes and plants both feet toward "+(left?"left":"right"));
    }

    // Composite the two real, transparent WPF windows in their actual screen positions.
    // This keeps the test desktop out of the artwork and includes the independently moving prop.
    static BitmapSource WindowFrame(Window window)
    {
        var root=(FrameworkElement)window.Content;root.UpdateLayout();
        var frame=new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth),(int)Math.Ceiling(root.ActualHeight),96,96,PixelFormats.Pbgra32);
        frame.Render(root);return frame;
    }
    static void Capture(Window guest,string filename)
    {
        var getDips=PresentationSource.FromVisual(pet).CompositionTarget.TransformFromDevice;
        var hostPoint=getDips.Transform(pet.PointToScreen(new Point()));
        var guestPoint=getDips.Transform(guest.PointToScreen(new Point()));
        var hostFrame=WindowFrame(pet);var guestFrame=WindowFrame(guest);
        var bounds=new Rect(hostPoint,new Size(hostFrame.Width,hostFrame.Height));
        bounds.Union(new Rect(guestPoint,new Size(guestFrame.Width,guestFrame.Height)));
        var drawing=new DrawingVisual();
        using(var dc=drawing.RenderOpen()){
            dc.DrawImage(hostFrame,new Rect(hostPoint.X-bounds.Left,hostPoint.Y-bounds.Top,hostFrame.Width,hostFrame.Height));
            dc.DrawImage(guestFrame,new Rect(guestPoint.X-bounds.Left,guestPoint.Y-bounds.Top,guestFrame.Width,guestFrame.Height));
        }
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(bounds.Width),(int)Math.Ceiling(bounds.Height),96,96,PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
        using(var f=File.Create(filename))png.Save(f);
    }
    static void Start(string id,bool natural)
    {
        app.Data.Character=id;pet.Sync();Call("FinishPerformance");
        if(natural){app.Data.GuitarRoutine=6;Call("Ambient");}
        else Call("StartVisit",true);
        scene=Read<object>(pet,"visitor");
        Need(scene!=null && Read<Window>(scene,"Layer").IsVisible && Read<bool>(scene,"Independent"),id+" starts a visible unattended visit");
        Flush();
    }
    static void Story(string id,string guestId,bool first,bool capture)
    {
        Start(id,first);var d=Read<object>(scene,"Duet");
        Need(d!=null && Read<string>(d,"Guest")==guestId,id+" meets "+guestId);
        var guest=Read<Window>(scene,"Layer");var prop=Read<Image>(d,"Prop");
        var actor=Read<Image>(scene,"Actor");var guestSteps=Read<BitmapSource[]>(scene,"GuestSteps");
        double side=Read<double>(scene,"Side");
        Need(guestSteps!=null && guestSteps.Length==6,guestId+" loads its own turnstep strip");
        CheckSteps(actor,guestSteps,0,1800,side>0);
        CheckSteps(actor,guestSteps,11200,3100,side<0);
        Need(guest.Owner==pet && !guest.IsHitTestVisible && new WindowInteropHelper(guest).Handle!=IntPtr.Zero,"visitor and independent prop are click-through");
        CheckSpacing(actor,2100);CheckSpacing(actor,5500);CheckSpacing(actor,7400);
        if(id=="steve" || id=="hitori"){
            bool steveHost=id=="steve";
            var steps=steveHost?Read<BitmapSource[]>(scene,"HostSteps"):guestSteps;
            var walker=steveHost?pet.LiveSprite:actor;bool toLeft=steveHost?side<0:side>0;
            CheckSteps(walker,steps,3650,900,toLeft);CheckSteps(walker,steps,9950,900,!toLeft);
            Advance(2100);Need(prop.Visibility!=Visibility.Visible,"held pick is drawn once in Bocchi's fingers");
            Advance(2500);var drop=Center(prop);Need(prop.IsVisible,"released pick is visible");
            Advance(4550);var floor=Center(prop);Need(floor.Y>drop.Y && Math.Abs(floor.X-drop.X)>8,"pick rolls across the floor");
            Advance(5500);var held=Center(prop);Need(held.Y<floor.Y-12 && prop.IsVisible,"Steve picks the same object up");
            Advance(6650);var toss=Center(prop);
            Advance(6820);var arc=Center(prop);Need(Math.Abs(toss.X-arc.X)>4 && arc.Y<toss.Y,"returned pick has a visible flight arc");
            Advance(7400);Need(Center(prop).Y>arc.Y+20,"returned pick lands by Bocchi");
            Advance(7700);var collected=pet.LiveSprite.Source;Need(!prop.IsVisible,"collecting the pick removes the loose sprite");
            Advance(9600);Need(id=="steve" || pet.LiveSprite.Source!=collected,"Bocchi bows after the pickup");
        }else{
            Advance(2900);Need(!prop.IsVisible,"sheet begins inside Kurisu's notebook");
            Advance(3950);var outgoing=Center(prop);Need(prop.IsVisible,"loose sheet flutters towards Rem");
            Advance(4400);Need(Math.Abs(Center(prop).X-outgoing.X)>12,"outbound page crosses the gap");
            Advance(5100);Need(!prop.IsVisible,"Rem's hands own the page");
            Advance(6900);var returning=Center(prop);Advance(7500);
            Need(prop.IsVisible && Math.Abs(Center(prop).X-returning.X)>12,"Rem returns the same page");
            Advance(8300);Need(!prop.IsVisible,"Kurisu takes the page back");
        }
        var folder=Path.Combine(output,id+"-frames");if(capture)Directory.CreateDirectory(folder);
        for(int ms=0;ms<=14200;ms+=100){
            Advance(ms);pet.UpdateLayout();guest.UpdateLayout();
            if(!WindowArea(pet).Contains(VisibleInk(pet.LiveSprite)) || !WindowArea(guest).Contains(VisibleInk(actor)))
                throw new Exception(id+" clipped during the story at "+ms+" ms");
            if(capture)Capture(guest,Path.Combine(folder,ms.ToString("D5")+".png"));
        }
        Need(true,"all 143 sampled frames keep the full opaque host and guest inside their own real windows");
        int turn=id=="steve" || id=="hitori"?1:2;
        Advance(14300);Need(!guest.IsVisible && Read<object>(pet,"visitor")==null && (Store.Load().DuetTeaTurns&turn)!=0,"completed story closes and persists its turn");
        Start(id,false);Need(Read<object>(scene,"Duet")==null,"next autonomous visit uses the original tea scene");
        actor=Read<Image>(scene,"Actor");side=Read<double>(scene,"Side");guestSteps=Read<BitmapSource[]>(scene,"GuestSteps");
        CheckSteps(actor,guestSteps,0,3000,side>0);CheckSteps(actor,guestSteps,11100,3200,side<0);
        scene.GetType().GetField("GuestSteps",fields).SetValue(scene,null);
        Advance(150);Need(actor.Source!=null && !MatchesStep(actor,guestSteps[1],side>0),"missing optional authored footsteps fall back to the base walk cycle");
        Advance(14300);Need((Store.Load().DuetTeaTurns&turn)==0,"tea completion restores story eligibility");
    }
    [STAThread] public static int Main(string[] args)
    {
        output=Path.GetFullPath(args[0]);string state=Path.Combine(output,"state-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(state);Environment.SetEnvironmentVariable("DESKATELIER_DATA",state);
        try{
            app=new Studio(state);app.Data.PetScale=1.25;app.Data.Character="hitori";app.Data.HideFullscreen=false;
            app.Data.SingAlongEnabled=false;app.Data.PetWelcomeSeen=true;app.StartStudio(null);pet=app.Pet;
            Flush();Read<DispatcherTimer>(pet,"timer").Stop();Read<DispatcherTimer>(app,"ticks").Stop();
            bool frames=Array.IndexOf(args,"--frames")>=0;
            Story("hitori","steve",true,frames);Story("kurisu","rem",false,frames);
            Call("Position",32,64);
            Story("steve","hitori",false,false);Story("rem","kurisu",false,false);
            Start("steve",false);Advance(5400);
            var guest=Read<Window>(scene,"Layer");var d=Read<object>(scene,"Duet");
            var old=Read<Transform>(d,"PreviousTransform");Call("PlayPaperAction",false);
            Need(!guest.IsVisible && Read<object>(pet,"visitor")==null && pet.LiveSprite.RenderTransform==old && pet.CurrentActivity=="handoff","giving a note closes the story and restores the host's position");
            Start("hitori",false);pet.SetKaraoke("fixture","",.25,true);Call("Tick",null,EventArgs.Empty);
            Need(Read<object>(pet,"visitor")==null && app.Data.DuetTeaTurns==0,"playing music interrupts the scene without spending its turn");
            pet.ClearKaraoke();Start("rem",false);app.Data.Quiet=true;pet.Sync();
            Need(Read<object>(pet,"visitor")==null,"quiet mode removes the visitor and loose prop");
            app.Data.Quiet=false;pet.Sync();pet.Visit();scene=Read<object>(pet,"visitor");
            Need(scene!=null && !Read<bool>(scene,"Independent") && Read<object>(scene,"Duet")==null,"manual visits preserve the tea routine");
            Console.WriteLine("ALL DUET CHECKS PASSED");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        finally{if(app!=null)app.Quit();}
    }
}
