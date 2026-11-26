using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
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

    static void Publish(string title,string source,bool playing,int seconds)
    {
        bool timeline=source=="smtc" || source=="netease-web";
        var sample=Snapshot.Invoke(new object[]{
            title,timeline?Artist:"","chrome",source,"browser",TimeSpan.FromSeconds(seconds),
            TimeSpan.FromSeconds(timeline?72:0),playing,true,timeline,DateTime.UtcNow,1d,"","","",""
        });
        Latest.SetValue(watcher,sample);
    }

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
