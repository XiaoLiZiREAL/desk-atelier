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
    internal sealed class NetEaseBrowserPlayer : IDisposable
    {
        const string Origin="https://music.163.com/";
        static readonly Regex Times=new Regex(@"^(\d{1,3}):([0-5]\d)\s*/\s*(\d{1,3}):([0-5]\d)$",RegexOptions.CultureInvariant);
        static readonly Regex AudioLabel=new Regex(@"^(?:正在播放音频|此标签页正在播放音频|此标签页正在播放声音|Audio is playing|Playing audio|This tab is playing audio)(?:$| - | – )",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        static readonly Regex MuteLabel=new Regex(@"(?:静音|\bmuted\b)",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        readonly object gate=new object();
        readonly Stopwatch clock=Stopwatch.StartNew();
        string cacheBrowser="";
        MediaReading cache;
        bool busy;
        volatile bool disposed;
        long next;
        VerifiedTab lastTab; // Accessed only by the single scan worker.

        sealed class VerifiedTab
        {
            internal AutomationElement Element;
            internal int[] Runtime;
            internal int Pid;
            internal string Browser,Title,Artist;
        }
        delegate bool EnumWindow(IntPtr window,IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindow callback,IntPtr data);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window,out int pid);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr window,StringBuilder text,int max);

        internal MediaReading Sample(string browserName)
        {
            if(browserName!="chrome" && browserName!="msedge")return null;
            lock(gate)
            {
                if(disposed)return null;
                if(!busy && (clock.ElapsedMilliseconds>=next || cacheBrowser!=browserName))
                {
                    busy=true;
                    Task.Run(delegate {
                        MediaReading result=null;
                        try { if(!disposed)result=Scan(browserName); } catch(Exception) { lastTab=null; }
                        lock(gate) {
                            if(disposed)lastTab=null;
                            else { cache=result; cacheBrowser=browserName; next=clock.ElapsedMilliseconds+700; }
                            busy=false;
                        }
                    });
                }
                return cache!=null && cacheBrowser==browserName && (DateTime.UtcNow-cache.SampledUtc).TotalSeconds<2.5 ? cache:null;
            }
        }
        public void Dispose()
        {
            lock(gate) { disposed=true; cache=null; if(!busy)lastTab=null; }
        }

        static bool Site(string url)
        {
            Uri address;
            return Uri.TryCreate(url,UriKind.Absolute,out address) && address.Scheme==Uri.UriSchemeHttps &&
                address.IsDefaultPort && address.UserInfo.Length==0 && address.IdnHost.Equals("music.163.com",StringComparison.OrdinalIgnoreCase);
        }
        static bool Resource(string value,string path)
        {
            Uri u;
            if(String.IsNullOrWhiteSpace(value) || !Uri.TryCreate(new Uri(Origin),value,out u) || !Site(u.AbsoluteUri))return false;
            string route=u.AbsolutePath+u.Query;
            if(route=="/" && u.Fragment.StartsWith("#/"))route=u.Fragment.Substring(1);
            return Regex.IsMatch(route,"^/"+path+@"\?id=\d+(?:&|$)",RegexOptions.CultureInvariant);
        }
        internal static bool SameAudibleTab(string label,string title)
        {
            string prefix="▶ "+title+" - ";
            if(label==null || !label.StartsWith(prefix,StringComparison.Ordinal))return false;
            string status=label.Substring(prefix.Length);
            return AudioLabel.IsMatch(status) && !MuteLabel.IsMatch(status);
        }
        // A song details page, its iframe and the current transport can refer to three different tracks.
        // Only a verified top document plus the fixed transport's matching song link proves the identity.
        internal static MediaReading ParseBar(string browser,string url,string caption,IEnumerable<Tuple<string,string,bool>> parts)
        {
            if(!Site(url) || caption==null || !caption.StartsWith("▶ ",StringComparison.Ordinal))return null;
            string title=caption.Substring(2).Trim();
            if(title.Length==0 || title.Length>240)return null;
            var bar=parts.ToList();
            int prev=bar.FindIndex(n=>n.Item3 && n.Item1=="上一首");
            int pause=bar.FindIndex(n=>n.Item3 && n.Item1=="播放/暂停");
            int nextIndex=bar.FindIndex(n=>n.Item3 && n.Item1=="下一首");
            if(prev<0 || pause<=prev || nextIndex<=pause)return null;
            var track=bar.Skip(nextIndex+1).TakeWhile(n=>!n.Item3 || (n.Item1!="收藏" && n.Item1!="分享")).ToList();
            int song=track.FindIndex(n=>n.Item3 && n.Item1==title && Resource(n.Item2,"song"));
            if(song<0)return null;
            var after=track.Skip(song+1).ToList();
            string artist=String.Join(" / ",after.Where(n=>n.Item3 && Resource(n.Item2,"artist") && n.Item1.Length>0 && n.Item1.Length<=160)
                .Select(n=>n.Item1).Distinct(StringComparer.Ordinal));
            var chunks=after.Where(n=>!n.Item3 && Regex.IsMatch(n.Item1,@"^[\d\s:/]+$")).Select(n=>n.Item1)
                .SkipWhile(s=>!Regex.IsMatch(s,@"^\s*\d"));
            var m=Times.Match(String.Concat(chunks).Trim());
            int at=0,total=0;
            if(m.Success) { at=Int32.Parse(m.Groups[1].Value)*60+Int32.Parse(m.Groups[2].Value); total=Int32.Parse(m.Groups[3].Value)*60+Int32.Parse(m.Groups[4].Value); }
            bool timeline=total>0 && at<=total;
            return new MediaReading(title,artist,browser,"netease-web","browser",TimeSpan.FromSeconds(timeline?at:0),TimeSpan.FromSeconds(timeline?total:0),
                true,true,timeline,DateTime.UtcNow,1,timeline?"":"网易云网页未提供播放进度");
        }
        static List<IntPtr> Windows(string browser)
        {
            var owners=new HashSet<int>();
            foreach(var p in Process.GetProcessesByName(browser))using(p)owners.Add(p.Id);
            var result=new List<IntPtr>();
            EnumWindows(delegate(IntPtr h,IntPtr unused){
                int pid;GetWindowThreadProcessId(h,out pid);
                if(owners.Contains(pid) && IsWindowVisible(h)){
                    var caption=new StringBuilder(512);GetWindowText(h,caption,caption.Capacity);
                    if(caption.ToString().StartsWith("▶ ",StringComparison.Ordinal))result.Add(h);
                }
                return true;
            },IntPtr.Zero);
            return result;
        }
        static PropertyCondition Is(AutomationProperty property,object value){return new PropertyCondition(property,value);}
        static string Value(AutomationElement element){return element.GetCurrentPropertyValue(ValuePattern.ValueProperty,true) as string??"";}
        static MediaReading Visible(string browser,IntPtr handle,out VerifiedTab verified)
        {
            verified=null;
            var root=AutomationElement.FromHandle(handle);
            var doc=root.FindFirst(TreeScope.Descendants,new AndCondition(Is(AutomationElement.ControlTypeProperty,ControlType.Document),
                Is(AutomationElement.AutomationIdProperty,"RootWebArea")));
            if(doc==null)return null;
            string url=Value(doc);
            if(!Site(url))return null;
            string caption=doc.Current.Name;
            if(!caption.StartsWith("▶ ",StringComparison.Ordinal))return null;
            // Chromium flattens #g_player in its accessibility tree. Its containing
            // m-playbar remains a direct child of the main document, alongside the content iframe.
            var bars=doc.FindAll(TreeScope.Children,Is(AutomationElement.ControlTypeProperty,ControlType.Group));
            var bar=bars.Cast<AutomationElement>().SingleOrDefault(e=>e.Current.ClassName.Split(' ').Contains("m-playbar"));
            if(bar==null)return null;
            var filter=new OrCondition(Is(AutomationElement.ControlTypeProperty,ControlType.Hyperlink),Is(AutomationElement.ControlTypeProperty,ControlType.Text));
            var parts=new List<Tuple<string,string,bool>>();
            var properties=new CacheRequest { TreeScope=TreeScope.Element };
            properties.Add(AutomationElement.NameProperty);properties.Add(AutomationElement.ControlTypeProperty);properties.Add(ValuePattern.ValueProperty);
            properties.Add(AutomationElement.ClassNameProperty);
            AutomationElementCollection nodes;
            using(properties.Activate())nodes=bar.FindAll(TreeScope.Descendants,filter);
            bool playing=false;
            foreach(AutomationElement node in nodes) {
                if(node.Cached.ControlType==ControlType.Hyperlink && node.Cached.Name=="播放/暂停" &&
                    node.Cached.ClassName.Split(' ').Contains("pas"))playing=true;
                parts.Add(Tuple.Create(node.Cached.Name.Trim(),node.GetCachedPropertyValue(ValuePattern.ValueProperty,true) as string??"",
                    node.Cached.ControlType==ControlType.Hyperlink));
            }
            if(!playing)return null;
            var result=ParseBar(browser,url,caption,parts);
            if(result==null || !Site(Value(doc)) || doc.Current.Name!=caption)return null;
            var tabs=root.FindFirst(TreeScope.Descendants,Is(AutomationElement.ControlTypeProperty,ControlType.Tab));
            if(tabs!=null){
                var selected=tabs.FindFirst(TreeScope.Descendants,new AndCondition(Is(AutomationElement.ControlTypeProperty,ControlType.TabItem),
                    Is(SelectionItemPattern.IsSelectedProperty,true)));
                if(selected!=null) {
                    if(!SameAudibleTab(selected.Current.Name,result.Title))return null;
                    verified=new VerifiedTab{Element=selected,Runtime=selected.GetRuntimeId(),Pid=selected.Current.ProcessId,Browser=browser,Title=result.Title,Artist=result.Artist};
                }
            }
            return result;
        }
        MediaReading Scan(string browser)
        {
            MediaReading found=null;
            VerifiedTab pin=null;
            foreach(var h in Windows(browser)){
                if(disposed)return null;
                VerifiedTab candidate;
                var r=Visible(browser,h,out candidate);
                if(r==null)continue;
                if(found!=null){lastTab=null;return null;}
                found=r;pin=candidate;
            }
            if(found!=null){lastTab=pin;return found;}
            var tab=lastTab;
            lastTab=null;
            if(disposed || tab==null || tab.Browser!=browser)return null;
            var current=tab.Element.Current;
            if(current.ProcessId!=tab.Pid || !tab.Runtime.SequenceEqual(tab.Element.GetRuntimeId()) ||
                !SameAudibleTab(current.Name,tab.Title) || !Object.Equals(tab.Element.GetCurrentPropertyValue(SelectionItemPattern.IsSelectedProperty,true),false))return null;
            lastTab=tab;
            return new MediaReading(tab.Title,tab.Artist,browser,"netease-web","browser",TimeSpan.Zero,TimeSpan.Zero,true,true,false,
                DateTime.UtcNow,1,"网易云后台标签正在播放，进度暂不可读");
        }
    }
}
