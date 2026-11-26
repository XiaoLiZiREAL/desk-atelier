using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DeskMate
{
    sealed class PaperSheet : FrameworkElement
    {
        internal static readonly string[] Names={"黄色","蓝色","红色"};
        static readonly string[] Backgrounds={"#FFF2B4","#DDEFFC","#FBDDDC"};
        static readonly string[] Folds={"#EAD06D","#A4C9E7","#E9AAA9"};
        static readonly string[] Edges={"#D4BD6C","#A3BFD5","#D2A19F"};
        int tone;
        internal void SetTone(int value) {tone=value;InvalidateVisual();}

        static StreamGeometry Polygon(params Point[] corners)
        {
            var shape=new StreamGeometry();
            using(var context=shape.Open()){
                context.BeginFigure(corners[0],true,true);
                for(int i=1;i<corners.Length;i++)context.LineTo(corners[i],true,false);
            }
            shape.Freeze();return shape;
        }
        protected override void OnRender(DrawingContext dc)
        {
            double right=ActualWidth-3,bottom=ActualHeight-5;
            if(right<30 || bottom<30)return;
            bool contrast=SystemParameters.HighContrast;
            Brush paper=contrast?SystemColors.WindowBrush:Ui.Color(Backgrounds[tone]);
            Brush edge=contrast?SystemColors.WindowTextBrush:Ui.Color(Edges[tone]);
            var outline=new Pen(edge,1);
            dc.DrawRoundedRectangle(contrast?SystemColors.WindowBrush:Ui.Color("#F1F2EE"),outline,new Rect(4,5,right-4,bottom-1),2,2);
            const int fold=22;
            dc.DrawGeometry(paper,outline,Polygon(new Point(1,1),new Point(right-fold,1),new Point(right,fold+1),
                new Point(right,bottom),new Point(1,bottom)));
            if(!contrast){
                var ruled=new Pen(edge,.55);dc.PushOpacity(.48);
                for(double y=79;y<bottom-10;y+=26)dc.DrawLine(ruled,new Point(15,y),new Point(right-15,y));
                dc.Pop();
            }
            dc.DrawGeometry(contrast?SystemColors.ControlBrush:Ui.Color(Folds[tone]),outline,
                Polygon(new Point(right-fold,1),new Point(right,fold+1),new Point(right-fold,fold+1)));
        }
    }

    public partial class StudioWindow
    {
        readonly Random paperRandom=new Random();
        int journalIndex, journalTone;
        MemoryLine journalEntry,journalHead;
        Grid journalSheet;
        PaperSheet paperStock;
        TextBlock paperDate,paperTime,paperText,paperCounter;
        ScrollViewer paperScroll;
        Button newerPage,olderPage;

        void ResetJournalReader(){journalIndex=0;journalEntry=null;}

        UIElement JournalPage()
        {
            journalSheet=new Grid {Height=207};
            AutomationProperties.SetAutomationId(journalSheet,"journal-sheet");
            paperStock=new PaperSheet {IsHitTestVisible=false};journalSheet.Children.Add(paperStock);
            var ink=SystemParameters.HighContrast?SystemColors.WindowTextBrush:Ui.Color("#343635");
            var inner=new Grid {Margin=new Thickness(17,14,22,19)};
            inner.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});inner.RowDefinitions.Add(new RowDefinition());
            paperDate=Ui.Heading("",14);paperDate.Foreground=ink;
            paperTime=Ui.Text("",12,Ui.Muted);paperTime.Foreground=ink;
            var dateLine=new DockPanel{Margin=new Thickness(0,0,12,11)};
            DockPanel.SetDock(paperTime,Dock.Right);dateLine.Children.Add(paperTime);dateLine.Children.Add(paperDate);
            inner.Children.Add(dateLine);
            paperText=Ui.Text("",16,Ui.Ink);paperText.Foreground=ink;paperText.VerticalAlignment=VerticalAlignment.Top;
            paperText.LineHeight=26;paperText.LineStackingStrategy=LineStackingStrategy.BlockLineHeight;
            AutomationProperties.SetAutomationId(paperText,"journal-entry");
            paperScroll=new ScrollViewer{Content=paperText,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
            Grid.SetRow(paperScroll,1);inner.Children.Add(paperScroll);journalSheet.Children.Add(inner);
            newerPage=Ui.Button("上一页",delegate{TurnJournal(-1);});olderPage=Ui.Button("下一页",delegate{TurnJournal(1);});
            newerPage.Margin=olderPage.Margin=new Thickness(0);
            AutomationProperties.SetHelpText(newerPage,"查看较新的记录");AutomationProperties.SetHelpText(olderPage,"查看较早的记录");
            paperCounter=Ui.Text("",13,Ui.Muted);paperCounter.HorizontalAlignment=HorizontalAlignment.Center;
            AutomationProperties.SetAutomationId(paperCounter,"journal-position");
            var pager=new DockPanel {Margin=new Thickness(1,9,2,1)};
            DockPanel.SetDock(newerPage,Dock.Left);DockPanel.SetDock(olderPage,Dock.Right);
            pager.Children.Add(newerPage);pager.Children.Add(olderPage);pager.Children.Add(paperCounter);
            UpdateJournal(true);
            return Ui.Stack(journalSheet,pager);
        }

        void UpdateJournal(bool rebind)
        {
            var entries=app.Data.Journal;
            journalHead=entries.FirstOrDefault();displayedJournalCount=entries.Count;
            int index=journalEntry==null?-1:entries.IndexOf(journalEntry);
            journalIndex=index>=0?index:Math.Max(0,Math.Min(journalIndex,entries.Count-1));
            MemoryLine visible=entries.Count==0?null:entries[journalIndex];
            if(rebind || visible!=journalEntry){
                journalEntry=visible;
                if(visible==null){paperDate.Text="还没有手记";paperTime.Text="";paperText.Text="伙伴的日常和你们的互动会记在这里。";}
                else {
                    DateTime time=new DateTime(visible.Time,DateTimeKind.Utc).ToLocalTime();
                    paperDate.Text=time.ToString("yyyy 年 M 月 d 日",CultureInfo.InvariantCulture);
                    paperTime.Text=time.ToString("HH:mm",CultureInfo.InvariantCulture);paperText.Text=visible.Text;
                }
                paperScroll.ScrollToTop();
            }
            string color=PaperSheet.Names[journalTone];paperStock.SetTone(journalTone);
            journalSheet.Tag=color;AutomationProperties.SetName(journalSheet,color+"纸签");
            paperCounter.Text=entries.Count==0?"0 / 0":(journalIndex+1)+" / "+entries.Count;
            newerPage.IsEnabled=journalIndex>0;olderPage.IsEnabled=journalIndex+1<entries.Count;
        }

        void TurnJournal(int direction)
        {
            UpdateJournal(false);
            int target=journalIndex+direction;
            if(target<0 || target>=app.Data.Journal.Count)return;
            journalIndex=target;journalEntry=app.Data.Journal[target];
            journalTone=(journalTone+1+paperRandom.Next(2))%PaperSheet.Names.Length;
            UpdateJournal(true);
            if(!SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast)return;
            var turn=new ScaleTransform(1,1);journalSheet.RenderTransformOrigin=new Point(direction>0?0:1,.5);
            journalSheet.RenderTransform=turn;
            turn.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.92,1,TimeSpan.FromMilliseconds(145)){
                EasingFunction=new QuadraticEase{EasingMode=EasingMode.EaseOut}});
        }
    }
}
