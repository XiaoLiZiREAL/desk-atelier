using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace DeskMate
{
    internal sealed class PlayerLabel
    {
        public string Id, Text, Kind, Context;
        public int Parent;
        public bool OnScreen;
        public PlayerLabel(string id,string text,string kind,string context,int parent,bool onScreen=true)
        { Id=id??"";Text=text??"";Kind=kind??"";Context=context??"";Parent=parent;OnScreen=onScreen; }
    }

    // Only inspect the top-level windows owned by the three named desktop players.
    internal sealed class ChinesePlayers
    {
        static readonly Dictionary<string,string[]> Processes=new Dictionary<string,string[]> {
            {"netease",new[]{"cloudmusic"}},{"qq",new[]{"QQMusic"}},{"kugou",new[]{"KuGou","KuGouMusic"}}
        };
        // Windows media sessions use a shortcut AppID, an executable path, or a packaged app ID.
        static readonly HashSet<string> BrowserIds=new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Chrome", "Chrome.exe", "ChromeBeta", "ChromeDev", "ChromeCanary", "Chrome SxS",
            "Chrome Beta", "Chrome Dev", "Chrome Canary", "MSEdge", "msedge.exe", "MSEdgeBeta", "MSEdgeDev", "MSEdgeCanary", "MSEdgeSxS",
            "Microsoft.MicrosoftEdge.Stable_8wekyb3d8bbwe!App", "Microsoft.MicrosoftEdge_8wekyb3d8bbwe!MicrosoftEdge",
            "Firefox", "firefox.exe", "Mozilla.Firefox_n80bbvh6b1yt2!App", "308046B0AF4A39CB", "E7CF176E110C211B"
        };
        static readonly string[] Brands={"网易云音乐","网易云","NetEase Cloud Music","QQ音乐","QQ 音乐","QQMusic","酷狗音乐","酷狗","KuGou Music"};
        static readonly Regex TimePair=new Regex(@"(?<![\d:])((?:\d{1,2}:)?\d{1,3}:[0-5]\d)\s*[/／]\s*((?:\d{1,2}:)?\d{1,3}:[0-5]\d)(?![\d:])",RegexOptions.CultureInvariant);
        static readonly Regex SingleTime=new Regex(@"^(?:\d{1,2}:)?\d{1,3}:[0-5]\d$",RegexOptions.CultureInvariant);
        static readonly Regex SongLabel=new Regex(@"^(?:当前歌曲|正在播放|歌曲名|歌曲名称|歌名|曲名|Title|Track)\s*[：:]\s*(.+)$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        static readonly Regex ArtistLabel=new Regex(@"^(?:歌手|歌手名|演唱者|艺人|Artist|Singer)\s*[：:]\s*(.+)$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly object scanLock=new object();
        readonly PlayerLyricOcr ocr=new PlayerLyricOcr();
        volatile bool readWindow;
        int generation;
        string lastChoice="",cachedChoice="";
        bool scanning;
        MediaReading cached,previous;
        long next;
        internal static string BrowserProcess(string id)
        {
            string value=(id??"").Trim();
            int slash=Math.Max(value.LastIndexOf('\\'),value.LastIndexOf('/'));
            string name=value.Substring(slash+1);
            if(!BrowserIds.Contains(name))return "";
            if(name.StartsWith("Chrome",StringComparison.OrdinalIgnoreCase))return "chrome";
            if(name.StartsWith("MSEdge",StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Microsoft.MicrosoftEdge",StringComparison.OrdinalIgnoreCase))return "msedge";
            return "firefox";
        }
        internal static string Key(string id)
        {
            string s=(id??"").Trim().ToLowerInvariant();
            if(BrowserProcess(s).Length>0)return "browser";
            if(s.Contains("cloudmusic")||s.Contains("网易云音乐"))return "netease";
            if(s.Contains("qqmusic")||s.Contains("qq音乐"))return "qq";
            if(s.Contains("kugou")||s.Contains("酷狗音乐"))return "kugou";
            return "other";
        }
        internal static bool IsProcess(string player,string name)
        {string[] names;return Processes.TryGetValue(player,out names)&&names.Contains(name,StringComparer.OrdinalIgnoreCase);}
        internal void SetReadWindow(bool value)
        {
            lock(scanLock){readWindow=value;Invalidate();}
        }
        internal void Invalidate()
        {lock(scanLock){generation++;cached=null;cachedChoice="";next=0;}}

        sealed class Candidate
        { public IntPtr Hwnd;public uint Pid;public string Player,Process,Title;public bool Foreground,LyricsWindow;public long Area; }
        [StructLayout(LayoutKind.Sequential)] struct Rect { public int L,T,R,B; }
        delegate bool WindowEnum(IntPtr hwnd,IntPtr data);
        [DllImport("user32.dll")]static extern bool EnumWindows(WindowEnum callback,IntPtr data);
        [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
        [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int n);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr hwnd,StringBuilder text,int n);

        internal static bool Dedicated(string title,string cls)
        {
            if(ContainsAny(cls,"desktoplyric","desktoplrc","desklyric","qqlyricwnd","kugoulyricwnd"))return true;
            string token=title.Replace(" ","").Replace("-","").Trim();
            return token=="桌面歌词" || Brands.Any(b=>token.Equals(b.Replace(" ","")+"桌面歌词",StringComparison.OrdinalIgnoreCase));
        }

        static List<Candidate> Candidates(string preferred)
        {
            var owners=new Dictionary<uint,Candidate>();
            foreach(var pair in Processes)
            {
                if(preferred!="auto" && pair.Key!=preferred)continue;
                foreach(string exe in pair.Value)foreach(var p in Process.GetProcessesByName(exe))using(p)
                    owners[(uint)p.Id]=new Candidate{Player=pair.Key,Process=exe,Title=""};
            }
            var found=new List<Candidate>();if(owners.Count==0)return found;
            IntPtr front=GetForegroundWindow();
            EnumWindows(delegate(IntPtr h,IntPtr unused){
                uint pid;GetWindowThreadProcessId(h,out pid);
                Candidate who;if(!owners.TryGetValue(pid,out who)||!IsWindowVisible(h))return true;
                Rect r;if(!GetWindowRect(h,out r)||r.R-r.L<90||r.B-r.T<24)return true;
                var name=new StringBuilder(360);GetWindowText(h,name,name.Capacity);
                var cls=new StringBuilder(120);GetClassName(h,cls,cls.Capacity);
                found.Add(new Candidate{Hwnd=h,Pid=pid,Player=who.Player,Process=who.Process,Title=name.ToString(),LyricsWindow=Dedicated(name.ToString(),cls.ToString()),Foreground=h==front,Area=(long)(r.R-r.L)*(r.B-r.T)});
                return true;
            },IntPtr.Zero);
            foreach(var p in owners.Values)if(!found.Any(w=>w.Player==p.Player))found.Add(p);
            return found.OrderByDescending(w=>w.Foreground).ThenByDescending(w=>w.Area).ToList();
        }

        static bool ContainsAny(string text,params string[] fragments)
        { return fragments.Any(s=>text.IndexOf(s,StringComparison.OrdinalIgnoreCase)>=0); }
        static bool IsPlayerContext(string text)
        {return ContainsAny(text,"playerbar","player-bar","player_bar","playbar","playingbar","playback","currenttrack","nowplaying","transport","playcontrol","progress","播放进度","播放控制","播放栏","当前播放");}

        sealed class Node{public AutomationElement Element;public string Context;public int Parent,Depth;}
        static List<PlayerLabel> Accessible(IntPtr hwnd)
        {
            var labels=new List<PlayerLabel>();
            if(hwnd==IntPtr.Zero)return labels;
            try
            {
                var request=new CacheRequest { TreeScope=TreeScope.Element };
                request.Add(AutomationElement.NameProperty);request.Add(AutomationElement.AutomationIdProperty);
                request.Add(AutomationElement.ControlTypeProperty);request.Add(AutomationElement.ClassNameProperty);
                request.Add(AutomationElement.ProcessIdProperty);request.Add(AutomationElement.IsOffscreenProperty);
                var queue=new Queue<Node>();
                AutomationElement root;using(request.Activate())root=AutomationElement.FromHandle(hwnd);
                if(root==null)return labels;
                int owner=root.Cached.ProcessId;
                queue.Enqueue(new Node { Element=root,Context="",Parent=-1,Depth=0 });
                Stopwatch budget=Stopwatch.StartNew();
                while(queue.Count>0 && labels.Count<175 && budget.ElapsedMilliseconds<600)
                {
                    var n=queue.Dequeue();var c=n.Element.Cached;
                    if(c.ProcessId!=owner || c.ControlType==ControlType.Edit ||
                        c.ControlType==ControlType.List || c.ControlType==ControlType.DataGrid || c.ControlType==ControlType.Tree)continue;
                    string id=c.AutomationId??"",name=c.Name??"", cls=c.ClassName??"";
                    string kind=c.ControlType.ProgrammaticName.Replace("ControlType.","");
                    string context=IsPlayerContext(id+" "+cls+" "+name)?id+" "+cls+" "+name:n.Context;
                    if(context.Length>180)context=context.Substring(0,180);
                    int index=labels.Count;labels.Add(new PlayerLabel(id,name.Length>250?"":name,kind,context,n.Parent,!c.IsOffscreen));
                    if(n.Depth>=10 || c.ControlType==ControlType.Text)continue;
                    var walker=TreeWalker.ControlViewWalker;
                    var child=walker.GetFirstChild(n.Element,request);
                    for(int i=0;child!=null && i<35 && labels.Count+queue.Count<180 && budget.ElapsedMilliseconds<600;i++)
                    {
                        queue.Enqueue(new Node { Element=child,Parent=index,Context=context,Depth=n.Depth+1 });
                        child=walker.GetNextSibling(child,request);
                    }
                }
            }catch(Exception){}
            return labels;
        }
        static bool Content(string s)
        {return !String.IsNullOrWhiteSpace(s)&&s.Length<=180&&!Brands.Any(b=>s.Equals(b,StringComparison.OrdinalIgnoreCase))&&!SingleTime.IsMatch(s)&&
            !ContainsAny(s,"听我想听","就是歌多","音乐总有新玩法","发现好音乐","搜索音乐");}
        static string[] Caption(string player,string caption)
        {
            string s=caption.Trim();
            var parts=Regex.Split(s,@"\s+(?:-|–|—|\|)\s+").Select(t=>t.Trim()).Where(t=>!Brands.Any(b=>t.Equals(b,StringComparison.OrdinalIgnoreCase))).ToList();
            if(parts.Count!=2||!parts.All(Content))return new[]{"",""};
            return player=="kugou"?new[]{parts[1],parts[0]}:new[]{parts[0],parts[1]};
        }
        static double TimeValue(string text)
        {
            if(!SingleTime.IsMatch(text))return -1;
            string[] p=text.Split(':');int a=int.Parse(p[0]),b=int.Parse(p[1]);
            if(p.Length==2)return a*60+b;
            return b>=60?-1:a*3600+b*60+int.Parse(p[2]);
        }
        static bool IsTimes(double at,double duration)
        {return at>=0&&duration>0&&at<=duration&&duration<=86400;}
        internal static bool UsableLyric(string line,string title,string artist)
        {
            return Content(line) && line.Length<=135 && line!=title && line!=artist && !line.Contains("\n") && !line.Contains(" - ") &&
                !TimePair.IsMatch(line) && !ContainsAny(line,"桌面歌词","歌词设置","歌词搜索","暂无歌词","没有歌词","纯音乐","当前歌词","解锁","锁定","点击","http:","https:") &&
                !Regex.IsMatch(line,@"^(?:关闭|设置|暂停|播放|上一首|下一首|上一句|下一句|翻译|原文|拼音|菜单|桌面|歌词|更多|最小化|最大化)$");
        }
        static string Lyric(IList<PlayerLabel> labels,bool dedicated,string title,string artist)
        {
            var exact=new List<string>();var visible=new List<string>();
            foreach(var label in labels)
            {
                if(!label.OnScreen || (label.Kind!="Text" && label.Kind!="Custom"))continue;
                string line=label.Text.Trim();
                bool tagged=ContainsAny(label.Id,"currentlyric","activelyric","playinglyric","lyriccurrent","lyricnow","currentlrc","curlrcline");
                if(line.StartsWith("当前歌词：",StringComparison.Ordinal) || line.StartsWith("当前歌词:",StringComparison.Ordinal))
                {if(!tagged&&!dedicated&&!ContainsAny(label.Id,"lyric","lrc"))continue;line=line.Substring(5).Trim();}
                if(!UsableLyric(line,title,artist))continue;
                if(tagged)exact.Add(line);
                else if(dedicated && label.Kind=="Text" && line.Length>=2)visible.Add(line);
            }
            var options=(exact.Count>0?exact:visible).Distinct(StringComparer.Ordinal).ToList();
            return options.Count==1?options[0]:"";
        }
        // Minimized players may mark the entire tree offscreen. Prefer rendered controls
        // independently for each field, keeping the hidden controls when no visible one exists.
        sealed class FieldValues<T>
        {
            public readonly HashSet<T> Items;
            public bool Visible;
            public FieldValues(IEqualityComparer<T> comparer=null){Items=new HashSet<T>(comparer);}
            public void Add(T value,bool onScreen)
            {
                if(onScreen && !Visible){Items.Clear();Visible=true;}
                if(onScreen || !Visible)Items.Add(value);
            }
        }
        internal static MediaReading Parse(string player,string process,string caption,IList<PlayerLabel> labels,bool dedicated=false)
        {
            string[] cap=Caption(player,caption);
            string title=cap[0],artist=cap[1];
            var titles=new FieldValues<string>(StringComparer.OrdinalIgnoreCase);
            var artists=new FieldValues<string>(StringComparer.OrdinalIgnoreCase);
            var anchoredTimes=new FieldValues<Tuple<double,double>>();var otherTimes=new FieldValues<Tuple<double,double>>();
            var elapsedTimes=new FieldValues<double>();var endTimes=new FieldValues<double>();
            var states=new FieldValues<bool>();
            double at=-1,total=-1;
            var timeGroups=new Dictionary<int,List<Tuple<double,bool>>>();
            foreach(var item in labels)
            {
                string text=item.Text.Trim(), id=item.Id, context=item.Context;
                if(text.Length==0 || text.Length>250)continue;
                var a=ArtistLabel.Match(text);var n=SongLabel.Match(text);
                if(a.Success && Content(a.Groups[1].Value))artists.Add(a.Groups[1].Value.Trim(),item.OnScreen);
                else if(n.Success && Content(n.Groups[1].Value))
                {
                    string value=n.Groups[1].Value.Trim();string[] bits=Caption(player,value);
                    titles.Add(bits[0].Length>0?bits[0]:value,item.OnScreen);if(bits[1].Length>0)artists.Add(bits[1],item.OnScreen);
                }
                else if((ContainsAny(id,"songname","songtitle","trackname","tracktitle","musicname","musictitle","currenttitle") ||
                    (IsPlayerContext(context) && id.Equals("title",StringComparison.OrdinalIgnoreCase))) && Content(text)) titles.Add(text,item.OnScreen);
                else if((ContainsAny(id,"singername","artistname","currentartist","songartist","tracksinger") ||
                    (IsPlayerContext(context) && (id.Equals("artist",StringComparison.OrdinalIgnoreCase)||id.Equals("singer",StringComparison.OrdinalIgnoreCase)))) && Content(text))artists.Add(text,item.OnScreen);
                bool transport=IsPlayerContext(context)||ContainsAny(id,"playpause","btnplay","playbtn","pausebtn","btnpause","playbutton","pausebutton");
                if(item.Kind=="Button" && transport)
                {
                    if(Regex.IsMatch(text,@"^(?:暂停(?:播放)?|Pause)(?:\s*[（(].*[）)])?$",RegexOptions.IgnoreCase))states.Add(true,item.OnScreen);
                    if(Regex.IsMatch(text,@"^(?:播放|继续播放|Play|Resume)(?:\s*[（(].*[）)])?$",RegexOptions.IgnoreCase))states.Add(false,item.OnScreen);
                }
                var pair=TimePair.Match(text);
                if(pair.Success && (transport || item.Kind=="Slider" || pair.Value.Length==text.Length))
                {double t=TimeValue(pair.Groups[1].Value),d=TimeValue(pair.Groups[2].Value);if(IsTimes(t,d))(transport || item.Kind=="Slider"?anchoredTimes:otherTimes).Add(Tuple.Create(t,d),item.OnScreen);}
                else if(SingleTime.IsMatch(text))
                {
                    double t=TimeValue(text);
                    if(ContainsAny(id,"currenttime","elapsed","playedtime","positiontime"))elapsedTimes.Add(t,item.OnScreen);
                    else if(ContainsAny(id,"totaltime","duration","endtime"))endTimes.Add(t,item.OnScreen);
                    else if(transport && item.Parent>=0)
                    {
                        List<Tuple<double,bool>> values;if(!timeGroups.TryGetValue(item.Parent,out values))timeGroups[item.Parent]=values=new List<Tuple<double,bool>>();
                        values.Add(Tuple.Create(t,item.OnScreen));
                    }
                }
            }
            if(elapsedTimes.Items.Count==1 && endTimes.Items.Count==1)
            {
                at=elapsedTimes.Items.First();total=endTimes.Items.First();
                if(IsTimes(at,total))anchoredTimes.Add(Tuple.Create(at,total),elapsedTimes.Visible&&endTimes.Visible);
            }
            foreach(var group in timeGroups.Values)
            {
                var g=group.Count(t=>t.Item2)>=2?group.Where(t=>t.Item2).ToList():group;
                if(g.Count==2&&IsTimes(g[0].Item1,g[1].Item1))anchoredTimes.Add(Tuple.Create(g[0].Item1,g[1].Item1),g[0].Item2&&g[1].Item2);
            }
            var clocks=(anchoredTimes.Items.Count>0 && (anchoredTimes.Visible || !otherTimes.Visible)?anchoredTimes:otherTimes).Items;
            if(clocks.Count==1){var pair=clocks.First();at=pair.Item1;total=pair.Item2;}else{at=-1;total=-1;}
            if(titles.Items.Count==1)
            {
                string explicitTitle=titles.Items.First();
                if(!explicitTitle.Equals(title,StringComparison.OrdinalIgnoreCase))artist="";
                title=explicitTitle;
            }
            if(artists.Items.Count==1)artist=artists.Items.First();
            bool timeline=title.Length>0&&IsTimes(at,total),known=states.Items.Count==1;
            return new MediaReading(title,artist,process,"window",player,timeline?TimeSpan.FromSeconds(at):TimeSpan.Zero,
                timeline?TimeSpan.FromSeconds(total):TimeSpan.Zero,known&&states.Items.First(),known,timeline,DateTime.UtcNow,1,
                title.Length==0?"识别到播放器，尚未读取到当前歌曲":!timeline?"播放器窗口未显示可同步的进度":!known?"已读取窗口进度，播放状态未知":"",
                Lyric(labels,dedicated,title,artist));
        }

        // A slow accessibility provider has its own single worker. System-media polling and
        // UI input never wait for a foreign window to answer.
        internal MediaReading Sample(string preferred)
        {
            lock(scanLock)
            {
                if(!scanning && (preferred!=lastChoice || clock.ElapsedMilliseconds>=next))
                {
                    scanning=true;lastChoice=preferred;next=clock.ElapsedMilliseconds+1300;
                    int revision=generation;
                    Task.Run(delegate {
                        MediaReading result=null;
                        try{result=Scan(preferred,revision);}catch(Exception){}
                        lock(scanLock){if(revision==generation){cached=result;cachedChoice=preferred;}scanning=false;}
                    });
                }
                return cachedChoice==preferred && cached!=null && (DateTime.UtcNow-cached.SampledUtc).TotalSeconds<6 ? cached : null;
            }
        }
        MediaReading Scan(string preferred,int revision)
        {
            var choices=Candidates(preferred);var reads=new List<MediaReading>();
            var lyricsWindows=new Dictionary<string,Candidate>();
            foreach(var group in choices.GroupBy(w=>w.Player).Take(3))
            {
                var c=group.FirstOrDefault(w=>!w.LyricsWindow)??group.First();
                var dedicated=group.FirstOrDefault(w=>w.LyricsWindow);if(dedicated!=null)lyricsWindows[c.Player]=dedicated;
                var reading=Parse(c.Player,c.Process,c.Title,Accessible(c.Hwnd),c.LyricsWindow);
                var desk=group.FirstOrDefault(w=>w.LyricsWindow && w!=c);
                if(reading.LiveLyric.Length==0 && desk!=null)
                {
                    string sung=Lyric(Accessible(desk.Hwnd),true,reading.Title,reading.Artist);
                    if(sung.Length>0)reading=new MediaReading(reading.Title,reading.Artist,reading.AppId,"window",reading.PlayerKey,reading.Position,
                        reading.Duration,reading.Playing,reading.StateKnown,reading.HasTimeline,reading.SampledUtc,reading.Rate,reading.Diagnostic,sung);
                }
                reads.Add(reading);
            }
            MediaReading selected=reads.OrderByDescending(r=>r.Playing && r.StateKnown).ThenByDescending(r=>r.HasTimeline)
                .ThenByDescending(r=>r.Title.Length>0).FirstOrDefault();
            if(selected!=null && previous!=null && selected.HasTimeline && previous.HasTimeline && !selected.StateKnown &&
                selected.Title==previous.Title && selected.AppId==previous.AppId && selected.Duration==previous.Duration)
            {
                double traveled=(selected.Position-previous.Position).TotalSeconds, elapsed=(selected.SampledUtc-previous.SampledUtc).TotalSeconds;
                if(traveled>0 && elapsed>0 && Math.Abs(traveled-elapsed)<1.6)
                    selected=new MediaReading(selected.Title,selected.Artist,selected.AppId,"window",selected.PlayerKey,selected.Position,selected.Duration,true,true,true,selected.SampledUtc,1,"",selected.LiveLyric);
            }
            Candidate capture;
            if(selected!=null && selected.LiveLyric.Length==0 && readWindow && lyricsWindows.TryGetValue(selected.PlayerKey,out capture))
            {
                string diag;
                string line=ocr.Read(capture.Hwnd,selected.PlayerKey,capture.Pid,selected.Title,selected.Artist,
                    ()=>readWindow && System.Threading.Volatile.Read(ref generation)==revision,out diag);
                selected=new MediaReading(selected.Title,selected.Artist,selected.AppId,selected.Source,selected.PlayerKey,selected.Position,selected.Duration,
                    selected.Playing,selected.StateKnown,selected.HasTimeline,selected.SampledUtc,selected.Rate,selected.Diagnostic,line,line==""?"":"ocr",diag);
            }
            previous=selected;return selected;
        }
    }
}
