using System;
using System.IO;
using System.IO.Compression;
using DeskMate;

static class SteveFocusPackChecks
{
    static int checks;
    static void Need(bool condition,string label)
    {
        if(!condition)throw new InvalidOperationException(label);
        checks++;
    }

    static void Reject(SteveFocusPack pack,string file,string why)
    {
        try{pack.Install(file);}
        catch(InvalidDataException){checks++;return;}
        throw new InvalidOperationException(why);
    }

    static void Main(string[] args)
    {
        string real=Path.GetFullPath(args[0]);
        string data=Path.Combine(Path.GetTempPath(),"StevePackTest-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        try
        {
            var pack=new SteveFocusPack(data);
            Need(!pack.Available,"clean installs stay optional");
            string marker=Path.Combine(data,"test-save.txt");
            File.WriteAllText(marker,"save remains");
            string invalid=Path.Combine(data,"not-an-expansion.zip");
            using(var zip=new ZipArchive(File.Create(invalid),ZipArchiveMode.Create))
            using(var text=new StreamWriter(zip.CreateEntry("../escape.txt").Open()))text.Write("no");
            Reject(pack,invalid,"any nonmatching archive must be rejected");
            Need(!File.Exists(Path.Combine(data,"escape.txt")) && !pack.Available,"unknown entries never unpack");

            pack.Install(real);
            Need(pack.Available,"verified archive installed");
            foreach(string role in MineShift.Catalog)
                Need(File.Exists(pack.Locate(SteveFocusPack.FileName(role))),"every cave is present");
            Need(new SteveFocusPack(data).Available,"new process finds installed pack");
            Reject(pack,invalid,"failed replace cannot remove old pack");
            Need(new SteveFocusPack(data).Available && pack.Available,"old pack still usable");
            string bad=Path.Combine(data,"bad-sha.zip");
            using(var original=new ZipArchive(File.OpenRead(real),ZipArchiveMode.Read))
            using(var target=new ZipArchive(File.Create(bad),ZipArchiveMode.Create))
            {
                bool flipped=false;
                foreach(var entry in original.Entries)
                {
                    using(var from=entry.Open())
                    using(var to=target.CreateEntry(entry.FullName,CompressionLevel.Fastest).Open())
                    {
                        int b=from.ReadByte();
                        if(b>=0)to.WriteByte((byte)(flipped?b:b^1));
                        from.CopyTo(to);
                    }
                    flipped=true;
                }
            }
            Reject(pack,bad,"same sizes but different SHA must fail");
            Need(pack.Available,"failed checksum leaves installed pack untouched");
            pack.Install(real);
            Need(pack.Available,"successful reinstall replaces atomically");
            File.Delete(pack.Locate(SteveFocusPack.FileName(MineShift.Catalog[4])));
            Need(!new SteveFocusPack(data).Available,"an incomplete install is not advertised");
            pack.Install(real);
            Need(new SteveFocusPack(data).Available,"repair works after missing cave");
            pack.Uninstall();
            Need(!pack.Available && !new SteveFocusPack(data).Available,"remove and restart retain no active bank");
            pack.Uninstall();
            Need(File.ReadAllText(marker)=="save remains","uninstall never writes user data");
        }
        finally{Directory.Delete(data,true);}
        Console.WriteLine("Pass: "+checks+" optional Steve pack checks.");
    }
}
