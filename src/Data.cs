using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace DeskMate
{
    public class NoteItem
    {
        public string Text = "";
        public bool Done;
        public long DueUtc;
        public bool Notified;
        public bool HeldByPet;
        public int PaperTone = -1;
        public string PaperTimeKey;
        public string WrittenTimeAtChoice;
        public string PaperRepeatKey;
        public List<string> AttachmentPaths = new List<string>();
        public bool ShouldSerializeAttachmentPaths() { return AttachmentPaths.Count>0; }
        internal bool HasContent { get { return !String.IsNullOrWhiteSpace(Text) || AttachmentPaths.Count>0; } }
        internal string Preview { get { return !String.IsNullOrWhiteSpace(Text)?Text:
            AttachmentPaths.Count>0?Path.GetFileName(AttachmentPaths[0]):""; } }
    }

    public class BagItem
    {
        public string Label = "";
        public string Target = "";
        public string Kind = "file";
    }

    public partial class SaveData
    {
        public double PetX = double.NaN;
        public double PetY = double.NaN;
        public double PetScale = 1;
        public bool AlwaysOnTop = true;
        public bool Quiet;
        public bool HideFullscreen = true;
        public string Character = "rem";
        public string MutsumiOutfit = "uniform";
        public string RoomTheme = "sage";
        public string Nickname = "";
        public string LastGift = "";
        // Save compatibility for installations updated from the earlier window-based puzzle.
        public string PuzzleTiles = "";
        public int PuzzleSteps, PuzzleWins;
        public string MusicPath = "";
        public bool Chair = true;
        public bool Lamp = true;
        public bool Cup = true;
        public int GamesWon;
        public int GameRounds;
        public int FocusCount;
        public int FocusMinutesTotal;
        public int PlannedMinutes = 25;
        public long FocusEndUtc;
        public int PausedSeconds;
        public List<NoteItem> Notes = new List<NoteItem>();
        public List<BagItem> Bag = new List<BagItem>();
    }

    public static class Store
    {
        private static readonly XmlSerializer Serializer = new XmlSerializer(typeof(SaveData));
        private static readonly object FileLock = new object();
        private static string damagedFile;

        private static string DataFolder()
        {
            string custom = Environment.GetEnvironmentVariable("DESKATELIER_DATA");
            return String.IsNullOrWhiteSpace(custom) ?
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DeskAtelier") :
                Path.GetFullPath(custom);
        }

        public static SaveData Load()
        {
            lock (FileLock)
            {
                string folder = DataFolder();
                string main = Path.Combine(folder, "state.xml");
                string backup = Path.Combine(folder, "state.bak.xml");
                SaveData result;
                damagedFile = null;
                if (TryRead(main, out result)) return result;
                if (File.Exists(main)) damagedFile = main;
                return TryRead(backup, out result) ? result : new SaveData();
            }
        }

        private static bool TryRead(string path, out SaveData result)
        {
            result = null;
            if (!File.Exists(path)) return false;
            try
            {
                XmlReaderSettings settings = new XmlReaderSettings {
                    DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                    MaxCharactersInDocument = 16 * 1024 * 1024
                };
                using (XmlReader reader = XmlReader.Create(path, settings))
                    result = (SaveData)Serializer.Deserialize(reader);
                if (result == null) return false;
                Normalize(result);
                return true;
            }
            catch (InvalidOperationException) { return false; }
            catch (XmlException) { return false; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static void Normalize(SaveData data)
        {
            if (!CompanionArchive.IsBuiltin(data.Character) && !CompanionArchive.IsId(data.Character)) data.Character = "rem";
            if (String.IsNullOrWhiteSpace(data.RoomTheme)) data.RoomTheme = "sage";
            if(data.MutsumiOutfit!="stage")data.MutsumiOutfit="uniform";
            if (Double.IsInfinity(data.PetX)) data.PetX = double.NaN;
            if (Double.IsInfinity(data.PetY)) data.PetY = double.NaN;
            if (Double.IsInfinity(data.PetScale) || Double.IsNaN(data.PetScale) || data.PetScale <= 0)
                data.PetScale = 1;
            data.PetScale = Math.Max(0.5, Math.Min(3, data.PetScale));
            data.PlannedMinutes = Math.Max(1, Math.Min(180, data.PlannedMinutes));
            data.PausedSeconds = Math.Max(0, Math.Min(data.PlannedMinutes * 60, data.PausedSeconds));
            if (data.FocusEndUtc < 0 || data.FocusEndUtc > DateTime.MaxValue.Ticks ||
                data.PausedSeconds > 0) data.FocusEndUtc = 0;
            data.FocusCount = Math.Max(0, data.FocusCount);
            data.GameRounds = Math.Max(0, data.GameRounds);
            data.GamesWon = Math.Max(0, Math.Min(data.GamesWon, data.GameRounds));
            data.FocusMinutesTotal = Math.Max(0, data.FocusMinutesTotal);
            data.Nickname = data.Nickname ?? "";
            data.LastGift = data.LastGift ?? "";
            data.MusicPath = data.MusicPath ?? "";
            if (data.Notes == null) data.Notes = new List<NoteItem>();
            foreach (NoteItem note in data.Notes.Where(n=>n!=null))
            {
                if(note.AttachmentPaths==null)note.AttachmentPaths=new List<string>();
                note.AttachmentPaths.RemoveAll(p => String.IsNullOrWhiteSpace(p) || p.IndexOfAny(Path.GetInvalidPathChars())>=0 || !Path.IsPathRooted(p));
                if (note.DueUtc < 0 || note.DueUtc > DateTime.MaxValue.Ticks) note.DueUtc = 0;
            }
            data.Notes.RemoveAll(n => n == null || !n.HasContent);
            if (data.Bag == null) data.Bag = new List<BagItem>();
            data.Bag.RemoveAll(b => b == null || String.IsNullOrWhiteSpace(b.Target));
            foreach (BagItem item in data.Bag)
                if (String.IsNullOrWhiteSpace(item.Label)) item.Label = item.Target;
        }

        public static void Write(SaveData data)
        {
            if (data == null) throw new ArgumentNullException("data");
            lock (FileLock)
            {
                PrepareStrings(data);
                string folder = DataFolder();
                Directory.CreateDirectory(folder);
                string main = Path.Combine(folder, "state.xml");
                string temp = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".tmp");
                try
                {
                    using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    {
                        Serializer.Serialize(stream, data);
                        stream.Flush(true);
                    }
                    if (File.Exists(main))
                    {
                        string backup = Path.Combine(folder, main == damagedFile ? "state.damaged.xml" : "state.bak.xml");
                        // Both files inherit the same private folder ACL. Restricted Windows
                        // accounts may replace data without being allowed to merge metadata.
                        File.Replace(temp, main, backup, true);
                    }
                    else File.Move(temp, main);
                    damagedFile = null;
                }
                finally
                {
                    if (File.Exists(temp)) File.Delete(temp);
                }
            }
        }

        public static string CleanText(string text)
        {
            if (String.IsNullOrEmpty(text)) return text ?? "";
            StringBuilder cleaned = null;
            int copyFrom = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char value = text[i];
                if (Char.IsHighSurrogate(value) && i + 1 < text.Length && Char.IsLowSurrogate(text[i + 1]))
                { i++; continue; }
                if (XmlConvert.IsXmlChar(value)) continue;
                if (cleaned == null) cleaned = new StringBuilder(text.Length);
                cleaned.Append(text, copyFrom, i - copyFrom);
                cleaned.Append('\uFFFD');
                copyFrom = i + 1;
            }
            return cleaned == null ? text : cleaned.Append(text, copyFrom, text.Length - copyFrom).ToString();
        }

        static void PrepareStrings(SaveData data)
        {
            data.Character = CleanText(data.Character);
            data.RoomTheme = CleanText(data.RoomTheme);
            data.Nickname = CleanText(data.Nickname);
            data.LastGift = CleanText(data.LastGift);
            data.MusicPath = CleanText(data.MusicPath);
            data.PuzzleTiles = CleanText(data.PuzzleTiles);
            data.LyricFolder = CleanText(data.LyricFolder);
            data.PreferredPlayer = CleanText(data.PreferredPlayer);
            data.GiftFrom = CleanText(data.GiftFrom);
            foreach (NoteItem note in data.Notes) note.Text = CleanText(note.Text);
            foreach (BagItem item in data.Bag)
            {
                item.Label = CleanText(item.Label); item.Target = CleanText(item.Target);
                item.Kind = CleanText(item.Kind);
            }
            foreach (MemoryLine line in data.Journal) line.Text = CleanText(line.Text);
        }
    }

    public partial class Studio
    {
        public bool FocusActive { get { return FocusPaused || Data.FocusEndUtc > 0; } }
        public bool FocusPaused { get { return Data.PausedSeconds > 0; } }
        public int RemainingSeconds
        {
            get
            {
                if (FocusPaused) return Data.PausedSeconds;
                if (Data.FocusEndUtc == 0) return 0;
                double remaining = (Data.FocusEndUtc - DateTime.UtcNow.Ticks) / (double)TimeSpan.TicksPerSecond;
                return (int)Math.Max(0, Math.Min(Data.PlannedMinutes * 60, Math.Ceiling(remaining)));
            }
        }

        public void StartFocus(int minutes)
        {
            if (FocusActive || minutes < 1 || minutes > 180) return;
            Data.PlannedMinutes = minutes;
            Data.FocusEndUtc = DateTime.UtcNow.AddMinutes(minutes).Ticks;
            Data.PausedSeconds = 0;
            Save();
            React("focus", Data.Character == "kurisu" ? "计时开始。我也来整理一下实验笔记。" : "一起安静工作吧，时间到了我来叫你。");
        }

        public void PauseFocus()
        {
            if (FocusPaused || Data.FocusEndUtc == 0) return;
            int remaining = RemainingSeconds;
            if (remaining == 0) { TickTools(); return; }
            Data.PausedSeconds = remaining;
            Data.FocusEndUtc = 0;
            Save();
        }

        public void ResumeFocus()
        {
            if (!FocusPaused) return;
            Data.FocusEndUtc = DateTime.UtcNow.AddSeconds(Data.PausedSeconds).Ticks;
            Data.PausedSeconds = 0;
            Save();
        }

        public void CancelFocus()
        {
            if (Data.FocusEndUtc > 0 && RemainingSeconds == 0) { TickTools(); return; }
            Data.PausedSeconds = 0;
            Data.FocusEndUtc = 0;
            Save();
        }

        public void TickTools()
        {
            long now = DateTime.UtcNow.Ticks;
            bool finished = Data.FocusEndUtc > 0 && Data.FocusEndUtc <= now;
            List<NoteItem> due = Data.Notes.Where(n => !n.Done && !n.Notified && n.DueUtc > 0 && n.DueUtc <= now).ToList();
            if (!finished && due.Count == 0) return;
            if (finished)
            {
                Data.FocusEndUtc = 0;
                Data.PausedSeconds = 0;
                if (Data.FocusCount < Int32.MaxValue) Data.FocusCount++;
                Data.FocusMinutesTotal = (int)Math.Min(Int32.MaxValue, (long)Data.FocusMinutesTotal + Data.PlannedMinutes);
            }
            foreach (NoteItem note in due) { note.Notified = true; note.HeldByPet = true; }
            if (due.Count>0 && NotesDesk!=null && NotesDesk.IsVisible) NotesDesk.RefreshStickyCollection();
            Save();
            List<string> messages = new List<string>();
            if (finished) messages.Add(Data.FocusCount == 3 ? "专注完成！收音机组装好了，去房间听听吧。" : "完成了 " + Data.PlannedMinutes + " 分钟专注，起来活动一下吧。");
            if (due.Count > 0)
                messages.Add((due.Count == 1 ? "便签提醒 · " : due.Count + " 项便签到期 · ") +
                    String.Join("、", due.Take(2).Select(n => ReminderPreview(n.Preview))) +
                    (due.Count > 2 ? " 等" : ""));
            Notify(String.Join("　", messages));
            RefreshReturnedPaper();
        }

        static string ReminderPreview(string text)
        {
            if (text.Length <= 36) return text;
            int end = Char.IsHighSurrogate(text[34]) ? 34 : 35;
            return text.Substring(0, end) + "…";
        }
    }
}
