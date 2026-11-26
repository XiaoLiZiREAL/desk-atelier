using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DeskMate
{
    public enum LyricSource { None, Local, Cache, Online }

    public sealed class LyricFrame
    {
        public int Index { get; internal set; }
        public string Text { get; internal set; }
        public string Translation { get; internal set; }
        public string NextText { get; internal set; }
        public double StartSeconds { get; internal set; }
        public double EndSeconds { get; internal set; }
        public double Progress { get; internal set; }
        public bool IsInstrumental { get { return Text.Length == 0; } }
    }

    public sealed class LrcDocument
    {
        // Integer milliseconds avoid loss of precision when grouping translation lines.
        sealed class Cue
        {
            public long Time;
            public string Text, Translation, Next;
        }
        readonly Cue[] cues;
        internal const int MaxBytes = 512 * 1024;
        const int MaxEntries = 12000;
        static readonly Regex Stamp = new Regex(@"\[([0-9]{1,4}):([0-5]?[0-9])(?:[.:]([0-9]{1,3}))?\]",RegexOptions.CultureInvariant);
        static readonly Regex Offset = new Regex(@"\[offset\s*:\s*([+-]?[0-9]{1,8})\s*\]",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
        static readonly Regex WordTime = new Regex(@"<\d{1,4}:[0-5]?\d(?:[.:]\d{1,3})?>", RegexOptions.CultureInvariant);
        public int Count { get { return cues.Length; } }
        public bool HasText { get; private set; }
        LrcDocument(Cue[] lines)
        {
            cues = lines;
            string next = "";
            for (int i = cues.Length-1; i >= 0; i--)
            {
                cues[i].Next = next;
                if (cues[i].Text.Length != 0) { next = cues[i].Text; HasText = true; }
            }
        }

        public static LrcDocument Parse(string input)
        {
            string source = (input ?? "").TrimStart('\uFEFF');
            if (source.Length > MaxBytes) throw new InvalidDataException("歌词文件太大。");
            var groups = new SortedDictionary<long,List<string>>();
            int offset = 0, entries = 0;
            foreach (string original in source.Replace("\r\n","\n").Replace('\r','\n').Split('\n'))
            {
                string line = original.Trim();
                if (line.Length == 0) continue;
                var offsetTag = Offset.Match(line);
                if (offsetTag.Success)
                {
                    int.TryParse(offsetTag.Groups[1].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out offset);
                    line = Offset.Replace(line, "").TrimStart();
                }
                Match first = Stamp.Match(line);
                if (!first.Success || first.Index != 0) continue;
                if (line.Length > 4096) throw new InvalidDataException("单行歌词过长。");
                var times = new List<long>();
                Match stamp = first;
                int after = 0;
                while (stamp.Success && stamp.Index == after)
                {
                    int minutes = Int32.Parse(stamp.Groups[1].Value,CultureInfo.InvariantCulture);
                    int seconds = Int32.Parse(stamp.Groups[2].Value,CultureInfo.InvariantCulture);
                    string fraction = stamp.Groups[3].Value;
                    int millis = fraction.Length == 0 ? 0 : Int32.Parse(fraction.PadRight(3,'0'),CultureInfo.InvariantCulture);
                    times.Add((long)minutes * 60000 + seconds * 1000 + millis);
                    after = stamp.Index + stamp.Length;
                    while (after < line.Length && Char.IsWhiteSpace(line[after])) after++;
                    if (after >= line.Length || line[after] != '[') break;
                    stamp = Stamp.Match(line, after);
                }
                string text = WordTime.Replace(line.Substring(after),"").Trim();
                foreach (long time in times)
                {
                    if (++entries > MaxEntries) throw new InvalidDataException("歌词时间点过多。");
                    List<string> lines;
                    if (!groups.TryGetValue(time, out lines)) groups.Add(time, lines = new List<string>());
                    if (text.Length != 0 && !lines.Contains(text)) lines.Add(text);
                }
            }
            return new LrcDocument(groups.Select(pair => new Cue {
                Time = pair.Key - offset, Text = pair.Value.FirstOrDefault() ?? "",
                Translation = String.Join(" / ",pair.Value.Skip(1))
            }).ToArray());
        }

        public static LrcDocument Load(string path)
        {
            return Parse(ReadText(path));
        }

        internal static string ReadText(string path)
        {
            byte[] bytes;
            using (var stream = new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete))
            {
                if (stream.Length > MaxBytes) throw new InvalidDataException("歌词文件超过 512 KB。");
                bytes = new byte[(int)stream.Length];
                int read=0;
                while (read<bytes.Length)
                {
                    int n=stream.Read(bytes,read,bytes.Length-read);
                    if (n==0) throw new EndOfStreamException("读取歌词时文件内容发生变化。");
                    read+=n;
                }
            }
            if (bytes.Length >= 2 && bytes[0]==0xff && bytes[1]==0xfe)
                return new UnicodeEncoding(false,true,true).GetString(bytes,2,bytes.Length-2);
            if (bytes.Length >= 2 && bytes[0]==0xfe && bytes[1]==0xff)
                return new UnicodeEncoding(true,true,true).GetString(bytes,2,bytes.Length-2);
            if (bytes.Length >= 3 && bytes[0]==0xef && bytes[1]==0xbb && bytes[2]==0xbf)
                return new UTF8Encoding(false,true).GetString(bytes,3,bytes.Length-3);
            if (bytes.Length >= 8)
            {
                int pairs = Math.Min(bytes.Length/2, 64), odd = 0, even = 0;
                for(int i=0;i<pairs;i++) { if(bytes[i*2]==0) even++; if(bytes[i*2+1]==0) odd++; }
                if (odd >= 4 && odd > even*3) return new UnicodeEncoding(false,false,true).GetString(bytes);
                if (even >= 4 && even > odd*3) return new UnicodeEncoding(true,false,true).GetString(bytes);
            }
            try { return new UTF8Encoding(false,true).GetString(bytes); }
            catch (DecoderFallbackException)
            { return Encoding.GetEncoding(54936, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes); }
        }

        public LyricFrame At(double positionSeconds, double durationSeconds = 0)
        {
            double position = Double.IsNaN(positionSeconds) ? 0 : Math.Max(0,positionSeconds);
            bool known = durationSeconds > 0 && !Double.IsInfinity(durationSeconds);
            int left=0, right=cues.Length;
            while (left<right)
            {
                int mid=left+(right-left)/2;
                if (cues[mid].Time <= position * 1000 + 0.000001) left=mid+1; else right=mid;
            }
            int idx=left-1;
            if (known && position>=durationSeconds)
                return new LyricFrame {Index=cues.Length,Text="",Translation="",NextText="",StartSeconds=durationSeconds,EndSeconds=durationSeconds,Progress=1};
            if (idx<0)
            {
                double end=cues.Length==0 ? (known?durationSeconds:Double.PositiveInfinity) : Math.Max(0,cues[0].Time/1000.0);
                if(known) end=Math.Min(end,durationSeconds);
                string next=cues.Length==0?"":cues[0].Text.Length==0?cues[0].Next:cues[0].Text;
                return new LyricFrame {Index=-1,Text="",Translation="",NextText=next,StartSeconds=0,EndSeconds=end,Progress=0};
            }
            Cue active=cues[idx];
            double start = Math.Max(0,active.Time/1000.0);
            double finish = idx+1<cues.Length ? cues[idx+1].Time/1000.0 : Double.PositiveInfinity;
            if (known) finish=Math.Min(finish,durationSeconds);
            return new LyricFrame {Index=idx,Text=active.Text,Translation=active.Translation,NextText=active.Next,StartSeconds=start,EndSeconds=finish,
                Progress=finish>start&&!Double.IsInfinity(finish)?Math.Min(1,Math.Max(0,(position-start)/(finish-start))):0};
        }
    }

    public sealed class LyricLookup
    {
        public LrcDocument Document { get; internal set; }
        public LyricSource Source { get; internal set; }
        public string Message { get; internal set; }
        public bool Instrumental { get; internal set; }
        // Helps the UI decide whether to show its optional scratch-line entry/explicit import button.
        public bool HasTimeline { get { return Document!=null && Document.Count>0; } }
    }

    public sealed class LyricsLibrary
    {
        readonly string directory;
        readonly Func<Uri,CancellationToken,string> fetch;
        static readonly object WriteLock = new object();
        const int MaxResponse = 1024 * 1024;
        public LyricsLibrary(string userFolder) : this(userFolder,Fetch) {}
        public LyricsLibrary(string userFolder, Func<Uri,CancellationToken,string> httpFetch)
        {
            if(String.IsNullOrWhiteSpace(userFolder)) throw new ArgumentException("缺少歌词存档目录。","userFolder");
            if(httpFetch==null) throw new ArgumentNullException("httpFetch");
            directory=Path.Combine(Path.GetFullPath(userFolder),"lyrics"); fetch=httpFetch;
        }

        static string Normal(string name)
        {
            string text=(name??"").Trim();
            try { text=text.Normalize(NormalizationForm.FormKC); }
            catch(ArgumentException) { return ""; }
            return Regex.Replace(text,@"\s+"," ").ToLowerInvariant();
        }
        public static string SongKey(string title,string artist)
        {
            string track=Normal(title), person=Normal(artist);
            using(var sha=SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(track.Length.ToString(CultureInfo.InvariantCulture)+":"+track+person))).Replace("-","").ToLowerInvariant();
        }
        static LyricLookup Empty(string why){return new LyricLookup{Message=why,Source=LyricSource.None};}
        static LyricLookup Available(LrcDocument doc,LyricSource source,string why,bool instrumental)
        {return new LyricLookup{Document=doc,Source=source,Message=why,Instrumental=instrumental};}
        static bool ValidDuration(double seconds)
        {return seconds>0&&seconds<=24*60*60&&!Double.IsInfinity(seconds)&&!Double.IsNaN(seconds);}
        static int Rounded(double seconds){return (int)Math.Round(seconds,MidpointRounding.AwayFromZero);}
        string ImportedPath(string key){return Path.Combine(directory,key+".lrc");}
        string CachedPath(string key,int seconds){return Path.Combine(directory,"cache",key+"-"+seconds.ToString(CultureInfo.InvariantCulture));}

        public LyricLookup Import(string title,string artist,string lrcPath)
        {
            if (Normal(title).Length==0) return Empty("播放器尚未提供曲名。");
            try
            {
                if (!String.Equals(Path.GetExtension(lrcPath),".lrc",StringComparison.OrdinalIgnoreCase)) return Empty("请选择 .lrc 格式的歌词文件。");
                return ImportText(title,artist,LrcDocument.ReadText(lrcPath));
            }
            catch(InvalidDataException e){return Empty(e.Message);}
            catch(DecoderFallbackException){return Empty("歌词文字编码无法识别，请保存为 UTF-8 后再导入。");}
            catch(IOException){return Empty("读取或保存歌词失败，请检查文件位置。");}
            catch(UnauthorizedAccessException){return Empty("Windows 未允许读取或保存这份歌词。");}
            catch(ArgumentException){return Empty("无法读取这个歌词文件路径。");}
        }
        public LyricLookup ImportText(string title,string artist,string lrcText)
        {
            if (Normal(title).Length==0) return Empty("播放器尚未提供曲名。");
            try
            {
                var doc=LrcDocument.Parse(lrcText);
                if(doc.Count==0||!doc.HasText) return Empty("没有带时间标签的歌词，无法逐行同步。");
                AtomicWrite(ImportedPath(SongKey(title,artist)),lrcText);
                return Available(doc,LyricSource.Local,"已为这首歌保存本地歌词。",false);
            }
            catch(InvalidDataException e){return Empty(e.Message);}
            catch(EncoderFallbackException){return Empty("歌词含有不完整的文字，无法保存。");}
            catch(IOException){return Empty("本地歌词暂时无法保存。");}
            catch(UnauthorizedAccessException){return Empty("Windows 未允许保存本地歌词。");}
        }

        public Task<LyricLookup> ResolveAsync(string title,string artist,double durationSeconds,bool allowOnline,CancellationToken token)
        {
            return ResolveAsync(title,artist,durationSeconds,allowOnline,token,null);
        }
        public Task<LyricLookup> ResolveAsync(string title,string artist,double durationSeconds,bool allowOnline,CancellationToken token,string lyricFolder)
        {
            return Task.Factory.StartNew(()=>Resolve(title,artist,durationSeconds,allowOnline,token,lyricFolder),token,TaskCreationOptions.None,TaskScheduler.Default);
        }
        LyricLookup Resolve(string title,string artist,double duration,bool online,CancellationToken token,string lyricFolder)
        {
            token.ThrowIfCancellationRequested();
            if(Normal(title).Length==0)return Empty("播放器尚未提供曲名。");
            string key=SongKey(title,artist);
            LyricLookup local=ReadLocal(key,duration,lyricFolder,title,artist);
            if(local!=null)return local;
            if(!online)return Empty("还没有这首歌的本地同步歌词。");
            if(Normal(artist).Length==0||!ValidDuration(duration)||title.Length>300||artist.Length>300)
                return Empty("需要曲名、歌手和有效的歌曲时长才能准确查找在线歌词。");
            using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(10000);
                try
                {
                    string criteria="track_name="+Uri.EscapeDataString(title.Trim())+"&artist_name="+Uri.EscapeDataString(artist.Trim())+
                        "&duration="+Rounded(duration).ToString(CultureInfo.InvariantCulture);
                    bool plainOnly=false;
                    foreach(string route in new[]{"get","search"})
                    {
                        deadline.Token.ThrowIfCancellationRequested();
                        string payload=fetch(new Uri("https://lrclib.net/api/"+route+"?"+criteria),deadline.Token);
                        deadline.Token.ThrowIfCancellationRequested();
                        Found hit=Find(payload,title,artist,duration,ref plainOnly);
                        if(hit==null)continue;
                        local=ReadLocal(key,duration,lyricFolder,title,artist);
                        if(local!=null)return local;
                        string cache=CachedPath(key,Rounded(hit.Duration));
                        string exactDuration=hit.Duration.ToString("R",CultureInfo.InvariantCulture);
                        try{AtomicWrite(cache+(hit.Instrumental?".instrumental":".lrc"),hit.Instrumental?exactDuration:"[desk-duration:"+exactDuration+"]\n"+hit.Raw);}
                        catch(IOException){} catch(UnauthorizedAccessException){}
                        return Available(hit.Document,LyricSource.Online,hit.Instrumental?"已匹配纯音乐。":"已匹配在线同步歌词。",hit.Instrumental);
                    }
                    return Empty(plainOnly?"找到的歌词没有时间轴，无法逐行同步。":"暂未找到曲名、歌手和时长都匹配的同步歌词。");
                }
                catch(OperationCanceledException)
                {token.ThrowIfCancellationRequested();return Empty("在线歌词查询超时。");}
                catch(WebException e)
                {token.ThrowIfCancellationRequested();return Empty(e.Status==WebExceptionStatus.Timeout?"在线歌词查询超时。":"歌词服务暂时无法连接。");}
                catch(InvalidDataException e){return Empty(e.Message);}
                catch(IOException){return Empty("在线歌词响应不完整，请稍后再试。");}
                catch(ArgumentException){return Empty("未能识别歌词服务的响应。");}
                catch(InvalidOperationException){return Empty("未能识别歌词服务的响应。");}
            }
        }

        LyricLookup ReadLocal(string key,double duration,string lyricFolder,string title,string artist)
        {
            var local=ReadSaved(ImportedPath(key),LyricSource.Local); if(local!=null)return local;
            local=FromFolder(lyricFolder,title,artist); if(local!=null)return local;
            if(!ValidDuration(duration))return null;
            int t=Rounded(duration);
            foreach(int nearby in new[]{t,t-1,t+1,t-2,t+2})
            {
                if(nearby<1)continue;
                string cache=CachedPath(key,nearby);
                local=ReadSaved(cache+".lrc",LyricSource.Cache,duration); if(local!=null)return local;
                try
                {
                    string marker=cache+".instrumental";
                    if(File.Exists(marker)&&DurationMatches(LrcDocument.ReadText(marker),duration))
                        return Available(null,LyricSource.Cache,"这首曲目是纯音乐。",true);
                }
                catch(InvalidDataException){}catch(IOException){}catch(UnauthorizedAccessException){}catch(DecoderFallbackException){}
            }
            return null;
        }
        static readonly Regex Credit = new Regex(@"(?im)^\s*\[(ar|ti)\s*:\s*([^\]\r\n]*)\]");

        static LyricLookup FromFolder(string directory,string title,string artist)
        {
            if(String.IsNullOrWhiteSpace(directory)||!Path.IsPathRooted(directory))return null;
            string track=(title??"").Trim(), singer=(artist??"").Trim();
            if(!SafeFilePart(track)||!SafeFilePart(singer))return null;
            // Construct a bounded set of filenames. Only an artist-tagged file may match by title alone.
            string[] stems={singer+" - "+track,track+" - "+singer,singer+"-"+track,track+"-"+singer,track};
            foreach(string stem in stems.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if(stem.Length>248)continue;
                try
                {
                    string path=Path.Combine(directory,stem+".lrc");
                    if(!File.Exists(path))continue;
                    string raw=LrcDocument.ReadText(path);
                    bool hasArtist=false, mismatched=false;
                    foreach(Match tag in Credit.Matches(raw.TrimStart('\uFEFF')))
                    {
                        bool artistTag=tag.Groups[1].Value.Equals("ar",StringComparison.OrdinalIgnoreCase);
                        string supplied=Normal(tag.Groups[2].Value);
                        if(supplied.Length==0)continue;
                        if(artistTag)hasArtist=true;
                        if(supplied!=Normal(artistTag?singer:track))mismatched=true;
                    }
                    if(mismatched||(stem==track&&!hasArtist))continue;
                    var doc=LrcDocument.Parse(raw);
                    if(doc.Count>0&&doc.HasText)return Available(doc,LyricSource.Local,"来自所选歌词目录 · "+stem+".lrc",false);
                }
                catch(InvalidDataException){}
                catch(IOException){}
                catch(UnauthorizedAccessException){}
                catch(ArgumentException){}
            }
            return null;
        }
        static bool SafeFilePart(string text)
        {return text.Length>0 && text.Length<=240 && text!="." && text!=".." && text.IndexOfAny(Path.GetInvalidFileNameChars())<0;}
        static bool DurationMatches(string value,double expected)
        {
            double actual;
            return Double.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out actual) &&
                ValidDuration(actual) && Math.Abs(actual-expected)<=2;
        }
        static LyricLookup ReadSaved(string path,LyricSource source,double duration=0)
        {
            try
            {
                if(!File.Exists(path))return null;
                string raw=LrcDocument.ReadText(path);
                if(source==LyricSource.Cache)
                {
                    var stamp=Regex.Match(raw,@"^\[desk-duration:([^\]\r\n]{1,32})\]");
                    if(!stamp.Success||!DurationMatches(stamp.Groups[1].Value,duration))return null;
                }
                var doc=LrcDocument.Parse(raw);
                return doc.Count>0&&doc.HasText?Available(doc,source,source==LyricSource.Local?"本地同步歌词":"已缓存的同步歌词",false):null;
            }
            catch(InvalidDataException){return null;}
            catch(IOException){return null;}
            catch(UnauthorizedAccessException){return null;}
            catch(DecoderFallbackException){return null;}
        }

        sealed class Found{public string Raw;public double Duration;public bool Instrumental;public LrcDocument Document;}
        static string Field(IDictionary<string,object> item,string key)
        {object val;return item.TryGetValue(key,out val)?val as string:null;}
        static Found Find(string json,string title,string artist,double duration,ref bool plainOnly)
        {
            if(String.IsNullOrWhiteSpace(json))return null;
            if(json.Length>MaxResponse)throw new InvalidDataException("歌词响应过大。");
            var serializer=new JavaScriptSerializer{MaxJsonLength=MaxResponse,RecursionLimit=12};
            object value=serializer.DeserializeObject(json);
            var one=value as IDictionary<string,object>;
            var records=one!=null?new object[]{one}:value as object[] ?? new object[0];
            var hits=new List<Found>();
            string normTitle=Normal(title),normArtist=Normal(artist);
            foreach(var record in records)
            {
                var item=record as IDictionary<string,object>;object sec;
                if(item==null||Normal(Field(item,"trackName"))!=normTitle||Normal(Field(item,"artistName"))!=normArtist||!item.TryGetValue("duration",out sec))continue;
                double seconds;
                if(!Double.TryParse(Convert.ToString(sec,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out seconds)||
                   !ValidDuration(seconds)||Math.Abs(seconds-duration)>2)continue;
                string raw=Field(item,"syncedLyrics");
                if(!String.IsNullOrWhiteSpace(raw))
                {
                    LrcDocument doc;
                    try{doc=LrcDocument.Parse(raw);}catch(InvalidDataException){continue;}
                    if(doc.Count>0 && doc.HasText){hits.Add(new Found{Raw=raw,Duration=seconds,Document=doc});continue;}
                }
                object instrumental;
                if(item.TryGetValue("instrumental",out instrumental)&&instrumental is bool&&(bool)instrumental)
                    hits.Add(new Found{Duration=seconds,Instrumental=true});
                else if(!String.IsNullOrWhiteSpace(Field(item,"plainLyrics")))plainOnly=true;
            }
            if(hits.Count==0)return null;
            hits=hits.OrderBy(h=>Math.Abs(h.Duration-duration)).ToList();
            var best=hits[0];
            if(hits.Skip(1).Any(h=>Math.Abs(Math.Abs(h.Duration-duration)-Math.Abs(best.Duration-duration))<.001 &&
                (h.Raw!=best.Raw||h.Instrumental!=best.Instrumental)))return null;
            return best;
        }
        static void AtomicWrite(string path,string contents)
        {
            byte[] bytes=new UTF8Encoding(false,true).GetBytes(contents);
            if(bytes.Length>LrcDocument.MaxBytes)throw new InvalidDataException("歌词文件超过 512 KB。");
            lock(WriteLock)
            {
                string dir=Path.GetDirectoryName(path);Directory.CreateDirectory(dir);
                string tmp=Path.Combine(dir,Guid.NewGuid().ToString("N")+".tmp");
                try
                {
                    File.WriteAllBytes(tmp,bytes);
                    if(File.Exists(path))File.Replace(tmp,path,null,true);else File.Move(tmp,path);
                }
                finally {if(File.Exists(tmp))File.Delete(tmp);}
            }
        }
        static string Fetch(Uri url,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var request=(HttpWebRequest)WebRequest.Create(url);
            request.Method="GET";request.UserAgent="DeskAtelier/1.0 (Windows desktop lyrics)";
            request.Accept="application/json";request.AllowAutoRedirect=false;
            request.Timeout=7000;request.ReadWriteTimeout=7000;
            // Older Framework applications may inherit a TLS 1.0-only default. On modern
            // systems, keep the OS-selected protocols. The destination always requires HTTPS.
            if((int)ServicePointManager.SecurityProtocol!=0)
                ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
            request.AutomaticDecompression=DecompressionMethods.GZip|DecompressionMethods.Deflate;
            using(token.Register(request.Abort))
            {
                try
                {
                    using(var resp=(HttpWebResponse)request.GetResponse())
                    using(var stream=resp.GetResponseStream())
                    using(var result=new MemoryStream())
                    {
                        if(resp.ContentLength>MaxResponse)throw new InvalidDataException("歌词响应过大。");
                        byte[] buf=new byte[8192];int n;
                        while((n=stream.Read(buf,0,buf.Length))>0)
                        {
                            token.ThrowIfCancellationRequested();
                            if(result.Length+n>MaxResponse)throw new InvalidDataException("歌词响应过大。");
                            result.Write(buf,0,n);
                        }
                        return new UTF8Encoding(false,true).GetString(result.ToArray());
                    }
                }
                catch(WebException ex)
                {
                    token.ThrowIfCancellationRequested();
                    using(var response=ex.Response as HttpWebResponse)
                    {
                        if(response!=null&&(response.StatusCode==HttpStatusCode.NotFound||response.StatusCode==HttpStatusCode.BadRequest))return null;
                        if(response!=null&&(int)response.StatusCode==429)throw new InvalidDataException("歌词服务请求较多，请稍后再试。");
                        if(response!=null&&(int)response.StatusCode>=500)throw new InvalidDataException("歌词服务暂时不可用，请稍后再试。");
                    }
                    throw;
                }
            }
        }
    }
}
