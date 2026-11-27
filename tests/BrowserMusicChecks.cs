using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using DeskMate;

// Exercise the retail listening room and real WPF pet without polling or playing any audio.
internal static class BrowserMusicChecks
{
    const string First="窗边网页首曲", Second="窗边网页次曲", Artist="网页测试艺人";
    const BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    static Studio app;
    static MediaWatcher watcher;
    static readonly FieldInfo Latest=typeof(MediaWatcher).GetField("latest",Fields);
    static readonly MethodInfo Update=typeof(ListeningRoom).GetMethod("Update",Fields);
    static readonly ConstructorInfo Snapshot=typeof(MediaReading).GetConstructors(Fields)[0];

    static void AssertEventually(Func<bool> expected,string message)
    {
        var clock=Stopwatch.StartNew();
        while(clock.ElapsedMilliseconds<5000)
        {
            Update.Invoke(app.Music,null);
            if(expected()){Console.WriteLine("PASS "+message);return;}
            var frame=new DispatcherFrame();
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(90)};
            timer.Tick+=delegate{timer.Stop();frame.Continue=false;};
            timer.Start();Dispatcher.PushFrame(frame);
        }
        throw new InvalidOperationException(message);
    }

    static void Publish(string title,string source,bool playing,int seconds,string player="browser",string appId="chrome",string live="")
    {
        bool identified=title.Length>0 && (source=="smtc" || source.EndsWith("-web",StringComparison.Ordinal));
        bool timeline=identified && source!="soda-web";
        var sample=Snapshot.Invoke(new object[]{
            title,identified?Artist:"",appId,source,player,TimeSpan.FromSeconds(seconds),
            TimeSpan.FromSeconds(timeline?72:0),playing,true,timeline,DateTime.UtcNow,1d,"",live,live.Length>0?"accessibility":"",""
        });
        Latest.SetValue(watcher,sample);
    }

    static bool ContainsText(DependencyObject node,string text)
    {
        var label=node as TextBlock;
        if(label!=null && label.IsVisible && label.Text==text)return true;
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
            if(ContainsText(VisualTreeHelper.GetChild(node,i),text))return true;
        return false;
    }

    static bool CardContains(string text)
    {
        foreach(Window window in app.Windows)
            if(window.Title=="桌宠歌词" && window.IsVisible && ContainsText(window,text))return true;
        return false;
    }

    static bool UsesPose(string character,string action)
    {
        var poses=(Array)typeof(PetWindow).GetMethod("ActionFrames",Fields).Invoke(app.Pet,new object[]{character,action});
        foreach(object pose in poses)if(Object.ReferenceEquals(app.Pet.LiveSprite.Source,pose))return true;
        return false;
    }
    static string Motion()
    {return typeof(PetWindow).GetField("musicMotion",Fields).GetValue(app.Pet).ToString();}

    static void CheckPresentation()
    {
        watcher=new MediaWatcher();
        // Prevent the poller from querying the user's real players while these snapshots are supplied.
        typeof(MediaWatcher).GetField("next",Fields).SetValue(watcher,Int64.MaxValue);
        typeof(ListeningRoom).GetField("watcher",Fields).SetValue(app.Music,watcher);
        app.Data.SingAlongEnabled=true;
        Publish(First,"smtc",true,0);
        AssertEventually(()=>app.Music.Title==First && app.Music.Artist==Artist && app.Pet.ShowingBrowserTrack &&
            app.Pet.KaraokeMoving && String.IsNullOrEmpty(app.Music.CurrentLine) && String.IsNullOrEmpty(app.Pet.KaraokeLyric),
            "an active browser song with no LRC displays its track card and moves");
        Publish(First,"smtc",false,0);
        AssertEventually(()=>!app.Pet.KaraokeVisible && !app.Pet.KaraokeMoving,"a pause hides the track card and stops movement");
        Publish(Second,"netease-web",true,14);
        AssertEventually(()=>app.Music.Title==Second && app.Music.CurrentLine=="浏览器中的第二句" &&
            app.Pet.KaraokeVisible && app.Pet.KaraokeMoving && !app.Pet.ShowingBrowserTrack && app.Pet.KaraokeLyric==app.Music.CurrentLine,
            "a new NetEase Web track and seek switch the card to its synchronized local lyric");
        Publish("","browser-audio",true,0);
        AssertEventually(()=>!app.Music.HasTrack && !app.Music.HasTimeline && String.IsNullOrEmpty(app.Music.CurrentLine) &&
            app.Pet.ShowingBrowserTrack && app.Pet.KaraokeMoving && String.IsNullOrEmpty(app.Pet.KaraokeLyric),
            "unidentified browser audio clears the previous lyric and shows the generic card");
        Publish(First,"smtc",true,8,"soda","SodaMusic.exe");
        AssertEventually(()=>app.Music.HasTrack && app.Pet.KaraokeMoving && !app.Pet.ShowingBrowserTrack &&
            String.IsNullOrEmpty(app.Pet.KaraokeLyric) && CardContains(First) && CardContains("汽水音乐 · "+Artist),
            "Soda Music metadata without lyrics shows the client, artist and song separately from lyrics");
        Publish(First,"smtc",false,8,"soda","SodaMusic.exe");
        AssertEventually(()=>!app.Pet.KaraokeVisible && !app.Pet.KaraokeMoving,"pausing Soda hides the song card");
        Publish(Second,"smtc",true,14,"soda","SodaMusic.exe");
        AssertEventually(()=>app.Pet.KaraokeMoving && app.Music.CurrentLine=="浏览器中的第二句",
            "a Soda track with a matching LRC uses the reported playhead after resuming");
        Publish("","player-audio",true,0,"soda","SodaMusic.exe");
        AssertEventually(()=>!app.Music.HasTrack && !app.Music.HasTimeline && String.IsNullOrEmpty(app.Music.CurrentLine) &&
            app.Pet.KaraokeMoving && CardContains("汽水音乐正在播放音频"),
            "Soda audio with no metadata drops the previous lyrics and displays only its source");
        Publish(First,"smtc",true,0,"other","Unsupported.exe");
        AssertEventually(()=>!app.Pet.KaraokeVisible && !app.Pet.KaraokeMoving && String.IsNullOrEmpty(app.Music.CurrentLine),
            "an unidentified native app without lyrics clears the recognized player's card");
        Publish(First,"netease-web",true,8);
        AssertEventually(()=>app.Pet.ShowingBrowserTrack && CardContains("网易云音乐网页版 · "+Artist),
            "verified web controls label the web service instead of a generic browser");
        Publish(Second,"soda-web",true,0,live:"试听页当前行");
        AssertEventually(()=>!app.Music.HasTimeline && app.Pet.KaraokeMoving && app.Pet.KaraokeLyric=="试听页当前行",
            "Soda share sings the visible official lyric even when a full song LRC exists");
        Publish(Second,"soda-web",true,0,live:"试听页下一行");
        AssertEventually(()=>app.Pet.KaraokeLyric=="试听页下一行","Soda share follows the next highlighted line without a guessed clock");
        Publish(Second,"soda-web",false,0);
        AssertEventually(()=>!app.Pet.KaraokeVisible && !app.Pet.KaraokeMoving,"pausing a Soda excerpt clears its live lyric");
        Publish(First,"qianqian-web",true,8);
        AssertEventually(()=>app.Pet.ShowingBrowserTrack && CardContains("千千音乐网页版 · "+Artist),
            "a web-only service gets its verified player label");
        app.Data.Character="hitori";app.Pet.Sync();
        Publish("三段小曲","netease-web",true,1);
        AssertEventually(()=>Motion()=="Intro" && UsesPose("hitori","focus") && app.Music.CurrentLine=="",
            "Bocchi picks the guitar during the intro with no sung words");
        Publish("三段小曲","netease-web",true,2,live:"播放器当前歌词");
        AssertEventually(()=>Motion()=="Singing" && app.Pet.KaraokeLyric=="播放器当前歌词","a verified live vocal survives a late-starting local lyric file");
        Publish("三段小曲","netease-web",true,8);
        AssertEventually(()=>Motion()=="Singing" && UsesPose("hitori","song") && app.Pet.KaraokeLyric=="第一句",
            "the first timed line changes Bocchi to her singing poses");
        Publish("三段小曲","netease-web",true,22);
        AssertEventually(()=>Motion()=="Interlude" && UsesPose("hitori","focus") && app.Music.CurrentLine=="",
            "a marked break puts her hands back on the quiet guitar phrase");
        Publish("三段小曲","netease-web",true,22,live:"第一句");
        AssertEventually(()=>Motion()=="Interlude" && app.Music.CurrentLine=="",
            "a player still displaying the previous line does not sing over an explicit break");
        Publish("三段小曲","netease-web",false,22);
        AssertEventually(()=>!app.Pet.KaraokeMoving && !app.Pet.KaraokeVisible,"pausing a blank instrumental cue stops the performance");
        Publish("三段小曲","netease-web",true,36);
        AssertEventually(()=>Motion()=="Singing" && UsesPose("hitori","song") && app.Pet.KaraokeLyric=="第二句",
            "seeking to a new lyric resumes singing at the new playhead");
        Publish(First,"netease-web",true,4);
        AssertEventually(()=>Motion()=="Listening" && app.Music.CurrentLine=="" && app.Pet.ShowingBrowserTrack,
            "a metadata-only track clears the sung phrase and listens");
        app.Music.SetEnabled(false);
        AssertEventually(()=>!app.Music.Playing && !app.Pet.KaraokeVisible && !app.Pet.KaraokeMoving,
            "the listening switch clears the card and stops movement");
    }

    [STAThread] static int Main(string[] args)
    {
        string root=Directory.CreateDirectory(Path.GetFullPath(args[0])).FullName;
        string state=Directory.CreateDirectory(Path.Combine(root,"state")).FullName;
        string lyrics=Directory.CreateDirectory(Path.Combine(root,"lyrics")).FullName;
        Environment.SetEnvironmentVariable("DESKATELIER_DATA",state);
        File.WriteAllText(Path.Combine(lyrics,Artist+" - "+Second+".lrc"),
            "[00:00.00]开始听歌\n[00:10.00]浏览器中的第二句\n[01:10.00]收尾",Encoding.UTF8);
        File.WriteAllText(Path.Combine(lyrics,Artist+" - 三段小曲.lrc"),"[00:07.00]第一句\n[00:14.00]\n[00:34.00]第二句",Encoding.UTF8);
        try
        {
            app=new Studio(state);
            app.Data.PetWelcomeSeen=true;
            app.Data.HideFullscreen=false;
            app.Data.LyricFolder=lyrics;
            app.Data.OnlineLyrics=false;
            app.Data.ReadLyricWindow=false;
            app.Data.SingAlongEnabled=false;
            app.StartStudio(null);
            CheckPresentation();
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine("FAIL "+error.GetBaseException().Message);return 1;}
        finally{if(app!=null)app.Quit();}
    }
}
