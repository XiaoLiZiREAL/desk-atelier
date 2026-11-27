using System;
using System.Globalization;
using System.Threading;
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
        MediaWatcher watcher;
        MediaReading reading;
        CancellationTokenSource request;
        LyricLookup lyrics;
        bool disposed, overlayFilled, sawNativeLyric, triedOnline;
        DateTime? nativeLostAt;
        string identity = "";
        double matchedDuration;

        public string Title { get; private set; }
        public string Artist { get; private set; }
        public bool Playing { get; private set; }
        public bool HasTrack { get { return !String.IsNullOrWhiteSpace(Title); } }
        public bool HasTimeline { get { return reading != null && reading.HasTimeline; } }
        public string CurrentLine { get; private set; }
        public string PlayerLabel { get { return reading == null ? "" : reading.PlayerKey; } }
        public string Connection { get { return reading == null ? "" : reading.Source; } }
        public string TimeLabel
        {
            get { return HasTimeline ? ((long)reading.PositionAt(DateTime.UtcNow).TotalSeconds).ToString(CultureInfo.InvariantCulture) : ""; }
        }

        public ListeningRoom(Studio app)
        {
            this.app = app;
            library = new LyricsLibrary(app.UserFolder);
            timer.Tick += delegate { Update(); };
            if (app.Data.SingAlongEnabled) StartWatching();
        }

        public void SetEnabled(bool enabled)
        {
            if (disposed || app.Data.SingAlongEnabled == enabled) return;
            app.Data.SingAlongEnabled = enabled;
            if (enabled) StartWatching();
            else StopWatching();
            app.Save();
        }

        internal void SetLyricPrivacy(bool online, bool readWindow)
        {
            bool networkChanged = app.Data.OnlineLyrics != online;
            app.Data.OnlineLyrics = online;
            app.Data.ReadLyricWindow = readWindow;
            if (disposed || watcher == null) return;
            watcher.ReadLyricWindow = readWindow;
            if (networkChanged)
            {
                triedOnline = false;
                if (HasTrack) BeginLookup(watcher.Latest.LiveLyric.Length != 0);
            }
            Update();
        }

        void StartWatching()
        {
            // Always arbitrate across players, including profiles with a former preferred-player setting.
            watcher = new MediaWatcher(TimeSpan.FromMilliseconds(750));
            watcher.ReadLyricWindow = app.Data.ReadLyricWindow;
            timer.Start();
            Update();
        }

        void StopWatching()
        {
            timer.Stop();
            if (watcher != null) { watcher.Dispose(); watcher = null; }
            ResetTrack();
            reading = null;
            Playing = false;
            Paint("", "", 0, false);
        }

        void CancelLookup()
        {
            if (request != null) { request.Cancel(); request = null; }
        }

        void ResetTrack()
        {
            CancelLookup();
            lyrics = null;
            identity = Title = Artist = "";
            matchedDuration = 0;
            sawNativeLyric = triedOnline = false;
            nativeLostAt = null;
        }

        async void BeginLookup(bool hasLiveLyric)
        {
            CancelLookup();
            var current = new CancellationTokenSource();
            request = current;
            bool online = app.Data.OnlineLyrics && !hasLiveLyric;
            if (online) triedOnline = true;
            string folder = app.Data.LyricFolder;
            if (String.IsNullOrWhiteSpace(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            try
            {
                var found = await library.ResolveAsync(Title, Artist, matchedDuration, online, current.Token, folder);
                if (request == current) lyrics = found;
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                System.Diagnostics.Trace.WriteLine("Lyric lookup failed: " + error.GetBaseException().GetType().Name);
            }
            finally
            {
                if (request == current) request = null;
                current.Dispose();
            }
        }

        void Update()
        {
            if (disposed || watcher == null) return;
            watcher.Poll();
            reading = watcher.Latest;
            timer.Interval = TimeSpan.FromMilliseconds(reading.HasSession ? 200 : 1100);
            Playing = reading.StateKnown && reading.Playing;
            bool live = reading.LiveLyric.Length > 0;
            string key = reading.HasSession ? reading.AppId + "|" + LyricsLibrary.SongKey(reading.Title, reading.Artist) : "";
            if (key != identity)
            {
                ResetTrack();
                identity = key;
                Title = reading.Title;
                Artist = reading.Artist;
                matchedDuration = reading.Duration.TotalSeconds;
                if (HasTrack) BeginLookup(live);
            }
            else if (HasTrack && matchedDuration <= 0 && reading.Duration.TotalSeconds > 0)
            {
                matchedDuration = reading.Duration.TotalSeconds;
                if (lyrics == null || lyrics.Document == null) BeginLookup(live);
            }

            if (live)
            {
                sawNativeLyric = true;
                nativeLostAt = null;
            }
            else if (sawNativeLyric && HasTrack && app.Data.OnlineLyrics && !triedOnline && request == null &&
                     (lyrics == null || (lyrics.Document == null && !lyrics.Instrumental)))
            {
                if (!nativeLostAt.HasValue) nativeLostAt = DateTime.UtcNow;
                else if (DateTime.UtcNow - nativeLostAt.Value > TimeSpan.FromSeconds(1.5)) BeginLookup(false);
            }

            if (lyrics != null && lyrics.Document != null && reading.HasTimeline)
            {
                double offset = Math.Max(-10000, Math.Min(10000, app.Data.LyricOffsetMs)) / 1000d;
                var position = reading.PositionAt(DateTime.UtcNow).TotalSeconds;
                var frame = lyrics.Document.At(Math.Max(0, position + offset), reading.Duration.TotalSeconds);
                if (!String.IsNullOrWhiteSpace(frame.Text))
                {
                    Paint(frame.Text, frame.Translation.Length > 0 ? frame.Translation : frame.NextText,
                          frame.Progress, Playing && !frame.IsInstrumental);
                    return;
                }
            }
            if (live) Paint(reading.LiveLyric, "", 0, !reading.StateKnown || Playing);
            else if (Playing && (reading.PlayerKey == "browser" || ChinesePlayers.IsSupportedPlayer(reading.PlayerKey)))
            {
                CurrentLine = "";
                app.Pet.ShowTrack(reading);
                overlayFilled = true;
            }
            else Paint("", "", 0, false);
        }

        void Paint(string current, string next, double fraction, bool sing)
        {
            CurrentLine = current;
            if (!String.IsNullOrWhiteSpace(current))
            {
                app.Pet.SetKaraoke(current, next, fraction, sing);
                overlayFilled = true;
            }
            else if (overlayFilled)
            {
                app.Pet.ClearKaraoke();
                overlayFilled = false;
            }
        }

        public void Dispose()
        {
            disposed = true;
            StopWatching();
        }
    }
}
