using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace DeskMate
{
    public partial class StudioWindow
    {
        const string ToolInk = "#263F35";
        const string ToolMuted = "#586D60";
        const string ToolAccent = "#35634B";
        TextBlock focusClock, focusState, focusCaption, noteFeedback, bagFeedback;
        Border focusTrack, focusFill;
        int shownFocusMode, previewMinutes, reminderChoice;
        string notesFilter = "全部";
        string draftNote = "", draftAddress = "";
        readonly Dictionary<NoteItem, TextBlock> reminderLabels = new Dictionary<NoteItem, TextBlock>();

        static T ToolMargin<T>(T element, double top, double right, double bottom) where T : FrameworkElement
        {
            element.Margin = new Thickness(0, top, right, bottom);
            return element;
        }

        static TextBlock ToolHeading(string label)
        {
            TextBlock view = Ui.Text(label, 17, ToolInk);
            view.FontWeight = FontWeights.SemiBold;
            return ToolMargin(view, 0, 0, 8);
        }

        static TextBlock ToolDetail(string label)
        {
            TextBlock view = Ui.Text(label, 13, ToolMuted);
            view.TextWrapping = TextWrapping.Wrap;
            return view;
        }

        static UIElement ToolGapCard(UIElement content)
        {
            Border wrap = Ui.Card(content); wrap.Padding = new Thickness(11,9,11,9); wrap.Margin = new Thickness(0,0,0,8);
            return wrap;
        }

        static Button ToolAction(string label, Action click, bool primary = false)
        {
            return ToolMargin(Ui.Button(label, click, primary), 4, 8, 4);
        }

        static WrapPanel ToolActions(params UIElement[] controls)
        {
            WrapPanel wrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            foreach (UIElement control in controls) wrap.Children.Add(control);
            return wrap;
        }

        static TextBlock ToolFeedback()
        {
            return new TextBlock { Foreground = Ui.Color("#934F35"), TextWrapping = TextWrapping.Wrap,
                FontSize = 13, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };
        }

        static void Feedback(TextBlock label, string message)
        {
            if (label == null) return;
            label.Text = message;
            label.Visibility = String.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }

        static string ClockTime(int seconds)
        {
            return String.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", seconds / 60, seconds % 60);
        }

        int FocusMode { get { return app.FocusPaused ? 2 : app.FocusActive ? 1 : 0; } }

        bool focusDetails;
        public UIElement FocusPage()
        {
            shownFocusMode=FocusMode;
            if(app.FocusActive || previewMinutes<1)previewMinutes=app.Data.PlannedMinutes;
            var panel=new StackPanel();
            focusState=Ui.Text("",12,"#D9E8D9");
            focusClock=Ui.Text("",43,"#FFFBEA");focusClock.FontWeight=FontWeights.SemiBold;
            focusClock.FontFamily=new FontFamily("Bahnschrift, Segoe UI, Microsoft YaHei UI");
            focusCaption=Ui.Text("",12,"#D7E3D5");
            focusTrack=new Border{Height=3,Margin=new Thickness(0,9,0,0),CornerRadius=new CornerRadius(2),Background=Ui.Color("#62826F")};
            focusFill=new Border{Height=3,Width=0,Background=Ui.Color("#D7E4A8"),HorizontalAlignment=HorizontalAlignment.Left};
            focusTrack.Child=focusFill;focusTrack.SizeChanged+=delegate{PaintFocusClock();};
            panel.Children.Add(new Border{Background=Ui.Color("#315542"),Padding=new Thickness(15,10,15,12),CornerRadius=new CornerRadius(10),
                Child=Ui.Stack(focusState,focusClock,focusCaption,focusTrack),Margin=new Thickness(0,0,0,8)});
            PaintFocusClock();
            if(!app.FocusActive)
            {
                var duration=Ui.Input(previewMinutes.ToString());duration.Width=51;duration.MaxLength=3;duration.ToolTip="1–180 分钟";
                var err=ToolFeedback();var units=ToolDetail("分钟");
                Action start=delegate{
                    int minutes;
                    if(!Int32.TryParse(duration.Text.Trim(),out minutes)||minutes<1||minutes>180){Feedback(err,"请输入 1 到 180 分钟。");duration.Focus();return;}
                    app.StartFocus(minutes);RefreshPage();
                };
                duration.TextChanged+=delegate{int n;if(Int32.TryParse(duration.Text,out n)&&n>=1&&n<=180){previewMinutes=n;PaintFocusClock();Feedback(err,"");}};
                duration.KeyDown+=delegate(object o,KeyEventArgs e){if(e.Key==Key.Enter){e.Handled=true;start();}};
                var row=ToolActions(duration,ToolMargin(units,0,12,0),ToolAction("开始专注",start,true));
                panel.Children.Add(row);panel.Children.Add(err);
                var shortcuts=new WrapPanel();
                foreach(int choice in new[]{15,25,45}){
                    int n=choice;var preset=ToolAction(n+" 分钟",delegate{duration.Text=n.ToString();});
                    preset.MinHeight=26;preset.Padding=new Thickness(8,3,8,3);shortcuts.Children.Add(preset);
                }
                panel.Children.Add(shortcuts);
            }
            else panel.Children.Add(ToolActions(ToolAction(app.FocusPaused?"继续专注":"暂停",delegate{
                if(app.FocusPaused)app.ResumeFocus();else app.PauseFocus();RefreshPage();
            },true),ToolAction("结束本轮",delegate{
                if(MessageBox.Show(this,"提前结束的这轮不会计入专注记录。结束本轮吗？","番茄钟",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes)
                {app.CancelFocus();RefreshPage();}
            })));
            int stage=Math.Min(3,app.Data.FocusCount);
            var summary=Ui.Stack(ToolDetail("累计完成 "+app.Data.FocusCount+" 轮 · "+app.Data.FocusMinutesTotal+" 分钟"),
                ToolMargin(ToolDetail(stage<3?"收音机已装好 "+stage+" / 3 个零件。每完成一轮，装好一件。":"窗边收音机已经装好。"),5,0,0));
            if(stage==3)summary.Children.Add(ToolAction("去小屋听歌",delegate{Go("room");}));
            panel.Children.Add(CompactDetails("专注记录",summary,focusDetails,v=>focusDetails=v));
            return panel;
        }
        void PaintFocusClock()
        {
            if (focusClock == null) return;
            int remaining = app.FocusActive ? app.RemainingSeconds : previewMinutes * 60;
            focusClock.Text = ClockTime(remaining);
            focusState.Text = app.FocusPaused ? "计时已暂停" : app.FocusActive ? "专注中" : "准备专注";
            focusCaption.Text = app.FocusPaused ? "还剩 " + ClockTime(remaining) + "，准备好再继续。" :
                app.FocusActive ? (app.Data.Character == "kurisu" ? "红莉栖正在整理实验手记。" : app.CharacterName + "在一旁安静地陪着你。") :
                "选一个时长，和" + app.CharacterName + "一起开始。";
            double part = app.FocusActive ? 1.0 - remaining / (double)Math.Max(60, app.Data.PlannedMinutes * 60) : 0;
            focusFill.Width = Math.Max(0, focusTrack.ActualWidth * Math.Min(1, part));
        }

        public void UpdateToolClock()
        {
            if (focusClock != null && focusClock.IsLoaded)
            {
                if (shownFocusMode != FocusMode) { RefreshPage(); return; }
                PaintFocusClock();
            }
            foreach (KeyValuePair<NoteItem, TextBlock> entry in reminderLabels)
                if (entry.Value.IsLoaded) entry.Value.Text = NoteTime(entry.Key);
        }

        static string NoteTime(NoteItem item)
        {
            if (item.Done) return "已完成";
            if (item.DueUtc == 0) return "无提醒";
            DateTime local = new DateTime(item.DueUtc, DateTimeKind.Utc).ToLocalTime();
            return (item.Notified ? "已提醒 · " : "提醒 · ") + local.ToString("M 月 d 日  HH:mm");
        }

        public UIElement NotesPage()
        {
            reminderLabels.Clear();
            StackPanel page = new StackPanel();
            StackPanel composer = new StackPanel();
            composer.Children.Add(ToolHeading("写一张新便签"));
            TextBox body = Ui.Input(draftNote);
            body.MaxLength = 240; body.ToolTip = "最多 240 个字，按 Enter 保存";
            body.TextChanged += delegate { draftNote = body.Text; };
            composer.Children.Add(body);
            ComboBox when = new ComboBox { MinWidth = 146, Height = 33,
                VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4) };
            string[] options = { "不设提醒", "1 分钟后", "5 分钟后", "10 分钟后", "30 分钟后", "1 小时后", "明早 9 点" };
            foreach (string option in options) when.Items.Add(option);
            when.SelectedIndex = reminderChoice;
            when.SelectionChanged += delegate { reminderChoice = when.SelectedIndex; };
            noteFeedback = ToolFeedback();
            Action add = delegate {
                string value = body.Text.Trim();
                if (String.IsNullOrEmpty(value)) { Feedback(noteFeedback, "先写下想记的事。"); body.Focus(); return; }
                int[] minutes = { 0, 1, 5, 10, 30, 60 };
                int choice = when.SelectedIndex;
                long due = choice == 6 ? DateTime.Today.AddDays(1).AddHours(9).ToUniversalTime().Ticks :
                    choice > 0 ? DateTime.UtcNow.AddMinutes(minutes[choice]).Ticks : 0;
                string issue = AddNote(value,due);
                if (issue != null) { Feedback(noteFeedback, issue); return; }
                draftNote = ""; reminderChoice = 0;
                if (notesFilter == "已完成") notesFilter = "待办";
                app.Save(); RefreshPage();
            };
            body.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Enter) { e.Handled = true; add(); }
            };
            composer.Children.Add(ToolMargin(ToolActions(when, ToolAction("收好便签", add, true)), 6, 0, 0));
            composer.Children.Add(noteFeedback);
            page.Children.Add(ToolGapCard(composer));
            StackPanel list = new StackPanel();
            int left = app.Data.Notes.Count(n => !n.Done);
            list.Children.Add(ToolHeading("便签册   ·   " + left + " 项待办"));
            WrapPanel filters = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
            foreach (string choice in new string[] { "全部", "待办", "已完成" })
            {
                string filter = choice;
                filters.Children.Add(ToolAction(filter, delegate { notesFilter = filter; RefreshPage(); }, notesFilter == filter));
            }
            list.Children.Add(filters);
            List<NoteItem> items = app.Data.Notes.Where(n => notesFilter == "全部" ||
                (notesFilter == "已完成" ? n.Done : !n.Done)).OrderBy(n => n.Done).ToList();
            if (items.Count == 0)
                list.Children.Add(ToolDetail(notesFilter == "已完成" ? "勾选完成的便签，会收在这里。" :
                    notesFilter == "待办" ? "待办都处理好了。" : "写下第一件想记的事，交给桌宠保管。"));
            foreach (NoteItem note in items) list.Children.Add(NoteRow(note));
            page.Children.Add(ToolGapCard(list));
            return page;
        }

        UIElement NoteRow(NoteItem item)
        {
            Grid grid = ThreeToolColumns();
            CheckBox done = new CheckBox { IsChecked = item.Done, VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 13, 0), ToolTip = item.Done ? "移回待办" : "标为完成",
                Cursor = Cursors.Hand };
            done.Click += delegate {
                item.Done = done.IsChecked == true;
                app.Save(); RefreshPage();
            };
            grid.Children.Add(done);
            StackPanel lines = new StackPanel();
            TextBlock title = Ui.Text(item.Text, 14, item.Done ? ToolMuted : ToolInk);
            title.TextWrapping = TextWrapping.Wrap;
            if (item.Done) title.TextDecorations = TextDecorations.Strikethrough;
            lines.Children.Add(title);
            TextBlock due = ToolMargin(Ui.Text(NoteTime(item), 12, ToolMuted), 2, 0, 0);
            reminderLabels[item] = due; lines.Children.Add(due);
            Grid.SetColumn(lines, 1); grid.Children.Add(lines);
            Button remove = ToolAction("移除", delegate {
                if (MessageBox.Show(this, "移除这张便签？", "便签册", MessageBoxButton.YesNo, MessageBoxImage.Question,
                    MessageBoxResult.No) != MessageBoxResult.Yes) return;
                app.Data.Notes.Remove(item); app.Save(); RefreshPage();
            });
            Grid.SetColumn(remove, 2); grid.Children.Add(remove);
            return ToolSeparator(grid);
        }

        static Grid ThreeToolColumns()
        {
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            return grid;
        }

        static Border ToolSeparator(UIElement content)
        {
            return new Border { BorderBrush = Ui.Color("#E3E7DD"), BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 8, 0, 8), Child = content };
        }

        public UIElement BagPage()
        {
            StackPanel page = new StackPanel();
            StackPanel actions = new StackPanel();
            actions.Children.Add(ToolHeading("放进背包"));
            actions.Children.Add(ToolDetail("常用文件和文件夹可以直接拖进这里，原件保存在原处。"));
            bagFeedback = ToolFeedback();
            actions.Children.Add(ToolActions(ToolAction("选择文件", PickFiles),
                ToolAction("选择文件夹", PickFolder)));

            TextBox address = Ui.Input(draftAddress);
            address.MaxLength = 2048; address.ToolTip = "例如 https://example.com";
            address.TextChanged += delegate { draftAddress = address.Text; };
            actions.Children.Add(ToolMargin(ToolDetail("或粘贴一个网址"), 11, 0, 5));
            Grid urlRow = new Grid();
            urlRow.ColumnDefinitions.Add(new ColumnDefinition());
            urlRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            address.Margin = new Thickness(0, 4, 8, 4);
            urlRow.Children.Add(address);
            Action addUrl = delegate {
                Uri uri;
                if (!WebAddress(address.Text, out uri)) { Feedback(bagFeedback, "请输入有效的 http 或 https 网址。"); address.Focus(); return; }
                string issue = AddBagEntry(new BagItem { Kind = "url", Target = uri.AbsoluteUri, Label = uri.Host });
                if (issue == null)
                { draftAddress = ""; app.Save(); RefreshPage(); }
                else Feedback(bagFeedback,issue);
            };
            address.KeyDown += delegate(object sender, KeyEventArgs e) {
                if (e.Key == Key.Enter) { e.Handled = true; addUrl(); }
            };
            Button add = ToolAction("收藏网址", addUrl, true);
            Grid.SetColumn(add, 1); urlRow.Children.Add(add);
            actions.Children.Add(urlRow);
            actions.Children.Add(bagFeedback);
            page.Children.Add(ToolGapCard(actions));

            StackPanel links = new StackPanel();
            links.Children.Add(ToolHeading("随手打开   ·   " + app.Data.Bag.Count + " 项"));
            if (app.Data.Bag.Count == 0) links.Children.Add(ToolDetail("背包还空着。放进第一个常用文件或网址，下次从这里打开。"));
            foreach (BagItem item in app.Data.Bag) links.Children.Add(BagRow(item));
            page.Children.Add(ToolGapCard(links));
            page.AllowDrop = true;
            page.Background = Brushes.Transparent;
            page.DragOver += delegate(object sender, DragEventArgs e) {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
                e.Handled = true;
            };
            page.Drop += delegate(object sender, DragEventArgs e) {
                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null) return;
                AddPaths(paths); e.Handled = true;
            };
            return page;
        }

        static bool WebAddress(string input, out Uri uri)
        {
            uri = null;
            string text = (input ?? "").Trim();
            if (text.Length < 1 || text.Length > 2048 || text.Any(Char.IsWhiteSpace)) return false;
            if (!text.Contains("://") && text.IndexOf('.') > 0 && text.IndexOf(':') < 0) text = "https://" + text;
            return Uri.TryCreate(text, UriKind.Absolute, out uri) && !String.IsNullOrEmpty(uri.Host) &&
                (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
        }

        string AddBagEntry(BagItem entry)
        {
            bool exists = app.Data.Bag.Any(b => b.Kind == entry.Kind && String.Equals(b.Target, entry.Target,
                entry.Kind == "url" ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
            if (exists) return "这项已经在背包里了。";
            if (app.Data.Bag.Count >= 250) return "背包已满，请先移除用不到的项目。";
            app.Data.Bag.Insert(0, entry); return null;
        }

        void AddPaths(IEnumerable<string> paths)
        {
            string message;
            if (CollectPaths(paths,out message)>0) { app.Save(); RefreshPage(); }
            Feedback(bagFeedback,message);
        }

        int CollectPaths(IEnumerable<string> paths, out string message)
        {
            int added=0, skipped=0;
            string reason=null;
            foreach (string path in paths)
            {
                bool folder=Directory.Exists(path);
                if (!folder && !File.Exists(path)) { skipped++; reason="这个位置已经移动或无法访问。"; continue; }
                if (!Path.IsPathRooted(path)) { skipped++; reason="请从资源管理器拖入文件或文件夹。"; continue; }
                string target=Path.GetFullPath(path);
                string name=Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar));
                string issue=AddBagEntry(new BagItem { Kind=folder?"folder":"file", Target=target, Label=String.IsNullOrWhiteSpace(name)?target:name });
                if (issue==null) added++; else { skipped++; reason=issue; }
            }
            message=added==0 ? reason??"没有收到文件。" : "背包收好了 " + added + " 项。" + (skipped==0?"":"另有 "+skipped+" 项未加入。");
            return added;
        }

        string AddNote(string text,long dueUtc)
        {
            if (String.IsNullOrWhiteSpace(text)) return "先写下想记的事。";
            if (text.Length>240) return "便签最多 240 个字，请缩短后再交给我。";
            if (app.Data.Notes.Count>=1500) return "便签已满，请先移除用完的便签。";
            app.Data.Notes.Insert(0,new NoteItem { Text=Store.CleanText(text), DueUtc=dueUtc });
            return null;
        }

        public bool CanAcceptPetDrop(IDataObject payload)
        {
            if (payload==null) return false;
            try { return payload.GetDataPresent(DataFormats.FileDrop) || payload.GetDataPresent(DataFormats.UnicodeText) || payload.GetDataPresent(DataFormats.Text); }
            catch (ExternalException) { return false; }
        }

        public void AcceptPetDrop(IDataObject payload)
        {
            if (!CanAcceptPetDrop(payload)) return;
            try
            {
                if (payload.GetDataPresent(DataFormats.FileDrop))
                {
                    var paths=payload.GetData(DataFormats.FileDrop) as string[];
                    if (paths==null || paths.Length==0) { app.React("wave","没有读到拖入的文件。"); return; }
                    if (paths.Length==1 && String.Equals(Path.GetExtension(paths[0]),".deskpet",StringComparison.OrdinalIgnoreCase))
                    {
                        app.Open("gift");
                        app.React("gift", PreviewGift(paths[0]) ? "收到一份礼物，先看看朋友写了什么吧。" : "这份礼物没有成功打开，礼物邮局里可以查看原因。");
                        return;
                    }
                    string message;
                    PetDropResult(CollectPaths(paths,out message)>0,"bag",message);
                    return;
                }
                string value=payload.GetData(DataFormats.UnicodeText) as string ?? payload.GetData(DataFormats.Text) as string;
                AcceptPetText(value);
            }
            catch (ExternalException) { app.React("wave","没有读到拖入的内容，请再试一次。"); }
            catch (IOException) { app.React("wave","这个文件暂时无法访问。"); }
            catch (ArgumentException) { app.React("wave","没有识别出有效的文件或文字。"); }
            catch (NotSupportedException) { app.React("wave","暂时无法读取这种拖入内容。"); }
            catch (UnauthorizedAccessException) { app.React("wave","Windows 未允许读取这个位置。"); }
        }

        void AcceptPetText(string input)
        {
            string text=(input??"").Trim();
            if (text.StartsWith("https://",StringComparison.OrdinalIgnoreCase) || text.StartsWith("http://",StringComparison.OrdinalIgnoreCase))
            {
                Uri uri;
                if (!WebAddress(text,out uri)) { app.React("wave","网址无效或超过 2048 个字符。"); return; }
                string issue=AddBagEntry(new BagItem { Kind="url", Target=uri.AbsoluteUri, Label=uri.Host });
                PetDropResult(issue==null,"bag",issue??"这个网址放进背包了。");
                return;
            }
            if (text.Length>300) { app.React("wave","文字太长，便签正文最多 240 个字。"); return; }
            Match timed=Regex.Match(text,@"^([0-9]+)\s*分钟后\s*提醒我\s*(.*)$",RegexOptions.Singleline);
            long due=0;
            int minutes=0;
            if (timed.Success)
            {
                if (!Int32.TryParse(timed.Groups[1].Value,out minutes) || minutes<1 || minutes>10080)
                { app.React("wave","提醒时间可以设在 1 到 10080 分钟后。"); return; }
                due=DateTime.UtcNow.AddMinutes(minutes).Ticks;
                text=timed.Groups[2].Value.Trim();
            }
            string noteIssue=AddNote(text,due);
            PetDropResult(noteIssue==null,"notes",noteIssue??(due==0?"已记进便签册。": "记好了，"+minutes+" 分钟后提醒你。"));
        }

        void PetDropResult(bool changed,string destination,string message)
        {
            if (changed)
            {
                app.Save();
                if (IsVisible && page==destination) RefreshPage();
            }
            app.React("wave",message);
        }

        void PickFiles()
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Multiselect = true, CheckFileExists = true,
                Title = "选择常用文件或程序", Filter = "所有文件 (*.*)|*.*" };
            if (picker.ShowDialog(this) == true) AddPaths(picker.FileNames);
        }

        sealed class DialogOwner : Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }
            public DialogOwner(Window window) { Handle = new System.Windows.Interop.WindowInteropHelper(window).Handle; }
        }

        void PickFolder()
        {
            using (var picker = new Forms.FolderBrowserDialog { Description = "选择要收藏的文件夹", ShowNewFolderButton = false })
                if (picker.ShowDialog(new DialogOwner(this)) == Forms.DialogResult.OK) AddPaths(new[] { picker.SelectedPath });
        }

        UIElement BagRow(BagItem item)
        {
            Grid grid = ThreeToolColumns();
            string type = item.Kind == "url" ? "网址" : item.Kind == "folder" ? "目录" : "文件";
            Border tag = new Border { Background = Ui.Color("#E8EEE3"), CornerRadius = new CornerRadius(6),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 11, 0),
                Padding = new Thickness(7, 4, 7, 4), Child = Ui.Text(type, 12, ToolAccent) };
            grid.Children.Add(tag);
            StackPanel labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            TextBlock name = Ui.Text(item.Label, 14, ToolInk);
            name.TextWrapping = TextWrapping.NoWrap; name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.ToolTip = item.Label; labels.Children.Add(name);
            TextBlock target = Ui.Text(item.Target, 11, ToolMuted);
            target.TextWrapping = TextWrapping.NoWrap; target.TextTrimming = TextTrimming.CharacterEllipsis;
            target.ToolTip = item.Target; labels.Children.Add(target);
            Grid.SetColumn(labels, 1); grid.Children.Add(labels);
            WrapPanel buttons = ToolActions(ToolAction("打开", delegate { OpenBagItem(item); }),
                ToolAction("移出", delegate { app.Data.Bag.Remove(item); app.Save(); RefreshPage(); }));
            Grid.SetColumn(buttons, 2); grid.Children.Add(buttons);
            return ToolSeparator(grid);
        }

        void OpenBagItem(BagItem item)
        {
            try
            {
                string dest = item.Target;
                if (item.Kind == "url")
                {
                    Uri uri;
                    if (!WebAddress(dest, out uri)) { Feedback(bagFeedback, "这个网址无效，请重新收藏。"); return; }
                    dest = uri.AbsoluteUri;
                }
                else if (!Path.IsPathRooted(dest) || (!File.Exists(dest) && !Directory.Exists(dest)))
                { Feedback(bagFeedback, "找不到原来的位置，文件可能已移动。"); return; }
                Process started = Process.Start(new ProcessStartInfo { FileName = dest, UseShellExecute = true });
                if (started != null) started.Dispose();
                Feedback(bagFeedback, "");
            }
            catch (Win32Exception) { Feedback(bagFeedback, "Windows 无法打开这项内容，请检查默认应用及文件位置。"); }
            catch (InvalidOperationException) { Feedback(bagFeedback, "无法从当前位置打开，请重新选择。"); }
            catch (ArgumentException) { Feedback(bagFeedback, "路径无效，请重新收藏。"); }
            catch (IOException) { Feedback(bagFeedback, "无法读取这个位置。"); }
            catch (UnauthorizedAccessException) { Feedback(bagFeedback, "Windows 未允许打开这个位置。"); }
        }
    }
}

