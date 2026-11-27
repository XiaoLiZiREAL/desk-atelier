using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DeskMate;

class DomesticWebChecks
{
    const BindingFlags Hidden=BindingFlags.Static|BindingFlags.NonPublic;
    const string Qq="https://y.qq.com/n/ryqq_v2/player", Kuwo="https://www.kuwo.cn/rankList",
        Kugou="https://www.kugou.com/mixsong/abc.html", Migu="https://music.migu.cn/v5/#/songDetail";
    static readonly Type Player=typeof(MediaWatcher).Assembly.GetType("DeskMate.DomesticWebPlayer",true);
    static readonly Type Part=Player.GetNestedType("Part",BindingFlags.NonPublic);
    static object Node(string cls="",string id="",string name="",string help="",string url="",bool text=false) {
        object node=Activator.CreateInstance(Part,true);
        var fields=new Dictionary<string,object>{{"Class",cls},{"Id",id},{"Name",name},{"Help",help},{"Url",url},{"Link",url.Length!=0},{"Text",text}};
        foreach(var field in fields)Part.GetField(field.Key,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(node,field.Value);
        return node;
    }
    static object Text(string name){return Node(name:name,text:true);}
    static MediaReading Read(string url,string caption,params object[] nodes) {
        var list=(IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(Part));
        foreach(var n in nodes)list.Add(n);
        return (MediaReading)Player.GetMethod("Parse",Hidden).Invoke(null,new object[]{"chrome",url,caption,list});
    }
    static void Check(bool result,string name){if(!result)throw new Exception(name);Console.WriteLine("PASS "+name);}
    static bool Good(MediaReading r,string source){return r!=null && r.Playing && r.StateKnown && r.Source==source && r.PlayerKey=="browser" &&
        r.Title=="测试曲" && r.Artist=="歌手甲" && r.HasTimeline && r.Position.TotalSeconds==24 && r.Duration.TotalSeconds==225;}
    static int Main(){
        var qq=new[]{Node("btn_big_prev"),Node("btn_big_play btn_big_play--pause"),Node("btn_big_next"),Node(name:"测试曲",url:"https://y.qq.com/n/ryqq_v2/songDetail/abc"),
            Node(name:"歌手甲",url:"https://y.qq.com/n/ryqq/singer/abc"),Text("00:24 / 03:45"),Node("player_progress"),Node(name:"另一个歌曲",url:"https://y.qq.com/n/ryqq_v2/songDetail/xyz")};
        Check(Good(Read(Qq,"QQ音乐",qq),"qq-web"),"QQ uses current footer links and clock, ignores other links on the same page");
        Check(Read(Qq,"QQ音乐",qq.Where(n=>n!=qq[1]).ToArray())==null,"QQ paused transport cannot report a playing song");
        Check(Read(Qq.Replace("/player","/songDetail/abc"),"QQ音乐",qq)==null,"QQ ordinary detail pages are not the transport");
        Check(Read(Qq,"QQ音乐",qq.Concat(new[]{qq[3]}).Where(n=>n!=qq[6]).ToArray())==null,"ambiguous QQ identity rejected");
        var unknown=Read(Qq,"QQ音乐",qq.Where(n=>n!=qq[5]).ToArray());
        Check(unknown!=null && !unknown.HasTimeline && unknown.Position==TimeSpan.Zero,"missing QQ clock stays unknown");
        var kugou=new[]{Node("icon icon-playbar-prev","prev"),Node("icon icon-playbar-pause","toggle"),Node("icon icon-playbar-next","next"),
            Text("歌手甲 - 测试曲"),Text("00:24"),Text("/"),Text("03:45")};
        string caption="测试曲_歌手甲_高音质在线试听_测试曲歌词|歌曲下载_酷狗音乐";
        Check(Good(Read(Kugou,caption,kugou),"kugou-web"),"Kugou checks title against actual transport and pause control");
        Check(Read(Kugou,caption.Replace("歌手甲","乙"),kugou)==null,"Kugou same-title different performer rejected");
        Check(Read(Kugou,caption.Replace("测试曲歌词","错歌歌词"),kugou)==null,"Kugou mixed song page and current metadata rejected");
        var kuwo=new[]{Node(url:"https://www.kuwo.cn/play_detail/123"),Text("测试曲"),Node("artist",help:"歌手甲"),Text("- 歌手甲"),Text("00:24/03:45"),
            Node("prev",help:"上一首"),Node("play",help:"暂停"),Node("next",help:"下一首")};
        Check(Good(Read(Kuwo,"酷我音乐",kuwo),"kuwo-web"),"Kuwo uses current track beside transport even on rankings");
        Check(Read(Kuwo,"酷我音乐",kuwo.Skip(1).ToArray())==null,"Kuwo requires current song link");
        Check(Read(Kuwo,"酷我音乐",kuwo.Where(n=>n!=kuwo[6]).ToArray())==null,"paused Kuwo rejected");
        var migu=new[]{Node("default-cover"),Text("测试曲"),Node("singers",help:"歌手甲"),Text("歌手甲"),Node("custom-icon is-hoverAble",help:"上一首"),
            Node("custom-icon is-hoverAble",help:"暂停"),Node("custom-icon is-hoverAble",help:"下一首"),Text("00:24"),Text("03:45")};
        Check(Good(Read(Migu,"乐库 - 咪咕音乐",migu),"migu-web"),"Migu fixed footer pairs current singer with playing controls");
        Check(Read(Migu,"乐库 - 咪咕音乐",migu.Where(n=>n!=migu[2]).ToArray())==null,"Migu requires footer performer");
        var badTime=Read(Migu,"乐库 - 咪咕音乐",migu.Take(7).Concat(new[]{Text("04:00"),Text("00:10")}).ToArray());
        Check(badTime!=null && !badTime.HasTimeline,"impossible time range stays unknown");
        var qian=new[]{Text("测试曲 -"),Text("歌手甲"),Node("collect iconfont"),Node("iconfont share"),Node("download iconfont"),
            Node("prev iconfont"),Node("pause iconfont"),Node("next iconfont"),Text("00:24"),Text("/ 03:45")};
        Check(Good(Read("https://music.91q.com/player","千千音乐",qian),"qianqian-web"),"Qianqian identifies the dedicated player's now-playing section");
        Check(Read("https://music.91q.com/player","千千音乐",qian.Where(n=>n!=qian[2]).ToArray())==null,"Qianqian rejects an unanchored track label");
        Check(Read("https://music.91q.com/songlist","千千音乐",qian)==null,"Qianqian song list is not the player");
        const string sodaUrl="https://music.douyin.com/qishui/share/track?track_id=1234", sodaCaption="《测试曲》@汽水音乐";
        var soda=new[]{Node("track-title"),Node("title",name:"测试曲",text:true),Node("track-artist"),Text("歌手甲"),Text("关注"),
            Node("action-container"),Text("\uE67A"),Node("track-progress-hit-area")};
        var excerpt=Read(sodaUrl,sodaCaption,soda);
        Check(excerpt!=null && excerpt.Source=="soda-web" && excerpt.Title=="测试曲" && excerpt.Artist=="歌手甲" && !excerpt.HasTimeline,
            "Soda official share uses the real playing icon and has no fabricated full-song time");
        Check(Read(sodaUrl,sodaCaption,soda.Select(n=>n==soda[6]?Text("\uE6DE"):n).ToArray())==null,"Soda paused preview with a visible song does not report playback");
        Check(Read(sodaUrl,"《另一曲》@汽水音乐",soda)==null,"Soda card and tab must name the same song");
        Check(Read(sodaUrl,"测试曲-歌手甲-汽水音乐",soda)!=null && Read(sodaUrl,"测试曲-另一位歌手-汽水音乐",soda)==null,
            "Soda hydrated tab title verifies both the track and the performer");
        Check(Read("https://music.douyin.com/qishui/share/track",sodaCaption,soda)==null,"Soda preview requires an explicit track identity");
        foreach(string bad in new[]{"https://y.qq.com.evil.test/n/ryqq/player","https://y.qq.com@other.test/n/ryqq/player","https://music.migu.cn:8443/v5/",
            "http://www.kuwo.cn/","https://www.douyin.com/qishui/","https://www.kugou.com/","https://www.kugou.com/video/abc"})
            Check(((string)Player.GetMethod("Service",Hidden).Invoke(null,new object[]{bad})).Length==0,"rejects unrelated origin or page "+bad);
        var browser=typeof(MediaWatcher).Assembly.GetType("DeskMate.NetEaseBrowserPlayer",true);
        var canPin=browser.GetMethod("CanPin",Hidden);
        Check((bool)canPin.Invoke(null,new object[]{"soda-web"}) && !(bool)canPin.Invoke(null,new object[]{"qq-web"}),
            "verified Soda share can retain its track in the background but a static player tab title cannot");
        foreach(string label in new[]{"QQ音乐 - 已静音","QQ音乐 - 正在播放音频 - 此标签页已静音","QQ音乐 - 内存用量","别的网页 - 正在播放音频"})
            Check(!(bool)browser.GetMethod("AudibleCaption",Hidden).Invoke(null,new object[]{label,"QQ音乐"}),"rejects silent or mismatched selection "+label);
        return 0;
    }
}
