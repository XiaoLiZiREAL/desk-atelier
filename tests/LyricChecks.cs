using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using DeskMate;

internal static class LyricChecks
{
    static int count;
    static string root;
    static readonly CancellationToken None=CancellationToken.None;
    static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
    static void Check(bool good,string description)
    {if(!good)throw new Exception(description);Console.WriteLine("PASS  "+description);count++;}
    static bool Near(double x,double y){return Math.Abs(x-y)<.00001;}
    static string Temp(string name){return Path.Combine(root,name);}
    static LyricLookup Read(LyricsLibrary lib,string title,string artist,double seconds,bool network=false,string folder=null)
    {return lib.ResolveAsync(title,artist,seconds,network,None,folder).GetAwaiter().GetResult();}
    static Dictionary<string,object> Item(string title,string artist,double duration,string lyrics,bool instrumental=false,string plain=null)
    {return new Dictionary<string,object>{{"trackName",title},{"artistName",artist},{"duration",duration},{"instrumental",instrumental},{"syncedLyrics",lyrics},{"plainLyrics",plain}};}
    static readonly string OurLrc="[00:02.00]窗边灯亮起\n[00:09.000]开始新的一页";
    public static int Main(string[] args)
    {
        root=Path.Combine(Path.GetFullPath(args.Length>0?args[0]:@"work\qa"),"lyrics-state-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            if(args.Skip(1).Contains("--probe"))
            {
                var probe=new LyricsLibrary(root);
                var result=Read(probe,"No Song Desk Atelier QA "+Guid.NewGuid().ToString("N"),"Desk Atelier QA nonexistent test artist",121,true);
                Console.WriteLine("Live provider returned: "+result.Message);
                Check(result.Message.Contains("暂未找到"),"Real HTTPS LRCLIB get/search returned a valid no-match result");
            }
            else {Parser();Encodings();Library();Provider();Cancellation();}
            Console.WriteLine(count+" checks passed");return 0;
        }
        catch(Exception e){Console.WriteLine(e.GetType().Name+": "+e.Message);Console.WriteLine(e.StackTrace);return 1;}
    }
    static void Parser()
    {
        var timeline=LrcDocument.Parse("\uFEFF[ti:窗边手写小调]\r\n[00:01.125] [00:03.25]<00:01.1>青色窗帘\n[00:01.125]Teal curtains\n[00:01.125]Teal curtains\n[00:05]\n[00:07.5]最后一页\n[offset:+125]");
        Check(timeline.Count==4 && timeline.HasText,"metadata, repeated timestamps and timestamp-only breaks parsed");
        var intro=timeline.At(.3,12);
        Check(intro.Index==-1 && intro.IsInstrumental && intro.NextText=="青色窗帘" && Near(intro.EndSeconds,1),"no early lyric during instrumental intro");
        var boundary=timeline.At(1,12);
        Check(boundary.Text=="青色窗帘"&&boundary.Translation=="Teal curtains"&&Near(boundary.StartSeconds,1),"positive offset displays early and bilingual duplicate is retained once");
        var repeat=timeline.At(3.125,12);
        Check(repeat.Text=="青色窗帘"&&repeat.Translation==""&&Near(repeat.EndSeconds,4.875),"multi-stamp cue repeats with correct next boundary");
        var gap=timeline.At(5,12);
        Check(gap.IsInstrumental&&gap.NextText=="最后一页"&&Near(gap.EndSeconds,7.375),"explicit blank cue clears old lyrics through interlude");
        var last=timeline.At(8.5,12);
        Check(last.Index==3&&Near(last.EndSeconds,12)&&last.Progress>0&&last.Progress<1,"last segment duration and fraction use media length");
        Check(timeline.At(12,12).IsInstrumental&&timeline.At(12,12).Text=="","known track end clears final lyric");
        Check(timeline.At(1,12).Index==0,"seeking backward re-evaluates via binary search");
        var phrases=LrcDocument.Parse("[00:06.00]作词：甲\n[00:09.00]<00:09.00>第一句<00:13.50>\n[00:27.00]第二句\n[00:51.00]（间奏）\n[01:01.00]尾声");
        Check(phrases.At(1,75).MotionAt(1)==MusicMotion.Intro && phrases.At(7,75).MotionAt(7)==MusicMotion.Intro,"opening silence and credits use the intro gesture");
        Check(phrases.At(13.4,75).MotionAt(13.4)==MusicMotion.Singing && phrases.At(13.5,75).MotionAt(13.5)==MusicMotion.Interlude,"enhanced LRC ends the vocal gesture at the final word boundary");
        Check(phrases.At(27,75).MotionAt(27)==MusicMotion.Singing && phrases.At(40,75).MotionAt(40)==MusicMotion.Interlude && phrases.At(40,75).Text=="第二句","line-only LRC retains the caption during a long breathing pause");
        Check(phrases.At(52,75).MotionAt(52)==MusicMotion.Interlude && phrases.At(62,75).MotionAt(62)==MusicMotion.Singing,"instrumental marker listens and the next lyric resumes singing");
        var held=LrcDocument.Parse("[00:01]长<00:19>句\n[00:27]新句");
        Check(LrcDocument.IsNonVocalLabel("纯音乐，请欣赏。") && LrcDocument.IsNonVocalLabel("（吉他间奏）") &&
            LrcDocument.IsNonVocalLabel("暂无滚动歌词") && !LrcDocument.IsNonVocalLabel("这是我们的前奏"),
            "instrumental placeholders in Chinese players are not sung as words");
        var delayed=LrcDocument.Parse("[00:01]<00:06>第一句<00:08>\n[00:15]<00:19>第二句");
        Check(delayed.At(3,25).MotionAt(3)==MusicMotion.Intro && delayed.At(6,25).MotionAt(6)==MusicMotion.Singing &&
            delayed.At(8,25).MotionAt(8)==MusicMotion.Interlude && delayed.At(17,25).MotionAt(17)==MusicMotion.Interlude,
            "a preloaded caption waits for the first timestamped word in each phrase");
        Check(held.At(19,40).MotionAt(19)==MusicMotion.Singing,"an explicit late word prevents an early rest");
        var negative=LrcDocument.Parse("[00:10:25]十点一刻\n[offset:-500]");
        Check(negative.At(10.74).IsInstrumental&&negative.At(10.75).Text=="十点一刻","negative offset and colon fractions supported");
        var cut=LrcDocument.Parse("[00:00.2]甲\n[00:00.5]乙\n[offset:+500]");
        Check(cut.At(0).Text=="乙","shifted cues before zero resolve in order");
        Check(LrcDocument.Parse("只有一行纯文本").Count==0,"plain text never creates an invented timeline");
        var many=new StringBuilder();
        for(int i=0;i<4000;i++)many.AppendFormat("[{0:00}:{1:00}.00]第{2}页\n",i/60,i%60,i);
        var large=LrcDocument.Parse(many.ToString());
        Check(large.Count==4000 && large.At(3555.2).Text=="第3555页","long timeline supports random seek");
    }
    static void Encodings()
    {
        var codecs=new[]{new UTF8Encoding(true),new UTF8Encoding(false),new UnicodeEncoding(false,true),new UnicodeEncoding(true,true),
            new UnicodeEncoding(false,false),new UnicodeEncoding(true,false),Encoding.GetEncoding(54936),Encoding.GetEncoding(936)};
        int n=0;
        foreach(var codec in codecs)
        {
            string name=Temp("encoding-"+(++n)+".lrc");
            File.WriteAllText(name,OurLrc,codec);
            Check(LrcDocument.Load(name).At(2).Text=="窗边灯亮起","local text decoded: "+codec.EncodingName+" / "+n);
        }
    }
    static void Library()
    {
        int calls=0;
        var lib=new LyricsLibrary(Temp("library"),delegate{calls++;return null;});
        string artist="测试搭档", title="窗边小调";
        string folder=Temp("export"); Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder,title+".lrc"),OurLrc,Encoding.UTF8);
        Check(Read(lib,title,artist,100,false,folder).Source==LyricSource.None,"ambiguous title-only LRC rejected without artist metadata");
        File.WriteAllText(Path.Combine(folder,title+".lrc"),"[ar:"+artist+"]\n"+OurLrc,Encoding.UTF8);
        Check(Read(lib,title,artist,100,false,folder).Source==LyricSource.Local,"artist metadata disambiguates title-only export");
        File.WriteAllText(Path.Combine(folder,artist+" - "+title+".lrc"),"[ar:另一位歌手]\n[00:00]错误",Encoding.UTF8);
        File.WriteAllText(Path.Combine(folder,title+" - "+artist+".lrc"),"[00:00]反向命名",Encoding.UTF8);
        Check(Read(lib,title,artist,100,false,folder).Document.At(1).Text=="反向命名","reverse filename matched after contradictory metadata rejected");
        File.WriteAllText(Path.Combine(folder,artist+" - "+title+".lrc"),"[00:00]推荐命名",Encoding.UTF8);
        Check(Read(lib,title,artist,100,false,folder).Document.At(1).Text=="推荐命名","canonical artist - title preferred deterministically");
        string original=Path.Combine(folder,artist+" - "+title+".lrc");string originalValue=File.ReadAllText(original);
        var imported=lib.ImportText(title,artist,"[00:00]私人校对稿");
        Check(imported.Source==LyricSource.Local&&Read(lib,title,artist,100,true,folder).Document.At(1).Text=="私人校对稿"&&calls==0,"manual mapping outranks folder and never contacts network");
        Check(File.ReadAllText(original)==originalValue,"explicit import leaves third-party export unchanged");
        Check(lib.ImportText(title,artist,"普通文字").Source==LyricSource.None&&Read(lib,title,artist,100).Document.At(1).Text=="私人校对稿","failed import preserves last good local lyrics");
        Check(lib.Import(title,artist,original).Document.At(1).Text=="推荐命名","file import can replace an existing mapping");
        Check(Read(lib,"  ＷＩＮＤＯＷ   MeMo ","Ａ",100).Source==LyricSource.None,"fresh normalized key has no initial document");
        lib.ImportText("  ＷＩＮＤＯＷ   MeMo ","Ａ","[00:00]晚风");
        Check(Read(lib,"window memo","a",100).Document.At(1).Text=="晚风","key normalizes case, Unicode width and repeated whitespace");
        var noNet=Read(lib,"unknown","track",100);
        Check(noNet.Source==LyricSource.None&&calls==0,"missing lyrics do not opt the user into online lookups");
        Check(lib.ImportText("../a/b","../../test","[00:00]只作键值").Source==LyricSource.Local&&!File.Exists(Temp("test.lrc")),"song metadata cannot choose an arbitrary output path");
        var large=Temp("large.lrc");using(var fs=File.Create(large))fs.SetLength(600*1024);
        Check(!lib.Import(title,artist,large).HasTimeline,"oversized file rejected before parsing");
    }
    static void Provider()
    {
        const string song="窗边一页", artist="手记小组";
        int count=0;var sent=new List<Uri>();
        var lib=new LyricsLibrary(Temp("lookup"),delegate(Uri u,CancellationToken t)
        {
            count++;sent.Add(u);
            if(u.AbsolutePath.EndsWith("/get"))return Json.Serialize(Item(song,artist+"他人",150,OurLrc));
            return Json.Serialize(new object[]{Item(song,artist,166,"[00:00]错误时长"),Item(song+"（现场）",artist,150,"[00:00]错误版本"),Item(song,artist,150,OurLrc)});
        });
        Check(Read(lib,song,artist,150).Source==LyricSource.None&&count==0,"no HTTP if network toggle is disabled");
        var hit=Read(lib,song,artist,150,true);
        Check(hit.Source==LyricSource.Online&&hit.Document.At(2).Text=="窗边灯亮起"&&count==2,"only matching artist, title and duration are accepted");
        Check(sent.All(u=>u.Scheme=="https"&&u.Host=="lrclib.net"&&u.Query.Contains("duration=150")&&Uri.UnescapeDataString(u.Query).Contains(artist)),"requests only contact the HTTPS LRCLIB endpoint with song metadata");
        Check(Read(lib,song,artist,151).Source==LyricSource.Cache&&count==2,"positive matched cache available offline near recorded duration");
        Check(Read(lib,song,artist,180).Source==LyricSource.None&&count==2,"different-length version does not reuse stale online cache");
        var precise=new LyricsLibrary(Temp("precision"),delegate{return Json.Serialize(Item(song,artist,149.64,OurLrc));});
        Check(Read(precise,song,artist,150,true).HasTimeline && Read(precise,song,artist,151.65).Source==LyricSource.None &&
            Read(precise,song,artist,147.65).Source==LyricSource.Cache,"cache revalidates actual fractional duration, not a rounded estimate");
        Check(Read(lib,song,"",150,true).Source==LyricSource.None&&Read(lib,song,artist,0,true).Source==LyricSource.None&&count==2,"incomplete metadata never leaks to HTTP provider");
        string folder=Temp("priority");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,artist+" - "+song+".lrc"),"[00:00]导出歌词",Encoding.UTF8);
        Check(Read(lib,song,artist,150,false,folder).Document.At(0).Text=="导出歌词","selected export outranks a previously matched service cache");
        int plainCalls=0;var plain=new LyricsLibrary(Temp("plain"),delegate{plainCalls++;return Json.Serialize(Item(song,artist,150,null,false,"作者的普通文字"));});
        var p=Read(plain,song,artist,150,true);var again=Read(plain,song,artist,150,true);
        Check(!p.HasTimeline&&!again.HasTimeline&&p.Message.Contains("没有时间轴")&&plainCalls==4,"unsynced provider text remains explicitly unsynced; no negative cache");
        var instrumental=new LyricsLibrary(Temp("instrumental"),delegate{return Json.Serialize(Item(song,artist,150,null,true));});
        Check(Read(instrumental,song,artist,150,true).Instrumental&&Read(instrumental,song,artist,150).Instrumental,"verified all-instrumental track caches its status");
        var ambiguous=new LyricsLibrary(Temp("ambiguous"),delegate{return Json.Serialize(new[]{Item(song,artist,150,"[00:01]稿甲"),Item(song,artist,150,"[00:02]稿乙")});});
        Check(Read(ambiguous,song,artist,150,true).Source==LyricSource.None,"conflicting equally likely lyrics are not chosen arbitrarily");
        var malformed=new LyricsLibrary(Temp("malformed"),delegate{return "{";});
        Check(Read(malformed,song,artist,150,true).Source==LyricSource.None,"invalid provider JSON does not crash playback");
    }
    static void Cancellation()
    {
        using(var started=new ManualResetEventSlim(false))
        using(var cancel=new CancellationTokenSource())
        {
            var lib=new LyricsLibrary(Temp("cancel"),delegate(Uri u,CancellationToken t)
            {started.Set();t.WaitHandle.WaitOne(3000);t.ThrowIfCancellationRequested();return null;});
            var pending=lib.ResolveAsync("切换测试","测试歌手",120,true,cancel.Token);
            started.Wait(1500);cancel.Cancel();
            bool cancelled=false;
            try{pending.GetAwaiter().GetResult();}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled,"track switch cancels in-flight lookup and its cache write");
        }
    }
}
