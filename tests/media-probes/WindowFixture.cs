using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using DeskMate;

public static class WindowFixture
{
    static TextBlock Label(string id,string name)
    {var t=new TextBlock { Text=name };AutomationProperties.SetAutomationId(t,id);return t;}
    [STAThread]
    public static int Main(string[] args)
    {
        bool hold=args.Length==2 && args[0]=="--hold";
        if(!hold && (args.Length!=1 || args[0]!="--fixture"))return 2;
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        var host=new Window{Title="QQ音乐", Width=350,Height=170,ShowActivated=false,ShowInTaskbar=false,Opacity=0};
        var lyrics=new Window{Title="QQ音乐桌面歌词",Width=300,Height=100,ShowActivated=false,ShowInTaskbar=false,Opacity=0};
        var panel=new StackPanel();AutomationProperties.SetAutomationId(panel,"playerBar");
        var time=Label("elapsedTotal","00:36 / 04:00");var button=new Button{Content="暂停"};AutomationProperties.SetAutomationId(button,"btnPause");
        panel.Children.Add(Label("songName","窗边验收歌"));panel.Children.Add(Label("artistName","验收伙伴"));panel.Children.Add(time);panel.Children.Add(button);
        host.Content=panel;lyrics.Content=Label("","雨滴敲着窗沿");
        host.Show();lyrics.Show();
        if(hold)
        {
            var deadline=DateTime.UtcNow.AddSeconds(35);var stop=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
            stop.Tick+=delegate{
                if(System.IO.File.Exists(args[1]) || DateTime.UtcNow>=deadline){stop.Stop();app.Shutdown();return;}
                button.Content=System.IO.File.Exists(args[1]+".pause")?"播放":"暂停";
            };
            stop.Start();app.Run();return 0;
        }
        int result=1,phase=0;
        using(var watch=new MediaWatcher(TimeSpan.FromMilliseconds(180)))
        {
            watch.PreferredPlayer="qq";var clock=Stopwatch.StartNew();
            var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
            timer.Tick+=delegate
            {
                watch.Poll();var r=watch.Latest;
                if(r.PlayerKey=="qq"&&r.Source=="window"&&r.Title=="窗边验收歌"&&r.Artist=="验收伙伴"&&r.HasTimeline&&r.LiveLyric=="雨滴敲着窗沿")
                {
                    if(phase==0&&r.StateKnown&&r.Playing&&r.Position.TotalSeconds==36)
                    {Console.WriteLine("PASS actual native Windows/UIA player discovery, reading a separate desktop lyric window");phase=1;time.Text="01:19 / 04:00";button.Content="播放";}
                    else if(phase==1&&r.StateKnown&&!r.Playing&&r.Position.TotalSeconds==79)
                    {Console.WriteLine("PASS changed accessibility value and paused transport");result=0;timer.Stop();app.Shutdown();}
                }
                if(clock.ElapsedMilliseconds>9000){Console.Error.WriteLine("UIA time out; "+r.PlayerKey+"/"+r.Source+" title="+(r.Title=="窗边验收歌")+" lyric="+(r.LiveLyric=="雨滴敲着窗沿")+" timeline="+r.HasTimeline+" known="+r.StateKnown+" playing="+r.Playing);timer.Stop();app.Shutdown();}
            };
            timer.Start();app.Run();
        }
        return result;
    }
}
