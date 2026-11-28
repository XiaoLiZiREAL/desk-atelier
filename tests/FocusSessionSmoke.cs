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
        Need(caves.Length==18,"18 cave scene and behavior combinations embedded");
        foreach(string name in caves)
            using(var input=new BinaryReader(File.OpenRead(app.Asset(name.Substring(4)))))
            {
                if(!name.EndsWith(".dca") || new string(input.ReadChars(8))!="DACAVE01" ||
                    input.ReadInt32()!=192 || input.ReadInt32()!=208 || input.ReadInt32()!=252)
                    throw new Exception(name);
            }
        Need(true,"WPF sprite and packed mine resource headers verified");
        int count=app.Data.FocusCount,total=app.Data.FocusMinutesTotal;
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
