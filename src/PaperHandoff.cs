using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace DeskMate
{
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

        internal const string ChosenPaperTime="manual";
        internal string GivePaper(NoteItem note,int tone)
        {
            if(note==null || !Data.Notes.Contains(note) || String.IsNullOrWhiteSpace(note.Text))return null;
            if(note.HeldByPet && note.PaperTimeKey==ChosenPaperTime){
                FinishHandoff(note,tone);return null;
            }
            var time=PaperTime.Read(note.Text,DateTime.Now);
            bool same=note.HeldByPet && String.Equals(note.PaperTimeKey,time.Key,StringComparison.Ordinal);
            if(time.Error!=null && !(same && time.Key!=null)){
                if(NotesDesk!=null)NotesDesk.ChoosePaperTime(time.Error);
                return time.Error;
            }
            bool keepLegacyTime=!note.HeldByPet && !note.Done && note.DueUtc>0 && time.Key==null;
            if(!same && !keepLegacyTime){
                note.DueUtc=time.Utc;note.Done=false;note.Notified=false;note.PaperTimeKey=time.Key;note.WrittenTimeAtChoice=null;
            }
            FinishHandoff(note,tone);
            return null;
        }
        internal void GivePaperWithTime(NoteItem note,int tone,DateTime? local)
        {
            if(note==null || !Data.Notes.Contains(note) || String.IsNullOrWhiteSpace(note.Text))return;
            note.DueUtc=local.HasValue?local.Value.ToUniversalTime().Ticks:0;
            note.WrittenTimeAtChoice=PaperTime.Read(note.Text,DateTime.Now).Key;
            note.PaperTimeKey=ChosenPaperTime;note.Notified=false;note.Done=false;
            FinishHandoff(note,tone);
        }
        void FinishHandoff(NoteItem note,int tone)
        {
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
        }
        internal void SheetEdited(NoteItem note)
        {
            if(note==null || !note.HeldByPet)return;
            var time=PaperTime.Read(note.Text,DateTime.Now);
            if(note.PaperTimeKey==ChosenPaperTime){
                if(time.Error!=null || time.Key==null || note.WrittenTimeAtChoice==time.Key)return;
                note.WrittenTimeAtChoice=null;
            }
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
