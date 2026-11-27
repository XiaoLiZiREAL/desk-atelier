using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using DeskMate;

internal static class ActionPackChecks
{
    static void Entry(ZipArchive zip,string name,byte[] bytes)
    {
        using(var s=zip.CreateEntry(name).Open())s.Write(bytes,0,bytes.Length);
    }
    static string Make(Studio app,string folder,string tag,bool actions,bool variant,bool invalid)
    {
        string path=Path.Combine(folder,tag+".zip");
        using(var stream=File.Create(path))
        using(var zip=new ZipArchive(stream,ZipArchiveMode.Create))
        {
            Entry(zip,"role/pet.json",Encoding.UTF8.GetBytes("{\"displayName\":\"动作验证\",\"spritesheetPath\":\"sheet.png\"}"));
            Entry(zip,"role/sheet.png",File.ReadAllBytes(app.Asset("steve.png")));
            if(actions)foreach(string part in new[]{"tea","game","song","focus","rest","mine","projectstart","projectwork","projectfinish","propbridge","turnstep"})
            {
                string id=variant && part=="tea"?"rem":"steve";
                string file=invalid && part=="tea"?"logo.png":"motion-"+id+"-"+part+".png";
                Entry(zip,"role/actions/"+part+".png",File.ReadAllBytes(app.Asset(file)));
            }
        }
        return path;
    }
    static void Check(bool condition,string text){if(!condition)throw new Exception(text);Console.WriteLine("PASS "+text);}
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            string folder=Path.GetFullPath(args[0]);Directory.CreateDirectory(folder);
            Environment.SetEnvironmentVariable("DESKATELIER_DATA",folder);
            var app=new Studio(folder);app.ExtractArt();
            if(args.Length>1 && !args[1].StartsWith("--"))Check(app.ImportCompanion(Path.GetFullPath(args[1]))=="steve","distributed Steve pack matches the built-in rig");
            foreach(string role in new[]{"hitori","rem","kurisu"}){
                int flag=Array.IndexOf(args,"--"+role);
                if(flag>=0)Check(app.ImportCompanion(Path.GetFullPath(args[flag+1]))==role,"distributed "+role+" pack matches its built-in artwork");
            }
            Check(app.ImportCompanion(Make(app,folder,"base",false,false,false))=="steve","legacy base-only Steve deduplicated");
            Check(app.ImportCompanion(Make(app,folder,"complete",true,false,false))=="steve","complete built-in actions deduplicated");
            string changed=Make(app,folder,"changed",true,true,false),id=app.ImportCompanion(changed);
            Check(id.StartsWith("custom-") && app.CustomName(id)=="动作验证","alternate action art registers a separate character");
            Check(app.ImportCompanion(changed)==id,"same alternate pack deduplicated");
            foreach(string part in new[]{"tea","game","song","focus","rest","mine","projectstart","projectwork","projectfinish","propbridge","turnstep"})
                Check(File.Exists(Path.Combine(folder,"companions",id,part+".png")),"installed pose strip "+part);
            bool rejected=false;
            try{app.ImportCompanion(Make(app,folder,"wrong-size",true,false,true));}catch(InvalidDataException){rejected=true;}
            Check(rejected,"invalid living animation rejected before installing");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
