using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace DeskMate
{
    public partial class SaveData
    {
        public bool SingAlongEnabled = true;
        public bool OnlineLyrics;
        public bool ReadLyricWindow;
        public string PreferredPlayer = "auto";
        public string LyricFolder = "";
        public int LyricOffsetMs;
    }

    public sealed class ListeningRoom : IDisposable
    {
        readonly Studio app;
        readonly LyricsLibrary library;
        readonly DispatcherTimer timer = new DispatcherTimer(DispatcherPriority.Background);
        readonly Stopwatch manualClock = new Stopwatch();
        readonly Stopwatch demoClock = new Stopwatch();
        readonly SemaphoreSlim importWriter = new SemaphoreSlim(1,1);
        readonly LrcDocument demoLyrics = LrcDocument.Parse(
            "[00:00.00]\n[00:01.00]窗边响起第一声\n[00:05.00]茶杯轻轻碰一碰\n[00:09.00]等你抬头看一眼\n[00:13.00]一起听到最后一声\n[00:17.20]");
        MediaWatcher watcher;
        MediaReading reading;
        CancellationTokenSource request;
        LyricLookup lyrics;
        SoundPlayer demoSound;
        bool disposed, overlayFilled, manual, standalone, manualWantsPlay;
        int lookupRevision;
        int bindingRevision=-1;
        bool sawNativeLyric, triedOnline;
        long nativeLostAt;
        double manualBase, matchedDuration;
        string identity="", title="", artist="", sourceNote="", nativeLine="";
        public string Title { get { return title; } }
        public string Artist { get { return artist; } }
        public string PlayerLabel { get; private set; }
        public string Connection { get; private set; }
        public string Status { get; private set; }
        public string CurrentLine { get; private set; }
        public string NextLine { get; private set; }
        public string TimeLabel { get; private set; }
        public bool Playing { get; private set; }
        public bool HasTimeline { get { return IsDemo || (!standalone && reading!=null && reading.HasTimeline); } }
        public bool ManualActive { get { return manual; } }
        public bool IsDemo { get; private set; }
        public bool HasTrack { get { return !String.IsNullOrWhiteSpace(title); } }
        public int OffsetMs { get { return app.Data.LyricOffsetMs; } }
        static string FormatTime(double seconds)
        {
            int s=(int)Math.Max(0,Math.Min(604800,seconds));
            return (s/60).ToString(CultureInfo.InvariantCulture)+":"+(s%60).ToString("00",CultureInfo.InvariantCulture);
        }
        static string PlayerName(MediaReading m)
        {
            if (m==null || !m.HasSession) return "等待播放器";
            if (m.PlayerKey=="netease") return "网易云音乐";
            if (m.PlayerKey=="qq") return "QQ 音乐";
            if (m.PlayerKey=="kugou") return "酷狗音乐";
            return "系统媒体播放器";
        }
        public ListeningRoom(Studio app)
        {
            this.app=app;
            library=new LyricsLibrary(app.UserFolder);
            timer.Tick+=delegate { Update(); };
            Configure();
        }
        public void Configure()
        {
            if(disposed) return;
            var d=app.Data;
            if(d.PreferredPlayer!="qq" && d.PreferredPlayer!="kugou" && d.PreferredPlayer!="netease")d.PreferredPlayer="auto";
            d.LyricOffsetMs=Math.Max(-10000,Math.Min(10000,d.LyricOffsetMs));
            if(!d.SingAlongEnabled)
            {
                StopDemoAudio(); IsDemo=false; timer.Stop(); ResetTrack();
                if(watcher!=null){watcher.Dispose();watcher=null;}
                reading=null;Playing=false;PlayerLabel="听歌陪伴";Connection="已关闭";Status="开启后会读取播放器公开的播放信息。";TimeLabel="";
                Paint("", "", 0, false); return;
            }
            if(watcher==null)watcher=new MediaWatcher(TimeSpan.FromMilliseconds(750));
            watcher.ReadLyricWindow=d.ReadLyricWindow;
            if(watcher.PreferredPlayer!=d.PreferredPlayer){watcher.PreferredPlayer=d.PreferredPlayer;if(!IsDemo)ResetTrack();}
            timer.Interval=TimeSpan.FromMilliseconds(220);timer.Start();
            if(HasTrack&&!IsDemo)BeginLookup();
            Update();
        }
        void CancelLookup()
        {
            lookupRevision++;
            if(request!=null){request.Cancel();request=null;}
        }
        void ResetTrack()
        {
            CancelLookup();identity=title=artist=sourceNote=nativeLine="";
            lyrics=null;manual=false;standalone=false;manualWantsPlay=false;manualBase=matchedDuration=0;manualClock.Reset();
            sawNativeLyric=triedOnline=false; nativeLostAt=0;
        }
        void SetSong(string newIdentity,string newTitle,string newArtist,double duration)
        {
            ResetTrack();identity=newIdentity;title=newTitle??"";artist=newArtist??"";matchedDuration=duration;
        }
        async void BeginLookup()
        {
            CancelLookup();lyrics=null;
            if(!HasTrack || IsDemo) return;
            var tokenSource=new CancellationTokenSource();request=tokenSource;int revision=lookupRevision;
            bool online=app.Data.OnlineLyrics && nativeLine.Length==0;
            if(online)triedOnline=true;
            sourceNote=online?"正在匹配同步歌词…":"正在查找本地歌词…";
            try
            {
                var found=await library.ResolveAsync(title,artist,matchedDuration,online,tokenSource.Token,app.Data.LyricFolder??"");
                if(disposed || revision!=lookupRevision || tokenSource.IsCancellationRequested) return;
                lyrics=found;sourceNote=found==null?"暂未找到同步歌词。":found.Message;
            }
            catch(OperationCanceledException) { }
            catch(Exception e) { if(!disposed && revision==lookupRevision)sourceNote="歌词暂时无法读取。"+e.GetBaseException().Message; }
            finally { if(request==tokenSource)request=null;tokenSource.Dispose(); }
        }
        public void Reload()
        {
            if(disposed)return;
            if(!IsDemo)BeginLookup();
            Update();
        }
        public void SetOffset(int milliseconds)
        {
            app.Data.LyricOffsetMs=Math.Max(-10000,Math.Min(10000,milliseconds));app.Save();Update();
        }
        public void SetManualTime(double seconds)
        {
            if(!HasTrack || IsDemo || Double.IsNaN(seconds)||Double.IsInfinity(seconds)||seconds<0||seconds>604800)return;
            if(!standalone && reading!=null && reading.Duration.TotalSeconds>0) seconds=Math.Min(reading.Duration.TotalSeconds,seconds);
            manual=true;manualBase=seconds;manualClock.Reset();manualWantsPlay=true;Update();
        }
        public void ToggleManual()
        {
            if(!HasTrack || IsDemo)return;
            if(!manual){SetManualTime(reading!=null&&reading.HasTimeline?reading.PositionAt(DateTime.UtcNow).TotalSeconds:0);return;}
            manualWantsPlay=!manualWantsPlay;Update();
        }
        public void LeaveManual()
        {
            if(IsDemo){ExitDemo();return;}
            if(standalone)ResetTrack();
            else {manual=false;manualClock.Reset();}
            Update();
        }
        public void StartDemo()
        {
            if(disposed)return;
            if(!app.Data.SingAlongEnabled){app.Data.SingAlongEnabled=true;app.Save();Configure();}
            StopDemoAudio();ResetTrack();
            try
            {
                demoSound=new SoundPlayer(app.Asset("window-song.wav"));demoSound.Load();demoSound.Play();
                IsDemo=true;demoClock.Restart();title="窗边四句";artist="窗边手记 · 原创伴奏";Update();
            }
            catch(Exception e){StopDemoAudio();IsDemo=false;app.Desk.ShowStatus("试听音频无法播放。"+e.GetBaseException().Message);}
        }
        void StopDemoAudio()
        {
            demoClock.Reset();
            if(demoSound!=null){demoSound.Stop();demoSound.Dispose();demoSound=null;}
        }
        public void ExitDemo()
        {
            if(!IsDemo)return;
            StopDemoAudio();IsDemo=false;ResetTrack();Update();
        }
        public void BindFile(string path){ Bind(path,false); }
        public void BindText(string text){ Bind(text,true); }
        async void Bind(string input,bool pasted)
        {
            if(disposed || String.IsNullOrWhiteSpace(input))return;
            if(IsDemo)ExitDemo();
            if(!app.Data.SingAlongEnabled){app.Data.SingAlongEnabled=true;app.Save();Configure();}
            string fallback=pasted?"手动歌词":Path.GetFileNameWithoutExtension(input);
            bool createManual=standalone || !HasTrack;
            string currentIdentity=identity, song=createManual?fallback:title, who=createManual?"":artist;
            var store=library;
            CancelLookup();int revision=lookupRevision;bindingRevision=revision;
            try
            {
                var contentToBind=await Task.Run(delegate {
                    string content=pasted?input:LrcDocument.ReadText(input);
                    if(createManual)
                    {
                        Match t=Regex.Match(content,@"(?im)^\s*\[ti:([^\]\r\n]{1,180})\]"),a=Regex.Match(content,@"(?im)^\s*\[ar:([^\]\r\n]{1,180})\]");
                        if(t.Success)song=t.Groups[1].Value.Trim();
                        if(a.Success)who=a.Groups[1].Value.Trim();
                    }
                    return content;
                });
                if(disposed || revision!=lookupRevision || currentIdentity!=identity)return;
                await importWriter.WaitAsync();
                LyricLookup bound;
                try
                {
                    if(disposed || revision!=lookupRevision || currentIdentity!=identity)return;
                    bound=await Task.Run(()=>store.ImportText(song,who,contentToBind));
                }
                finally {importWriter.Release();}
                if(disposed || revision!=lookupRevision || currentIdentity!=identity)return;
                if(bound==null || bound.Document==null){app.Desk.ShowStatus(bound==null?"未能读取歌词。":bound.Message);return;}
                if(createManual)
                {
                    SetSong("manual|"+LyricsLibrary.SongKey(song,who),song,who,0);standalone=true;manual=true;manualWantsPlay=false;
                }
                lyrics=bound;sourceNote=bound.Message;Update();app.Desk.ShowStatus("同步歌词已保存。");
            }
            catch(Exception e){if(!disposed && revision==lookupRevision)app.Desk.ShowStatus("歌词未导入。"+e.GetBaseException().Message);}
            finally {if(bindingRevision==revision)bindingRevision=-1;}
        }
        void Update()
        {
            if(disposed || !app.Data.SingAlongEnabled)return;
            if(watcher!=null){watcher.Poll();reading=watcher.Latest;}
            double pos=0,total=0;bool hasTime=false;
            if(IsDemo)
            {
                pos=demoClock.Elapsed.TotalSeconds;total=18;
                if(pos>=total){ExitDemo();return;}
                PlayerLabel="内置试听";Connection="原创演示";Playing=true;
                TimeLabel=FormatTime(pos)+" / 0:18";Status="正在试听原创伴奏与逐句字幕。";
                RenderDocument(demoLyrics,pos,total,true);return;
            }
            var m=reading;
            if(!standalone)
            {
                string key=m!=null && m.HasSession ? m.AppId+"|"+LyricsLibrary.SongKey(m.Title,m.Artist) : "";
                if(key!=identity)
                {
                    SetSong(key,m==null?"":m.Title,m==null?"":m.Artist,m==null?0:m.Duration.TotalSeconds);
                    nativeLine=m==null?"":m.LiveLyric;if(HasTrack)BeginLookup();
                }
                else if(m!=null && matchedDuration<=0 && m.Duration.TotalSeconds>0 && HasTrack)
                { matchedDuration=m.Duration.TotalSeconds;if(lyrics==null || lyrics.Document==null)BeginLookup(); }
                nativeLine=m==null?"":m.LiveLyric;
                if(nativeLine.Length>0){sawNativeLyric=true;nativeLostAt=0;}
                else if(sawNativeLyric && HasTrack && app.Data.OnlineLyrics && !triedOnline && request==null &&
                    bindingRevision!=lookupRevision && (lyrics==null || (lyrics.Document==null && !lyrics.Instrumental)))
                {
                    long now=DateTime.UtcNow.Ticks;
                    if(nativeLostAt==0)nativeLostAt=now;
                    else if(now-nativeLostAt>TimeSpan.FromSeconds(1.5).Ticks)BeginLookup();
                }
            }
            PlayerLabel=standalone?"手动歌词":PlayerName(m);
            Connection=standalone?"手动对时":m==null||!m.HasSession?"等待连接":m.Source=="smtc"?"Windows 媒体会话":"播放器窗口";
            Playing=m!=null && m.StateKnown && m.Playing;
            if(manual)
            {
                Playing=manualWantsPlay && (standalone || m==null || !m.StateKnown || m.Playing);
                if(Playing)manualClock.Start();else manualClock.Stop();
                pos=manualBase+manualClock.Elapsed.TotalSeconds;hasTime=true;if(!standalone && m!=null)total=m.Duration.TotalSeconds;
                Connection+=" · 手动计时";
            }
            else if(m!=null && m.HasTimeline){pos=m.PositionAt(DateTime.UtcNow).TotalSeconds;total=m.Duration.TotalSeconds;hasTime=true;}
            TimeLabel=hasTime?FormatTime(pos)+(total>0?" / "+FormatTime(total):""):"未提供播放进度";
            if(!manual && nativeLine.Length!=0 && (lyrics==null || lyrics.Document==null || !hasTime))
            {
                Status=m!=null && m.LyricSource=="ocr"?"正在本机识别播放器的桌面歌词浮窗。":"正在显示播放器公开的当前歌词。";
                Paint(nativeLine,"",0,m!=null && (!m.StateKnown || m.Playing));
            }
            else if(lyrics!=null && lyrics.Document!=null && hasTime)
            {
                Status=(lyrics.Instrumental?"这是一首纯音乐。":sourceNote)+(manual?" · 手动对时":"");
                RenderDocument(lyrics.Document,pos,total,Playing);
            }
            else
            {
                Status=!HasTrack?(m!=null && m.HasSession?"已连接，等待播放器提供歌名或桌面歌词。":"请在播放器中播放一首歌。"):
                    lyrics!=null && lyrics.Document!=null?"歌词已就绪。播放器未公开进度，可以手动对时。":sourceNote;
                if(!manual && m!=null && !String.IsNullOrWhiteSpace(m.LyricDiagnostic))
                    Status=(Status??"")+" "+m.LyricDiagnostic;
                Paint("","",0,false);
            }
        }
        void RenderDocument(LrcDocument doc,double position,double duration,bool playing)
        {
            var frame=doc.At(Math.Max(0,position+OffsetMs/1000d),duration);
            Paint(frame.Text,frame.Translation.Length>0?frame.Translation:frame.NextText,frame.Progress,playing&&!frame.IsInstrumental);
        }
        void Paint(string current,string next,double fraction,bool sing)
        {
            CurrentLine=current;NextLine=next;
            if(!String.IsNullOrWhiteSpace(current))
            { app.Pet.SetKaraoke(current,next,fraction,sing);overlayFilled=true; }
            else if(overlayFilled){app.Pet.ClearKaraoke();overlayFilled=false;}
            if(timer.IsEnabled)
            {
                bool active=IsDemo || manual || (reading!=null && reading.HasSession);
                TimeSpan target=TimeSpan.FromMilliseconds(active?200:1100);
                if(timer.Interval!=target)timer.Interval=target;
            }
            if(app.Desk!=null && app.Desk.IsVisible)app.Desk.UpdateMusicPage();
        }
        public void Dispose()
        {
            disposed=true;timer.Stop();CancelLookup();StopDemoAudio();manualClock.Stop();
            if(watcher!=null)watcher.Dispose();
            if(app.Pet!=null)app.Pet.ClearKaraoke();
        }
    }
}
