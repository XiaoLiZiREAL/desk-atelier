using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using Forms=System.Windows.Forms;

namespace DeskMate
{
    internal sealed class TurntableDial : FrameworkElement
    {
        static readonly DependencyProperty AngleProperty=DependencyProperty.Register("Angle",typeof(double),typeof(TurntableDial),
            new FrameworkPropertyMetadata(0d,FrameworkPropertyMetadataOptions.AffectsRender));
        bool playing, spinning;
        internal TurntableDial()
        {
            Width=75;Height=75;IsHitTestVisible=false;
            Loaded+=delegate { UpdateMotion(); };
            IsVisibleChanged+=delegate { UpdateMotion(); };
            Unloaded+=delegate { UpdateMotion(); };
        }
        internal bool Playing { set { if(playing!=value){playing=value;UpdateMotion();} } }
        void UpdateMotion()
        {
            bool active=playing && IsVisible && IsLoaded;
            if(active==spinning)return;
            spinning=active;
            if(active)BeginAnimation(AngleProperty,new DoubleAnimation((double)GetValue(AngleProperty),(double)GetValue(AngleProperty)+360,TimeSpan.FromSeconds(7)){RepeatBehavior=RepeatBehavior.Forever});
            else {double hold=(double)GetValue(AngleProperty)%360; BeginAnimation(AngleProperty,null);SetValue(AngleProperty,hold);}
        }
        protected override void OnRender(DrawingContext dc)
        {
            var center=new Point(37.5,37.5);
            dc.DrawEllipse(Ui.Color("#283748"),new Pen(Ui.Color("#637387"),1),center,36,36);
            for(int i=0;i<6;i++){double r=31-i*2.5;dc.DrawEllipse(null,new Pen(Ui.Color(i%2==0?"#677A8E":"#45586B"),.6),center,r,r);}
            dc.PushTransform(new RotateTransform((double)GetValue(AngleProperty),37.5,37.5));
            dc.DrawEllipse(Ui.Color("#8CB8DF"),new Pen(Ui.Color("#A9CAE8"),1),center,12,12);
            dc.DrawLine(new Pen(Ui.Color("#DFF0FF"),1),new Point(32,31.5),new Point(42,31.5));
            dc.DrawLine(new Pen(Ui.Color("#DFF0FF"),1),new Point(34,34),new Point(40,34));
            dc.DrawEllipse(Ui.Color(Ui.Ink),null,center,2.5,2.5);
            dc.Pop();
        }
    }

    public partial class StudioWindow
    {
        TextBlock songTitle,songMeta,songTime,songLine,songNext,songMode,songSource,songState,songFolder,songOffset,songAlert;
        TurntableDial recordDial;
        Button demoMusic,pauseManual,leaveManual,clearFolder,applyManual,pasteButton,loadLyrics,togglePaste,downOffset,upOffset,zeroOffset;
        TextBox manualClock,pastedLyrics;
        StackPanel pasteArea,musicSurface;
        CheckBox musicEnabled;
        ComboBox preferredMusicPlayer;
        string manualDraft="00:00",lrcDraft="";
        bool pasteOpen, playerOptionsOpen, lyricOptionsOpen, timingOptionsOpen;
        static readonly string[] MusicPlayerIds={"auto","netease","qq","kugou"};
        static readonly string[] MusicPlayerNames={"自动识别","网易云音乐","QQ 音乐","酷狗音乐"};

        static T MusicMargin<T>(T item,double top=0,double right=0,double bottom=0,double left=0) where T:FrameworkElement
        {item.Margin=new Thickness(left,top,right,bottom);return item;}

        static TextBlock MusicSmall(string text)
        {return Ui.Text(text,13,Ui.Muted);}

        static void ChangeMusicText(TextBlock block,string value)
        {value=value??"";if(block.Text!=value){block.Text=value;block.ToolTip=value.Length>35?value:null;}}

        static CheckBox MusicCheck(string label,bool value,Action<bool> changed)
        {
            var box=new CheckBox{Content=label,IsChecked=value,FontSize=14,Foreground=Ui.Color(Ui.Ink),
                VerticalContentAlignment=VerticalAlignment.Center,MinHeight=29,Margin=new Thickness(0,0,14,0),Cursor=Cursors.Hand};
            box.Click+=delegate{changed(box.IsChecked==true);};return box;
        }
        void MusicFeedback(string text)
        {
            if(songAlert==null)return;
            songAlert.Text=text??"";songAlert.Visibility=String.IsNullOrEmpty(text)?Visibility.Collapsed:Visibility.Visible;
            if(!String.IsNullOrWhiteSpace(text))ShowStatus(text);
        }
        bool MusicAction(Action action)
        {
            try{MusicFeedback("");action();UpdateMusicPage();return true;}
            catch(Exception ex){MusicFeedback(ex.GetBaseException().Message);return false;}
        }
        void SaveMusicPreferences()
        {app.Save();app.Music.Configure();UpdateMusicPage();}

        public UIElement MusicPage()
        {
            var stack=new StackPanel();musicSurface=stack;
            recordDial=new TurntableDial(); var dialView=new Viewbox{Width=48,Height=48,Child=recordDial,Margin=new Thickness(0,0,10,0),VerticalAlignment=VerticalAlignment.Center};
            songTitle=Ui.Heading("",18);
            songTitle.MaxHeight=44;songTitle.TextTrimming=TextTrimming.CharacterEllipsis;
            songMeta=MusicMargin(Ui.Text("",12,Ui.Muted),2);songMeta.TextWrapping=TextWrapping.NoWrap;songMeta.TextTrimming=TextTrimming.CharacterEllipsis;
            songMode=MusicMargin(Ui.Text("",12,Ui.Accent),0,0,3);
            var track=Ui.Stack(songMode,songTitle,songMeta);
            var recordHeader=new Grid();
            recordHeader.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            recordHeader.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
            Grid.SetColumn(track,1);recordHeader.Children.Add(dialView);recordHeader.Children.Add(track);
            songLine=Ui.Text("",16,Ui.Ink);songLine.FontWeight=FontWeights.SemiBold;songLine.MaxHeight=45;songLine.TextTrimming=TextTrimming.CharacterEllipsis;
            songNext=MusicMargin(Ui.Text("",13,Ui.Muted),3);songNext.MaxHeight=34;songNext.TextTrimming=TextTrimming.CharacterEllipsis;
            songTime=Ui.Text("",12,Ui.Muted);Typography.SetNumeralAlignment(songTime,FontNumeralAlignment.Tabular);
            songTime.TextAlignment=TextAlignment.Right;
            var footer=new DockPanel();DockPanel.SetDock(songTime,Dock.Right);footer.Children.Add(songTime);
            songState=Ui.Text("",12,Ui.Muted);songState.MaxHeight=36;songState.TextTrimming=TextTrimming.CharacterEllipsis;footer.Children.Add(songState);
            var stage=new Border{Background=Ui.Color("#F0F5FA"),CornerRadius=new CornerRadius(8),
                Padding=new Thickness(13,11,13,10),Margin=new Thickness(0,0,0,5),Child=Ui.Stack(recordHeader,
                    new Border{Height=1,Background=Ui.Color("#D5E1EC"),Margin=new Thickness(0,9,0,8)},songLine,songNext,MusicMargin(footer,7))};
            stack.Children.Add(stage);

            var scan=Ui.Button("重新识别",delegate{MusicAction(app.Music.Reload);});
            demoMusic=Ui.Button("试听内置演示",delegate{MusicAction(delegate{if(app.Music.IsDemo)app.Music.ExitDemo();else app.Music.StartDemo();});});
            musicEnabled=MusicCheck("桌宠跟唱字幕",app.Data.SingAlongEnabled,delegate(bool v){app.Data.SingAlongEnabled=v;SaveMusicPreferences();});
            var selector=new ComboBox{Width=142,MinHeight=31,FontSize=13,VerticalContentAlignment=VerticalAlignment.Center,Margin=new Thickness(0,3,12,3),
                Padding=new Thickness(6,3,5,3),ItemsSource=MusicPlayerNames};
            preferredMusicPlayer=selector;
            AutomationProperties.SetName(selector,"优先识别的音乐播放器");
            int selected=Array.IndexOf(MusicPlayerIds,app.Data.PreferredPlayer);
            selector.SelectedIndex=selected<0?0:selected;
            selector.SelectionChanged+=delegate{int i=selector.SelectedIndex;if(i>=0 && i<MusicPlayerIds.Length){app.Data.PreferredPlayer=MusicPlayerIds[i];SaveMusicPreferences();}};
            songSource=MusicMargin(MusicSmall(""),4);
            stack.Children.Add(Ui.Row(musicEnabled,demoMusic));
            stack.Children.Add(CompactDetails("播放器",Ui.Stack(Ui.Row(selector,scan),songSource),playerOptionsOpen,v=>playerOptionsOpen=v));
            var chooseFolder=Ui.Button("选择歌词目录",ChooseMusicFolder);
            clearFolder=Ui.Button("清除目录",delegate{app.Data.LyricFolder="";SaveMusicPreferences();});
            songFolder=MusicSmall("");
            var online=MusicCheck("联网查找时间轴歌词",app.Data.OnlineLyrics,delegate(bool v){app.Data.OnlineLyrics=v;SaveMusicPreferences();});
            var readWindow=MusicCheck("读取播放器的画面歌词",app.Data.ReadLyricWindow,delegate(bool v){app.Data.ReadLyricWindow=v;SaveMusicPreferences();});
            loadLyrics=Ui.Button("导入 LRC 文件",ImportMusicFile,true);
            togglePaste=Ui.Button("粘贴歌词",delegate{SetPasteOpen(!pasteOpen,true);});
            pastedLyrics=Ui.Input(lrcDraft);pastedLyrics.AcceptsReturn=true;pastedLyrics.TextWrapping=TextWrapping.Wrap;
            pastedLyrics.VerticalContentAlignment=VerticalAlignment.Top;pastedLyrics.Height=104;pastedLyrics.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;
            pastedLyrics.FontFamily=new FontFamily("Consolas, Microsoft YaHei UI");pastedLyrics.MaxLength=512000;
            AutomationProperties.SetName(pastedLyrics,"带时间标签的 LRC 歌词");
            pastedLyrics.TextChanged+=delegate{lrcDraft=pastedLyrics.Text;};
            pasteButton=Ui.Button("使用这份歌词",delegate
            {
                if(String.IsNullOrWhiteSpace(pastedLyrics.Text)){MusicFeedback("请先粘贴带时间标签的歌词。");pastedLyrics.Focus();return;}
                MusicAction(delegate{app.Music.BindText(pastedLyrics.Text);});
            },true);
            pasteArea=Ui.Stack(MusicSmall("每行保留时间标签，例如 [00:12.50]窗边的月光。"),pastedLyrics,Ui.Row(pasteButton));
            stack.Children.Add(CompactDetails("歌词来源",Ui.Stack(Ui.Row(loadLyrics,togglePaste),pasteArea,
                Ui.Row(chooseFolder,clearFolder),songFolder,MusicMargin(readWindow,6),
                MusicSmall("用本机文字识别读取播放器的桌面歌词浮窗。"),MusicMargin(online,5),
                MusicSmall("开启后向 LRCLIB 查询当前曲名和歌手。")),lyricOptionsOpen,v=>lyricOptionsOpen=v));
            manualClock=Ui.Input(manualDraft);
            manualClock.Width=86;manualClock.MaxLength=12;manualClock.ToolTip="播放器当前进度，例如 01:23";AutomationProperties.SetName(manualClock,"播放器当前进度，分和秒");
            manualClock.TextChanged+=delegate{manualDraft=manualClock.Text;};
            manualClock.KeyDown+=delegate(object s,KeyEventArgs e){if(e.Key==Key.Enter && applyManual.IsEnabled){ApplyMusicTime();e.Handled=true;}};
            applyManual=Ui.Button("从此刻计时",ApplyMusicTime);
            pauseManual=Ui.Button("暂停手动计时",delegate{MusicAction(app.Music.ToggleManual);});
            leaveManual=Ui.Button("结束手动计时",delegate{MusicAction(app.Music.LeaveManual);});
            songOffset=MusicSmall("");
            downOffset=Ui.Button("−0.5 秒",delegate{MusicAction(delegate{app.Music.SetOffset(app.Music.OffsetMs-500);});});
            upOffset=Ui.Button("+0.5 秒",delegate{MusicAction(delegate{app.Music.SetOffset(app.Music.OffsetMs+500);});});
            zeroOffset=Ui.Button("归零",delegate{MusicAction(delegate{app.Music.SetOffset(0);});});
            stack.Children.Add(CompactDetails("字幕校时",Ui.Stack(songOffset,Ui.Row(downOffset,upOffset,zeroOffset),
                MusicSmall("手动对时可输入播放器当前的分和秒。"),Ui.Row(manualClock,applyManual,pauseManual,leaveManual)),timingOptionsOpen,v=>timingOptionsOpen=v));
            songAlert=MusicMargin(Ui.Text("",13,"#914E36"),2,0,6);songAlert.Visibility=Visibility.Collapsed;
            stack.Children.Add(songAlert);SetPasteOpen(pasteOpen,false);PaintMusicPage();return stack;
        }

        void SetPasteOpen(bool open,bool focus)
        {
            pasteOpen=open;pasteArea.Visibility=open?Visibility.Visible:Visibility.Collapsed;
            togglePaste.Content=open?"收起输入框":"粘贴歌词";
            if(open && focus)pastedLyrics.Focus();
        }

        void ChooseMusicFolder()
        {
            using(var dialog=new Forms.FolderBrowserDialog{Description="选择用于自动查找 .lrc 文件的目录",ShowNewFolderButton=false})
            {
                if(Directory.Exists(app.Data.LyricFolder))dialog.SelectedPath=app.Data.LyricFolder;
                var owner=new System.Windows.Interop.WindowInteropHelper(this);var native=new MusicDialogOwner(owner.Handle);
                if(dialog.ShowDialog(native)!=Forms.DialogResult.OK)return;
                app.Data.LyricFolder=dialog.SelectedPath;SaveMusicPreferences();
            }
        }
        sealed class MusicDialogOwner:Forms.IWin32Window
        {
            internal MusicDialogOwner(IntPtr handle){Handle=handle;} public IntPtr Handle{get;private set;}
        }
        void ImportMusicFile()
        {
            var dialog=new OpenFileDialog {Title="选择带时间标签的歌词",Filter="LRC 歌词文件|*.lrc|文本歌词|*.txt",CheckFileExists=true};
            if(dialog.ShowDialog(this)==true)MusicAction(delegate{app.Music.BindFile(dialog.FileName);});
        }
        internal static bool TryReadMusicTime(string text,out double seconds)
        {
            seconds=0;
            string[] parts=(text??"").Trim().Replace('：',':').Split(':');
            int minutes;double part;
            if(parts.Length!=2 || !int.TryParse(parts[0],NumberStyles.None,CultureInfo.InvariantCulture,out minutes) ||
                !double.TryParse(parts[1],NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out part) ||
                double.IsNaN(part) || double.IsInfinity(part) || part<0 || part>=60)return false;
            seconds=minutes*60d+part;return seconds<=TimeSpan.FromDays(7).TotalSeconds;
        }
        void ApplyMusicTime()
        {
            double seconds;
            if(!TryReadMusicTime(manualClock.Text,out seconds)){MusicFeedback("请输入分:秒，例如 01:23。秒数应小于 60。");manualClock.Focus();return;}
            MusicAction(delegate{app.Music.SetManualTime(seconds);});
        }

        public void UpdateMusicPage()
        {
            if(musicSurface!=null && musicSurface.IsVisible)PaintMusicPage();
        }
        void PaintMusicPage()
        {
            if(songTitle==null || app.Music==null)return;
            var m=app.Music;
            musicEnabled.IsChecked=app.Data.SingAlongEnabled;
            preferredMusicPlayer.IsEnabled=!m.IsDemo;
            ChangeMusicText(songMode,m.IsDemo?"内置演示":m.ManualActive?"手动歌词计时":m.HasTrack?m.PlayerLabel+(m.Playing?" · 播放中":" · 已识别"):"桌宠跟唱");
            ChangeMusicText(songTitle,String.IsNullOrWhiteSpace(m.Title)?"等待音乐开始":m.Title);
            ChangeMusicText(songMeta,m.Artist);
            songMeta.Visibility=String.IsNullOrWhiteSpace(m.Artist)?Visibility.Collapsed:Visibility.Visible;
            string line=m.CurrentLine;
            if(String.IsNullOrWhiteSpace(line))line=!app.Data.SingAlongEnabled?"桌宠跟唱字幕已关闭":m.HasTrack?"等待下一句歌词":"播放音乐后，歌词会出现在这里。";
            ChangeMusicText(songLine,line);ChangeMusicText(songNext,m.NextLine);
            songNext.Visibility=String.IsNullOrWhiteSpace(m.NextLine)?Visibility.Collapsed:Visibility.Visible;
            ChangeMusicText(songState,m.IsDemo?"":m.Status);ChangeMusicText(songTime,m.TimeLabel);
            ChangeMusicText(songSource,m.Connection);songSource.Visibility=String.IsNullOrEmpty(m.Connection)?Visibility.Collapsed:Visibility.Visible;
            recordDial.Playing=m.Playing;
            ChangeMusicText(songFolder,String.IsNullOrWhiteSpace(app.Data.LyricFolder)?"尚未选择自动查找目录":app.Data.LyricFolder);
            songFolder.ToolTip=app.Data.LyricFolder;clearFolder.IsEnabled=!String.IsNullOrEmpty(app.Data.LyricFolder);
            demoMusic.Content=m.IsDemo?"退出内置演示":"试听 18 秒演示";
            string offset=(m.OffsetMs/1000.0).ToString("+0.0;-0.0;0.0",CultureInfo.InvariantCulture);
            ChangeMusicText(songOffset,"歌词偏移  "+offset+" 秒  ·  正值提前，负值延后");
            pauseManual.Content=m.Playing?"暂停手动计时":"继续手动计时";
            leaveManual.Visibility=pauseManual.Visibility=m.ManualActive&&!m.IsDemo?Visibility.Visible:Visibility.Collapsed;
            bool calibrate=!m.IsDemo && (m.ManualActive || (m.HasTrack&&!m.HasTimeline));
            applyManual.IsEnabled=manualClock.IsEnabled=calibrate;
            bool canOffset=!m.IsDemo&&m.HasTrack;
            downOffset.IsEnabled=canOffset&&m.OffsetMs>-10000;upOffset.IsEnabled=canOffset&&m.OffsetMs<10000;
            zeroOffset.IsEnabled=canOffset&&m.OffsetMs!=0;
            loadLyrics.IsEnabled=togglePaste.IsEnabled=pasteButton.IsEnabled=!m.IsDemo;
        }
    }
}


