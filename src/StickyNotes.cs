using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DeskMate
{
    public partial class SaveData { public bool StickyPinned; }

    // The turned corner is part of the paper silhouette. Its transparent button
    // has a larger target so that the corner also works on high-DPI displays.
    sealed class StickyStock : FrameworkElement
    {
        internal static readonly string[] Names={"黄色","蓝色","红色"};
        static readonly string[] Faces={"#FFF2B8","#E0EFF9","#FADBD8"};
        static readonly string[] Edges={"#C8B477","#A5BECF","#D0AAA6"};
        static readonly string[] Folds={"#E7CC75","#AFCDE1","#E4AAA5"};
        int tone;
        bool lit;
        internal void SetTone(int value){tone=value;InvalidateVisual();}
        internal void HighlightFold(bool value){lit=value;InvalidateVisual();}
        static StreamGeometry Polygon(params Point[] points)
        {
            var shape=new StreamGeometry();
            using(var ctx=shape.Open()){
                ctx.BeginFigure(points[0],true,true);
                for(int i=1;i<points.Length;i++)ctx.LineTo(points[i],true,false);
            }
            shape.Freeze();return shape;
        }
        protected override void OnRender(DrawingContext dc)
        {
            double x=1.5,y=1.5,r=ActualWidth-2.5,b=ActualHeight-3.5;
            const double fold=30;
            if(r<=fold+4 || b<=fold+4)return;
            bool contrast=SystemParameters.HighContrast;
            Brush stock=contrast?SystemColors.WindowBrush:Ui.Color(Faces[tone]);
            Brush edge=contrast?SystemColors.WindowTextBrush:Ui.Color(Edges[tone]);
            var pen=new Pen(edge,1);
            var face=Polygon(new Point(x,y),new Point(r-fold,y),new Point(r,y+fold),new Point(r,b),new Point(x,b));
            if(!contrast){
                dc.PushTransform(new TranslateTransform(.8,1.5));dc.DrawGeometry(Ui.Color("#374A3F2F"),null,face);dc.Pop();
            }
            dc.DrawGeometry(stock,pen,face);
            if(!contrast){
                var rule=new Pen(edge,.65);
                dc.PushOpacity(.4);
                for(double line=68.5;line<=b-34;line+=25)dc.DrawLine(rule,new Point(x+18,line),new Point(r-18,line));
                dc.Pop();
            }
            var turn=Polygon(new Point(r-fold,y),new Point(r,y+fold),new Point(r-fold+1.5,y+fold-2));
            if(!contrast){dc.PushTransform(new TranslateTransform(-.7,1));dc.DrawGeometry(Ui.Color("#29463E30"),null,turn);dc.Pop();}
            dc.DrawGeometry(contrast?SystemColors.ControlBrush:Ui.Color(Folds[tone]),pen,turn);
            if(lit && !contrast){dc.PushOpacity(.26);dc.DrawGeometry(Brushes.White,null,turn);dc.Pop();}
        }
    }

    public partial class StudioWindow
    {
        const int StickyCapacity=1500;
        const int StickyTextLimit=2048;
        readonly Random stickyRandom=new Random();
        Grid stickySurface;
        StickyStock stickyStock;
        TextBox stickyEditor;
        TextBlock stickyHint,stickyCounter,stickyStatus;
        Button stickyPin,stickyTrash,stickyUndo;
        Path pinCap;
        DispatcherTimer stickySave;
        NoteItem stickyNote,deletedNote;
        int stickySlot,deletedSlot,stickyTone,knownNoteCount;
        bool loadingSticky;
        internal bool KeepsStickyOpen { get { return page=="notes" && app.Data.StickyPinned; } }

        static Button PaperAction(string id,string name,Action invoke)
        {
            var button=Ui.Button(name,invoke);
            button.Margin=new Thickness(0);button.Padding=new Thickness(0);button.MinHeight=0;
            button.BorderBrush=Brushes.Transparent;button.BorderThickness=new Thickness(0);
            button.Background=Brushes.Transparent;button.ToolTip=name;
            AutomationProperties.SetAutomationId(button,id);AutomationProperties.SetName(button,name);
            return button;
        }
        static Path PaperGlyph(string path,double width,double height)
        {
            return new Path{Data=Geometry.Parse(path),Width=width,Height=height,Stretch=Stretch.Uniform,
                Stroke=SystemParameters.HighContrast?SystemColors.WindowTextBrush:Ui.Color("#514F47"),StrokeThickness=1.65,
                StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round};
        }
        Grid StickyPage()
        {
            if(stickySurface!=null){RefreshStickyCollection();return stickySurface;}
            stickySave=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(450) };
            stickySave.Tick+=delegate{FlushStickySave();};
            stickySurface=new Grid{Height=244,Margin=new Thickness(4,3,4,4)};
            AutomationProperties.SetAutomationId(stickySurface,"StickyPaper");
            stickyStock=new StickyStock{IsHitTestVisible=false};
            stickySurface.Children.Add(stickyStock);

            stickyPin=PaperAction("StickyPin","置顶便签",ToggleStickyPin);
            pinCap=PaperGlyph("M7,2 L17,2 L15.3,5.2 L15.3,10 L18.3,13.5 L18.3,14.5 L5.7,14.5 L5.7,13.5 L8.7,10 L8.7,5.2 Z M12,14.5 L12,21",16,21);
            pinCap.RenderTransformOrigin=new Point(.5,.5);
            stickyPin.Content=pinCap;stickyPin.Width=34;stickyPin.Height=34;stickyPin.Margin=new Thickness(8,5,0,0);
            stickyPin.HorizontalAlignment=HorizontalAlignment.Left;stickyPin.VerticalAlignment=VerticalAlignment.Top;
            stickySurface.Children.Add(stickyPin);
            var fold=PaperAction("StickyFold","翻到下一张（Shift + 点击翻回上一张）",delegate{TurnSticky(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?-1:1);});
            fold.Content=null;fold.Width=42;fold.Height=42;fold.HorizontalAlignment=HorizontalAlignment.Right;fold.VerticalAlignment=VerticalAlignment.Top;
            fold.MouseEnter+=delegate{stickyStock.HighlightFold(true);};fold.MouseLeave+=delegate{stickyStock.HighlightFold(false);};
            stickySurface.Children.Add(fold);

            var writing=new Grid{Margin=new Thickness(20,45,19,38)};
            var textHost=new FrameworkElementFactory(typeof(ScrollViewer),"PART_ContentHost");
            textHost.SetValue(Control.BackgroundProperty,Brushes.Transparent);
            textHost.SetValue(UIElement.FocusableProperty,false);
            stickyEditor=new TextBox {AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,FontSize=16,
                VerticalContentAlignment=VerticalAlignment.Top,Foreground=SystemParameters.HighContrast?SystemColors.WindowTextBrush:Ui.Color("#383B38"),
                Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),
                VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
                Template=new ControlTemplate(typeof(TextBox)){VisualTree=textHost} };
            TextBlock.SetLineHeight(stickyEditor,25);
            TextBlock.SetLineStackingStrategy(stickyEditor,LineStackingStrategy.BlockLineHeight);
            AutomationProperties.SetAutomationId(stickyEditor,"StickyEditor");AutomationProperties.SetName(stickyEditor,"便签内容");
            stickyEditor.TextChanged+=delegate{EditSticky();};
            stickyEditor.PreviewTextInput+=delegate(object sender,TextCompositionEventArgs e){
                if(!FitsSticky(e.Text))e.Handled=true;
            };
            DataObject.AddPastingHandler(stickyEditor,delegate(object sender,DataObjectPastingEventArgs e){
                var pasted=e.DataObject.GetData(DataFormats.UnicodeText) as string;
                if(pasted!=null && !FitsSticky(pasted))e.CancelCommand();
            });
            stickyEditor.PreviewDrop+=delegate(object sender,DragEventArgs e){
                var text=e.Data.GetData(DataFormats.UnicodeText) as string;
                if(text!=null && !FitsSticky(text)){e.Effects=DragDropEffects.None;e.Handled=true;}
            };
            writing.Children.Add(stickyEditor);
            stickyHint=Ui.Text("写下便签…",16,Ui.Muted);
            stickyHint.VerticalAlignment=VerticalAlignment.Top;stickyHint.Margin=new Thickness(2,2,0,0);
            stickyHint.Opacity=.8;stickyHint.IsHitTestVisible=false;writing.Children.Add(stickyHint);
            stickySurface.Children.Add(writing);

            var foot=new Grid{Height=30,Margin=new Thickness(19,0,8,6),VerticalAlignment=VerticalAlignment.Bottom};
            foot.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            foot.ColumnDefinitions.Add(new ColumnDefinition());
            foot.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            foot.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            stickyCounter=Ui.Text("",11,Ui.Muted);stickyCounter.FontFamily=new FontFamily("Consolas, Segoe UI");
            AutomationProperties.SetAutomationId(stickyCounter,"StickyPage");foot.Children.Add(stickyCounter);
            stickyStatus=Ui.Text("",11,Ui.Muted);stickyStatus.Margin=new Thickness(10,0,0,0);
            stickyStatus.TextTrimming=TextTrimming.CharacterEllipsis;stickyStatus.TextWrapping=TextWrapping.NoWrap;
            Grid.SetColumn(stickyStatus,1);foot.Children.Add(stickyStatus);
            stickyUndo=PaperAction("StickyUndo","撤销删除",RestoreSticky);stickyUndo.Content="撤销删除";
            stickyUndo.FontSize=11;stickyUndo.Padding=new Thickness(4,0,4,0);
            Grid.SetColumn(stickyUndo,2);foot.Children.Add(stickyUndo);
            stickyTrash=PaperAction("StickyTrash","删除这张便签",DeleteSticky);stickyTrash.Width=32;stickyTrash.Height=28;
            stickyTrash.Content=PaperGlyph("M3,5 L17,5 M8,2.4 L12,2.4 L13,5 M5,5 L5.8,18 L14.2,18 L15,5 M8.4,8 L8.7,15 M11.6,8 L11.3,15",15,17);
            Grid.SetColumn(stickyTrash,3);foot.Children.Add(stickyTrash);stickySurface.Children.Add(foot);
            stickySurface.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){
                if(Keyboard.Modifiers!=ModifierKeys.Control || (e.Key!=Key.PageDown && e.Key!=Key.PageUp))return;
                TurnSticky(e.Key==Key.PageDown?1:-1);e.Handled=true;
            };
            IsVisibleChanged+=delegate{
                if(IsVisible && page=="notes")Dispatcher.BeginInvoke(DispatcherPriority.Input,(Action)delegate{
                    if(IsVisible && IsActive && page=="notes")stickyEditor.Focus();
                });
            };
            SelectSticky(0,false);
            return stickySurface;
        }

        bool FitsSticky(string addition)
        {
            // Keep a large legacy note editable and reject an over-limit paste
            // as a whole instead of letting TextBox.MaxLength silently clip it.
            int limit=Math.Max(StickyTextLimit,stickyEditor.Text.Length);
            if((long)stickyEditor.Text.Length-stickyEditor.SelectionLength+addition.Length<=limit)return true;
            stickyStatus.Text="每张最多 "+limit+" 个字符";stickyStatus.ToolTip=stickyStatus.Text;return false;
        }
        void EditSticky()
        {
            if(loadingSticky)return;
            var notes=app.Data.Notes;
            string value=stickyEditor.Text;
            int index=stickyNote==null?-1:notes.IndexOf(stickyNote);
            // A pet drop can claim the last slot after an existing sheet was
            // cleared. Enforce the limit here too in case an edit was queued.
            if(index<0 && notes.Count>=StickyCapacity && !String.IsNullOrWhiteSpace(value)){
                loadingSticky=true;
                try{stickyEditor.Text=stickyNote==null?"":stickyNote.Text;}
                finally{loadingSticky=false;}
                UpdateStickyDetails();return;
            }
            if(String.IsNullOrWhiteSpace(value)){
                if(index>=0){stickySlot=index;notes.RemoveAt(index);}
                if(stickyNote!=null)stickyNote.Text=value;
            }else{
                if(stickyNote==null){stickySlot=notes.Count;stickyNote=new NoteItem();}
                if(index<0)notes.Insert(Math.Min(stickySlot,notes.Count),stickyNote);
                stickyNote.Text=value;
            }
            UpdateStickyDetails();
            stickySave.Stop();stickySave.Start();
        }
        void FlushStickySave()
        {
            if(stickySave==null || !stickySave.IsEnabled)return;
            stickySave.Stop();app.Save();
        }
        void SelectSticky(int index,bool turn)
        {
            var notes=app.Data.Notes;
            stickySlot=Math.Max(0,Math.Min(index,notes.Count));
            stickyNote=stickySlot<notes.Count?notes[stickySlot]:null;
            if(turn)stickyTone=(stickyTone+1+stickyRandom.Next(2))%3;
            stickyStock.SetTone(stickyTone);
            AutomationProperties.SetName(stickySurface,StickyStock.Names[stickyTone]+"便签");
            loadingSticky=true;
            try{
                stickyEditor.IsUndoEnabled=false;
                stickyEditor.Text=stickyNote==null?"":stickyNote.Text;
                stickyEditor.MaxLength=Math.Max(StickyTextLimit,stickyEditor.Text.Length);
                stickyEditor.CaretIndex=0;stickyEditor.ScrollToHome();
            }finally{stickyEditor.IsUndoEnabled=true;loadingSticky=false;}
            UpdateStickyDetails();
        }
        void UpdateStickyDetails()
        {
            var notes=app.Data.Notes;
            int index=stickyNote==null?-1:notes.IndexOf(stickyNote);
            int total=notes.Count+(notes.Count<StickyCapacity?1:0);
            knownNoteCount=notes.Count;
            stickyCounter.Text=Math.Min(total,index<0?notes.Count+1:index+1)+" / "+total;
            stickyHint.Visibility=stickyEditor.Text.Length==0?Visibility.Visible:Visibility.Collapsed;
            stickyEditor.IsReadOnly=index<0 && notes.Count>=StickyCapacity;
            stickyTrash.IsEnabled=index>=0;
            stickyUndo.Visibility=deletedNote!=null && notes.Count<StickyCapacity?Visibility.Visible:Visibility.Collapsed;
            stickyStatus.Text="";
            if(stickyEditor.IsReadOnly)stickyStatus.Text="便签已满";
            else if(index>=0 && stickyNote.DueUtc>0){
                DateTime due=new DateTime(stickyNote.DueUtc,DateTimeKind.Utc).ToLocalTime();
                stickyStatus.Text=(stickyNote.Done?"已完成":stickyNote.Notified?"已提醒":"提醒")+" · "+due.ToString("M/d HH:mm");
            }
            else if(index>=0 && stickyNote.Done)stickyStatus.Text="已完成";
            stickyStatus.ToolTip=stickyStatus.Text;
            bool pinned=app.Data.StickyPinned;
            pinCap.Fill=SystemParameters.HighContrast?(pinned?SystemColors.HighlightBrush:SystemColors.WindowBrush):Ui.Color(pinned?"#A45D4E":"#FBF8E8");
            pinCap.RenderTransform=new RotateTransform(pinned?9:31);
            string name=pinned?"取消置顶":"置顶便签";
            stickyPin.ToolTip=name;AutomationProperties.SetName(stickyPin,name);
        }
        void TurnSticky(int direction)
        {
            FlushStickySave();
            var notes=app.Data.Notes;
            int index=stickyNote==null?-1:notes.IndexOf(stickyNote);
            int total=notes.Count+(notes.Count<StickyCapacity?1:0);
            int current=index<0?total-1:index;
            SelectSticky((current+direction+total)%total,true);
        }
        void ToggleStickyPin()
        {
            app.Data.StickyPinned=!app.Data.StickyPinned;
            Topmost=app.Data.StickyPinned;stickySave.Stop();app.Save();
            UpdateStickyDetails();app.Pet.UpdateFlyoutDismissal();
        }
        void DeleteSticky()
        {
            int index=app.Data.Notes.IndexOf(stickyNote);
            if(index<0)return;
            deletedNote=stickyNote;deletedSlot=index;
            app.Data.Notes.RemoveAt(index);stickySave.Stop();app.Save();
            SelectSticky(Math.Min(index,Math.Max(0,app.Data.Notes.Count-1)),true);
        }
        void RestoreSticky()
        {
            if(deletedNote==null || app.Data.Notes.Count>=StickyCapacity)return;
            int index=Math.Min(deletedSlot,app.Data.Notes.Count);
            app.Data.Notes.Insert(index,deletedNote);deletedNote=null;
            stickySave.Stop();app.Save();SelectSticky(index,true);
        }
        public void RefreshStickyCollection()
        {
            if(stickySurface==null)return;
            if(stickyNote==null && stickyEditor.Text.Length==0 && app.Data.Notes.Count>knownNoteCount &&
                (!stickyEditor.IsKeyboardFocusWithin || app.Data.Notes.Count>=StickyCapacity))
                SelectSticky(0,true);
            else UpdateStickyDetails();
        }
    }
}
