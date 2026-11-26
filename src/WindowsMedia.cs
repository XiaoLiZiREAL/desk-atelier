using System;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Media.Control;

namespace DeskMate
{
    public sealed class MediaReading
    {
        public readonly string Title, Artist, AppId, Source, PlayerKey, Diagnostic, LiveLyric, LyricSource, LyricDiagnostic;
        public readonly TimeSpan Position, Duration;
        public readonly bool Playing, StateKnown, HasTimeline, HasSession;
        public readonly DateTime SampledUtc;
        public readonly double Rate;

        internal MediaReading(string title,string artist,string appId,string source,string player,
            TimeSpan at,TimeSpan total,bool playing,bool known,bool timeline,DateTime sampled,double rate,string diagnostic,string liveLyric="",string lyricSource="",string lyricDiagnostic="")
        {
            Title=title??""; Artist=artist??""; AppId=appId??""; Source=source; PlayerKey=player;
            Position=at; Duration=total; Playing=playing; StateKnown=known; HasTimeline=timeline;
            SampledUtc=sampled; Rate=rate; Diagnostic=diagnostic??""; HasSession=source!="none";LiveLyric=liveLyric??"";
            LyricSource=LiveLyric.Length==0?"":String.IsNullOrEmpty(lyricSource)?"accessibility":lyricSource;LyricDiagnostic=lyricDiagnostic??"";
        }
        public TimeSpan PositionAt(DateTime utc)
        {
            if(!HasTimeline)return TimeSpan.Zero;
            double t=Position.TotalSeconds;
            if(Playing && StateKnown)t+=Math.Max(0,(utc-SampledUtc).TotalSeconds)*Rate;
            return TimeSpan.FromSeconds(Math.Max(0,Math.Min(t,Duration.TotalSeconds)));
        }
        internal static MediaReading Empty(string diagnostic)
        { return new MediaReading("","","","none","",TimeSpan.Zero,TimeSpan.Zero,false,false,false,DateTime.UtcNow,1,diagnostic); }
    }

    public sealed class MediaWatcher : IDisposable
    {
        readonly object gate=new object();
        readonly Stopwatch clock=Stopwatch.StartNew();
        readonly long interval;
        readonly ChinesePlayers windows=new ChinesePlayers();
        WinMediaSource smtc;
        MediaReading latest=MediaReading.Empty("等待播放器");
        string choice="auto";
        bool busy,disposed,readWindow;
        int revision;
        long next,startRetry;
        string systemError="";
        MediaReading lastSystem;
        DateTime currentSongSince;

        public MediaWatcher():this(TimeSpan.FromMilliseconds(420)) { }
        public MediaWatcher(TimeSpan minimumInterval)
        { interval=(long)Math.Max(120,Math.Min(30000,minimumInterval.TotalMilliseconds)); }
        public MediaReading Latest { get { return Volatile.Read(ref latest); } }
        public bool ReadLyricWindow
        {
            get{lock(gate)return readWindow;}
            set
            {
                lock(gate)
                {
                    if(readWindow==value)return;
                    readWindow=value;revision++;windows.SetReadWindow(value);next=0;
                    if(!value && (latest.LyricSource=="ocr" || latest.LyricDiagnostic.Length>0))
                    {
                        var r=latest;
                        Volatile.Write(ref latest,new MediaReading(r.Title,r.Artist,r.AppId,r.Source,r.PlayerKey,r.Position,r.Duration,
                            r.Playing,r.StateKnown,r.HasTimeline,r.SampledUtc,r.Rate,r.Diagnostic,r.LyricSource=="ocr"?"":r.LiveLyric));
                    }
                }
                Poll();
            }
        }
        public string PreferredPlayer
        {
            get { lock(gate)return choice; }
            set
            {
                string p=value=="netease" || value=="qq" || value=="kugou" ? value : "auto";
                lock(gate)
                {
                    if(choice==p)return;
                    choice=p;revision++;windows.Invalidate();next=0;Volatile.Write(ref latest,MediaReading.Empty("正在切换播放器"));
                }
                Poll();
            }
        }

        public void Poll()
        {
            string selected;int version;
            lock(gate)
            {
                if(disposed || busy || clock.ElapsedMilliseconds<next)return;
                busy=true;next=clock.ElapsedMilliseconds+interval;selected=choice;version=revision;
            }
            Task.Run(()=>Read(selected,version));
        }
        void Changed() { lock(gate)next=0; }
        async Task Read(string selected,int version)
        {
            try
            {
                MediaReading reading=null;
                if(clock.ElapsedMilliseconds>=startRetry)
                {
                    try
                    {
                        if(smtc==null)smtc=new WinMediaSource(Changed);
                        reading=await smtc.Sample(selected).ConfigureAwait(false);
                        systemError="";
                    }
                    catch(Exception error)
                    {
                        systemError="系统媒体接口暂不可用（0x"+error.GetBaseException().HResult.ToString("X8")+"）";
                        startRetry=clock.ElapsedMilliseconds+3000;ReleaseSystem();
                    }
                }
                if(reading!=null && (lastSystem==null || reading.AppId!=lastSystem.AppId || !SameTag(reading.Title,lastSystem.Title) ||
                    !SameTag(reading.Artist,lastSystem.Artist)))currentSongSince=reading.SampledUtc;
                lastSystem=reading;
                bool inspect=reading==null || reading.PlayerKey!="other" || selected=="auto";
                if(inspect)
                {
                    var window=windows.Sample(WindowPreference(selected,reading));
                    reading=Choose(selected,reading,window,currentSongSince);
                }
                if(reading==null)reading=MediaReading.Empty(systemError.Length>0?systemError:
                    selected=="auto"?"等待播放器发布歌曲信息":"暂未检测到所选播放器的歌曲信息");
                lock(gate)if(!disposed && revision==version)Volatile.Write(ref latest,reading);
            }
            catch(Exception ex)
            {
                lock(gate)if(!disposed && revision==version)Volatile.Write(ref latest,MediaReading.Empty("播放器状态暂不可读（0x"+ex.GetBaseException().HResult.ToString("X8")+"）"));
            }
            finally
            {
                bool close;
                lock(gate){busy=false;close=disposed;}
                if(close)ReleaseSystem();
            }
        }
        internal static string WindowPreference(string selected,MediaReading system)
        {
            if(selected!="auto" || system==null || !system.StateKnown || !system.Playing || system.PlayerKey=="other")return selected;
            return system.PlayerKey;
        }
        static bool SameTag(string a,string b)
        {
            if(String.Equals(a,b,StringComparison.OrdinalIgnoreCase))return true;
            try
            {
                string left=String.Join(" ",(a??"").Normalize(NormalizationForm.FormKC).Split((char[])null,StringSplitOptions.RemoveEmptyEntries));
                string right=String.Join(" ",(b??"").Normalize(NormalizationForm.FormKC).Split((char[])null,StringSplitOptions.RemoveEmptyEntries));
                return String.Equals(left,right,StringComparison.OrdinalIgnoreCase);
            }catch(ArgumentException){return false;}
        }
        internal static MediaReading Choose(string selected,MediaReading system,MediaReading window,DateTime songSince=default(DateTime))
        {
            if(window==null || (selected!="auto" && window.PlayerKey!=selected))return system;
            if(system==null)return window;
            if(system.PlayerKey==window.PlayerKey && window.SampledUtc<songSince &&
                (window.Title.Length==0 || !SameTag(system.Title,window.Title) || (window.Artist.Length>0 && !SameTag(system.Artist,window.Artist))))return system;
            if(window.StateKnown && window.Playing && window.Title.Length>0 && (!system.StateKnown || !system.Playing || system.PlayerKey=="other"))
                return window;
            return Merge(system,window);
        }
        static MediaReading Merge(MediaReading system,MediaReading window)
        {
            if(system==null)return window;
            if(system.PlayerKey!=window.PlayerKey || (system.Title.Length>0 && window.Title.Length>0 && !SameTag(system.Title,window.Title)) ||
                (system.Artist.Length>0 && window.Artist.Length>0 && !SameTag(system.Artist,window.Artist)))return system;
            bool times=!system.HasTimeline && window.HasTimeline, text=system.Title.Length==0 && window.Title.Length>0;
            if(!times && !text && window.LiveLyric.Length==0 && window.LyricDiagnostic.Length==0)return system;
            DateTime now=DateTime.UtcNow;
            return new MediaReading(text?window.Title:system.Title,system.Artist.Length>0?system.Artist:window.Artist,system.AppId,
                times?"window":"smtc",system.PlayerKey,times?window.PositionAt(now):system.PositionAt(now),times?window.Duration:system.Duration,
                system.StateKnown?system.Playing:window.Playing,system.StateKnown||window.StateKnown,system.HasTimeline||times,now,system.Rate,system.Diagnostic,
                window.LiveLyric,window.LyricSource,window.LyricDiagnostic);
        }
        void ReleaseSystem()
        {
            var old=smtc;smtc=null;
            if(old!=null)try{old.Dispose();}catch(Exception){}
        }
        public void Dispose()
        {
            bool release;
            lock(gate){if(disposed)return;disposed=true;release=!busy;windows.SetReadWindow(false);Volatile.Write(ref latest,MediaReading.Empty("已停止读取"));}
            if(release)Task.Run((Action)ReleaseSystem);
        }
    }

    internal sealed class WinMediaSource:IDisposable
    {
        readonly Action changed;
        GlobalSystemMediaTransportControlsSessionManager manager;
        GlobalSystemMediaTransportControlsSession session;
        long metadataVersion,loadedVersion;
        DateTime loadedAt=DateTime.MinValue;
        string title="",artist="";
        MediaReading previous;
        DateTimeOffset lastStamp;
        TimeSpan lastRaw,lastDuration;
        bool freshTrack;
        internal WinMediaSource(Action changed) { this.changed=changed; }

        static async Task<T> Result<T>(IAsyncOperation<T> operation)
        {
            var completion=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            operation.Completed=delegate(IAsyncOperation<T> op,AsyncStatus status)
            {
                try{completion.TrySetResult(op.GetResults());}catch(Exception e){completion.TrySetException(e);}
            };
            Task<T> ready=completion.Task;
            if(!ready.IsCompleted && await Task.WhenAny(ready,Task.Delay(3200)).ConfigureAwait(false)!=ready)
            {try{operation.Cancel();}catch(Exception){}throw new TimeoutException("系统媒体服务未及时回应。");}
            return await ready.ConfigureAwait(false);
        }
        void ManagerChanged(GlobalSystemMediaTransportControlsSessionManager source,CurrentSessionChangedEventArgs e){changed();}
        void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager source,SessionsChangedEventArgs e){changed();}
        void Metadata(GlobalSystemMediaTransportControlsSession source,MediaPropertiesChangedEventArgs e)
        {Interlocked.Increment(ref metadataVersion);changed();}
        void Playback(GlobalSystemMediaTransportControlsSession source,PlaybackInfoChangedEventArgs e){changed();}
        void Timeline(GlobalSystemMediaTransportControlsSession source,TimelinePropertiesChangedEventArgs e){changed();}
        void Bind(GlobalSystemMediaTransportControlsSession selected)
        {
            if(Object.ReferenceEquals(selected,session))return;
            if(session!=null)
            {
                try{session.MediaPropertiesChanged-=Metadata;}catch(Exception){}
                try{session.TimelinePropertiesChanged-=Timeline;}catch(Exception){}
                try{session.PlaybackInfoChanged-=Playback;}catch(Exception){}
            }
            session=selected;title=artist="";previous=null;freshTrack=false;loadedAt=DateTime.MinValue;
            if(session!=null)
            {
                session.MediaPropertiesChanged+=Metadata;session.TimelinePropertiesChanged+=Timeline;session.PlaybackInfoChanged+=Playback;
            }
        }
        GlobalSystemMediaTransportControlsSession Select(string preferred)
        {
            var current=manager.GetCurrentSession();
            if(current!=null && preferred!="auto" && ChinesePlayers.Key(current.SourceAppUserModelId)!=preferred)current=null;
            if(current!=null && (preferred!="auto" || ChinesePlayers.Key(current.SourceAppUserModelId)!="other") && IsPlaying(current))return current;
            var all=manager.GetSessions().Where(s=>preferred=="auto" || ChinesePlayers.Key(s.SourceAppUserModelId)==preferred).ToList();
            if(preferred=="auto")
            {
                var domestic=all.FirstOrDefault(s=>ChinesePlayers.Key(s.SourceAppUserModelId)!="other" && IsPlaying(s));
                if(domestic!=null)return domestic;
            }
            if(current!=null && IsPlaying(current))return current;
            return all.FirstOrDefault(IsPlaying)??current??all.FirstOrDefault();
        }
        static bool IsPlaying(GlobalSystemMediaTransportControlsSession candidate)
        {try{return candidate.GetPlaybackInfo().PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;}catch(Exception){return false;}}
        internal async Task<MediaReading> Sample(string preferred)
        {
            if(manager==null)
            {
                manager=await Result(GlobalSystemMediaTransportControlsSessionManager.RequestAsync()).ConfigureAwait(false);
                manager.CurrentSessionChanged+=ManagerChanged;manager.SessionsChanged+=SessionsChanged;
            }
            Bind(Select(preferred));if(session==null)return null;
            long version=Interlocked.Read(ref metadataVersion);
            if(version!=loadedVersion || (DateTime.UtcNow-loadedAt).TotalSeconds>4)
            {
                string next="",nextArtist="";
                try
                {
                    var metadata=await Result(session.TryGetMediaPropertiesAsync()).ConfigureAwait(false);
                    next=metadata.Title??"";nextArtist=metadata.Artist??"";
                }catch(Exception){}
                if(previous!=null && (next!=title || nextArtist!=artist))freshTrack=true;
                title=next;artist=nextArtist;loadedAt=DateTime.UtcNow;loadedVersion=version;
            }
            GlobalSystemMediaTransportControlsSessionPlaybackInfo play=null;
            try{play=session.GetPlaybackInfo();}catch(Exception){}
            var status=play!=null?play.PlaybackStatus:GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing;
            bool playing=status==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            bool known=playing || status==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused ||
                status==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped;
            double rate=play==null?1:play.PlaybackRate.GetValueOrDefault(1);if(rate<=0 || rate>16)rate=1;
            GlobalSystemMediaTransportControlsSessionTimelineProperties timeline=null;
            try{timeline=session.GetTimelineProperties();}catch(Exception){}
            DateTime now=DateTime.UtcNow;
            TimeSpan duration=timeline!=null && timeline.EndTime>timeline.StartTime?timeline.EndTime-timeline.StartTime:TimeSpan.Zero;
            TimeSpan raw=timeline!=null && timeline.Position>=timeline.StartTime?timeline.Position-timeline.StartTime:TimeSpan.Zero;
            DateTimeOffset stamp=timeline==null?DateTimeOffset.MinValue:timeline.LastUpdatedTime;
            bool same=previous!=null && raw==lastRaw && stamp==lastStamp && lastDuration==duration;
            if(!same)freshTrack=false;
            bool has=duration>TimeSpan.Zero && duration<=TimeSpan.FromDays(7) && raw<=duration+TimeSpan.FromSeconds(1) && !freshTrack;
            double seconds=raw.TotalSeconds;
            if(has && same && previous.HasTimeline)seconds=previous.PositionAt(now).TotalSeconds;
            else if(has && playing && stamp.Year>=2000)seconds+=Math.Max(0,(now-stamp.UtcDateTime).TotalSeconds)*rate;
            TimeSpan at=has?TimeSpan.FromSeconds(Math.Max(0,Math.Min(duration.TotalSeconds,seconds))):TimeSpan.Zero;
            lastRaw=raw;lastStamp=stamp;lastDuration=duration;
            string id=session.SourceAppUserModelId??"";
            previous=new MediaReading(title,artist,id,"smtc",ChinesePlayers.Key(id),at,has?duration:TimeSpan.Zero,playing,known,has,now,rate,
                title.Length==0?"播放器尚未提供歌名":!has?"播放器未提供可用的歌曲进度":"");
            return previous;
        }
        public void Dispose()
        {
            Bind(null);
            if(manager!=null)
            {
                try{manager.CurrentSessionChanged-=ManagerChanged;}catch(Exception){}
                try{manager.SessionsChanged-=SessionsChanged;}catch(Exception){}
                manager=null;
            }
        }
    }
}
