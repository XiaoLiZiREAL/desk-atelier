using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media;
using DeskMate;
using Forms = System.Windows.Forms;

internal static class MusicChecks
{
    [STAThread] public static int Main(string[] args){return Integration.TestListeningRoom(args);}
}

internal static partial class Integration
{
    const string FixtureArtist="窗边手记", FirstTrack="窗边来信", SecondTrack="纸船出发";
    static string localLibrary, nativeFixture;
    const string FixtureLyrics="[ar:窗边手记]\n[ti:窗边来信]\n[00:00]把这一页留在窗边\n[00:08]窗沿落下一点光\n[00:22]灯影在纸上慢下来\n[00:44]翻向下一页\n[01:02]把明天写在背面\n[01:45]\n";

    internal static int TestListeningRoom(string[] args)
    {
        string root=Path.GetFullPath(args.Length>0?args[0]:@"work\qa");
        nativeFixture=args.Length>1?Path.GetFullPath(args[1]):null;
        screenDir=Path.Combine(root,"music-screens");
        stateDir=Path.Combine(root,"music-integration-"+Guid.NewGuid().ToString("N"));
        localLibrary=Path.Combine(stateDir,"导出歌词"); Directory.CreateDirectory(localLibrary);Directory.CreateDirectory(screenDir);
        Environment.SetEnvironmentVariable("DESKATELIER_DATA",stateDir);
        File.WriteAllText(Path.Combine(localLibrary,FixtureArtist+" - "+FirstTrack+".lrc"),FixtureLyrics,Encoding.UTF8);
        bool good=false;
        try
        {
            using(var sha=SHA256.Create())using(var input=File.OpenRead(typeof(Studio).Assembly.Location))
                Say("Build SHA256  "+BitConverter.ToString(sha.ComputeHash(input)).Replace("-",""));
            app=new Studio(stateDir);
            Check(app.Data.SingAlongEnabled,"Fresh profile automatically enables listening");
            Check(!app.Data.OnlineLyrics&&!app.Data.ReadLyricWindow,"Both optional lyric permissions default off");
            app.Data.HideFullscreen=false;app.Data.Quiet=false;app.Data.PetWelcomeSeen=true;
            app.Data.SingAlongEnabled=false;app.Data.PreferredPlayer="kugou";
            app.Data.LyricFolder=localLibrary;app.Data.OnlineLyrics=true;app.Data.ReadLyricWindow=true;app.Save();
            app.StartStudio(null);Pump(350);
            Check(app.Music!=null&&!StudioWindow.SupportsTool("music")&&!app.Desk.IsVisible,"Listening no longer opens a tool window");
            Nav("settings");Expand("歌词隐私");
            Check(LyricPermission("联网匹配同步歌词").IsChecked==true&&LyricPermission("识别播放器的歌词画面").IsChecked==true,
                "Existing lyric permissions remain visible and individually revocable");
            SetPermission("联网匹配同步歌词",false);SetPermission("识别播放器的歌词画面",false);app.Desk.Hide();
            Check(!Store.Load().OnlineLyrics&&!Store.Load().ReadLyricWindow&&!app.Data.SingAlongEnabled,
                "Settings persist individual revocation while the music master switch is off");
            var tray=(Forms.ContextMenuStrip)typeof(PetWindow).GetField("menu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(app.Pet);
            var traySwitch=tray.Items.OfType<Forms.ToolStripMenuItem>().Single(i=>i.Text=="听音乐");
            Check(!traySwitch.Checked,"Tray reflects the saved off setting at startup");
            SwitchFromMenu(false,"01-switch-off");
            Check(traySwitch.Checked&&Store.Load().SingAlongEnabled,"Right-click switch enables and saves background listening");
            traySwitch.PerformClick();Pump(90);
            Check(!app.Data.SingAlongEnabled&&!Store.Load().SingAlongEnabled&&!app.Desk.IsVisible,"Tray switch disables listening without opening a card");
            SwitchFromMenu(false,null);
            TestNativeTimeline();TestWindowLyric();
            var disk=Store.Load();
            Check(disk.SingAlongEnabled&&!disk.OnlineLyrics&&!disk.Quiet&&disk.LyricFolder==localLibrary,"Lyrics settings and selected folder survive the upgrade");
            app.Quit();
            Check(app.Exiting && !app.Pet.KaraokeVisible,"Quit closes the companion's lyric window");
            Say("ALL MUSIC INTEGRATION CHECKS PASSED");good=true;
        }
        catch(Exception e){Say("FAIL  "+e.GetType().Name+"  "+e.Message);Say(e.StackTrace);}
        finally
        {
            if(app!=null&&!app.Exiting)try{app.Quit();}catch{}
            File.WriteAllLines(Path.Combine(screenDir,"report.txt"),log.ToArray(),new UTF8Encoding(true));
        }
        return good?0:1;
    }

    static void Until(Func<bool> test,string label,int timeout=9500)
    {
        var clock=Stopwatch.StartNew();
        while(!test()&&clock.ElapsedMilliseconds<timeout)Pump(90);
        if(!test())throw new Exception(label+" timeout. Source="+app.Music.Connection+", titleMatchesFixture="+
            (app.Music.Title==FirstTrack||app.Music.Title==SecondTrack||app.Music.Title=="窗边验收歌")+
            ", visible="+app.Pet.KaraokeVisible+", playback="+app.Music.Playing+", time="+app.Music.TimeLabel);
        Check(true,label);
    }

    static void SwitchFromMenu(bool before,string screenshot)
    {
        var menu=Quick();Pump(90);menu.UpdateLayout();
        var item=menu.Items.OfType<MenuItem>().Single(i=>Equals(i.Header,"听音乐"));
        Check(item.IsCheckable && item.IsChecked==before,"Menu switch shows the stored "+(before?"on":"off")+" state");
        if(screenshot!=null)
        {
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight),96,96,PixelFormats.Pbgra32);
            bitmap.Render(menu);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using(var file=File.Create(Path.Combine(screenDir,screenshot+".png")))encoder.Save(file);
        }
        var peer=new MenuItemAutomationPeer(item);
        var toggle=(IToggleProvider)peer.GetPattern(PatternInterface.Toggle);
        toggle.Toggle();
        menu.IsOpen=false;Pump(110);
        Check(app.Data.SingAlongEnabled!=before&&!app.Desk.IsVisible,"Native menu toggle changes the background service without a workspace");
    }

    static CheckBox LyricPermission(string label)
    {
        return Elements<CheckBox>().Single(b=>Equals(b.Content,label));
    }

    static void SetPermission(string label,bool value)
    {
        var box=LyricPermission(label);
        box.BringIntoView();box.IsChecked=value;box.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent,box));
    }

    static void TestLivePrivacy()
    {
        var fields=BindingFlags.Instance|BindingFlags.NonPublic;
        var watcher=(MediaWatcher)typeof(ListeningRoom).GetField("watcher",fields).GetValue(app.Music);
        var field=typeof(ListeningRoom).GetField("library",fields);
        var original=(LyricsLibrary)field.GetValue(app.Music);
        using(var started=new ManualResetEventSlim())
        using(var cancelled=new ManualResetEventSlim())
        {
            // Use an offline responder so this test can observe cancellation without contacting LRCLIB.
            var testLibrary=new LyricsLibrary(stateDir,delegate(Uri uri,CancellationToken token){
                started.Set();
                if(!token.WaitHandle.WaitOne(5000))throw new TimeoutException("Privacy switch did not cancel the lookup");
                cancelled.Set();
                return "{\"trackName\":\"纸船出发\",\"artistName\":\"窗边手记\",\"duration\":120,\"syncedLyrics\":\"[00:00]撤回后的结果\"}";
            });
            field.SetValue(app.Music,testLibrary);
            try
            {
                Nav("settings");Expand("歌词隐私");
                SetPermission("识别播放器的歌词画面",true);
                Check(watcher.ReadLyricWindow&&!app.Data.OnlineLyrics,"OCR can be enabled independently on the active watcher");
                SetPermission("联网匹配同步歌词",true);
                Until(()=>started.IsSet,"Opting into network lookup starts matching the current song");
                SetPermission("联网匹配同步歌词",false);
                Until(()=>cancelled.IsSet&&app.Music.CurrentLine=="","Revoking network access cancels the active request and drops its late response");
                SetPermission("识别播放器的歌词画面",false);
                Check(!watcher.ReadLyricWindow&&!Store.Load().ReadLyricWindow&&app.Music.Playing,"Revoking OCR immediately updates the watcher while playback keeps running");
            }
            finally
            {
                if(app.Data.OnlineLyrics)SetPermission("联网匹配同步歌词",false);
                field.SetValue(app.Music,original);
                app.Desk.Hide();
            }
        }
    }

    static void TestNativeTimeline()
    {
        using(var song=new NativeTestSong(stateDir,FirstTrack,FixtureArtist))
        {
            var player=song.Player;
            song.Position(12);
            Until(()=>app.Music.Title==FirstTrack&&app.Music.Artist==FixtureArtist&&app.Music.HasTimeline&&app.Music.CurrentLine=="窗沿落下一点光"&&app.Music.Connection=="smtc",
                "Real Windows session and local LRC resolve automatically despite the legacy single-provider preference");
            Until(()=>app.Pet.KaraokeMoving&&app.Pet.KaraokeVisible&&app.Pet.KaraokeLyric==app.Music.CurrentLine,"Playing native media animates the pet with the matching lyric");
            SavePetAndPanel("04-desktop-native");
            player.Pause();player.PlaybackSession.Position=TimeSpan.FromSeconds(26);
            song.Controls.PlaybackStatus=MediaPlaybackStatus.Paused;song.Position(26);
            Until(()=>!app.Music.Playing&&app.Music.CurrentLine=="灯影在纸上慢下来"&&!app.Pet.KaraokeMoving,"Native pause updates the current line and stops singing");
            string held=app.Music.TimeLabel;Pump(1150);
            Check(app.Music.TimeLabel==held&&app.Music.CurrentLine=="灯影在纸上慢下来","Paused media keeps its exact lyric");
            player.PlaybackSession.Position=TimeSpan.FromSeconds(54);song.Position(54);
            Until(()=>app.Music.CurrentLine=="翻向下一页"&&!app.Music.Playing,"Seeking paused media updates its synchronized subtitle");
            player.Play();song.Controls.PlaybackStatus=MediaPlaybackStatus.Playing;song.Position(54);
            Until(()=>app.Music.Playing&&app.Pet.KaraokeMoving,"Native resume restarts the motion");
            Nav("settings");
            var quiet=Elements<CheckBox>().Single(b=>Equals(b.Content,"安静陪伴，暂停主动活动和字幕"));
            quiet.IsChecked=true;quiet.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent,quiet));
            Until(()=>!app.Pet.KaraokeMoving&&!app.Pet.KaraokeVisible,"Quiet mode suppresses the caption and motion");
            quiet.IsChecked=false;quiet.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent,quiet));app.Desk.Hide();
            Until(()=>app.Pet.KaraokeVisible,"Leaving quiet mode restores live captions");
            SwitchFromMenu(true,"02-switch-on");
            Check(!app.Music.HasTrack&&!app.Music.Playing&&!app.Pet.KaraokeVisible&&!app.Pet.KaraokeMoving,"Disabling listening immediately clears the track, caption and animation");
            Pump(1300);
            Check(!app.Pet.KaraokeVisible&&!app.Music.HasTrack&&!Store.Load().SingAlongEnabled,"An outstanding media sample cannot repopulate a disabled listener");
            SwitchFromMenu(false,null);
            Until(()=>app.Music.Title==FirstTrack&&app.Music.Playing&&app.Pet.KaraokeVisible,"Re-enabling detects the still-playing session without reopening the player");
            song.Track(SecondTrack,FixtureArtist);player.PlaybackSession.Position=TimeSpan.FromSeconds(1);song.Position(1);
            Until(()=>app.Music.Title==SecondTrack&&String.IsNullOrEmpty(app.Music.CurrentLine)&&!app.Pet.KaraokeVisible,"Changing to a song without lyrics clears the old caption");
            TestLivePrivacy();
        }
    }

    static void TestWindowLyric()
    {
        if(nativeFixture==null||!File.Exists(nativeFixture))throw new FileNotFoundException("QQ-shaped accessibility fixture required",nativeFixture);
        string sentinel=Path.Combine(stateDir,"end-window-fixture.signal");
        using(var helper=Process.Start(new ProcessStartInfo{FileName=nativeFixture,Arguments="--hold \""+sentinel+"\"",UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden}))
        {
            try
            {
                Until(()=>app.Music.Title=="窗边验收歌"&&app.Music.Artist=="验收伙伴"&&app.Music.Connection=="window"&&app.Music.CurrentLine=="雨滴敲着窗沿",
                    "Auto recognition picks up QQ-style player metadata and live desktop lyrics without a local file");
                Until(()=>app.Music.PlayerLabel=="qq"&&app.Pet.KaraokeLyric=="雨滴敲着窗沿"&&app.Pet.KaraokeVisible,
                    "Live accessible desktop lyrics reach the companion independently of a timeline");
            }
            finally{File.WriteAllText(sentinel,"finished");var watch=Stopwatch.StartNew();while(!helper.HasExited&&watch.ElapsedMilliseconds<4000)Pump(75);}
        }
    }
    static void SavePetAndPanel(string name)
    {
        var lyric=app.Windows.Cast<Window>().First(w=>w.Title=="桌宠歌词"&&w.IsVisible);
        var pet=app.Pet; var card=(Border)lyric.Content; Say("Overlay geometry window="+lyric.ActualHeight+" card="+card.ActualHeight+" padding="+card.Padding+" desired="+card.DesiredSize); foreach(var label in Walk(card).OfType<TextBlock>()){var p=label.TranslatePoint(new Point(0,0),card);Say("Overlay child y="+p.Y+" h="+label.ActualHeight+" desired="+label.DesiredSize+" visible="+label.IsVisible+" style="+label.FontSize);}var bar=Walk(card).OfType<Grid>().First(g=>Math.Abs(g.Height-2)<0.01);var barEnd=bar.TranslatePoint(new Point(0,bar.ActualHeight),card);Check(barEnd.Y<=card.ActualHeight-card.Padding.Bottom+.6,"Both subtitle lines and their progress bar fit the actual layered window");
        double left=Math.Min(lyric.Left,pet.Left),top=Math.Min(lyric.Top,pet.Top);
        double width=Math.Max(lyric.Left+lyric.ActualWidth,pet.Left+pet.ActualWidth)-left;
        double height=Math.Max(lyric.Top+lyric.ActualHeight,pet.Top+pet.ActualHeight)-top;
        var canvas=new DrawingVisual();
        using(var g=canvas.RenderOpen())
        {
            foreach(Window window in new Window[]{pet,lyric})
            {
                var content=(FrameworkElement)window.Content;
                var bitmap=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);
                bitmap.Render(content);
                g.DrawImage(bitmap,new Rect(window.Left-left,window.Top-top,content.ActualWidth,content.ActualHeight));
            }
        }
        var result=new RenderTargetBitmap((int)Math.Ceiling(width),(int)Math.Ceiling(height),96,96,PixelFormats.Pbgra32);
        result.Render(canvas);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(result));
        using(var file=File.Create(Path.Combine(screenDir,name+".png")))png.Save(file);
        Say("PNG   "+name+"  "+result.PixelWidth+"x"+result.PixelHeight+" separate real layered windows");
    }
}
