// WPF player-process probe. Renders to a bitmap only; no Window, input, or production data.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeskMate;

static class MinePlaybackProbe
{
    static readonly Queue<int> Order=new Queue<int>();
    static readonly Dictionary<int,BitmapSource[]> Cache=new Dictionary<int,BitmapSource[]>();
    static string folder;
    static long bytesStart;
    static BitmapSource[] Read(int id)
    {
        BitmapSource[] cels;
        if(Cache.TryGetValue(id,out cels))return cels;
        var watch=Stopwatch.StartNew();
        string path=Path.Combine(folder,"motion-steve-focuscave-"+MineShift.Catalog[id]+".png");
        BitmapSource atlas;
        using(var f=File.OpenRead(path))
            atlas=new PngBitmapDecoder(f,BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.OnLoad).Frames[0];
        atlas.Freeze();
        if(atlas.PixelWidth!=1152 || atlas.PixelHeight!=8736)throw new Exception("Wrong atlas dimensions");
        cels=new BitmapSource[MineShift.CelCount];
        for(int i=0;i<cels.Length;i++)
        {
            var pose=new CroppedBitmap(atlas,new Int32Rect(i%6*192,i/6*208,192,208));
            pose.Freeze();cels[i]=pose;
        }
        Cache[id]=cels;Order.Enqueue(id);
        if(Order.Count>2)Cache.Remove(Order.Dequeue());
        Console.WriteLine("decode {0} {1:F1} ms",MineShift.Catalog[id],watch.Elapsed.TotalMilliseconds);
        return cels;
    }
    static double PrivateMiB()
    {
        using(var p=Process.GetCurrentProcess()){p.Refresh();return (p.PrivateMemorySize64-bytesStart)/1048576.0;}
    }
    [STAThread]
    static int Main(string[] args)
    {
        folder=Path.GetFullPath(args[0]);
        using(var p=Process.GetCurrentProcess())bytesStart=p.PrivateMemorySize64;
        bool packed=args.Length>1 && args[1]=="--packed";
        var bank=new MineFrameCache(name=>Path.Combine(folder,name));
        var shift=new MineShift(23);int priorId=-1,priorFrame=-1,transitions=0;
        var element=new DrawingVisual();var target=new RenderTargetBitmap(192,208,96,96,PixelFormats.Pbgra32);
        var sw=Stopwatch.StartNew();
        long due=0;double maxRender=0,maxPrefetch=0,maxTransition=0;
        while(sw.ElapsedMilliseconds<shift.SectionStart(3)+2500)
        {
            // Feed the same 20Hz artwork using the app's 34ms desktop refresh cadence.
            var before=sw.ElapsedMilliseconds;
            int id=shift.Variant(before),frame=shift.Frame(before);
            var one=Stopwatch.StartNew();
            BitmapSource picture=packed?bank.Get(id).Draw(frame):Read(id)[frame];
            if(priorId!=id || frame!=priorFrame)
            {
                using(var painter=element.RenderOpen())painter.DrawImage(picture,new Rect(0,0,192,208));
                target.Clear();target.Render(element);
            }
            double show=one.Elapsed.TotalMilliseconds;
            if(priorId!=id)
            {
                if(priorId>=0){transitions++;maxTransition=Math.Max(maxTransition,show);}
                Console.WriteLine("entry {0:F2}s {1} show={2:F1} ms private delta={3:F1} MiB cache={4}",
                    before/1000.0,MineShift.Catalog[id],show,PrivateMiB(),packed?bank.Count:Cache.Count);
            }
            else maxRender=Math.Max(maxRender,show);
            priorId=id;priorFrame=frame;
            int next=shift.Preload(before);
            if(next>=0 && before<shift.SectionStart(3))
            {
                one.Restart();
                if(packed)bank.Get(next);else Read(next);
                maxPrefetch=Math.Max(maxPrefetch,one.Elapsed.TotalMilliseconds);
            }
            due+=34;int left=(int)(due-sw.ElapsedMilliseconds);
            if(left>0)Thread.Sleep(left);
        }
        Console.WriteLine("cut-count={0} max-cut={1:F1}ms max-draw={2:F1}ms max-prefetch={3:F1}ms retained={4}",
            transitions,maxTransition,maxRender,maxPrefetch,packed?bank.Count:Cache.Count);
        return transitions==3 && (packed?bank.Count:Cache.Count)==2 ? 0 : 1;
    }
}
