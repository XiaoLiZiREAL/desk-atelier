using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskMate
{
    public partial class StudioWindow
    {
        Border paperTimePicker;

        void ClosePaperTimePicker()
        {
            if(paperTimePicker==null)return;
            stickySurface.Children.Remove(paperTimePicker);
            paperTimePicker=null;
        }

        internal void ChoosePaperTime(string reason)
        {
            if(stickyNote==null || !app.Data.Notes.Contains(stickyNote))return;
            ClosePaperTimePicker();
            var note=stickyNote;
            DateTime now=DateTime.Now;
            var parsed=PaperTime.Read(note.Text,now);
            long ticks=!note.Done && note.DueUtc>0?note.DueUtc:parsed.Error==null?parsed.Utc:0;
            DateTime initial=ticks>now.ToUniversalTime().Ticks?new DateTime(ticks,DateTimeKind.Utc).ToLocalTime():now.AddMinutes(30);
            var body=new StackPanel();
            var heading=Ui.Text(String.IsNullOrEmpty(reason)?"提醒时间":reason,13,Ui.Ink);
            heading.TextWrapping=TextWrapping.Wrap;
            body.Children.Add(heading);
            var pair=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,8)};
            var date=new TextBox{Text=initial.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture),Width=110,MaxLength=10};
            var time=new TextBox{Text=initial.ToString("HH:mm",CultureInfo.InvariantCulture),Width=64,MaxLength=5,Margin=new Thickness(6,0,0,0)};
            AutomationProperties.SetAutomationId(date,"PaperDate");AutomationProperties.SetName(date,"提醒日期，年-月-日");
            AutomationProperties.SetAutomationId(time,"PaperClock");AutomationProperties.SetName(time,"提醒时间，24 小时制");
            date.ToolTip="年-月-日";time.ToolTip="小时:分钟";
            pair.Children.Add(date);pair.Children.Add(time);body.Children.Add(pair);
            Action confirm=delegate{
                DateTime chosen;
                string input=date.Text.Trim()+" "+time.Text.Trim().Replace('：',':');
                bool valid=DateTime.TryParseExact(input,"yyyy-M-d H:m",CultureInfo.InvariantCulture,DateTimeStyles.None,out chosen);
                if(valid && !note.Done && note.DueUtc==ticks && ticks>0 &&
                    chosen.Ticks/TimeSpan.TicksPerMinute==initial.Ticks/TimeSpan.TicksPerMinute &&
                    note.DueUtc> DateTime.UtcNow.Ticks)chosen=new DateTime(ticks,DateTimeKind.Utc).ToLocalTime();
                if(!valid || chosen<=DateTime.Now || TimeZoneInfo.Local.IsInvalidTime(chosen)){
                    heading.Text="请输入有效的未来日期和时间";return;
                }
                ClosePaperTimePicker();
                app.GivePaperWithTime(note,stickyTone,DateTime.SpecifyKind(chosen,DateTimeKind.Local));
            };
            var actions=new StackPanel{Orientation=Orientation.Horizontal};
            actions.Children.Add(TimeAction("ConfirmPaperTime","定时递回",confirm));
            actions.Children.Add(TimeAction("KeepPaperOnly","只保管",delegate{ClosePaperTimePicker();app.GivePaperWithTime(note,stickyTone,null);}));
            actions.Children.Add(TimeAction("CancelPaperTime","取消",delegate{ClosePaperTimePicker();stickyEditor.Focus();}));
            body.Children.Add(actions);
            paperTimePicker=new Border{Child=body,Background=StickyStock.Face(stickyTone),
                BorderBrush=SystemParameters.HighContrast?SystemColors.WindowTextBrush:Ui.Color("#A69D80"),
                BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(5),Padding=new Thickness(12),
                VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(13,0,13,0)};
            paperTimePicker.PreviewKeyDown+=delegate(object sender,KeyEventArgs e){
                if(e.Key==Key.Enter && (date.IsKeyboardFocused || time.IsKeyboardFocused)){e.Handled=true;confirm();}
                else if(e.Key==Key.Escape){e.Handled=true;ClosePaperTimePicker();stickyEditor.Focus();}
            };
            stickySurface.Children.Add(paperTimePicker);
            time.Focus();time.SelectAll();
        }

        static Button TimeAction(string id,string label,Action action)
        {
            var button=Ui.Button(label,action);
            button.Padding=new Thickness(6,3,6,3);button.Margin=new Thickness(0,0,4,0);
            button.MinHeight=25;button.FontSize=12;
            AutomationProperties.SetAutomationId(button,id);
            return button;
        }
    }
}
