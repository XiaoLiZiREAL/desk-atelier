using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DeskMate
{
    // Each revolution is one hour. Accumulating angle deltas lets the user turn
    // through twelve o'clock in either direction without jumping back an hour.
    sealed class FocusDial : RangeBase
    {
        const double Diameter = 192, Center = Diameter / 2, RingRadius = 78;
        const double DegreesPerMinute = 6;
        double previousAngle, dragMinutes, travel, progress;
        bool validAngle, dragged;
        int wheelRemainder;
        readonly bool paused;

        public FocusDial(double minutes, bool editing, bool isPaused)
        {
            Width = Height = Diameter;
            Minimum = 1; Maximum = 180; SmallChange = 1; LargeChange = 5;
            Value = minutes;
            IsEnabled = editing;
            Focusable = editing;
            FocusVisualStyle = null;
            paused = isPaused;
            Cursor = editing ? Cursors.Hand : Cursors.Arrow;
            AutomationProperties.SetAutomationId(this, "FocusDurationDial");
            AutomationProperties.SetName(this, "专注时长，分钟");
            if (editing)
            {
                const string help = "沿圆环拖动，每圈 60 分钟。也可用滚轮或方向键微调。";
                AutomationProperties.SetHelpText(this, help);
                ToolTip = help;
            }
            IsKeyboardFocusedChanged += delegate { InvalidateVisual(); };
        }

        protected override AutomationPeer OnCreateAutomationPeer() { return new DialPeer(this); }
        sealed class DialPeer : RangeBaseAutomationPeer
        {
            internal DialPeer(FocusDial owner) : base(owner) { }
            protected override string GetClassNameCore() { return "FocusDial"; }
            protected override AutomationControlType GetAutomationControlTypeCore() { return AutomationControlType.Slider; }
        }

        internal void SetProgress(double remaining)
        {
            remaining = Math.Max(0, Math.Min(1, remaining));
            if (remaining == progress) return;
            progress = remaining;
            InvalidateVisual();
        }

        protected override void OnValueChanged(double oldValue, double newValue)
        {
            base.OnValueChanged(oldValue,newValue);
            InvalidateVisual();
        }

        static bool TryAngle(Point point, out double degrees)
        {
            double x = point.X - Center, y = point.Y - Center;
            degrees = Math.Atan2(x,-y) * 180 / Math.PI;
            if (degrees < 0) degrees += 360;
            return x*x + y*y >= 36*36;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (!IsEnabled || !TryAngle(e.GetPosition(this), out previousAngle)) return;
            Focus();
            if (!CaptureMouse()) return;
            dragMinutes = Value;
            travel = 0;
            validAngle = true;
            dragged = false;
            e.Handled = true;
        }

        void DragTo(Point position)
        {
            double angle;
            if (!TryAngle(position, out angle)) { validAngle=false; dragged=true; return; }
            if (validAngle)
            {
                double delta = (angle - previousAngle + 540) % 360 - 180;
                travel += Math.Abs(delta);
                dragMinutes = Math.Max(Minimum,Math.Min(Maximum,dragMinutes + delta / DegreesPerMinute));
                if (travel >= 3) dragged = true;
                if (dragged) Value = Math.Round(dragMinutes,MidpointRounding.AwayFromZero);
            }
            previousAngle = angle;
            validAngle = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!IsMouseCaptured) return;
            if (e.LeftButton != MouseButtonState.Pressed) { ReleaseMouseCapture(); return; }
            DragTo(e.GetPosition(this));
            e.Handled = true;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!IsMouseCaptured) return;
            DragTo(e.GetPosition(this));
            if (!dragged && validAngle)
            {
                int tick = (int)Math.Round(previousAngle / DegreesPerMinute);
                int hour = ((int)Value - 1) / 60;
                Value = hour*60 + (tick == 0 ? 60 : tick);
            }
            ReleaseMouseCapture();
            e.Handled = true;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            wheelRemainder += e.Delta;
            int steps = wheelRemainder / Mouse.MouseWheelDeltaForOneLine;
            wheelRemainder %= Mouse.MouseWheelDeltaForOneLine;
            Value = Math.Max(Minimum, Math.Min(Maximum, Value + steps));
            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            double target;
            switch (e.Key)
            {
                case Key.Right: case Key.Up: target = Value + SmallChange; break;
                case Key.Left: case Key.Down: target = Value - SmallChange; break;
                case Key.PageUp: target = Value + LargeChange; break;
                case Key.PageDown: target = Value - LargeChange; break;
                case Key.Home: target = Minimum; break;
                case Key.End: target = Maximum; break;
                default: return;
            }
            Value = Math.Max(Minimum, Math.Min(Maximum, target));
            e.Handled = true;
        }

        static Point OnRing(double fraction, double radius)
        {
            double angle = fraction * 2 * Math.PI;
            return new Point(Center + Math.Sin(angle)*radius, Center - Math.Cos(angle)*radius);
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            bool hc = SystemParameters.HighContrast;
            var ink = hc ? SystemColors.WindowTextBrush : Ui.Color("#8096AC");
            var quiet = hc ? SystemColors.WindowTextBrush : Ui.Color("#BBC9D6");
            var accent = hc ? SystemColors.HighlightBrush : Ui.Color(paused ? "#788898" : Ui.Accent);
            var track = hc ? SystemColors.WindowTextBrush : Ui.Color("#E4ECF3");
            var center = new Point(Center,Center);
            dc.DrawEllipse(Brushes.Transparent, null, center, Center, Center);
            if (IsKeyboardFocused) dc.DrawEllipse(null,new Pen(accent,1.5),center,Center-1,Center-1);
            var major = new Pen(ink,1.4);
            var minor = new Pen(quiet,1);
            for (int tick=0; tick<60; tick++)
            {
                bool strong=tick%5==0;
                dc.DrawLine(strong?major:minor, OnRing(tick/60.0,strong?86:89), OnRing(tick/60.0,92));
            }
            dc.DrawEllipse(hc ? SystemColors.WindowBrush : Ui.Color("#F6F9FC"), new Pen(track,6), center, RingRadius, RingRadius);
            double arc = IsEnabled ? ((Math.Round(Value)-1)%60+1)/60 : progress;
            var stroke = new Pen(accent,6) {StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round};
            if (arc >= 1) dc.DrawEllipse(null,stroke,center,RingRadius,RingRadius);
            else if (arc > 0)
            {
                var shape = new StreamGeometry();
                using (var path=shape.Open())
                {
                    path.BeginFigure(OnRing(0,RingRadius),false,false);
                    path.ArcTo(OnRing(arc,RingRadius),new Size(RingRadius,RingRadius),0,arc>.5,SweepDirection.Clockwise,true,false);
                }
                dc.DrawGeometry(null,stroke,shape);
            }
            if (IsEnabled)
            {
                var knob=OnRing(arc,RingRadius);
                dc.DrawEllipse(hc ? SystemColors.WindowBrush : Brushes.White,new Pen(accent,2.5),knob,6.8,6.8);
                dc.DrawEllipse(accent,null,knob,2.2,2.2);
            }
        }
    }

    public partial class StudioWindow
    {
        TextBlock focusClock;
        FocusDial focusDial;
        int previewMinutes, shownFocusMode;
        int FocusMode { get { return app.FocusPaused ? 2 : app.FocusActive ? 1 : 0; } }

        public UIElement FocusPage()
        {
            shownFocusMode = FocusMode;
            bool editing = !app.FocusActive;
            if (!editing || previewMinutes < 1) previewMinutes = app.Data.PlannedMinutes;
            focusDial = new FocusDial(previewMinutes, editing, app.FocusPaused);
            var state = Ui.Text(editing ? "专注时长" : app.FocusPaused ? "已暂停" : "专注中",12,Ui.Muted);
            state.HorizontalAlignment = HorizontalAlignment.Center;
            state.FontWeight = FontWeights.Medium;
            focusClock = Ui.Text("",35,Ui.Ink);
            focusClock.FontWeight=FontWeights.SemiBold;
            focusClock.FontFamily = new FontFamily("Segoe UI Variable Display, Segoe UI");
            Typography.SetNumeralAlignment(focusClock,FontNumeralAlignment.Tabular);
            focusClock.HorizontalAlignment = HorizontalAlignment.Center;
            var digits = Ui.Stack(state,focusClock);
            digits.IsHitTestVisible = false;
            digits.VerticalAlignment=VerticalAlignment.Center;
            var face = new Grid {HorizontalAlignment=HorizontalAlignment.Center};
            face.Children.Add(focusDial); face.Children.Add(digits);
            focusDial.ValueChanged += delegate { previewMinutes=(int)Math.Round(focusDial.Value);PaintFocusClock(); };
            PaintFocusClock();
            var hint = Ui.Text(editing ? "沿圆环拖动 · 每圈 60 分钟" : "本轮共 " + app.Data.PlannedMinutes + " 分钟",12,Ui.Muted);
            hint.Margin = new Thickness(0,5,0,13);
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            var actions = new Grid {Width=216,HorizontalAlignment=HorizontalAlignment.Center};
            if (editing)
                actions.Children.Add(FocusButton("开始专注","\uE768",true,delegate{
                    app.StartFocus(previewMinutes);
                    ClearStatus();
                    RefreshPage();
                }));
            else
            {
                actions.ColumnDefinitions.Add(new ColumnDefinition());
                actions.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(8)});
                actions.ColumnDefinitions.Add(new ColumnDefinition());
                var pause=FocusButton(app.FocusPaused?"继续专注":"暂停", app.FocusPaused?"\uE768":"\uE769",true,delegate{
                    if(app.FocusPaused)app.ResumeFocus();else app.PauseFocus();
                    RefreshPage();
                });
                var stop=FocusButton("结束本轮","\uE71A",false,delegate {
                    if(MessageBox.Show(this,"要提前结束本轮计时吗？","番茄钟",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)==MessageBoxResult.Yes)
                    {app.CancelFocus();RefreshPage();}
                });
                Grid.SetColumn(stop,2);
                actions.Children.Add(pause);actions.Children.Add(stop);
            }
            return Ui.Stack(face,hint,actions);
        }

        static Button FocusButton(string text,string glyph,bool primary,Action action)
        {
            var button=Ui.Button(text,action,primary);
            button.Margin=new Thickness(0,0,0,3);
            button.MinHeight=36;
            var icon=Ui.Icon(glyph,13,primary?Ui.Paper:Ui.Ink);
            icon.Foreground=button.Foreground;
            icon.Margin=new Thickness(0,0,7,0);
            var label=new TextBlock{Text=text,VerticalAlignment=VerticalAlignment.Center};
            button.Content=new StackPanel{Orientation=Orientation.Horizontal,Children={icon,label}};
            AutomationProperties.SetName(button,text);
            return button;
        }

        void PaintFocusClock()
        {
            int seconds=app.FocusActive?app.RemainingSeconds:previewMinutes*60;
            focusClock.Text=String.Format(CultureInfo.InvariantCulture,"{0:00}:{1:00}",seconds/60,seconds%60);
            if(app.FocusActive)focusDial.SetProgress(seconds/(double)Math.Max(60,app.Data.PlannedMinutes*60));
        }
    }
}
