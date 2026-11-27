using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace DeskMate
{
    // Read the leading time instruction. The original sheet, including its wording,
    // remains the single stored object throughout the handoff and the reminder.
    internal sealed class PaperTime
    {
        internal string Key, Error;
        internal long Utc;
        const string Number=@"[0-9零〇一二两三四五六七八九十百]+";
        static readonly Regex Relative=new Regex(@"^("+Number+@")\s*(分钟|小时)后",RegexOptions.CultureInvariant);
        static readonly Regex WallClock=new Regex(@"^(今天|明天|后天)?\s*(凌晨|早上|上午|中午|下午|晚上)?\s*("+Number+@")\s*(?:[：:]\s*([0-9]{1,2})(?![0-9])|[点时]\s*(?:([半整])|("+Number+@")分?)?)",RegexOptions.CultureInvariant);

        static int Count(string value)
        {
            int n;
            if(Int32.TryParse(value,out n))return n;
            const string digits="零一二三四五六七八九";
            int total=0,digit=-1,unit=1000;
            foreach(char c in value.Replace('两','二').Replace('〇','零')){
                int d=digits.IndexOf(c);
                if(d>=0){if(digit>0)return -1;digit=d;continue;}
                int multiple=c=='百'?100:c=='十'?10:0;
                if(multiple==0 || multiple>=unit || digit==0)return -1;
                total+=(digit<0?1:digit)*multiple;unit=multiple;digit=-1;
            }
            return total+Math.Max(0,digit);
        }
        internal static PaperTime Read(string text,DateTime now)
        {
            string value=(text??"").TrimStart();
            var relative=Relative.Match(value);
            if(relative.Success){
                int amount=Count(relative.Groups[1].Value);
                long minutes=(long)amount*(relative.Groups[2].Value=="小时"?60:1);
                if(minutes<1 || minutes>10080)return new PaperTime{Error="提醒可设在 1 分钟到 7 天后"};
                return new PaperTime{Key="+"+minutes,Utc=now.ToUniversalTime().AddMinutes(minutes).Ticks};
            }
            var wall=WallClock.Match(value);
            if(!wall.Success)return new PaperTime();
            string day=wall.Groups[1].Value,period=wall.Groups[2].Value;
            int hour=Count(wall.Groups[3].Value),minute=wall.Groups[5].Value=="半"?30:0;
            if(wall.Groups[4].Success)minute=Count(wall.Groups[4].Value);
            if(wall.Groups[6].Success)minute=Count(wall.Groups[6].Value);
            if(hour<0 || hour>23 || minute<0 || minute>59 || (period.Length>0 && hour>12))
                return new PaperTime{Error="请检查便签开头的小时和分钟"};
            if((period=="下午" || period=="晚上" || (period=="中午" && hour<=3)) && hour<12)hour+=12;
            if((period=="凌晨" || period=="上午" || period=="早上") && hour==12)hour=0;
            int days=day=="明天"?1:day=="后天"?2:0;
            bool endOfDay=period=="晚上" && hour==12;
            if(endOfDay){hour=0;days++;}
            DateTime local=now.Date.AddDays(days).AddHours(hour).AddMinutes(minute);
            if(day.Length==0 && local<=now)local=local.AddDays(1);
            var time=new PaperTime{Key=day+(endOfDay?"+1":"")+"@"+hour+":"+minute,Utc=local.ToUniversalTime().Ticks};
            if(local<=now)time.Error="这个提醒时间已经过去了";
            return time;
        }
    }

    public partial class Studio
    {
        const string PaperFormat="DeskAtelier.StickyPaper";
        NoteItem draggedPaper;
        int draggedTone;
        bool paperDelivered;

        internal void DragPaper(UIElement grip,NoteItem note,int tone)
        {
            if(note==null || !Data.Notes.Contains(note))return;
            draggedPaper=note;draggedTone=tone;paperDelivered=false;
            try{DragDrop.DoDragDrop(grip,new DataObject(PaperFormat,"sheet"),DragDropEffects.Copy);}
            finally{
                draggedPaper=null;
                if(paperDelivered && NotesDesk!=null && NotesDesk.IsVisible && !NotesDesk.KeepsStickyOpen)NotesDesk.Close();
                paperDelivered=false;
            }
        }
        internal bool IsPaperDrag(IDataObject payload)
        {
            return draggedPaper!=null && Data.Notes.Contains(draggedPaper) && payload.GetDataPresent(PaperFormat,false);
        }
        internal void GiveDraggedPaper(){GivePaper(draggedPaper,draggedTone);}

        internal string GivePaper(NoteItem note,int tone)
        {
            if(note==null || !Data.Notes.Contains(note) || String.IsNullOrWhiteSpace(note.Text))return null;
            var time=PaperTime.Read(note.Text,DateTime.Now);
            bool same=note.HeldByPet && String.Equals(note.PaperTimeKey,time.Key,StringComparison.Ordinal);
            if(time.Error!=null && !(same && time.Key!=null)){
                if(NotesDesk!=null)NotesDesk.ShowPaperStatus(time.Error);
                return time.Error;
            }
            bool keepLegacyTime=!note.HeldByPet && !note.Done && note.DueUtc>0 && time.Key==null;
            if(!same && !keepLegacyTime){
                note.DueUtc=time.Utc;note.Done=false;note.Notified=false;note.PaperTimeKey=time.Key;
            }
            if(!note.HeldByPet || note.PaperTone<0 || note.PaperTone>2)note.PaperTone=tone;
            note.HeldByPet=true;
            paperDelivered=draggedPaper!=null;
            Save();
            if(NotesDesk!=null) {
                NotesDesk.RefreshStickyCollection();
                if(draggedPaper==null && NotesDesk.IsVisible && !NotesDesk.KeepsStickyOpen)NotesDesk.Close();
            }
            RefreshReturnedPaper();
            Pet.PlayPaperAction(false);
            return null;
        }
        internal void SheetEdited(NoteItem note)
        {
            if(note==null || !note.HeldByPet)return;
            var time=PaperTime.Read(note.Text,DateTime.Now);
            if(note.PaperTimeKey!=time.Key){
                note.PaperTimeKey=time.Error==null?time.Key:null;
                note.DueUtc=time.Error==null?time.Utc:0;note.Notified=false;note.Done=false;
            }
            RefreshReturnedPaper();
        }
        internal NoteItem ReturnedPaper()
        {
            return Data.Notes.Where(n=>n.HeldByPet && n.Notified && !n.Done && n.DueUtc>0)
                .OrderBy(n=>n.DueUtc).FirstOrDefault();
        }
        internal void RefreshReturnedPaper()
        {
            if(Pet!=null)Pet.ShowReturnedPaper(ReturnedPaper());
        }
        internal bool TakeReturnedPaper()
        {
            var paper=ReturnedPaper();
            if(paper==null)return false;
            Open("notes"); NotesDesk.OpenPaper(paper);
            paper.HeldByPet=false;paper.Done=true;Save();
            NotesDesk.RefreshStickyCollection();RefreshReturnedPaper();
            return true;
        }
        internal void SnoozeReturnedPaper()
        {
            var paper=ReturnedPaper();
            if(paper==null)return;
            paper.DueUtc=DateTime.UtcNow.AddMinutes(10).Ticks;paper.Notified=false;
            Save();RefreshReturnedPaper();
            if(NotesDesk!=null)NotesDesk.RefreshStickyCollection();
        }
    }

    public sealed partial class PetWindow
    {
        Border returnCard;
        Button collectPaper;
        TextBlock paperPreview;
        NoteItem offeredPaper;

        void InstallReturnedPaper(Grid panel)
        {
            collectPaper=Ui.Button("收下便签",delegate{app.TakeReturnedPaper();});
            paperPreview=Ui.Text("",12,Ui.Ink);paperPreview.TextTrimming=TextTrimming.CharacterEllipsis;
            paperPreview.TextWrapping=TextWrapping.NoWrap;paperPreview.MaxWidth=110;
            collectPaper.Content=paperPreview;collectPaper.ToolTip="收下并打开原便签";
            var later=Ui.Button("10 分钟后",app.SnoozeReturnedPaper);
            later.ToolTip="10 分钟后再提醒";
            foreach(var b in new[]{collectPaper,later}){
                b.FontSize=11;b.Padding=new Thickness(5,4,5,4);b.Margin=new Thickness(0);
                b.MinHeight=24;b.BorderThickness=new Thickness(0);b.Background=System.Windows.Media.Brushes.Transparent;
            }
            AutomationProperties.SetAutomationId(collectPaper,"ReceivePaper");
            AutomationProperties.SetAutomationId(later,"SnoozePaper");
            var line=new StackPanel{Orientation=Orientation.Horizontal};line.Children.Add(collectPaper);line.Children.Add(later);
            returnCard=new Border{Child=line,Visibility=Visibility.Collapsed,Padding=new Thickness(3),
                HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(2,9,2,0),
                CornerRadius=new CornerRadius(4),BorderThickness=new Thickness(1),
                BorderBrush=SystemParameters.HighContrast?SystemColors.WindowTextBrush:Ui.Color("#BAA977")};
            Panel.SetZIndex(returnCard,8);panel.Children.Add(returnCard);
        }
        internal void ShowReturnedPaper(NoteItem note)
        {
            if(returnCard==null)return;
            bool changed=!Object.ReferenceEquals(offeredPaper,note);
            offeredPaper=note;
            if(note==null){returnCard.Visibility=Visibility.Collapsed;return;}
            paperPreview.Text=note.Text.Replace('\r',' ').Replace('\n',' ');
            AutomationProperties.SetName(collectPaper,"收下便签 · "+paperPreview.Text);
            returnCard.Background=StickyStock.Face(note.PaperTone);
            returnCard.Visibility=Visibility.Visible;
            if(changed)PlayPaperAction(true);
        }
    }
}
