using System;
using System.Linq;
using System.Reflection;
using DeskMate;

class NetEaseBrowserChecks
{
    const string Url="https://music.163.com/#/song?id=13", Song="窗边的测试曲";
    static Type player=typeof(MediaWatcher).Assembly.GetType("DeskMate.NetEaseBrowserPlayer",true);
    static object Call(string method,params object[] values)
    { return player.GetMethod(method,BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,values); }
    static void Check(bool result,string name)
    { if(!result)throw new Exception(name); Console.WriteLine("PASS "+name); }
    static Tuple<string,string,bool> Part(string name,string url="",bool link=true)
    { return Tuple.Create(name,url,link); }
    static MediaReading Read(string url,string caption,Tuple<string,string,bool>[] nodes)
    { return (MediaReading)Call("ParseBar","chrome",url,caption,nodes); }
    static int Main(){
        var parts=new[]{Part("上一首"),Part("播放/暂停"),Part("下一首"),Part(Song,"/song?id=27"),
            Part("演唱甲","https://music.163.com/artist?id=11"),Part("/","",false),Part("演唱乙","/artist?id=12"),
            Part("来自歌单","/playlist?id=12"),Part("01:13","",false),Part(" / 04:05","",false),Part("收藏")};
        var r=Read(Url,"▶ "+Song,parts);
        Check(r!=null&&r.Title==Song&&r.Artist=="演唱甲 / 演唱乙"&&r.Playing&&r.StateKnown&&r.Position.TotalSeconds==73&&r.Duration.TotalSeconds==245,
            "uses marked track and seekable playback bar even on a different song detail page");
        Check(Read(Url,"详情 - 网易云音乐",parts)==null,"paused page title cannot identify a playing song");
        Check(Read("https://music.163.com.example/","▶ "+Song,parts)==null,"lookalike site rejected");
        Check(Read("https://music.163.com@unrelated.test/","▶ "+Song,parts)==null,"userinfo cannot establish site identity");
        Check(Read(Url,"▶ "+Song,parts.Skip(1).ToArray())==null,"ordinary song details lack transport controls");
        Check(Read(Url,"▶ 未核实",parts)==null,"mismatched transport title rejected");
        Check(Read(Url,"▶ "+Song,parts.Where(n=>n.Item3).ToArray()).HasTimeline==false,"missing clock stays unknown");
        Check((bool)Call("SameAudibleTab","▶ "+Song+" - 正在播放音频 - 内存用量 - 100 MB",Song),"Chrome audible label");
        Check(!(bool)Call("SameAudibleTab","▶ "+Song+" - 内存用量 - 100 MB",Song),"silent background tab rejected");
        Check(!(bool)Call("SameAudibleTab","▶ "+Song+" - 已静音",Song),"muted song tab cannot displace audible media");
        Check(!(bool)Call("SameAudibleTab","▶ "+Song+" - 正在播放音频 - 此标签页已静音",Song),"mute indicator outranks a simultaneous play indicator");
        Check(!(bool)Call("SameAudibleTab","▶ 其他歌曲 - 正在播放音频",Song),"unverified background track switch rejected");
        return 0;
    }
}
