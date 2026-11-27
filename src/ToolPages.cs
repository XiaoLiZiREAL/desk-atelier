using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
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
        const string ToolInk = Ui.Ink;
        const string ToolMuted = Ui.Muted;
        const string ToolAccent = Ui.Accent;
        TextBlock bagFeedback;
        string draftAddress = "";

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
            return new TextBlock { Foreground = Ui.Color("#A12620"), TextWrapping = TextWrapping.Wrap,
                FontSize = 13, Margin = new Thickness(0, 5, 0, 0), Visibility = Visibility.Collapsed };
        }

        static void Feedback(TextBlock label, string message)
        {
            if (label == null) return;
            label.Text = message;
            label.Visibility = String.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }

        public void UpdateToolClock()
        {
            if (focusClock != null && focusClock.IsLoaded)
            {
                if (shownFocusMode != FocusMode) { RefreshPage(); return; }
                PaintFocusClock();
            }
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
            return new Border { BorderBrush = Ui.Color(Ui.Line), BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 8, 0, 8), Child = content };
        }

        bool bagOptionsOpen;
        public UIElement BagPage()
        {
            StackPanel page = new StackPanel();
            StackPanel links = new StackPanel();
            links.Children.Add(ToolHeading("已收藏 · " + app.Data.Bag.Count + " 项"));
            foreach (BagItem item in app.Data.Bag) links.Children.Add(BagRow(item));
            page.Children.Add(links);
            StackPanel actions = new StackPanel();
            actions.Children.Add(ToolDetail("把文件或文件夹拖给伙伴即可添加。背包记录位置，原件保存在原处。"));
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
            page.Children.Add(CompactDetails("添加其他项目",actions,bagOptionsOpen,v=>bagOptionsOpen=v));
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
            app.Data.Bag.Insert(0, entry);
            app.Pet.RefreshBagAttachment();
            return null;
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
            if (app.Data.Notes.Count>=StickyCapacity) return "便签已满，请先移除用完的便签。";
            app.Data.Notes.Insert(0,new NoteItem { Text=Store.CleanText(text), DueUtc=dueUtc });
            return null;
        }

        public bool CanAcceptPetDrop(IDataObject payload)
        {
            if (payload==null) return false;
            if (app.IsPaperDrag(payload)) return true;
            try { return payload.GetDataPresent(DataFormats.FileDrop) || payload.GetDataPresent(DataFormats.UnicodeText) || payload.GetDataPresent(DataFormats.Text); }
            catch (ExternalException) { return false; }
        }

        public void AcceptPetDrop(IDataObject payload)
        {
            if (!CanAcceptPetDrop(payload)) return;
            try
            {
                if (app.IsPaperDrag(payload)) { app.GiveDraggedPaper(); return; }
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
            var schedule=PaperTime.Read(text,DateTime.Now);
            long due=schedule.Error==null?schedule.Utc:0;
            // Keep the saved body of the original quick-drop command compatible.
            var quick=Regex.Match(text,@"^[0-9]+\s*分钟后\s*提醒我\s*(.+)$",RegexOptions.Singleline);
            if(due>0 && quick.Success)text=quick.Groups[1].Value.Trim();
            string noteIssue=AddNote(text,due);
            if(noteIssue==null && due>0 && !quick.Success){
                var note=app.Data.Notes[0];note.HeldByPet=true;note.PaperTimeKey=schedule.Key;note.PaperRepeatKey=schedule.RepeatKey;
            }
            string receipt=due>0?"记好了，"+new DateTime(due,DateTimeKind.Utc).ToLocalTime().ToString("M/d HH:mm")+" 提醒你。":
                schedule.Error!=null?"便签已保存，点纸上的小钟确认提醒时间。":"已记进便签册。";
            PetDropResult(noteIssue==null,"notes",noteIssue??receipt);
        }

        void PetDropResult(bool changed,string destination,string message)
        {
            if (changed)
            {
                app.Save();
                if (destination=="notes" && app.NotesDesk!=null) app.NotesDesk.RefreshStickyCollection();
                else if (IsVisible && page==destination) RefreshPage();
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
            Border tag = new Border { Background = Ui.Color(Ui.Selection), CornerRadius = new CornerRadius(4),
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
                ToolAction("移出", delegate {
                    app.Data.Bag.Remove(item);
                    app.Pet.RefreshBagAttachment();
                    app.Save();
                    if (app.Data.Bag.Count == 0) Close(); else RefreshPage();
                }));
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
                    if (!WebAddress(dest, out uri)) { ShowStatus("这个网址无效，请重新收藏。"); return; }
                    dest = uri.AbsoluteUri;
                }
                else if (!Path.IsPathRooted(dest) || (!File.Exists(dest) && !Directory.Exists(dest)))
                { ShowStatus("找不到原来的位置，文件可能已移动。"); return; }
                string issue=OpenWindowsItem(dest);
                if(issue==null)Feedback(bagFeedback,"");else ShowStatus(issue);
            }
            catch (ArgumentException) { ShowStatus("路径无效，请重新收藏。"); }
        }
        static string OpenWindowsItem(string target)
        {
            try{
                using(Process opened=Process.Start(new ProcessStartInfo{FileName=target,UseShellExecute=true})){}
                return null;
            }
            catch(Win32Exception){return "Windows 无法打开这项内容，请检查默认应用及文件位置。";}
            catch(InvalidOperationException){return "无法从当前位置打开，请重新选择。";}
            catch(ArgumentException){return "路径无效，请重新选择。";}
            catch(IOException){return "无法读取这个位置。";}
            catch(UnauthorizedAccessException){return "Windows 未允许打开这个位置。";}
        }
    }
}

