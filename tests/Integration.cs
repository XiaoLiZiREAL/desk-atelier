using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DeskMate;
using Forms = System.Windows.Forms;

internal static partial class Integration
{
    static string screenDir, stateDir;
    static Studio app;
    static readonly List<string> log = new List<string>();
    static bool successful;

    [STAThread]
    public static int Main(string[] args)
    {
        string root = Path.GetFullPath(args.Length == 0 ? @"work\qa" : args[0]);
        screenDir = Path.Combine(root, "screens");
        stateDir = Path.Combine(root, "integration-state-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(screenDir);
        Directory.CreateDirectory(stateDir);
        Environment.SetEnvironmentVariable("DESKATELIER_DATA", stateDir);
        try
        {
            string exe = typeof(Studio).Assembly.Location;
            using (SHA256 hash = SHA256.Create())
            using (Stream stream = File.OpenRead(exe))
                Say("Build SHA256  " + BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", ""));
            Say("Testing  " + exe);
            app = new Studio(stateDir);
            app.DispatcherUnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs e)
            {
                Say("UNHANDLED UI ERROR  " + e.Exception.GetType().Name + "  " + e.Exception.Message);
                e.Handled = false;
            };
            app.StartStudio(null);
            Pump(450);
            TestShell();
            TestTools();

            TestRoomAndGift();
            TestCharacterAndVisit();
            TestTrayAndPersist();
            TestPetDrop();
            Say("ALL INTEGRATION CHECKS PASSED");
            successful = true;
        }
        catch (Exception e)
        {
            Say("FAIL  " + e.GetType().FullName + "  " + e.Message);
            try { Say(e.StackTrace); } catch { }
            try { Capture(app.Desk, "failure"); } catch { }
        }
        finally
        {
            if (app != null) try { app.Quit(); } catch (Exception e) { Say("QUIT  " + e.Message); }
            File.WriteAllLines(Path.Combine(screenDir, "report.txt"), log.ToArray(), new UTF8Encoding(true));
        }
        return successful ? 0 : 1;
    }

    static void Say(string s) { log.Add(s); Console.WriteLine(s); }
    static void Check(bool ok, string description)
    {
        if (!ok) throw new InvalidOperationException(description);
        Say("PASS  " + description);
    }

    static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background);
        timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    static IEnumerable<DependencyObject> Walk(DependencyObject root)
    {
        var used = new HashSet<DependencyObject>();
        var stack = new Stack<DependencyObject>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n == null || !used.Add(n)) continue;
            yield return n;
            foreach (object c in LogicalTreeHelper.GetChildren(n)) if (c is DependencyObject) stack.Push((DependencyObject)c);
            if (n is Visual)
                for (int i = VisualTreeHelper.GetChildrenCount(n) - 1; i >= 0; i--) stack.Push(VisualTreeHelper.GetChild(n, i));
        }
    }
    static List<T> Elements<T>() where T : FrameworkElement
    { return Walk(app.NotesDesk!=null && app.NotesDesk.IsVisible && !app.Desk.IsVisible?app.NotesDesk:app.Desk)
        .OfType<T>().Where(x => x.IsLoaded && x.IsVisible).ToList(); }

    static bool Match(Button b, string label)
    {
        if (String.Equals(b.Content as string, label, StringComparison.Ordinal) ||
            String.Equals(AutomationProperties.GetName(b), label, StringComparison.Ordinal) ||
            String.Equals(b.ToolTip as string, label, StringComparison.Ordinal) || String.Equals(b.Tag as string, label, StringComparison.Ordinal)) return true;
        return Walk(b).OfType<TextBlock>().Any(t => t.Text == label);
    }
    static Button Find(params string[] labels)
    {
        var buttons = Elements<Button>();
        foreach (string label in labels)
        {
            var match = buttons.FirstOrDefault(b => Match(b, label));
            if (match != null) return match;
        }
        return null;
    }
    static void Click(Button b)
    {
        if (b == null) throw new InvalidOperationException("Expected button unavailable.");
        if (!b.IsEnabled) throw new InvalidOperationException("Disabled button " + b.Content);
        b.BringIntoView(); b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, b)); Pump(65);
    }
    static void Click(string label)
    {
        Button b = Find(label);
        if (b == null) throw new InvalidOperationException("Button missing " + label + ". Actual controls: " + ButtonNames());
        Click(b);
    }
    static string ButtonNames()
    {
        return String.Join(" | ", Elements<Button>().Select(b => b.Content as string ??
            String.Join("/", Walk(b).OfType<TextBlock>().Select(t => t.Text).Distinct())));
    }
    static bool HasText(string keyword)
    { return Elements<TextBlock>().Any(t => t.Text != null && t.Text.Contains(keyword)); }

    static ContextMenu Quick()
    {
        app.Open("home");
        var menu=CurrentMenu();
        if(menu==null||!menu.IsOpen)throw new InvalidOperationException("Compact quick menu did not open");
        menu.UpdateLayout();
        return menu;
    }
    static ContextMenu CurrentMenu()
    {
        return (ContextMenu)typeof(PetWindow).GetField("quickMenu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(app.Pet);
    }
    static Button PetAccessory(string name)
    {
        return Walk(app.Pet).OfType<Button>().FirstOrDefault(b=>b.IsVisible && AutomationProperties.GetName(b)==name);
    }
    static T PaperPart<T>(string id) where T:FrameworkElement
    {
        return Elements<T>().Single(e=>AutomationProperties.GetAutomationId(e)==id);
    }
    [StructLayout(LayoutKind.Sequential)]struct Bounds {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Bounds result);
    [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    static Rect NativeBounds(Window window)
    {
        Bounds result;
        if(!GetWindowRect(new WindowInteropHelper(window).Handle,out result))throw new Exception("Unable to measure native window");
        return new Rect(result.Left,result.Top,result.Right-result.Left,result.Bottom-result.Top);
    }
    static void Nav(string page,params string[] names)
    {
        if(page=="bag")
            Click(PetAccessory("打开随身背包"));
        else if(page=="room")
        {
            Nav("gift");Click("布置礼物房间");
        }
        else
        {
            var mapping=new Dictionary<string,string>{{"focus","番茄钟"},{"notes","便签"},{"gift","礼物"},{"settings","伙伴设置"}};
            var menu=Quick();
            var choice=menu.Items.OfType<MenuItem>().Single(i=>Equals(i.Header,mapping[page]));
            menu.IsOpen=false;choice.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent,choice));Pump(90);
        }
        var card=page=="notes"?app.NotesDesk:app.Desk;
        if(card==null||!card.IsVisible||card.CurrentTool!=page)throw new Exception("Tool did not open: "+page);
        if(card.ActualWidth>444.5||card.ActualHeight>428.5)throw new Exception("Oversized "+page+" card");
        Say("Pet tool  "+page+"  "+card.ActualWidth+"x"+card.ActualHeight+" DIPs");
    }
    static void Expand(string name,bool visible=true)
    {
        var expander=Elements<Expander>().First(e=>Walk(e.Header as DependencyObject).OfType<TextBlock>().Any(t=>t.Text==name));
        expander.IsExpanded=visible;Pump(80);
    }
    static void Capture(Window w, string name)
    {
        var element = w.Content as FrameworkElement;
        if (element == null) throw new Exception("Window has no renderable content");
        element.UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(w.ActualWidth)), height = Math.Max(1, (int)Math.Ceiling(w.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        if (!w.AllowsTransparency)
        {
            var backing = new DrawingVisual();
            using (var paint = backing.RenderOpen()) paint.DrawRectangle(w.Background, null, new Rect(0,0,width,height));
            bitmap.Render(backing);
        }
        bitmap.Render(element);
        byte[] rgba = new byte[width * height * 4];
        bitmap.CopyPixels(rgba, width * 4, 0);
        var distinct = new HashSet<int>();
        int visible = 0;
        for (int k = 0; k < rgba.Length; k += 4)
        {
            if (rgba[k + 3] < 30) continue;
            visible++;
            if (visible % 11 == 0 && distinct.Count < 10000)
                distinct.Add((rgba[k] << 16) + (rgba[k+1] << 8) + rgba[k+2]);
        }
        if (width < 100 || height < 90 || visible < 400 || distinct.Count < 25)
            throw new InvalidOperationException("Empty render " + name + ": " + width + "x" + height + ", pixels " + visible + ", colors " + distinct.Count);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(screenDir, name + ".png"))) encoder.Save(file);
        Say("PNG   " + name + "  " + width + "x" + height + "  distinct sampled colors " + distinct.Count);
    }

    static void TestShell()
    {
        Check(!app.Desk.IsVisible&&app.Pet.IsVisible&&app.Windows.Cast<Window>().Count(w=>w.IsVisible)==1,"First launch shows the pet only");
        Capture(app.Pet,"01-startup-pet");
        bool contains=Walk(app.Pet).OfType<Image>().Any(i=>i.Source!=null);
        Check(contains,"The desktop character is loaded");
        var rightClick=new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Right){RoutedEvent=UIElement.MouseRightButtonUpEvent};
        app.Pet.LiveSprite.RaiseEvent(rightClick);
        var rightMenu=CurrentMenu();
        Check(rightClick.Handled&&rightMenu!=null&&rightMenu.IsOpen&&rightMenu.Placement==PlacementMode.MousePoint,"Right-clicking the desktop sprite opens its own context menu");
        Check(rightMenu.Items.OfType<MenuItem>().Select(i=>(string)i.Header).SequenceEqual(new[]{"番茄钟","听音乐","便签","礼物","伙伴设置","收起桌宠","退出"})&&
            !StudioWindow.SupportsTool("play")&&!StudioWindow.SupportsTool("games"),"Flat desktop menu contains only its seven direct actions");
        Check(PetAccessory("打开随身背包")==null && PetAccessory("翻开生活手记")==null &&
            !StudioWindow.SupportsTool("journal") && !StudioWindow.SupportsTool("music"),"Fresh pet has no empty backpack, diary tag or obsolete tool routes");
        rightMenu.IsOpen=false;
        Nav("notes");
        Check(PaperPart<TextBox>("StickyEditor").Text=="" && app.Data.Notes.Count==0,"A blank sheet opens without adding anything to the save");
        app.NotesDesk.Close();
        var menu=Quick();
        Check(!app.Desk.IsVisible&&menu.Items.OfType<MenuItem>().Count()<=8,"External activation presents a short context menu without opening a card");
        var menuBitmap=new RenderTargetBitmap((int)Math.Ceiling(menu.ActualWidth),(int)Math.Ceiling(menu.ActualHeight),96,96,PixelFormats.Pbgra32);
        menuBitmap.Render(menu);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(menuBitmap));
        using(var f=File.Create(Path.Combine(screenDir,"02-quick-menu.png")))encoder.Save(f);
        menu.IsOpen=false;
        Nav("focus");Capture(app.Desk,"03-tomato-ready");
        var duration=Elements<RangeBase>().Single(d=>AutomationProperties.GetAutomationId(d)=="FocusDurationDial");
        double initial=duration.Value; duration.Value=180; app.Desk.UpdateLayout();
        var clock=Elements<TextBlock>().Single(t=>t.Text=="180:00");
        var digitBounds=clock.TransformToVisual(duration).TransformBounds(new Rect(clock.RenderSize));
        Check(duration.Minimum==1 && duration.Maximum==180 && digitBounds.Left>16 &&
            digitBounds.Right<duration.ActualWidth-16 && digitBounds.Width>0,
            "The 180-minute readout fits inside the rotary dial");
        duration.Value=initial;
        var wd=Forms.Screen.FromHandle(new WindowInteropHelper(app.Pet).Handle).WorkingArea;
        var workArea=new Rect(wd.Left,wd.Top,wd.Width,wd.Height);
        var cardRect=NativeBounds(app.Desk);var petRect=NativeBounds(app.Pet);
        Check(workArea.Contains(cardRect)&&!cardRect.IntersectsWith(petRect)&&
            Math.Min(Math.Abs(cardRect.Right-petRect.Left),Math.Abs(petRect.Right-cardRect.Left))<=20,
            "Native card is alongside the pet and inside its active Windows work area ("+cardRect.Width+"x"+cardRect.Height+" physical pixels)");
        if(workArea.Width>petRect.Width+cardRect.Width+32)
        {
            var handle=new WindowInteropHelper(app.Pet).Handle;
            var otherSide=cardRect.Left<petRect.Left?workArea.Left+6:workArea.Right-petRect.Width-6;
            try
            {
                if(!SetWindowPos(handle,IntPtr.Zero,(int)otherSide,(int)petRect.Y,0,0,0x0015))throw new Exception("Could not move isolated test pet");
                Pump(110);
                var movedPet=NativeBounds(app.Pet);var movedCard=NativeBounds(app.Desk);
                Check(workArea.Contains(movedCard)&&!movedCard.IntersectsWith(movedPet)&&
                    ((cardRect.Left<petRect.Left)!=(movedCard.Left<movedPet.Left)),"Moving the pet across the monitor transfers its open card to the free side");
            }
            finally{SetWindowPos(handle,IntPtr.Zero,(int)petRect.X,(int)petRect.Y,0,0,0x0015);Pump(60);}
        }
        var method=typeof(StudioWindow).GetMethod("CardBounds",BindingFlags.Static|BindingFlags.NonPublic);
        foreach(double scale in new[]{1.0,1.25})
        {
            Rect monitor=new Rect(-1700,45,1680,950),pet=new Rect(-1500,760,210*scale,170*scale);
            Rect result=(Rect)method.Invoke(null,new object[]{pet,monitor,new Size(364,420),scale});
            Check(monitor.Contains(result)&&result.Width==364*scale&&result.Height==420*scale&&!result.IntersectsWith(pet),
                "Negative-origin monitor anchoring at "+(scale*100)+"% DPI");
            var content=(FrameworkElement)app.Desk.Content;
            var shot=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth*scale),(int)Math.Ceiling(content.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);
            shot.Render(content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(shot));
            using(var f=File.Create(Path.Combine(screenDir,"tomato-"+(scale*100)+".png")))png.Save(f);
        }
        Click("返回");Check(!app.Desk.IsVisible,"Back returns to the pet menu");
        Quick().IsOpen=false;
    }
    static void TestTools()
    {
        Nav("notes", "便签", "便签提醒", "便签册");
        var note = Elements<TextBox>().Single();
        note.Text = "周六的小计划\r\n\r\n给窗台的薄荷浇水。\r\n去书店取预订的书。";
        Pump(600);
        Check(Store.Load().Notes.Single().Text.Contains("薄荷"), "Typing on the sheet autosaves without a confirmation button");
        Capture(app.NotesDesk,"03-notes");
        app.NotesDesk.Close();
        string sample=Path.Combine(stateDir,"书单.txt");
        File.WriteAllText(sample,"月亮与六便士");
        app.Desk.AcceptPetDrop(new DataObject(DataFormats.FileDrop,new[]{sample}));
        Pump(100);
        Check(!app.Desk.IsVisible && PetAccessory("打开随身背包")!=null,"File drop reveals backpack without opening the card");
        var bag=PetAccessory("打开随身背包");
        var sprite=app.Pet.LiveSprite;
        var bagCenter=bag.TranslatePoint(new Point(bag.ActualWidth/2,bag.ActualHeight/2),sprite);
        Check(bagCenter.X<sprite.ActualWidth*.5 && bagCenter.Y>sprite.ActualHeight*.73,"Backpack stays near the left foot");
        Capture(app.Pet,"03-pet-accessories");
        Nav("bag");
        Check(HasText("书单.txt"),"Backpack opens directly to the dropped file");
        File.Move(sample,sample+".moved");
        Click("打开");
        Check(HasText("文件可能已移动"),"Missing file feedback is visible without expanding backpack options");
        File.Move(sample+".moved",sample);
        Expand("添加其他项目");
        Elements<TextBox>().Single().Text = "example.org";
        Click("收藏网址");
        Check(app.Data.Bag.Count == 2 && app.Data.Bag[0].Target == "https://example.org/", "Backpack bookmark through UI");
        Capture(app.Desk, "04-bag");
        Click("移出");Click("移出");
        Check(!app.Desk.IsVisible && PetAccessory("打开随身背包")==null && File.Exists(sample),"Removing the last item closes the card, hides the backpack and retains the real file");
        app.Desk.AcceptPetDrop(new DataObject(DataFormats.UnicodeText,"https://example.org/"));
        Nav("focus","专注工坊","专注","专注计时");
        Elements<RangeBase>().Single(d=>AutomationProperties.GetAutomationId(d)=="FocusDurationDial").Value=2;
        Click("开始专注");
        Check(app.FocusActive && app.Data.PlannedMinutes == 2, "Custom focus duration accepted");
        Click("暂停"); Check(app.FocusPaused, "Timer paused");
        Capture(app.Desk,"05-focus-paused");
        Click("继续专注"); Check(!app.FocusPaused && app.FocusActive, "Timer resumed");
        // Move only the isolated deadline to simulate sleep and a reminder while the app is closed.
        app.Data.FocusEndUtc = DateTime.UtcNow.AddSeconds(-3).Ticks;
        app.Data.Notes[0].DueUtc = DateTime.UtcNow.AddSeconds(-2).Ticks;
        app.Save(); app.TickTools(); app.TickTools();
        Check(app.Data.FocusCount == 1 && app.Data.FocusMinutesTotal == 2 && app.Data.Notes[0].Notified, "Expiry reconciliation records focus and due note once");
        Check(app.Data.Notes.Count==1 && app.Data.Journal.Count==0, "A reminder does not create diary entries or extra notes");
        for (int round = 0; round < 2; round++)
        {
            Nav("focus","专注工坊");
            Elements<RangeBase>().Single(d=>AutomationProperties.GetAutomationId(d)=="FocusDurationDial").Value=1;
            Click("开始专注");
            app.Data.FocusEndUtc = DateTime.UtcNow.AddSeconds(-2).Ticks;
            app.TickTools();
        }
        Check(app.Data.FocusCount == 3 && app.Data.FocusMinutesTotal == 4, "Three completed sessions unlocked the workshop project");
    }

    static void TestRoomAndGift()
    {
        Nav("room","我的小屋","房间","桌边小屋","布置房间");
        Click("暮蓝"); Click("收起台灯");
        Check(!app.Data.Lamp && app.Data.RoomTheme == "dusk", "Room theme and furniture visibility saved");
        Click("一起喝茶"); Pump(700); Capture(app.Desk, "08-room");
        Check(Find("播放") != null && HasText("窗边小调"), "Completed project exposes the built-in playable radio");
        Click("播放");
        Check(Find("停止播放") != null, "Radio started its built-in track via play control");
        Click("停止播放"); Check(Find("播放") != null, "Radio stopped via the same control");
        Capture(app.Desk,"15-radio");
        Nav("gift","礼物邮局","礼物","桌边礼物","礼物包");
        Capture(app.Desk, "09-gifts");
        string gift = Path.Combine(stateDir,"friend.deskpet");
        File.WriteAllText(gift,"<deskpetGift version='1'><from>阿岚</from><card>给你留了一杯茶。</card><theme>apricot</theme><chair on='1' x='0.35' y='0.82'/><lamp on='1' x='0.15' y='0.75'/><cup on='0' x='0.8' y='0.83'/></deskpetGift>", Encoding.UTF8);
        Check(app.Desk.PreviewGift(gift), "External gift opened for preview");
        Pump(75); Check(app.Data.RoomTheme == "dusk" && HasText("阿岚"), "Preview shows sender without modifying the room");
        Capture(app.Desk, "10-gift-preview");
        Click("收下并布置");
        Check(app.Data.RoomTheme == "apricot" && app.Data.Lamp && !app.Data.Cup &&
            Math.Abs(app.Data.ChairX-.35) < .001 && app.Data.LastGift.Contains("阿岚"), "Accepted gift applied exact furniture, location and note");
    }

    static void TestCharacterAndVisit()
    {
        Nav("settings","伙伴设置","偏好设置","设置");
        Check(!HasText("给你留了一杯茶"),"Switching tools dismisses the previous card's notification");
        Capture(app.Desk, "11-settings");var pace=Elements<ComboBox>().Single(x=>AutomationProperties.GetName(x)=="主动活动频率");pace.SelectedIndex=2;Check(app.Data.ActivityPace=="lively","Activity frequency is configurable in the compact settings sheet");pace.SelectedIndex=0;
        var steve=Find("史蒂夫");
        Check(steve!=null,"Minecraft Steve is available as a built-in settings choice");
        Click(steve);
        Check(app.Data.Character=="steve"&&app.CharacterName=="史蒂夫"&&app.Pet.LiveSprite.Source!=null&&Store.Load().Character=="steve",
            "Switching to Steve applies an actual sprite and persists the character choice");
        Capture(app.Pet,"12-desktop-steve");
        if (app.Data.Character != "kurisu")
        {
            var pick = Find("红莉栖","牧濑红莉栖","选择红莉栖","换成红莉栖");
            if (pick == null) pick = Elements<Button>().FirstOrDefault(b => Walk(b).OfType<TextBlock>().Any(t => t.Text.Contains("红莉栖")));
            if (pick == null) throw new Exception("No character selector: " + ButtonNames());
            Click(pick);
        }
        Check(app.Data.Character == "kurisu", "Partner selected from actual settings control");
        Capture(app.Pet,"12-desktop-kurisu");
        app.Pet.Visit(); Pump(850);
        Window visitor=app.Windows.Cast<Window>().FirstOrDefault(w=>w.Title=="桌边来客");
        Check(visitor!=null && visitor.IsVisible, "Companion visit opened other character's transparent WPF window");
        Capture(visitor,"13-guest-pet");
    }

    static Forms.ToolStripMenuItem Menu(Forms.ContextMenuStrip menu,string label)
    { return menu.Items.OfType<Forms.ToolStripMenuItem>().First(i=>i.Text==label); }

    static void TestTrayAndPersist()
    {
        var menu = (Forms.ContextMenuStrip)typeof(PetWindow).GetField("menu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(app.Pet);
        app.Desk.Close(); Pump(70); Check(!app.Desk.IsVisible && !app.Exiting, "Closing a tool keeps the pet running");
        Menu(menu,"便签").PerformClick(); Pump(130);
        Check(app.NotesDesk.IsVisible&&!app.Desk.IsVisible, "Tray opens the bare sheet");
        var sheet=PaperPart<Grid>("StickyPaper");
        string color=AutomationProperties.GetName(sheet);
        Click(PaperPart<Button>("StickyFold"));
        Check(PaperPart<TextBox>("StickyEditor").Text=="" && color!=AutomationProperties.GetName(sheet),"Fold opens a fresh sheet and changes its paper color");
        PaperPart<TextBox>("StickyEditor").Text="临时的第二页";
        Click(PaperPart<Button>("StickyTrash"));
        Check(app.Data.Notes.Count==1 && PaperPart<TextBox>("StickyEditor").Text.Contains("薄荷"),"Trash removes only the selected sheet");
        
        Nav("room","我的小屋"); Pump(125); Capture(app.Desk,"16-compact-room");
        app.Save(); var disk=Store.Load();
        Check(disk.Notes.Count==1 && disk.Notes[0].Notified && disk.FocusCount==3 &&
            disk.Character=="kurisu" &&
            disk.Bag.Count==1 && disk.RoomTheme=="apricot" && disk.LastGift.Contains("阿岚"), "Actual persisted snapshot reload includes tool, character and gift state");
    }

    static void TestPetDrop()
    {
        Nav("notes","便签提醒");
        Elements<TextBox>().Single().Text="待会继续写的草稿";
        var reminder=new DataObject(DataFormats.UnicodeText,"10分钟后提醒我拿外卖");
        Check(app.Pet.AllowDrop && app.Desk.CanAcceptPetDrop(reminder), "Pet window accepts Windows text drag/drop");
        app.Desk.AcceptPetDrop(reminder); Pump(70);
        Check(app.Data.Notes.Count==2 && app.Data.Notes[0].Text=="拿外卖" &&
            app.Data.Notes[0].DueUtc>DateTime.UtcNow.AddMinutes(9).Ticks &&
            Elements<TextBox>().Single().Text=="待会继续写的草稿", "Timed pet memo preserves an open draft while refreshing its list");
        app.Desk.AcceptPetDrop(new DataObject(DataFormats.Text,new string('文',241)));
        Check(app.Data.Notes.Count==2,"Oversized dropped note refused intact");
        var url=new DataObject(DataFormats.UnicodeText,"https://example.net/path");
        app.Desk.AcceptPetDrop(url); app.Desk.AcceptPetDrop(url);
        string sample=Path.Combine(stateDir,"collection.txt");
        File.WriteAllText(sample,"sample");
        app.Desk.AcceptPetDrop(new DataObject(DataFormats.FileDrop,new[]{sample,stateDir}));
        var saved=Store.Load();
        Check(saved.Bag.Count==4 && saved.Bag.Any(b=>b.Kind=="folder") && saved.Bag.Any(b=>b.Target==sample),
            "Pet drops persist unique links, local file and folder in backpack");
        string fixture=Path.Combine(stateDir,"friend.deskpet");
        app.Desk.AcceptPetDrop(new DataObject(DataFormats.FileDrop,new[]{fixture})); Pump(75);
        Check(Find("收下并布置")!=null,"Gift file dropped on pet opens explicit preview");
    }
}





