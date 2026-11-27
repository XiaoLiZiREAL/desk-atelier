using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

[assembly: AssemblyTitle("窗边手记")]
[assembly: AssemblyProduct("窗边手记 · Desk Atelier")]
[assembly: AssemblyDescription("离线 Windows 桌面伙伴")]
[assembly: AssemblyVersion("1.7.0.0")]
[assembly: AssemblyFileVersion("1.7.0.0")]

namespace DeskMate
{
    public sealed class MemoryLine
    {
        public long Time;
        public string Text;
        public bool Automatic;
    }
    public partial class SaveData
    {
        // Retain old diary entries when upgrading an existing save.
        public List<MemoryLine> Journal = new List<MemoryLine>();
        public bool ShouldSerializeJournal() { return Journal != null && Journal.Count > 0; }
    }
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                string data = Environment.GetEnvironmentVariable("DESKATELIER_DATA");
                if (String.IsNullOrWhiteSpace(data)) data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskAtelier");
                data = Path.GetFullPath(data);
                Directory.CreateDirectory(data);
                string channel;
                using (var sha = SHA256.Create())
                    channel = "Local\\DeskAtelier." + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(data.ToUpperInvariant() + Environment.UserName))).Replace("-","").Substring(0,24);
                using (var mutex = new Mutex(false, channel))
                {
                    bool acquired;
                    try { acquired = mutex.WaitOne(0); } catch (AbandonedMutexException) { acquired = true; }
                    string gift = args.FirstOrDefault(p => p.EndsWith(".deskpet", StringComparison.OrdinalIgnoreCase) && File.Exists(p));
                    if (gift != null) gift = Path.GetFullPath(gift);
                    if (!acquired)
                    {
                        string id = Path.Combine(data, "open-" + Guid.NewGuid().ToString("N"));
                        File.WriteAllText(id + ".tmp", gift ?? "");
                        File.Move(id + ".tmp", id + ".request");
                        return 0;
                    }
                    try
                    {
                        var app = new Studio(data);
                        app.Startup += delegate { app.StartStudio(gift); };
                        return app.Run();
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception error)
            {
                MessageBox.Show("启动时遇到问题。\n" + error.GetBaseException().Message, "窗边手记", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
    public partial class Studio : Application
    {
        public SaveData Data;
        public PetWindow Pet;
        public StudioWindow Desk;
        public StudioWindow NotesDesk { get; private set; }
        public ListeningRoom Music;
        public string CharacterName {
            get {
                switch(Data.Character) {
                    case "kurisu": return "红莉栖";
                    case "steve": return "史蒂夫";
                    case "rem": return "蕾姆";
                    case "hitori": return "后藤独";
                    default: return CustomName(Data.Character) ?? "伙伴";
                }
            }
        }
        readonly string folder;
        public string UserFolder { get { return folder; } }
        readonly string assetFolder;
        public bool Exiting { get; private set; }
        readonly DispatcherTimer ticks = new DispatcherTimer();
        readonly Random random = new Random();
        int previousLine = -1;
        bool savePending;
        long nextRetry;
        public Studio(string folder)
        {
            this.folder = folder;
            assetFolder = Path.Combine(folder,"art",typeof(Studio).Module.ModuleVersionId.ToString("N"));
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Data = Store.Load();
            if (Data.Journal == null) Data.Journal = new List<MemoryLine>();
            Data.Journal.RemoveAll(x => x == null || x.Time <= 0 || x.Time > DateTime.MaxValue.Ticks || String.IsNullOrWhiteSpace(x.Text));
            SessionEnding += delegate { Quit(); };
        }
        public string Asset(string filename) { return LookupCustomAsset(filename) ?? Path.Combine(assetFolder,filename); }
        public void ExtractArt()
        {
            Directory.CreateDirectory(assetFolder);
            Assembly assembly = typeof(Studio).Assembly;
            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith("art.", StringComparison.Ordinal)) continue;
                string target = Asset(name.Substring(4));
                using (Stream source = assembly.GetManifestResourceStream(name))
                {
                    if (File.Exists(target) && new FileInfo(target).Length == source.Length) continue;
                    string temp = target + ".part";
                    using (FileStream output = File.Create(temp)) source.CopyTo(output);
                    if (File.Exists(target)) File.Replace(temp,target,null,true); else File.Move(temp,target);
                }
            }
        }
        public void StartStudio(string gift)
        {
            ExtractArt();
            VerifyCompanion();
            Pet = new PetWindow(this);
            Desk = new StudioWindow(this);
            Music = new ListeningRoom(this);
            Pet.AllowDrop = true;
            Pet.DragOver += delegate(object sender,DragEventArgs e) {
                e.Effects = Desk.CanAcceptPetDrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true;
            };
            Pet.Drop += delegate(object sender,DragEventArgs e) {
                if (Desk.CanAcceptPetDrop(e.Data)) Desk.AcceptPetDrop(e.Data); e.Handled = true;
            };
            MainWindow = Desk;
            Pet.Show();
            if (gift != null) {Open("gift");Desk.PreviewGift(gift);}
            else if(!Data.PetWelcomeSeen)
            {
                Data.PetWelcomeSeen=true;Save();
                Pet.React("wave","我在这儿。右击我可以计时、听歌。");
                Pet.RefreshRoutinePace();
            }
            ticks.Interval = TimeSpan.FromSeconds(1);
            ticks.Tick += delegate { Pulse(); };
            ticks.Start();
            TickTools();
            RefreshReturnedPaper();
        }
        void Pulse()
        {
            TickTools();
            if (Desk.IsVisible) Desk.Tick();
            if (savePending && DateTime.UtcNow.Ticks >= nextRetry) Save();
            foreach (string path in Directory.EnumerateFiles(folder,"open-*.request").Take(4))
            {
                try
                {
                    string gift = new FileInfo(path).Length <= 32768 ? File.ReadAllText(path) : "";
                    File.Delete(path);
                    if (gift.EndsWith(".deskpet",StringComparison.OrdinalIgnoreCase) && File.Exists(gift))
                    {Open("gift");Desk.PreviewGift(gift);}
                    else Open("home");
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
        public void Open(string page)
        {
            if (Desk == null) return;
            if (page != "notes" && NotesDesk != null && NotesDesk.IsVisible && !NotesDesk.KeepsStickyOpen) NotesDesk.Close();
            if(!StudioWindow.SupportsTool(page))
            {
                if(Desk.IsVisible)Desk.Hide();
                Pet.Reveal();Pet.ShowQuickMenu();return;
            }
            Pet.OpeningTool();
            var card=Desk;
            if(page=="notes")
            {
                if(Desk.IsVisible)Desk.Close();
                if(NotesDesk==null)NotesDesk=new StudioWindow(this);
                card=NotesDesk;
            }
            card.Go(page);
            if (!card.IsVisible) card.Show();
            if (card.WindowState == WindowState.Minimized) card.WindowState = WindowState.Normal;
            card.Activate();
        }
        public void Save()
        {
            try { Store.Write(Data); savePending = false; }
            catch (IOException ex) { MarkSaveFailure(ex); }
            catch (UnauthorizedAccessException ex) { MarkSaveFailure(ex); }
        }
        void MarkSaveFailure(Exception error)
        {
            if (!savePending && Pet != null) Pet.Notice("进度暂时无法保存，将自动重试。" + error.Message);
            savePending = true; nextRetry = DateTime.UtcNow.AddSeconds(30).Ticks;
        }
        public void React(string action, string text)
        {
            if (Pet != null) Pet.React(action,text);
            if (Desk != null && !String.IsNullOrWhiteSpace(text)) Desk.ShowStatus(text);
        }
        public void Notify(string message)
        {
            if (Pet != null) { Pet.React("wave",message); Pet.Notice(message); }
            if (Desk != null) Desk.ShowStatus(message);
        }
        public void Interact(string action)
        {
            bool lab = Data.Character == "kurisu";
            bool custom = Data.Character != "rem" && !lab;
            string[] lines;
            if (action == "visit")
            {
                Pet.Reveal(); Pet.Visit();
                Desk.ShowStatus("两位伙伴正在桌面上碰面。"); return;
            }
            if (action == "tea")
                lines = lab ? new []{"把杯子放在笔记旁边就好。……谢谢。","正好写完这一页。一起休息一会儿？","这温度刚刚好，实验数据晚点再看。"} :
                    new []{"这杯留给你，我泡了两人份。","杯子有点烫。要等一小会儿哦。","今天也给你留了窗边的位置。"};
            else if (action == "wave")
                lines = lab ? new []{"看得到。挥一下手就够了啦。","刚好抬头，你来得很巧。","嗯，我在这儿。"} :
                    new []{"你回来啦。","手上的事刚好忙完，一起坐坐吧。","今天要一起做些什么呢？"};
            else
                lines = lab ? new []{"嗯？刚才在想一个问题。","再给我一秒，这个算式快解出来了。","有点累了？那就陪我发会儿呆。"} :
                    new []{"我在呢。","刚把围裙整理好，陪你坐一会儿。","要休息一下吗？我去拿杯子。"};
            if (custom)
                lines = action == "tea" ? new []{"茶泡好了，一起歇一会儿吧。","谢谢你分给我的这杯茶。","窗边的茶香真好。"} :
                    new []{"嗨，我在这儿。","我给你留了个位置。","今天也一起待一会儿吧。"};
            if(Data.Character=="steve")
                lines=action=="tea"?new[]{"水瓶带上了。歇口气再挖。","喝口水，我把地图摊开。","这一瓶留给你。"}:
                    new[]{"镐子在，地图也在。出发？","刚挖到一块圆石。","我在这儿守着工作台。"};
            if(Data.Character=="hitori")
                lines=action=="tea"?new[]{"谢、谢谢。我两只手拿比较稳。","等茶凉一点，我再练一遍副歌。"}:
                    new[]{"刚才那段……你听见了吗？","我把音量调小一点。这样可以吗？","等、等一下，这次我能弹顺的。"};
            int pick = (previousLine + 1 + random.Next(lines.Length - 1)) % lines.Length;
            previousLine = pick;
            string words = lines[pick];
            if (!String.IsNullOrWhiteSpace(Data.Nickname) && random.Next(4) == 0) words = Data.Nickname + "，" + words;
            React(action,words);
        }
        public void Quit()
        {
            if (Exiting) return;
            Exiting = true; ticks.Stop(); Save();
            if (Desk != null) Desk.CloseActivities();
            if (Music != null) Music.Dispose();
            if (Pet != null) Pet.DisposeTray();
            Shutdown();
        }
    }
}
