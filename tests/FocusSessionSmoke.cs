using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;
using DeskMate;

static class FocusSessionSmoke
{
    static void Need(bool ok,string label){if(!ok)throw new InvalidOperationException(label);Console.WriteLine(label);}
    [STAThread]
    static void Main(string[] args)
    {
        string data=Path.Combine(Path.GetFullPath(args[0]),"independent-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        Environment.SetEnvironmentVariable("DESKATELIER_DATA",data);
        var app=new Studio(data);
        app.ExtractArt();
        var art=typeof(Studio).Assembly.GetManifestResourceNames().Where(n=>n.Contains("-focusmate")).ToArray();
        Need(art.Length==16,"16 embedded whole-frame strips present");
        foreach(string name in art)
        {
            using(var input=File.OpenRead(app.Asset(name.Substring(4))))
            {
                var f=new PngBitmapDecoder(input,BitmapCreateOptions.None,BitmapCacheOption.OnLoad).Frames[0];
                if(f.PixelWidth!=1152 || f.PixelHeight!=(name.Contains("steve-focusmate")?1664:208))throw new Exception(name);
            }
        }
        var caves=typeof(Studio).Assembly.GetManifestResourceNames().Where(n=>n.Contains("-focuscave-")).ToArray();
        Need(caves.Length==0,"No Steve mining assets embedded in the lean app");
        Need(!File.Exists(app.Asset("motion-steve-focuscave-torch-pass.dca")),"Fresh install has no hidden copy of the optional assets");
        Need(true,"WPF peer focus strips verified");
        if(args.Length>1)
        {
            const BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
            object pack=typeof(Studio).GetField("SteveMining",flags).GetValue(app);
            Type packType=pack.GetType();
            var available=packType.GetProperty("Available",flags);
            Need(!(bool)available.GetValue(pack,null),"fresh Steve build needs no expansion");
            packType.GetMethod("Install",flags).Invoke(pack,new object[]{Path.GetFullPath(args[1])});
            Need((bool)available.GetValue(pack,null),"installation is visible to the running application");
            var locate=(Func<string,string>)Delegate.CreateDelegate(typeof(Func<string,string>),pack,packType.GetMethod("Locate",flags));
            var cacheType=typeof(Studio).Assembly.GetType("DeskMate.MineFrameCache",true);
            object cache=Activator.CreateInstance(cacheType,flags,null,new object[]{locate},null);
            var get=cacheType.GetMethod("Get",flags);
            for(int variant=0;variant<18;variant++)
            {
                object bank=get.Invoke(cache,new object[]{variant});
                if(bank==null)throw new Exception("Cave bank missing after install: "+variant);
                var draw=bank.GetType().GetMethod("Draw",flags);
                foreach(int frame in new[]{0,50,110,180,251})
                {
                    var image=(BitmapSource)draw.Invoke(bank,new object[]{frame});
                    if(image.PixelWidth!=192 || image.PixelHeight!=208)throw new Exception("Decoded wrong frame dimensions");
                }
            }
            Need(true,"All 18 optional cave banks decode sample frames in WPF");
            packType.GetMethod("Uninstall",flags).Invoke(pack,null);
            Need(!(bool)available.GetValue(pack,null),"uninstall takes effect without touching the application data");
        }
        int count=app.Data.FocusCount,total=app.Data.FocusMinutesTotal;
        app.Data.Character="steve";
        app.StartFocus(2); Need(app.FocusActive && app.RemainingSeconds>115,"start uses existing focus session");
        app.PauseFocus(); int remaining=app.RemainingSeconds;
        System.Threading.Thread.Sleep(1100);
        Need(app.FocusPaused && app.RemainingSeconds==remaining,"paused session does not count down");
        app.ResumeFocus(); Need(app.FocusActive && !app.FocusPaused,"resume preserves a running timer");
        app.CancelFocus(); Need(!app.FocusActive && app.Data.FocusCount==count && app.Data.FocusMinutesTotal==total,"cancel adds no focus credit");
        app.StartFocus(1); app.Data.FocusEndUtc=DateTime.UtcNow.AddSeconds(-1).Ticks; app.TickTools();app.TickTools();
        Need(!app.FocusActive && app.Data.FocusCount==count+1 && app.Data.FocusMinutesTotal==total+1,"finish credits the session just once");
    }
}
