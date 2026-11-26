using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media;
using Windows.Media.Playback;
using DeskMate;

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
            app=new Studio(stateDir);app.Data.HideFullscreen=false;app.Data.Quiet=false;app.Data.PreferredPlayer="kugou";
            app.Data.LyricFolder=localLibrary;app.Data.OnlineLyrics=false;app.Save();
            app.StartStudio(null);Pump(350);Nav("music","听歌陪伴");
            Check(app.Music!=null&&app.Desk.CurrentTool=="music","Compiled studio opened its real music workspace");
            if(args.Skip(2).Contains("--picker-only"))
            {
                string sample=Path.Combine(stateDir,"picker.lrc");
                File.WriteAllText(sample,"[ti:选择文件验收]\n[00:00]通过窗口导入",Encoding.UTF8);
                ImportWithPicker(sample);Until(()=>app.Music.Title=="选择文件验收"&&app.Music.CurrentLine=="通过窗口导入","System file picker imported LRC through the application's normal entry");
                return 0;
            }
            TestManualAndDemo();TestNativeTimeline();TestWindowLyric();
            var disk=Store.Load();
            Check(disk.SingAlongEnabled&&!disk.OnlineLyrics&&!disk.Quiet&&disk.LyricFolder==localLibrary,"Settings and selected lyric folder persisted without networking");
            app.Quit();
            Check(app.Exiting && !app.Pet.KaraokeVisible,"Quit closed the companion's lyric window");
            Say("ALL MUSIC INTEGRATION CHECKS PASSED");good=true;
        }
        catch(Exception e)
        {Say("FAIL  "+e.GetType().Name+"  "+e.Message);Say(e.StackTrace);try{Capture(app.Desk,"failed-music");}catch{}}
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
            ", status="+app.Music.Status+", visible="+app.Pet.KaraokeVisible+", playback="+app.Music.Playing+", time="+app.Music.TimeLabel);
        Check(true,label);
    }
    static void SetCheck(string name,bool value)
    {
        if(name=="联网查找时间轴歌词"||name=="读取播放器的画面歌词")Expand("歌词来源"); var box=Elements<CheckBox>().First(b=>Equals(b.Content,name));box.BringIntoView();
        box.IsChecked=value;box.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent,box));Pump(85);
    }
    static void Source(int index)
    {
        Expand("播放器");var select=Elements<ComboBox>().First(b=>AutomationProperties.GetName(b)=="优先识别的音乐播放器");
        select.SelectedIndex=index;Pump(100);
    }
    static void Top()
    {
        var scrolls=Elements<ScrollViewer>();
        foreach(var viewer in scrolls)if(viewer.ViewportWidth>300)viewer.ScrollToTop();
        Pump(100);
    }
    static void TestManualAndDemo()
    {
        SetCheck("桌宠跟唱字幕",false);
        SetCheck("联网查找时间轴歌词",true);Check(app.Data.OnlineLyrics,"Online setting toggled while recognition disabled");
        SetCheck("读取播放器的画面歌词",true);Check(app.Data.ReadLyricWindow,"Explicit local screenshot recognition setting is switchable");SetCheck("读取播放器的画面歌词",false);
        SetCheck("联网查找时间轴歌词",false);SetCheck("桌宠跟唱字幕",true);
        Source(3);Until(()=>!app.Music.HasTrack,"Manual checks started without a player track");
        Expand("歌词来源");Click("粘贴歌词");
        var pasted=Elements<TextBox>().Single(x=>AutomationProperties.GetName(x)=="带时间标签的 LRC 歌词");
        pasted.Text="[ti:手写验收]\n[ar:窗边工作室]\n[00:00]第一张卡片\n[00:03]第二张卡片\n[00:08]第三张卡片";
        Click("使用这份歌词");
        Until(()=>app.Music.Title=="手写验收"&&app.Music.ManualActive&&!app.Music.Playing,"Pasted LRC opens a paused standalone song with parsed metadata");
        Expand("字幕校时");var time=Elements<TextBox>().Single(x=>AutomationProperties.GetName(x).StartsWith("播放器当前进度"));
        time.Text="00:04";Click("从此刻计时");
        Until(()=>app.Music.CurrentLine=="第二张卡片"&&app.Music.Playing,"Manual time input starts at the actual requested line");
        Click("暂停手动计时");var held=app.Music.TimeLabel;Pump(1250);
        Check(!app.Music.Playing&&held==app.Music.TimeLabel&&app.Music.CurrentLine=="第二张卡片","Pause freezes manual progress and keeps the current line");
        Click("继续手动计时");Click("+0.5 秒");Check(app.Music.OffsetMs==500,"Manual calibration persists +500 ms");Click("归零");
        Click("结束手动计时");Until(()=>!app.Music.HasTrack,"Ending standalone timing clears the pasted song");
        string path=Path.Combine(stateDir,"手写本地歌词.lrc");
        File.WriteAllText(path,"[ti:纸页样本]\n[ar:验收工坊]\n[00:00]文件中的开头\n[00:03]校时落在这里",Encoding.UTF8);
        ImportWithPicker(path);
        Until(()=>app.Music.Title=="纸页样本"&&app.Music.CurrentLine=="文件中的开头"&&app.Music.ManualActive,"Native file picker imported an LRC from the normal UI and entered standalone timing");
        time=Elements<TextBox>().Single(x=>AutomationProperties.GetName(x).StartsWith("播放器当前进度"));
        time.Text="00:05";Click("从此刻计时");Until(()=>app.Music.CurrentLine=="校时落在这里","Imported-file timeline honors manual seek");
        Click("暂停手动计时");Click("结束手动计时");
        var editor=Find("收起输入框"); if(editor!=null)Click(editor); Expand("播放器",false);Expand("歌词来源",false);Expand("字幕校时",false);Click("试听 18 秒演示");
        Until(()=>app.Music.IsDemo&&app.Music.CurrentLine=="窗边响起第一声"&&app.Pet.KaraokeVisible,"Bundled original WAV and its first stamped lyric render in the real companion");
        Top();Capture(app.Desk,"01-music-demo");SavePetAndPanel("02-desktop-demo");
        Click("退出内置演示");Until(()=>!app.Music.IsDemo&&app.Music.CurrentLine=="","Stopping built-in demo removes its stale caption");
    }

    [DllImport("user32.dll")]static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string cls,string title);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    static void ImportWithPicker(string path)
    {
        uint pid=(uint)Process.GetCurrentProcess().Id;
        var automation=Task.Run(delegate
        {
            IntPtr hwnd=IntPtr.Zero;
            try
            {
                var clock=Stopwatch.StartNew();
                while(clock.ElapsedMilliseconds<9500)
                {
                    IntPtr found=FindWindow("#32770","选择带时间标签的歌词");uint owner;
                    if(found!=IntPtr.Zero&&GetWindowThreadProcessId(found,out owner)!=0&&owner==pid){hwnd=found;break;}
                    Thread.Sleep(70);
                }
                if(hwnd==IntPtr.Zero)throw new Exception("Owned file picker was not available");
                var dialog=AutomationElement.FromHandle(hwnd);
                AutomationElement input=null;
                var ready=Stopwatch.StartNew();
                while(input==null&&ready.ElapsedMilliseconds<6000)
                {
                    var controls=dialog.FindAll(TreeScope.Descendants,new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Edit)).Cast<AutomationElement>().ToList();
                    input=controls.FirstOrDefault(c=>c.Current.Name.Contains("文件名")||c.Current.Name.IndexOf("file name",StringComparison.OrdinalIgnoreCase)>=0) ??
                        controls.FirstOrDefault(c=>c.Current.AutomationId=="1001");
                    if(input==null)Thread.Sleep(100);
                }
                if(input==null)throw new Exception("Native picker filename field unavailable");
                ((ValuePattern)input.GetCurrentPattern(ValuePattern.Pattern)).SetValue(path);
                var open=dialog.FindFirst(TreeScope.Descendants,new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty,ControlType.Button),new PropertyCondition(AutomationElement.AutomationIdProperty,"1")));
                if(open==null)throw new Exception("Native picker Open control unavailable");
                ((InvokePattern)open.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            }
            catch
            {if(hwnd!=IntPtr.Zero)PostMessage(hwnd,0x10,IntPtr.Zero,IntPtr.Zero);throw;}
        });
        Expand("歌词来源");Click("导入 LRC 文件");automation.GetAwaiter().GetResult();
    }

    static void NativeTrack(SystemMediaTransportControls smtc,string title)
    {
        smtc.DisplayUpdater.Type=MediaPlaybackType.Music;
        smtc.DisplayUpdater.MusicProperties.Title=title;smtc.DisplayUpdater.MusicProperties.Artist=FixtureArtist;smtc.DisplayUpdater.Update();
    }
    static void NativeTime(SystemMediaTransportControls smtc,double seconds)
    {
        smtc.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties{StartTime=TimeSpan.Zero,MinSeekTime=TimeSpan.Zero,
            MaxSeekTime=TimeSpan.FromMinutes(2),EndTime=TimeSpan.FromMinutes(2),Position=TimeSpan.FromSeconds(seconds)});
    }
    static string SilentWav()
    {
        string path=Path.Combine(stateDir,"original-silent-test.wav");int length=120*8000*2;
        using(var w=new BinaryWriter(File.Create(path)))
        {
            w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write(36+length);w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(8000);w.Write(16000);w.Write((short)2);w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data"));w.Write(length);w.Write(new byte[length]);
        }
        return path;
    }
    static void TestNativeTimeline()
    {
        Source(0);
        using(var player=new Windows.Media.Playback.MediaPlayer())
        {
            player.Volume=0;player.CommandManager.IsEnabled=false;
            player.Source=Windows.Media.Core.MediaSource.CreateFromUri(new Uri(SilentWav()));player.Play();
            var smtc=player.SystemMediaTransportControls;smtc.IsEnabled=true;smtc.IsPauseEnabled=true;smtc.IsPlayEnabled=true;
            try
            {
                NativeTrack(smtc,FirstTrack);smtc.PlaybackStatus=MediaPlaybackStatus.Playing;NativeTime(smtc,12);
                Until(()=>app.Music.Title==FirstTrack&&app.Music.Artist==FixtureArtist&&app.Music.HasTimeline&&app.Music.CurrentLine=="窗沿落下一点光"&&app.Music.Connection.Contains("Windows"),"Real Windows session + selected Chinese-named LRC resolve title, artist and exact lyric");
                Until(()=>app.Pet.KaraokeMoving&&app.Pet.KaraokeVisible&&app.Pet.KaraokeLyric==app.Music.CurrentLine,"Playing native media animates companion and matches the visible lyric");
                var editor=Find("收起输入框");if(editor!=null)Click(editor);Expand("播放器",false);Expand("歌词来源",false);Expand("字幕校时",false);Top();Capture(app.Desk,"03-music-native");SavePetAndPanel("04-desktop-native");
                player.Pause();player.PlaybackSession.Position=TimeSpan.FromSeconds(26);smtc.PlaybackStatus=MediaPlaybackStatus.Paused;NativeTime(smtc,26);
                Until(()=>!app.Music.Playing&&app.Music.CurrentLine=="灯影在纸上慢下来"&&!app.Pet.KaraokeMoving,"Native pause updates line and stops the singing motion");
                string pinned=app.Music.TimeLabel;Pump(1150);
                Check(app.Music.TimeLabel==pinned&&app.Music.CurrentLine=="灯影在纸上慢下来","Paused Windows time and line stay fixed across UI ticks");
                player.PlaybackSession.Position=TimeSpan.FromSeconds(54);NativeTime(smtc,54);
                Until(()=>app.Music.CurrentLine=="翻向下一页"&&!app.Music.Playing&&Math.Abs(player.PlaybackSession.Position.TotalSeconds-54)<1,
                    "A real MediaPlayer seek while paused moves to the later LRC segment");
                player.Play();smtc.PlaybackStatus=MediaPlaybackStatus.Playing;NativeTime(smtc,54);
                Until(()=>app.Music.Playing&&app.Pet.KaraokeMoving,"Native resume restarts motion");
                Nav("settings","伙伴设置");SetCheck("安静陪伴，暂停主动活动和字幕",true);
                Until(()=>!app.Pet.KaraokeVisible&&!app.Pet.KaraokeMoving,"Quiet mode hides lyrics and stops motion");
                SetCheck("安静陪伴，暂停主动活动和字幕",false);Until(()=>app.Pet.KaraokeVisible,"Leaving quiet mode restores live caption");
                Nav("music","听歌陪伴");app.Desk.Close();Pump(300);
                Check(!app.Desk.IsVisible&&app.Music.HasTrack&&app.Music.Playing,"Closing the workspace leaves native synchronization running"); Until(()=>app.Pet.KaraokeVisible,"Resident lyric returns after the first-close instruction bubble",12000);
                app.Open("music");Pump(120);
                NativeTrack(smtc,SecondTrack);player.PlaybackSession.Position=TimeSpan.FromSeconds(1);NativeTime(smtc,1);
                Until(()=>app.Music.Title==SecondTrack&&String.IsNullOrEmpty(app.Music.CurrentLine)&&!app.Pet.KaraokeVisible,"Track change clears previous line and overlay for an unbound song");
            }
            finally{smtc.PlaybackStatus=MediaPlaybackStatus.Closed;smtc.IsEnabled=false;player.Pause();}
        }
    }

    static void TestWindowLyric()
    {
        if(nativeFixture==null||!File.Exists(nativeFixture))throw new FileNotFoundException("QQ-shaped UIA fixture required",nativeFixture);
        Source(2);
        string sentinel=Path.Combine(stateDir,"end-window-fixture.signal");
        using(var helper=Process.Start(new ProcessStartInfo{FileName=nativeFixture,Arguments="--hold \""+sentinel+"\"",UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden}))
        {
            try
            {
                Until(()=>app.Music.Title=="窗边验收歌"&&app.Music.Artist=="验收伙伴"&&app.Music.Connection.Contains("窗口")&&app.Music.CurrentLine=="雨滴敲着窗沿",
                    "Native accessibility fixture supplies QQ-shaped player metadata and dedicated desktop lyric without an LRC");
                Until(()=>app.Music.PlayerLabel=="QQ 音乐"&&app.Pet.KaraokeLyric=="雨滴敲着窗沿"&&app.Pet.KaraokeVisible,
                    "Live desktop lyric reaches the companion independently of timed local lyrics");
                Top();Capture(app.Desk,"05-music-window");
            }
            finally{File.WriteAllText(sentinel,"finished");var watch=Stopwatch.StartNew();while(!helper.HasExited&&watch.ElapsedMilliseconds<4000)Pump(75);}
        }
        Source(0);
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





