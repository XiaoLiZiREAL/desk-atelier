using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace DeskMate
{
    public partial class StudioWindow : Window
    {
        readonly Studio app;
        readonly TextBlock title=Ui.Heading("",17);
        readonly TextBlock toastText=Ui.Text("",12,"#FFF9E8");
        readonly Border toast=new Border();
        readonly DispatcherTimer toastTimer=new DispatcherTimer();
        readonly ScrollViewer scroll=new ScrollViewer { VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,CanContentScroll=false };
        static readonly Dictionary<string,string> names=new Dictionary<string,string> {
            {"focus","番茄钟"},{"notes","便签"},{"music","听音乐"},{"bag","随身背包"},
            {"room","小屋"},{"gift","礼物"},{"journal","生活手记"},{"settings","伙伴设置"}
        };
        readonly List<string> history=new List<string>();
        string page="";
        int displayedFocusCount,displayedJournalCount;
        bool positioning;
        public string CurrentTool { get { return page; } }
        public static bool SupportsTool(string key) { return key!=null && names.ContainsKey(key); }
        public StudioWindow(Studio studio)
        {
            app=studio;
            Title="窗边手记";ShowInTaskbar=false;ShowActivated=true;
            WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;
            AllowsTransparency=true;Background=Brushes.Transparent;WindowStartupLocation=WindowStartupLocation.Manual;
            Width=364;MinWidth=0;MinHeight=0;MaxHeight=428;SizeToContent=SizeToContent.Height;
            FontFamily=new FontFamily("Microsoft YaHei UI, Microsoft YaHei, Segoe UI");Foreground=Ui.Color(Ui.Ink);
            UseLayoutRounding=true;SnapsToDevicePixels=true;TextOptions.SetTextFormattingMode(this,TextFormattingMode.Display);
            Icon=Ui.Bitmap(app.Asset("logo.png"));
            var container=new Grid();
            container.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
            container.RowDefinitions.Add(new RowDefinition());
            var header=new Grid { Margin=new Thickness(9,6,8,6) };
            header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            header.ColumnDefinitions.Add(new ColumnDefinition());header.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            var back=HeaderAction("返回","\uE72B",GoBack);
            var close=HeaderAction("关闭","\uE711",delegate{Close();});Grid.SetColumn(close,2);
            title.Margin=new Thickness(7,0,7,0);Grid.SetColumn(title,1);
            header.Children.Add(back);header.Children.Add(title);header.Children.Add(close);
            var head=new Border{Background=Ui.Color("#F1F2E9"),CornerRadius=new CornerRadius(13,13,0,0),
                BorderBrush=Ui.Color("#DEE1D6"),BorderThickness=new Thickness(0,0,0,1),Child=header};
            Grid.SetRow(scroll,1);container.Children.Add(head);container.Children.Add(scroll);
            toast.CornerRadius=new CornerRadius(7);toast.Background=Ui.Color("#304C3E");
            toast.Padding=new Thickness(10,6,10,6);toast.Margin=new Thickness(10,5,10,9);
            toast.VerticalAlignment=VerticalAlignment.Bottom;toast.HorizontalAlignment=HorizontalAlignment.Center;
            toast.IsHitTestVisible=false;toast.Visibility=Visibility.Collapsed;toast.Child=toastText;
            Grid.SetRow(toast,1);container.Children.Add(toast);
            Content=new Border{Background=Ui.Color("#FBFAF5"),CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1),
                BorderBrush=Ui.Color("#AEB9AC"),Child=container};
            toastTimer.Interval=TimeSpan.FromSeconds(3.5);
            toastTimer.Tick+=delegate{toastTimer.Stop();toast.Visibility=Visibility.Collapsed;};
            Closing+=delegate(object s,System.ComponentModel.CancelEventArgs e) {
                if(app.Exiting)return;e.Cancel=true;Hide();history.Clear();ClearStatus();
            };
            PreviewKeyDown+=delegate(object s,KeyEventArgs e){
                if(e.Key==Key.Escape){Close();e.Handled=true;}
                else if(e.Key==Key.Left && Keyboard.Modifiers==ModifierKeys.Alt){GoBack();e.Handled=true;}
            };
            SourceInitialized+=delegate{AnchorCard();};
            IsVisibleChanged+=delegate{if(IsVisible){AnchorCard();Dispatcher.BeginInvoke((Action)AnchorCard,DispatcherPriority.Loaded);}};
            SizeChanged+=delegate{if(IsVisible)AnchorCard();};
            app.Pet.LocationChanged+=delegate{if(IsVisible)AnchorCard();};
            app.Pet.SizeChanged+=delegate{if(IsVisible)AnchorCard();};
        }

        static Button HeaderAction(string label,string glyph,Action click)
        {
            var b=Ui.Button(label,click);b.ToolTip=label;b.Content=Ui.Icon(glyph,13,Ui.Ink);
            b.Width=29;b.Height=28;b.MinHeight=0;b.Margin=new Thickness(0);b.Padding=new Thickness(0);
            b.Background=Brushes.Transparent;b.BorderThickness=new Thickness(0);AutomationProperties.SetName(b,label);return b;
        }
        public void Go(string key)
        {
            if(!names.ContainsKey(key??""))
            {
                if(IsVisible)Hide();history.Clear();app.Pet.ShowQuickMenu();return;
            }
            if(IsVisible && page!=key && names.ContainsKey(page)){history.Add(page);if(history.Count>12)history.RemoveAt(0);}
            else if(!IsVisible)history.Clear();
            bool changed=page!=key;
            if(changed||!IsVisible)ClearStatus();
            page=key; DrawPage();
            if(changed || !IsVisible)scroll.ScrollToTop();
            Topmost=app.Data.AlwaysOnTop;AnchorCard();
        }
        void GoBack()
        {
            if(history.Count==0){Close();app.Pet.ShowQuickMenu();return;}
            page=history[history.Count-1];history.RemoveAt(history.Count-1);ClearStatus();DrawPage();scroll.ScrollToTop();AnchorCard();
        }
        public void RefreshPage(){if(names.ContainsKey(page))DrawPage();}
        void DrawPage()
        {
            displayedFocusCount=app.Data.FocusCount;displayedJournalCount=app.Data.Journal.Count;
            title.Text=names[page];Title="窗边手记 · "+title.Text;
            UIElement contents;
            switch(page){
                case "focus": contents=FocusPage();break;case "notes":contents=NotesPage();break;case "bag":contents=BagPage();break;
                case "room":contents=RoomPage();break;case "gift":contents=GiftPage();break;
                case "music":contents=MusicPage();break;case "journal":contents=JournalPage();break;default:contents=SettingsPage();break;
            }
            scroll.Content=new Border{Padding=new Thickness(12,11,12,8),Child=contents};
        }
        public void Tick()
        {
            if((page=="room" && displayedFocusCount!=app.Data.FocusCount) ||
                (page=="journal" && displayedJournalCount!=app.Data.Journal.Count))RefreshPage();
            UpdateToolClock();
        }
        public void ShowStatus(string text)
        {
            if(!IsVisible || String.IsNullOrWhiteSpace(text))return;
            toastText.Text=text;toast.Visibility=Visibility.Visible;toastTimer.Stop();toastTimer.Start();
        }
        void ClearStatus(){toastTimer.Stop();toast.Visibility=Visibility.Collapsed;}
        internal static Expander CompactDetails(string label,UIElement content,bool open=false,Action<bool> save=null)
        {
            var section=new Expander{Header=Ui.Heading(label,13),Content=new Border{Padding=new Thickness(5,7,2,3),Child=content},
                IsExpanded=open,Margin=new Thickness(0,4,0,4),Padding=new Thickness(1,3,1,3),Foreground=Ui.Color(Ui.Ink)};
            if(save!=null){
                section.Expanded+=delegate(object s,RoutedEventArgs e){if(e.OriginalSource==section)save(true);};
                section.Collapsed+=delegate(object s,RoutedEventArgs e){if(e.OriginalSource==section)save(false);};
            }
            return section;
        }
        static Border CompactBox(UIElement content)
        {
            return new Border{CornerRadius=new CornerRadius(9),Padding=new Thickness(11,9,11,9),
                BorderThickness=new Thickness(1),BorderBrush=Ui.Color("#DEE2D6"),Background=Ui.Color("#FFFDF7"),Child=content,
                Margin=new Thickness(0,0,0,8)};
        }

        UIElement JournalPage()
        {
            var entries=new StackPanel();
            if(app.Data.Journal.Count==0)entries.Children.Add(Ui.Text("相处的片段会慢慢记在这里。",13,Ui.Muted));
            string day="";
            foreach(var item in app.Data.Journal)
            {
                var time=new DateTime(item.Time,DateTimeKind.Utc).ToLocalTime();string current=time.ToString("M 月 d 日");
                if(current!=day){var heading=Ui.Heading(current,14);heading.Margin=new Thickness(0,6,0,6);entries.Children.Add(heading);day=current;}
                var line=new DockPanel();var stamp=Ui.Text(time.ToString("HH:mm"),11,Ui.Muted);stamp.Width=41;DockPanel.SetDock(stamp,Dock.Left);
                line.Children.Add(stamp);line.Children.Add(Ui.Text(item.Text,13,Ui.Ink));entries.Children.Add(CompactBox(line));
            }
            return entries;
        }
        bool collectionOpen,appearanceOpen;
        UIElement SettingsPage()
        {
            var settings=new StackPanel();
            var pair=new Grid();
            string[][] builtin={new[]{"rem","蕾姆","蕾姆"},new[]{"kurisu","牧濑红莉栖","红莉栖"},new[]{"steve","史蒂夫","史蒂夫"}};
            foreach(var item in builtin.Where(x=>System.IO.File.Exists(app.Asset(x[0]+"-still.png"))))
            {
                string id=item[0],name=item[1];bool active=app.Data.Character==id;
                int column=pair.ColumnDefinitions.Count;pair.ColumnDefinitions.Add(new ColumnDefinition());
                var button=Ui.Button(name,delegate{app.Data.Character=id;app.Save();app.Pet.Sync();app.Pet.Reveal();RefreshPage();});
                var pic=Ui.Picture(app.Asset(id+"-still.png"),39,45);
                var label=Ui.Heading(item[2],13);label.HorizontalAlignment=HorizontalAlignment.Center;
                var selection=Ui.Text(active?"当前伙伴":"切换",11,Ui.Muted);selection.HorizontalAlignment=HorizontalAlignment.Center;
                button.Content=Ui.Stack(pic,label,selection);button.Padding=new Thickness(3,5,3,5);button.Margin=new Thickness(column==0?0:3,0,3,4);
                button.Background=Ui.Color(active?"#E7EDDF":"#FFFDF8");button.ToolTip=id=="steve"?"史蒂夫 · Minecraft Java 1.8 经典造型":name;
                AutomationProperties.SetName(button,name);Grid.SetColumn(button,column);pair.Children.Add(button);
            }
            settings.Children.Add(pair);
            settings.Children.Add(CompactDetails("导入与切换其他角色",ImportedCompanionCard(),collectionOpen,v=>collectionOpen=v));
            var pace=new ComboBox{ItemsSource=new[]{"安静一些","自然","活泼"},MinHeight=30,MinWidth=100,
                VerticalContentAlignment=VerticalAlignment.Center,Margin=new Thickness(8,0,0,0)};
            pace.SelectedIndex=app.Data.ActivityPace=="calm"?0:app.Data.ActivityPace=="lively"?2:1;
            AutomationProperties.SetName(pace,"主动活动频率");
            pace.SelectionChanged+=delegate{if(pace.SelectedIndex<0)return;app.Data.ActivityPace=new[]{"calm","normal","lively"}[pace.SelectedIndex];app.Save();app.Pet.RefreshRoutinePace();};
            settings.Children.Add(CompactBox(Ui.Stack(Ui.Row(Ui.Heading("主动活动频率",13),pace),
                SettingCheck("安静陪伴，暂停主动活动和字幕",app.Data.Quiet,v=>app.Data.Quiet=v),
                SettingCheck("让伙伴保持在其他窗口上方",app.Data.AlwaysOnTop,v=>{app.Data.AlwaysOnTop=v;Topmost=v;}),
                SettingCheck("全屏游戏或放映时自动收起",app.Data.HideFullscreen,v=>app.Data.HideFullscreen=v))));
            var scale=new Slider{Minimum=.65,Maximum=1.8,Value=app.Data.PetScale,TickFrequency=.05,IsSnapToTickEnabled=true,Width=142,VerticalAlignment=VerticalAlignment.Center};
            var percent=Ui.Text(Math.Round(app.Data.PetScale*100)+"%",12,Ui.Muted);AutomationProperties.SetName(scale,"桌宠大小");
            scale.ValueChanged+=delegate{app.Data.PetScale=scale.Value;percent.Text=Math.Round(scale.Value*100)+"%";app.Pet.Sync();};
            scale.LostMouseCapture+=delegate{app.Save();};scale.LostKeyboardFocus+=delegate{app.Save();};
            var nickname=Ui.Input(app.Data.Nickname);nickname.MaxLength=18;nickname.Width=145;AutomationProperties.SetName(nickname,"伙伴对你的称呼");
            var saveName=Ui.Button("保存称呼",delegate{app.Data.Nickname=nickname.Text.Trim();app.Save();ShowStatus("称呼已保存。");});
            settings.Children.Add(CompactDetails("大小与称呼",Ui.Stack(Ui.Row(Ui.Text("桌宠大小  ",12,Ui.Muted),scale,percent),
                Ui.Text("伙伴怎么称呼你",12,Ui.Muted),Ui.Row(nickname,saveName)),appearanceOpen,v=>appearanceOpen=v));
            return settings;
        }
        CheckBox SettingCheck(string name,bool value,Action<bool> change)
        {
            var b=new CheckBox{Content=name,IsChecked=value,MinHeight=26,FontSize=13,Margin=new Thickness(0,4,0,0),Foreground=Ui.Color(Ui.Ink),VerticalContentAlignment=VerticalAlignment.Center};
            b.Click+=delegate{change(b.IsChecked==true);app.Save();app.Pet.Sync();};return b;
        }

        [StructLayout(LayoutKind.Sequential)]struct Bounds {public int Left,Top,Right,Bottom;}
        [StructLayout(LayoutKind.Sequential)]struct MonitorData {public int Size;public Bounds Display,Work;public uint Flags;}
        [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr hwnd,out Bounds bounds);
        [DllImport("user32.dll")]static extern IntPtr MonitorFromWindow(IntPtr hwnd,uint flags);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorData info);
        [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int cx,int cy,uint flags);
        internal static Rect CardBounds(Rect pet,Rect monitor,Size logical,double scale)
        {
            double gap=Math.Max(4,Math.Round(8*scale));
            double width=Math.Min(logical.Width*scale,Math.Max(1,monitor.Width-2*gap)),height=Math.Min(logical.Height*scale,Math.Max(1,monitor.Height-2*gap));
            double right=pet.Right+gap,left=pet.Left-width-gap;
            double x=right+width<=monitor.Right-gap?right:left;
            return new Rect(Math.Max(monitor.Left+gap,Math.Min(x,monitor.Right-gap-width)),
                Math.Max(monitor.Top+gap,Math.Min(pet.Bottom-height,monitor.Bottom-gap-height)),width,height);
        }
        void AnchorCard()
        {
            if(positioning || app.Pet==null)return;
            var nativePet=new WindowInteropHelper(app.Pet).Handle;
            if(nativePet==IntPtr.Zero)return;
            Bounds p;var m=new MonitorData{Size=Marshal.SizeOf(typeof(MonitorData))};
            if(!GetWindowRect(nativePet,out p)||!GetMonitorInfo(MonitorFromWindow(nativePet,2),ref m))return;
            positioning=true;
            try{
                Rect pet=new Rect(p.Left,p.Top,p.Right-p.Left,p.Bottom-p.Top),work=new Rect(m.Work.Left,m.Work.Top,m.Work.Right-m.Work.Left,m.Work.Bottom-m.Work.Top);
                double scale=app.Pet.ActualWidth>0?pet.Width/app.Pet.ActualWidth:1;if(scale<.5||scale>5)scale=1;
                MaxHeight=Math.Min(428,Math.Max(1,work.Height/scale-16));
                Width=Math.Min(page=="room"?430:page=="music"?380:364,Math.Max(1,work.Width/scale-16));
                IntPtr hwnd=new WindowInteropHelper(this).Handle;if(hwnd==IntPtr.Zero)return;
                double height=ActualHeight>0?ActualHeight:MaxHeight;
                Rect rect=CardBounds(pet,work,new Size(Width,height),scale);
                SetWindowPos(hwnd,IntPtr.Zero,(int)Math.Round(rect.X),(int)Math.Round(rect.Y),0,0,0x0015);
            }finally{positioning=false;}
        }
    }
    public partial class SaveData { public bool CloseTipSeen; }
}

