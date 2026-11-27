using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace DeskMate
{
    // DOM containers without an accessibility role are often flattened by Chromium.
    // Anchor each read to a distinctive transport control and inspect only its immediate
    // neighbourhood in the control tree. The current song may differ from the page open.
    internal static class DomesticWebPlayer
    {
        internal sealed class Part
        {
            internal string Class, Id, Name, Help, Url;
            internal bool Text, Link;
            internal bool Has(string token) { return Class.Split(' ').Contains(token); }
        }

        static readonly Regex Clock = new Regex(@"^(\d{1,3}):([0-5]\d)\s*/\s*(\d{1,3}):([0-5]\d)$",RegexOptions.CultureInvariant);
        static readonly Regex KugouTitle = new Regex(@"^(?<title>.+?)_(?<artist>.+?)_?高音质在线试听_\k<title>歌词\|歌曲下载_酷狗音乐$",RegexOptions.CultureInvariant);
        static readonly string[] Brands = {"QQ音乐","酷狗音乐","酷我音乐","咪咕音乐","千千音乐","汽水音乐"};
        internal static bool MayBePlayer(string caption)
        {
            return !String.IsNullOrEmpty(caption) && Brands.Any(b=>caption.IndexOf(b,StringComparison.OrdinalIgnoreCase)>=0);
        }

        internal static string Service(string url)
        {
            Uri u;
            if (!Uri.TryCreate(url,UriKind.Absolute,out u) || u.Scheme!=Uri.UriSchemeHttps || !u.IsDefaultPort || u.UserInfo.Length!=0) return "";
            string path=u.AbsolutePath.TrimEnd('/');
            switch(u.IdnHost.ToLowerInvariant()) {
                case "y.qq.com": return path=="/n/ryqq/player" || path=="/n/ryqq_v2/player" ? "qq" : "";
                case "kugou.com": case "www.kugou.com":
                    return Regex.IsMatch(path,@"^/(?:mixsong/[a-zA-Z0-9]+\.html|song(?:/[a-zA-Z0-9]+(?:\.html)?)?)$") ? "kugou" : "";
                case "kuwo.cn": case "www.kuwo.cn": return "kuwo";
                case "music.migu.cn": return path=="/v5" || path.StartsWith("/v5/",StringComparison.Ordinal) ? "migu" : "";
                case "music.91q.com": return path=="/player" ? "qianqian" : "";
                case "music.douyin.com":
                    return path=="/qishui/share/track" && Regex.IsMatch(u.Query,@"[?&]track_id=\d{1,22}(?:&|$)") ? "soda" : "";
                default: return "";
            }
        }

        static Condition Is(AutomationProperty property,object value) { return new PropertyCondition(property,value); }
        static Condition Anchor(string service)
        {
            switch(service) {
                case "qq": return Is(AutomationElement.ClassNameProperty,"btn_big_prev");
                case "kugou": return new AndCondition(Is(AutomationElement.AutomationIdProperty,"prev"),Is(AutomationElement.ClassNameProperty,"icon icon-playbar-prev"));
                case "kuwo": return new AndCondition(Is(AutomationElement.ClassNameProperty,"prev"),Is(AutomationElement.HelpTextProperty,"上一首"));
                case "migu": return Is(AutomationElement.ClassNameProperty,"default-cover");
                case "qianqian": return Is(AutomationElement.ClassNameProperty,"pause iconfont");
                case "soda": return Is(AutomationElement.ClassNameProperty,"track-title");
                default: throw new ArgumentException("Unknown service");
            }
        }

        static CacheRequest Properties()
        {
            var props=new CacheRequest();
            foreach(var p in new[]{AutomationElement.ClassNameProperty,AutomationElement.AutomationIdProperty,AutomationElement.NameProperty,
                AutomationElement.HelpTextProperty,AutomationElement.ControlTypeProperty,ValuePattern.ValueProperty}) props.Add(p);
            return props;
        }
        static Part Capture(AutomationElement e)
        {
            var n=e.Cached;
            return new Part{ Class=n.ClassName??"",Id=n.AutomationId??"",Name=(n.Name??"").Trim(),Help=(n.HelpText??"").Trim(),
                Url=e.GetCachedPropertyValue(ValuePattern.ValueProperty,true) as string??"",Text=n.ControlType==ControlType.Text,Link=n.ControlType==ControlType.Hyperlink };
        }

        static AutomationElement Next(AutomationElement el,AutomationElement doc,TreeWalker walker,CacheRequest props)
        {
            if(el.Cached.ControlType!=ControlType.Document) {
                var child=walker.GetFirstChild(el,props);
                if(child!=null)return child;
            }
            while(el!=null && !Automation.Compare(el,doc)) {
                var sibling=walker.GetNextSibling(el,props);
                if(sibling!=null)return sibling;
                el=walker.GetParent(el,props);
            }
            return null;
        }

        static AutomationElement Previous(AutomationElement el,AutomationElement doc,TreeWalker walker,CacheRequest props)
        {
            var sibling=walker.GetPreviousSibling(el,props);
            if(sibling==null) {
                var parent=walker.GetParent(el,props);
                return parent==null || Automation.Compare(parent,doc) ? null : parent;
            }
            while(sibling.Cached.ControlType!=ControlType.Document) {
                var child=walker.GetLastChild(sibling,props);
                if(child==null)break;
                sibling=child;
            }
            return sibling;
        }

        internal static MediaReading Read(AutomationElement doc,string browser,string url,string caption)
        {
            var service=Service(url);
            if(service.Length==0)return null;
            var props=Properties();
            AutomationElement anchor;
            using(props.Activate()) anchor=doc.FindFirst(TreeScope.Descendants,Anchor(service));
            if(anchor==null)return null;
            // Only the anchor in the verified top document is eligible, never a nested frame.
            var walker=TreeWalker.ControlViewWalker;
            var owner=walker.GetParent(anchor,props);
            while(owner!=null && owner.Cached.ControlType!=ControlType.Document) owner=walker.GetParent(owner,props);
            if(owner==null || !Automation.Compare(owner,doc))return null;
            var nodes=new List<Part>();
            if(service=="kuwo" || service=="qianqian") {
                var before=anchor;
                for(int i=0;i<26;i++) {
                    before=Previous(before,doc,walker,props);
                    if(before==null || before.Cached.ControlType==ControlType.Document)return null;
                    var part=Capture(before);
                    nodes.Insert(0,part);
                    if(service=="kuwo"?KuwoCover(part):QianTitle(part))break;
                }
                if(nodes.Count==0 || !(service=="kuwo"?KuwoCover(nodes[0]):QianTitle(nodes[0])))return null;
            }
            var current=anchor;
            // A transport's far edge varies with the number of singers and the availability of a clock.
            int limit=service=="migu"?58:32;
            for(int i=0;i<limit && current!=null && current.Cached.ControlType!=ControlType.Document;i++) {
                var part=Capture(current);
                nodes.Add(part);
                if((service=="qq" && (part.Has("player_progress") || part.Has("btn_big_like"))) ||
                   (service=="kuwo" && part.Has("next")) ||
                   (service=="kugou" && (part.Id=="bar" || part.Id=="volume")) ||
                   (service=="qianqian" && part.Id=="process") ||
                   (service=="soda" && part.Has("track-progress-hit-area")) ||
                   (service=="migu" && part.Help=="更多" && part.Has("custom-icon"))) break;
                current=Next(current,doc,walker,props);
            }
            var reading=Parse(browser,url,caption,nodes);
            if(reading==null || service!="soda")return reading;
            // The official two-line preview exposes the highlighted line first. Its progress
            // bar is for a clip that can start mid-song; it is not a full-song LRC clock.
            string lyric=CurrentSodaLine(doc,props,walker);
            if(lyric.Length==0)return reading;
            return new MediaReading(reading.Title,reading.Artist,browser,reading.Source,"browser",TimeSpan.Zero,TimeSpan.Zero,true,true,false,
                DateTime.UtcNow,1,reading.Diagnostic,lyric,"accessibility");
        }

        static string CurrentSodaLine(AutomationElement doc,CacheRequest props,TreeWalker walker)
        {
            var area=doc.FindFirst(TreeScope.Descendants,Is(AutomationElement.ClassNameProperty,"track-lyric-entry"));
            if(area==null)return "";
            var owner=walker.GetParent(area,props);
            while(owner!=null && owner.Cached.ControlType!=ControlType.Document)owner=walker.GetParent(owner,props);
            if(owner==null || !Automation.Compare(owner,doc))return "";
            AutomationElement item=walker.GetFirstChild(area,props);
            var lines=new List<string>();
            for(int i=0;item!=null && i<10;i++) {
                var part=Capture(item);
                if(part.Text && Valid(part.Name))lines.Add(part.Name);
                if(lines.Count>2 || item.Cached.ControlType==ControlType.Document)return "";
                item=Next(item,area,walker,props);
            }
            if(item!=null || lines.Count==0 || lines[0]=="暂无歌词" || lines[0]=="纯音乐")return "";
            return lines[0];
        }
        static bool KuwoCover(Part n)
        {
            Uri u;
            return n.Link && Uri.TryCreate(n.Url,UriKind.Absolute,out u) && Service(n.Url)=="kuwo" && Regex.IsMatch(u.AbsolutePath,@"^/play_detail/\d+/?$");
        }

        static bool QqLink(Part n,string kind)
        {
            Uri u;
            return n.Link && Uri.TryCreate(n.Url,UriKind.Absolute,out u) && u.Scheme==Uri.UriSchemeHttps && u.UserInfo.Length==0 && u.IsDefaultPort &&
                u.IdnHost=="y.qq.com" && Regex.IsMatch(u.AbsolutePath,@"^/n/(?:ryqq(?:_v2)?/"+kind+@"/[a-zA-Z0-9]+/?|yqq/"+(kind=="songDetail"?"song":kind)+@"/[a-zA-Z0-9]+\.html)$");
        }

        static int After(List<Part> nodes,int index,Predicate<Part> predicate)
        {
            return index<0 || index>=nodes.Count-1 ? -1 : nodes.FindIndex(index+1,predicate);
        }
        static string Join(IEnumerable<string> names) { return String.Join(" / ",names.Where(Valid).Distinct(StringComparer.Ordinal)); }
        static bool Valid(string s) { return !String.IsNullOrWhiteSpace(s) && s.Length<=240; }
        static bool QianTitle(Part n) { return n.Text && n.Name.EndsWith(" -",StringComparison.Ordinal); }
        static string Times(IEnumerable<Part> nodes)
        {
            return String.Concat(nodes.Where(n=>n.Text && Regex.IsMatch(n.Name,@"^[0-9\s:/]+$")).Select(n=>n.Name));
        }

        internal static MediaReading Parse(string browser,string url,string caption,List<Part> nodes)
        {
            string service=Service(url),title="",artist="",time="";
            if(nodes.Count==0 || service.Length==0)return null;
            int prev,pause,next;
            switch(service) {
                case "qq":
                    prev=nodes.FindIndex(n=>n.Has("btn_big_prev"));
                    pause=After(nodes,prev,n=>n.Has("btn_big_play") && n.Has("btn_big_play--pause"));
                    next=After(nodes,pause,n=>n.Has("btn_big_next"));
                    if(next<0)return null;
                    var info=nodes.Skip(next+1).TakeWhile(n=>!n.Has("player_progress") && !n.Class.StartsWith("btn_big_",StringComparison.Ordinal)).ToList();
                    var songs=info.Where(n=>QqLink(n,"songDetail")).ToList();
                    if(songs.Count!=1)return null;
                    title=songs[0].Name; artist=Join(info.Where(n=>QqLink(n,"singer")).Select(n=>n.Name));
                    time=Times(info.Skip(info.FindLastIndex(n=>QqLink(n,"songDetail") || QqLink(n,"singer"))+1));
                    break;
                case "kuwo":
                    int cover=nodes.FindIndex(KuwoCover);
                    int singer=After(nodes,cover,n=>n.Has("artist") && Valid(n.Help));
                    prev=After(nodes,singer,n=>n.Has("prev") && n.Help=="上一首");
                    pause=After(nodes,prev,n=>(n.Has("play") && n.Help=="暂停") || n.Has("icon-bar_icon_pause_"));
                    next=After(nodes,pause,n=>n.Has("next") && n.Help=="下一首");
                    if(next<0)return null;
                    var track=nodes.Skip(cover+1).Take(singer-cover-1).Where(n=>n.Text && Valid(n.Name)).ToList();
                    if(track.Count!=1)return null;
                    title=track[0].Name; artist=nodes[singer].Help;
                    time=Times(nodes.Skip(singer+1).Take(prev-singer-1));
                    break;
                case "kugou":
                    prev=nodes.FindIndex(n=>n.Id=="prev" && n.Has("icon-playbar-prev"));
                    pause=After(nodes,prev,n=>n.Id=="toggle" && n.Has("icon-playbar-pause"));
                    next=After(nodes,pause,n=>n.Id=="next" && n.Has("icon-playbar-next"));
                    if(String.IsNullOrEmpty(caption) || caption.Length>1024)return null;
                    var match=KugouTitle.Match(caption.Trim());
                    if(next<0 || !match.Success)return null;
                    title=match.Groups["title"].Value; artist=match.Groups["artist"].Value;
                    int label=After(nodes,next,n=>n.Text && (n.Name==title || n.Name==artist+" - "+title));
                    if(label<0)return null;
                    time=Times(nodes.Skip(label+1)); break;
                case "migu":
                    int coverIndex=nodes.FindIndex(n=>n.Has("default-cover"));
                    int name=After(nodes,coverIndex,n=>n.Text && Valid(n.Name));
                    prev=After(nodes,name,n=>n.Has("custom-icon") && n.Help=="上一首");
                    pause=After(nodes,prev,n=>n.Has("custom-icon") && n.Help=="暂停");
                    next=After(nodes,pause,n=>n.Has("custom-icon") && n.Help=="下一首");
                    if(next<0)return null;
                    title=nodes[name].Name;
                    artist=Join(nodes.Skip(name+1).Take(prev-name-1).Where(n=>n.Has("singers")).Select(n=>n.Help));
                    if(artist.Length==0)return null;
                    var clocks=nodes.Skip(next+1).Where(n=>n.Text && Regex.IsMatch(n.Name,@"^\d{1,3}:[0-5]\d$")).Select(n=>n.Name).ToList();
                    if(clocks.Count==2)time=String.Join("/",clocks);
                    break;
                case "qianqian":
                    int heading=nodes.FindIndex(QianTitle);
                    int collect=After(nodes,heading,n=>n.Has("iconfont") && (n.Has("collect") || n.Has("collected")));
                    int share=After(nodes,collect,n=>n.Has("iconfont") && n.Has("share"));
                    int download=After(nodes,share,n=>n.Has("iconfont") && n.Has("download"));
                    prev=After(nodes,download,n=>n.Class=="prev iconfont");
                    pause=After(nodes,prev,n=>n.Class=="pause iconfont");
                    next=After(nodes,pause,n=>n.Class=="next iconfont");
                    if(next<0)return null;
                    title=nodes[heading].Name.Substring(0,nodes[heading].Name.Length-2).Trim();
                    artist=Join(nodes.Skip(heading+1).Take(collect-heading-1).Where(n=>n.Text && n.Name!=",").Select(n=>n.Name));
                    if(artist.Length==0)return null;
                    time=Times(nodes.Skip(next+1));
                    break;
                case "soda":
                    int block=nodes.FindIndex(n=>n.Has("track-title"));
                    int headingText=After(nodes,block,n=>n.Text && n.Has("title"));
                    int artistBlock=After(nodes,headingText,n=>n.Has("track-artist"));
                    int follow=After(nodes,artistBlock,n=>n.Text && (n.Name=="关注" || n.Name=="已关注"));
                    int actions=After(nodes,follow,n=>n.Has("action-container"));
                    // Chromium exposes the official icon-stop_solid as this text glyph.
                    // Its idle icon-play_solid and loading spinner use different glyphs.
                    int stop=After(nodes,actions,n=>n.Has("icon-stop_solid") || (n.Text && n.Name=="\uE67A"));
                    int progress=After(nodes,stop,n=>n.Has("track-progress-hit-area"));
                    if(progress<0)return null;
                    title=nodes[headingText].Name;
                    artist=Join(nodes.Skip(artistBlock+1).Take(follow-artistBlock-1).Where(n=>n.Text).Select(n=>n.Name));
                    if(artist.Length==0 || (caption!="《"+title+"》@汽水音乐" && caption!=title+"-"+artist+"-汽水音乐"))return null;
                    break;
            }
            if(!Valid(title) || artist.Length>480)return null;
            var m=Clock.Match(time);
            int at=0,total=0;
            if(m.Success) { at=Int32.Parse(m.Groups[1].Value)*60+Int32.Parse(m.Groups[2].Value); total=Int32.Parse(m.Groups[3].Value)*60+Int32.Parse(m.Groups[4].Value); }
            bool timeline=total>0 && at<=total;
            return new MediaReading(title,artist,browser,service+"-web","browser",TimeSpan.FromSeconds(timeline?at:0),TimeSpan.FromSeconds(timeline?total:0),
                true,true,timeline,DateTime.UtcNow,1,timeline?"":"网页播放器未提供可靠的进度");
        }
    }
}
