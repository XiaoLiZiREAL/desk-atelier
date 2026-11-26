using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml;
using Microsoft.Win32;

namespace DeskMate
{
    public partial class SaveData
    {
        public double ChairX = .29, ChairY = .81, LampX = .13, LampY = .78, CupX = .77, CupY = .82;
        public string GiftFrom = "";
    }

    internal sealed class DeskGift
    {
        internal string From, Message, Theme;
        internal readonly bool[] Visible = new bool[3];
        internal readonly double[] X = new double[3], Y = new double[3];
        internal static readonly string[] Names = { "chair", "lamp", "cup" };
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        internal static DeskGift Capture(SaveData d, string from, string note)
        {
            var gift = new DeskGift { From = CardText(from, 32), Message = CardText(note, 180), Theme = d.RoomTheme == "apricot" || d.RoomTheme == "dusk" ? d.RoomTheme : "sage" };
            bool[] visible = { d.Chair, d.Lamp, d.Cup };
            double[] xs = { d.ChairX, d.LampX, d.CupX }, ys = { d.ChairY, d.LampY, d.CupY };
            for (int i = 0; i < 3; i++) { gift.Visible[i] = visible[i]; gift.X[i] = Bounded(xs[i], .09, .91, .15 + .31*i); gift.Y[i] = Bounded(ys[i], .71, .94, .81); }
            return gift;
        }
        static string CardText(string text, int limit)
        {
            string value = Store.CleanText(text ?? "").Trim();
            if (value.Length <= limit) return value;
            if (char.IsHighSurrogate(value[limit - 1])) limit--;
            return value.Substring(0,limit);
        }
        static double Bounded(double n, double min, double max, double fallback) { return double.IsNaN(n) || double.IsInfinity(n) ? fallback : Math.Max(min, Math.Min(max,n)); }
        internal void Apply(SaveData d)
        {
            d.RoomTheme = Theme;
            d.Chair = Visible[0]; d.ChairX = X[0]; d.ChairY = Y[0];
            d.Lamp = Visible[1]; d.LampX = X[1]; d.LampY = Y[1];
            d.Cup = Visible[2]; d.CupX = X[2]; d.CupY = Y[2];
            d.LastGift = (From.Length == 0 ? "一位朋友" : From) + "：" + Message;
        }
        internal void Save(string path)
        {
            if (Message.Length == 0) throw new InvalidDataException("先给朋友留一句话吧。");
            string target = Path.GetFullPath(path);
            string tmp = Path.Combine(Path.GetDirectoryName(target), "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (XmlWriter w = XmlWriter.Create(tmp, new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(false) }))
                {
                    w.WriteStartElement("deskpetGift"); w.WriteAttributeString("version", "1");
                    w.WriteElementString("from", From); w.WriteElementString("card", Message); w.WriteElementString("theme", Theme);
                    for (int i = 0; i < 3; i++)
                    {
                        w.WriteStartElement(Names[i]); w.WriteAttributeString("on", Visible[i] ? "1" : "0");
                        w.WriteAttributeString("x", X[i].ToString("R", Inv)); w.WriteAttributeString("y", Y[i].ToString("R", Inv));
                        w.WriteEndElement();
                    }
                    w.WriteEndElement();
                }
                if (File.Exists(target)) File.Replace(tmp, target, null, true); else File.Move(tmp, target);
            }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
        internal static DeskGift Read(string path)
        {
            if (!String.Equals(Path.GetExtension(path), ".deskpet", StringComparison.OrdinalIgnoreCase)) throw Invalid();
            var xml = new XmlDocument { XmlResolver = null };
            using (var stream = File.OpenRead(path))
            {
                if (stream.Length > 32768) throw Invalid();
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32768 };
                using (var reader = XmlReader.Create(stream, settings)) xml.Load(reader);
            }
            var el = xml.DocumentElement;
            if (el == null || el.Name != "deskpetGift" || el.GetAttribute("version") != "1") throw Invalid();
            var gift = new DeskGift { From = Field(el, "from", 32), Message = Field(el, "card", 180), Theme = Field(el, "theme", 12) };
            if (gift.Message.Length == 0 || !new[] { "sage", "apricot", "dusk" }.Contains(gift.Theme)) throw Invalid();
            for (int i = 0; i < 3; i++)
            {
                var item = el[Names[i]]; if (item == null) throw Invalid();
                string on = item.GetAttribute("on"); if (on != "0" && on != "1") throw Invalid();
                gift.Visible[i] = on == "1"; gift.X[i] = Coord(item.GetAttribute("x"), .09, .91);
                gift.Y[i] = Coord(item.GetAttribute("y"), .71, .94);
            }
            return gift;
        }
        static string Field(XmlElement root, string key, int max)
        {
            XmlElement el = root[key]; if (el == null) throw Invalid();
            string value = el.InnerText.Trim(); if (value.Length > max) throw Invalid(); return value;
        }
        static double Coord(string val, double min, double max)
        {
            double n;
            if (!double.TryParse(val, NumberStyles.Float, Inv, out n) || double.IsNaN(n) || n < min || n > max) throw Invalid();
            return n;
        }
        static InvalidDataException Invalid() { return new InvalidDataException("这份礼物的格式或版本不受支持。"); }
    }

    internal static class DrawingKit
    {
        static readonly Dictionary<string, SolidColorBrush> Brushes = new Dictionary<string, SolidColorBrush>();
        internal static SolidColorBrush B(string c)
        {
            SolidColorBrush b; if (!Brushes.TryGetValue(c, out b)) { b = (SolidColorBrush)new BrushConverter().ConvertFromString(c); b.Freeze(); Brushes.Add(c,b); }
            return b;
        }
        internal static void R(DrawingContext dc, double x,double y,double w,double h, double radius,string color)
        { dc.DrawRoundedRectangle(B(color), null, new Rect(x,y,w,h),radius,radius); }
        internal static void E(DrawingContext dc, double x,double y,double w,double h,string color)
        { dc.DrawEllipse(B(color),null,new Point(x+w/2,y+h/2),w/2,h/2); }
        internal static void L(DrawingContext dc, double x1,double y1,double x2,double y2,string color,double width)
        {
            var pen = new Pen(B(color),width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen,new Point(x1,y1),new Point(x2,y2));
        }
        internal static void P(DrawingContext dc, string color, params double[] points)
        {
            var g = new StreamGeometry(); using (var geo = g.Open()) { geo.BeginFigure(new Point(points[0],points[1]),true,true); for(int i=2;i<points.Length;i+=2) geo.LineTo(new Point(points[i],points[i+1]),true,false); }
            dc.DrawGeometry(B(color),null,g);
        }
        internal static void Label(DrawingContext dc, string txt, double size, string color, double x,double y)
        {
            var f = new FormattedText(txt, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,new Typeface("Microsoft YaHei UI"),size,B(color),1.0);
            dc.DrawText(f,new Point(x-f.Width/2,y));
        }
        internal static BitmapSource Load(string path)
        {
            if (!File.Exists(path)) return null;
            try { var b = new BitmapImage(); b.BeginInit(); b.CacheOption=BitmapCacheOption.OnLoad; b.UriSource=new Uri(Path.GetFullPath(path)); b.EndInit(); b.Freeze(); return b; }
            catch (Exception) { return null; }
        }
    }

    internal sealed class RoomScene : FrameworkElement
    {
        const double W=620,H=212;
        readonly DeskGift decor;
        readonly ImageSource portrait;
        readonly Action<int,double,double> dropped;
        readonly Action<int> touched;
        readonly Action<int> approaching;
        readonly BitmapSource[][] frames;
        readonly Func<string,BitmapSource[]> actionArt;
        BitmapSource[] roomAction;
        string roomPart="";
        readonly DispatcherTimer motion;
        readonly Stopwatch elapsed = new Stopwatch();
        double actorX=.54, actorY=.845, startX, startY, targetX, targetY, walkMs;
        int selected=-1, row, frame;
        bool arrived;
        const int Idle=0, WalkRight=1, WalkLeft=2, Wave=3, Waiting=6, Reviewing=8;
        int pressed=-1;
        Point start;
        double originalX,originalY;
        bool dragged;
        internal RoomScene(DeskGift data,ImageSource pet,Action<int,double,double> drag,Action<int> action,
            BitmapSource[][] spriteFrames=null,Action<int> onApproach=null,Func<string,BitmapSource[]> actionLookup=null)
        {
            decor=data;portrait=pet;dropped=drag;touched=action;frames=spriteFrames;approaching=onApproach;actionArt=actionLookup;
            MaxWidth=930;ClipToBounds=true; SnapsToDevicePixels=true;
            ToolTip = drag==null ? null : "拖动椅子、台灯或茶杯布置房间，单击道具与搭档互动";
            if (drag != null)
            {
                motion=new DispatcherTimer(DispatcherPriority.Background){Interval=TimeSpan.FromMilliseconds(44)};
                motion.Tick+=delegate{AdvanceVisit();};
                Unloaded+=delegate{EndVisit();};
                IsVisibleChanged+=delegate{if(!IsVisible)EndVisit();};
                MouseLeftButtonDown+=Down; MouseLeftButtonUp+=Up; MouseMove+=Move;
                LostMouseCapture+=delegate { if(pressed>=0 && dragged) { decor.X[pressed]=originalX;decor.Y[pressed]=originalY; } pressed=-1;dragged=false;InvalidateVisual(); };
            }
        }
        protected override Size MeasureOverride(Size available)
        {
            double width=double.IsPositiveInfinity(available.Width)?W:Math.Min(930,available.Width);
            return new Size(width,width*H/W);
        }
        double RenderScale { get { return Math.Min(ActualWidth/W,ActualHeight/H); } }
        Point ToRoom(Point p)
        {
            double scale=Math.Max(.001,RenderScale);
            return new Point((p.X-(ActualWidth-W*scale)/2)/scale,(p.Y-(ActualHeight-H*scale)/2)/scale);
        }
        static double Clamp(double d,double min,double max) { return Math.Max(min,Math.Min(max,d)); }
        int Hit(Point p)
        {
            foreach(int i in Enumerable.Range(0,3).OrderByDescending(j=>decor.Y[j]))
            {
                if (!decor.Visible[i]) continue;
                double w=i==0?69:i==1?59:40,h=i==0?69:i==1?77:35;
                if (new Rect(decor.X[i]*W-w/2,decor.Y[i]*H-h,w,h+4).Contains(p)) return i;
            }
            return -1;
        }
        void Down(object sender,MouseButtonEventArgs e)
        {
            start=ToRoom(e.GetPosition(this)); pressed=Hit(start); dragged=false;
            if(pressed<0)return;
            originalX=decor.X[pressed];originalY=decor.Y[pressed];CaptureMouse(); e.Handled=true;
        }
        void Move(object sender,MouseEventArgs e)
        {
            var p=ToRoom(e.GetPosition(this));
            if(pressed<0) { Cursor=Hit(p)>=0?Cursors.Hand:Cursors.Arrow;return; }
            double dx=p.X-start.X,dy=p.Y-start.Y;
            if(!dragged && Math.Abs(dx)+Math.Abs(dy)<5)return;
            dragged=true;Cursor=Cursors.SizeAll;
            decor.X[pressed]=Clamp(originalX+dx/W,.09,.91);decor.Y[pressed]=Clamp(originalY+dy/H,.71,.94);InvalidateVisual();
        }
        void Up(object sender,MouseButtonEventArgs e)
        {
            if(pressed<0)return;
            int which=pressed; bool moved=dragged; pressed=-1;dragged=false;ReleaseMouseCapture();Cursor=Cursors.Hand;e.Handled=true;
            if (moved) dropped(which,decor.X[which],decor.Y[which]);
            else Visit(which);
        }
        internal void Visit(int which)
        {
            if (motion==null || !decor.Visible[which]) return;
            startX=actorX;startY=actorY;selected=which;arrived=false;
            roomPart=which==0?"rest":which==1?"focus":"tea";
            roomAction=actionArt==null?null:actionArt(roomPart);
            targetX=Clamp(decor.X[which]+(decor.X[which]>.55?-.08:.08),.12,.88);
            targetY=Clamp(decor.Y[which]+.012,.755,.92);
            double dx=(targetX-startX)*W,dy=(targetY-startY)*H;
            double distance=Math.Sqrt(dx*dx+dy*dy);
            walkMs=distance<4?0:Clamp(distance*5.4,260,2200);
            if(approaching!=null && walkMs>0)approaching(which);
            elapsed.Restart();motion.Start();AdvanceVisit();
        }
        void AdvanceVisit()
        {
            double time=elapsed.Elapsed.TotalMilliseconds;
            if(time<walkMs)
            {
                double p=time/walkMs;
                double eased=p*.65+p*p*(3-2*p)*.35;
                actorX=startX+(targetX-startX)*eased;actorY=startY+(targetY-startY)*eased;
                row=targetX>=startX?WalkRight:WalkLeft;frame=(int)(time/100);
            }
            else
            {
                actorX=targetX;actorY=targetY;
                if(!arrived){arrived=true;if(touched!=null)touched(selected);}
                if(roomAction!=null)
                {
                    var part=ActionScore.For(roomPart);
                    if(time-walkMs>=part.Duration){EndVisit();return;}
                    frame=part.At((long)(time-walkMs));InvalidateVisual();return;
                }
                row=selected==0?Waiting:selected==1?Reviewing:Wave;
                int ms=selected==2?220:250;
                frame=(int)((time-walkMs)/ms);
                int count=frames!=null?frames[row].Length:(selected==2?4:6);
                if(frame>=count){EndVisit();return;}
            }
            InvalidateVisual();
        }
        void EndVisit()
        {
            if(motion!=null)motion.Stop();
            elapsed.Stop();selected=-1;arrived=false;row=Idle;frame=0;roomAction=null;InvalidateVisual();
        }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc); if(ActualWidth<=0||ActualHeight<=0)return;
            double scale=RenderScale;
            dc.PushTransform(new TranslateTransform((ActualWidth-W*scale)/2,(ActualHeight-H*scale)/2));
            dc.PushTransform(new ScaleTransform(scale,scale));
            bool evening=decor.Theme=="dusk",peach=decor.Theme=="apricot";
            string wall=evening?"#637080":peach?"#F2D7C2":"#D7E3D1", wall2=evening?"#536170":peach?"#DDC0AC":"#BFCEBB";
            string wood=evening?"#967868":peach?"#C99A78":"#B99B7D",trim=evening?"#EAD8C1":"#F9EDDA";
            var sky=new LinearGradientBrush(DrawingKit.B(evening?"#394667":peach?"#ECB9A1":"#AAD3CA").Color,DrawingKit.B(evening?"#DBA9A7":"#FEEDD3").Color,90);
            DrawingKit.R(dc,0,0,W,H,0,wall);
            for(int i=8;i<W;i+=20) DrawingKit.L(dc,i,0,i,145,wall2,.65);
            DrawingKit.R(dc,22,16,145,89,5,wall2); DrawingKit.R(dc,25,13,140,88,4,trim);
            dc.DrawRectangle(sky,null,new Rect(31,19,128,75));
            DrawingKit.E(dc,124,29,17,17,evening?"#FAE8CC":"#FFE7A8");
            DrawingKit.P(dc,evening?"#797D91":"#97B4A3",31,86,61,58,86,74,111,49,159,81,159,94,31,94);
            DrawingKit.P(dc,evening?"#59657B":"#799D8A",31,89,51,79,77,84,94,68,130,87,159,72,159,94,31,94);
            DrawingKit.R(dc,92,18,4,78,0,trim); DrawingKit.R(dc,28,60,132,3,0,trim);DrawingKit.R(dc,19,96,153,6,2,trim);
            DrawingKit.R(dc,419,58,165,6,2,wood);
            DrawingKit.R(dc,432,37,8,21,1,"#687B69");DrawingKit.R(dc,442,33,7,25,1,"#BD7869");
            DrawingKit.P(dc,"#E7CBA1",452,35,459,33,467,57,459,58);
            DrawingKit.R(dc,474,28,29,30,2,trim);DrawingKit.R(dc,478,32,21,22,1,wall2);
            DrawingKit.E(dc,485,37,8,8,"#D8A26B");
            DrawingKit.R(dc,538,45,17,12,3,"#B97860");
            DrawingKit.L(dc,546,45,547,25,"#4C715F",2);
            DrawingKit.E(dc,536,30,11,7,"#617F65");DrawingKit.E(dc,547,26,11,7,"#6C8B71");
            DrawingKit.R(dc,0,126,W,6,0,trim);DrawingKit.R(dc,0,132,W,80,0,wood);
            for(int i=0;i<3;i++) DrawingKit.L(dc,0,152+i*23,W,152+i*23,evening?"#84695D":"#AB896F",1);
            for(int i=0;i<6;i++) DrawingKit.L(dc,45+i*116,133,15+i*129,212,evening?"#84695D":"#AB896F",1);
            DrawingKit.E(dc,200,155,235,44,evening?"#C09A86":peach?"#DEB6A1":"#D5C9AF");
            for(int i=0;i<2;i++)
            {
                var pen=new Pen(DrawingKit.B(evening?"#CFAC96":"#E9DBC1"),1);
                dc.DrawEllipse(null,pen,new Point(317.5,177),105-i*5,16-i*2);
            }
            foreach(int i in Enumerable.Range(0,3).Where(j=>decor.Visible[j] && decor.Y[j]<=actorY).OrderBy(j=>decor.Y[j])) Furniture(dc,i);
            double actor=actorX*W,floor=actorY*H;
            ImageSource pose=arrived&&roomAction!=null?roomAction[frame]:frames==null?portrait:frames[row][frame%frames[row].Length];
            if(pose!=null)
            {
                DrawingKit.E(dc,actor-28,floor-4,56,8,"#42816C56");
                double size=frames==null?Math.Min(119.0/pose.Height,125.0/pose.Width):127.0/pose.Height;
                double baseline=frames==null?1:.964;
                dc.DrawImage(pose,new Rect(actor-pose.Width*size/2,floor-pose.Height*size*baseline,pose.Width*size,pose.Height*size));
            }
            foreach(int i in Enumerable.Range(0,3).Where(j=>decor.Visible[j] && decor.Y[j]>actorY).OrderBy(j=>decor.Y[j])) Furniture(dc,i);
            dc.Pop();dc.Pop();
        }
        void Furniture(DrawingContext dc,int i)
        {
            if(arrived && roomAction!=null && selected==i && (i==0 || i==2))return;
            dc.PushTransform(new TranslateTransform(decor.X[i]*W,decor.Y[i]*H));
            DrawingKit.E(dc,-25,-4,50,7,"#40816C56");
            if(i==0)
            {
                DrawingKit.L(dc,-20,-19,-23,-2,"#765A48",5);DrawingKit.L(dc,20,-19,23,-2,"#765A48",5);
                DrawingKit.R(dc,-28,-63,56,45,9,"#5C776F");DrawingKit.R(dc,-24,-60,48,35,8,"#8DA398");
                DrawingKit.R(dc,-29,-30,58,16,6,"#536D65");DrawingKit.R(dc,-29,-32,58,11,5,"#ABB9A5");
                DrawingKit.R(dc,-33,-42,9,23,4,"#799185");DrawingKit.R(dc,24,-42,9,23,4,"#799185");
                DrawingKit.L(dc,-14,-53,13,-53,"#C2C9B5",1.2);
            }
            else if(i==1)
            {
                if(arrived && selected==1)
                {
                    var glow=new RadialGradientBrush(DrawingKit.B("#BFFFDC87").Color,DrawingKit.B("#00FFDC87").Color);
                    dc.DrawEllipse(glow,null,new Point(0,-34),48,40);
                }
                DrawingKit.E(dc,-18,-7,36,7,"#6A624F");DrawingKit.L(dc,0,-7,0,-46,"#7B7159",4);
                DrawingKit.P(dc,"#BE9961",-25,-37,-13,-68,13,-68,25,-37);
                DrawingKit.P(dc,"#F0D499",-22,-40,-11,-66,9,-66,17,-40);
                DrawingKit.R(dc,-26,-40,52,4,2,"#E6BE80");DrawingKit.L(dc,11,-36,11,-23,"#635D4D",1.2);DrawingKit.E(dc,9,-24,4,4,"#635D4D");
            }
            else
            {
                if(arrived && selected==2)
                {
                    double rise=frame*1.4;
                    DrawingKit.L(dc,-6,-28-rise,-4,-33-rise,"#BCFFF2DB",1.5);
                    DrawingKit.L(dc,2,-30-rise,4,-37-rise,"#BCFFF2DB",1.5);
                }
                DrawingKit.E(dc,-21,-7,42,9,"#D8C6A9");DrawingKit.E(dc,-17,-7,34,6,"#F7EAD3");
                dc.DrawEllipse(null,new Pen(DrawingKit.B("#EFE1C6"),3),new Point(14,-13),6,5);
                DrawingKit.R(dc,-14,-22,27,19,7,"#F4E8D1");DrawingKit.E(dc,-14,-24,27,7,"#F9EFDB");DrawingKit.E(dc,-11,-22,21,4,"#9F7448");
                DrawingKit.P(dc,"#829983",-5,-13,0,-17,5,-13,0,-9);
            }
            dc.Pop();
        }
    }

    public partial class StudioWindow
    {
        static readonly string[] ThemeNames={"鼠尾草","杏子茶","暮蓝"};
        static readonly string[] ThemeKeys={"sage","apricot","dusk"};
        static readonly string[] ObjectNames={"扶手椅","台灯","茶杯"};
        DeskGift incoming;
        string giftStatus="", giftDraft="", giftSender=null;
        string radioMessage="", roomStatus="";
        bool radioOn;
        SoundPlayer builtInRadio;
        MemoryStream tune;
        MediaPlayer localRadio;
        BitmapSource roomSprite;
        string roomSpriteKey;
        static StackPanel Pile(double gap,params UIElement[] items)
        {
            var stack=new StackPanel();
            foreach(var item in items) if(item!=null)
            {
                if(stack.Children.Count>0)stack.Children.Add(new Border{Height=gap,IsHitTestVisible=false});
                stack.Children.Add(item);
            }
            return stack;
        }
        static WrapPanel Actions(params UIElement[] children)
        {
            var panel=new WrapPanel { Margin=new Thickness(0,0,-6,-5) };
            foreach(var child in children) if(child!=null) { var frame=new Border{Margin=new Thickness(0,0,6,5),Child=child};panel.Children.Add(frame); }
            return panel;
        }
        static UIElement Copy(string s) { return Ui.Text(s,13,"#52625A"); }
        static UIElement Heading(string s) { return Ui.Text(s,19,"#243B31"); }
        static string ThemeName(string key) { int ix=Array.IndexOf(ThemeKeys,key);return ThemeNames[ix<0?0:ix]; }
        ImageSource RoomSprite()
        {
            string key=app.Data.Character;if(key!=roomSpriteKey || roomSprite==null)
            { roomSprite=DrawingKit.Load(app.Asset(key+"-still.png"));roomSpriteKey=key; }
            return roomSprite;
        }
        void ActivitySave(){app.Save();}
        public UIElement RoomPage()
        {
            var decor=DeskGift.Capture(app.Data,"","");
            var caption=Ui.Text(roomStatus.Length==0?"拖动道具布置位置，点一下看看搭档的反应。":roomStatus,13,"#52625A");
            var scene=new RoomScene(decor,RoomSprite(),delegate(int i,double x,double y)
            {
                if(i==0){app.Data.ChairX=x;app.Data.ChairY=y;} else if(i==1){app.Data.LampX=x;app.Data.LampY=y;}else{app.Data.CupX=x;app.Data.CupY=y;}
                roomStatus="已记住"+ObjectNames[i]+"的位置。";caption.Text=roomStatus;ActivitySave();
            },delegate(int i)
            {
                string[] rem={"这里的椅垫刚刚整理好。请坐吧。","把灯打开，眼睛会舒服一点。","这杯茶的温度，应该刚刚好。"};
                string[] kurisu={"位置刚好。把笔记放这边吧。","嗯，这个角度不会在纸上留下阴影。","实验告一段落。喝点茶也不错。"};
                string[] neutral={"来这里歇一会儿吧。","灯光刚刚好。","喝点热茶吧。"};
                string[] lines=app.Data.Character=="kurisu"?kurisu:app.Data.Character=="rem"?rem:neutral;
                roomStatus=app.CharacterName+" · "+lines[i];caption.Text=roomStatus;
                app.React(i==0?"rest":i==1?"focus":"tea",roomStatus);
            },app.Pet==null?null:app.Pet.AnimationFrames(app.Data.Character),delegate(int i)
            {
                caption.Text=app.CharacterName+"正走向"+ObjectNames[i]+"。";
            }, key=>app.Pet.ActionFrames(app.Data.Character,key));
            var colors=new List<UIElement>();
            for(int i=0;i<ThemeKeys.Length;i++)
            {
                int k=i;colors.Add(Ui.Button(ThemeNames[i],delegate{app.Data.RoomTheme=ThemeKeys[k];roomStatus="";ActivitySave();RefreshPage();},app.Data.RoomTheme==ThemeKeys[i]));
            }
            var objects=new List<UIElement>();
            for(int i=0;i<3;i++)
            {
                int item=i;bool active=decor.Visible[i];
                objects.Add(Ui.Button((active?"收起":"摆出")+ObjectNames[i],delegate
                {
                    if(item==0)app.Data.Chair=!active;else if(item==1)app.Data.Lamp=!active;else app.Data.Cup=!active;
                    roomStatus=(active?"收好了":"摆好了")+ObjectNames[item]+"。";ActivitySave();RefreshPage();
                }));
            }
            var interactions=new List<UIElement>();
            for(int i=0;i<3;i++)if(decor.Visible[i]){int n=i;interactions.Add(Ui.Button(new[]{"去椅子旁","看看台灯","一起喝茶"}[i],delegate{scene.Visit(n);}));}
            return Pile(13,Ui.Card(Pile(10,Heading("搭档的桌边一角"),scene,caption,Actions(interactions.ToArray()))),
                Ui.Card(Pile(10,Heading("布置房间"),Copy("选择房间配色，摆出想用的道具。"),Actions(colors.ToArray()),Actions(objects.ToArray()))),RadioCard());
        }
        TextBlock radioStatusLabel;
        Button radioToggle;
        UIElement RadioCard()
        {
            if(app.Data.FocusCount<3)
                return Ui.Card(Pile(9,Heading("一起装收音机"),Copy("每完成一次专注，就装好一件零件。装好第 3 件后，可以播放内置旋律或自己的音乐。"),
                    Copy("已装好 "+app.Data.FocusCount+" / 3 件"),Ui.Button("去专注",delegate{Go("focus");})));
            radioStatusLabel=Ui.Text(radioMessage.Length==0?"旋钮已经装好，随时可以收听。":radioMessage,13,"#52625A");
            string track=string.IsNullOrWhiteSpace(app.Data.MusicPath)?"窗边小调 · 内置曲":Path.GetFileName(app.Data.MusicPath);
            radioToggle=Ui.Button(radioOn?"停止播放":"播放",delegate{if(radioOn){StopRadio();radioMessage="播放已停止。";}else PlayRadio();RefreshPage();},true);
            return Ui.Card(Pile(10,Heading("桌边收音机"),Copy(track),radioStatusLabel,
                Actions(radioToggle,Ui.Button("选本地音乐",SelectTrack),!string.IsNullOrEmpty(app.Data.MusicPath)?Ui.Button("换回内置曲",delegate{StopRadio();app.Data.MusicPath="";radioMessage="已调回窗边小调。";ActivitySave();RefreshPage();}):null)));
        }
        void SelectTrack()
        {
            var dlg=new OpenFileDialog{Title="选择收音机要播放的本地音乐",Filter="音乐文件|*.mp3;*.wav;*.wma;*.m4a",CheckFileExists=true};
            if(dlg.ShowDialog(this)!=true)return;
            StopRadio();app.Data.MusicPath=dlg.FileName;radioMessage="已选好音乐，按播放开始收听。";ActivitySave();RefreshPage();
        }
        void PlayRadio()
        {
            StopRadio();
            try
            {
                if(string.IsNullOrWhiteSpace(app.Data.MusicPath))
                {
                    tune=MakeTune();builtInRadio=new SoundPlayer(tune);builtInRadio.Load();builtInRadio.PlayLooping();radioOn=true;radioMessage="正在循环播放窗边小调。";return;
                }
                if(!File.Exists(app.Data.MusicPath)){radioMessage="没有找到这首歌。可以重新选曲或换回内置曲。";return;}
                var player=new MediaPlayer();localRadio=player;player.Volume=.45;
                player.MediaFailed+=delegate
                {
                    if(localRadio!=player)return;
                    StopRadio();radioMessage="这首歌无法播放。可以重新选曲或换回内置曲。";
                    if(radioStatusLabel!=null)radioStatusLabel.Text=radioMessage;
                    if(radioToggle!=null)radioToggle.Content="播放";
                };
                player.MediaEnded+=delegate{if(radioOn&&localRadio==player){player.Position=TimeSpan.Zero;player.Play();}};
                player.Open(new Uri(Path.GetFullPath(app.Data.MusicPath)));player.Play();
                radioOn=true;radioMessage="正在循环播放 "+Path.GetFileName(app.Data.MusicPath)+"。";
            }
            catch(Exception) {StopRadio();radioMessage="当前声音设备或文件无法播放，可以检查输出设备或重新选曲。";}
        }
        void StopRadio()
        {
            radioOn=false;
            if(localRadio!=null){var old=localRadio;localRadio=null;old.Close();}
            if(builtInRadio!=null){builtInRadio.Stop();builtInRadio.Dispose();builtInRadio=null;}
            if(tune!=null){tune.Dispose();tune=null;}
        }
        // Original, low-volume music box phrase. Build PCM in memory for offline playback.
        internal static MemoryStream MakeTune()
        {
            const int rate=16000;const double beat=.39;
            int[] melody={64,0,67,69,0,67,64,62,60,0,64,67,0,64,62,0,62,0,65,69,0,67,65,62,60,0,64,62,0,60,0,0};
            int[] bass={48,53,50,48};int count=(int)(melody.Length*beat*rate);
            var stream=new MemoryStream(44+count*2);
            var w=new BinaryWriter(stream,Encoding.ASCII,true);
            w.Write(Encoding.ASCII.GetBytes("RIFF"));w.Write(36+count*2);w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            w.Write(16);w.Write((short)1);w.Write((short)1);w.Write(rate);w.Write(rate*2);w.Write((short)2);w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data"));w.Write(count*2);
            var samples=new double[count];
            Action<int,double,double,double> voice=delegate(int midi,double start,double length,double gain)
            {
                double angle=2*Math.PI*440*Math.Pow(2,(midi-69)/12.0)/rate;
                int at=(int)(start*rate),n=Math.Min(count-at,(int)(length*rate));
                for(int i=0;i<n;i++)
                {
                    double t=(double)i/rate;
                    double env=Math.Min(1,t/.014)*Math.Exp(-4.8*t)*Math.Min(1,(double)(n-i)/(rate*.045));
                    samples[at+i]+=gain*env*(Math.Sin(angle*i)+.24*Math.Sin(2*angle*i)+.08*Math.Sin(3*angle*i));
                }
            };
            for(int i=0;i<melody.Length;i++) if(melody[i]>0)voice(melody[i],i*beat,1.1,.17);
            for(int i=0;i<bass.Length;i++){voice(bass[i],8*i*beat,1.6,.13);voice(bass[i]+7,(8*i+4)*beat,1.35,.08);}
            for(int i=0;i<count;i++)w.Write((short)(Math.Max(-1,Math.Min(1,samples[i]))*32760));
            w.Flush();w.Dispose();stream.Position=0;return stream;
        }
        public void CloseActivities(){StopRadio();}
        public UIElement GiftPage()
        {
            if(incoming!=null)
            {
                DeskGift pending=incoming;
                string sender=String.IsNullOrEmpty(pending.From)?"一位朋友":pending.From;
                return Ui.Card(Pile(12,Heading(sender+" 送来了一份礼物"),Copy("“"+pending.Message+"”"),
                    new RoomScene(pending,RoomSprite(),null,null),Copy(ThemeName(pending.Theme)+" · "+string.Join("、",Enumerable.Range(0,3).Where(i=>pending.Visible[i]).Select(i=>ObjectNames[i]).DefaultIfEmpty("空房间"))),
                    Copy("收下后会换成预览中的房间配色、家具和摆放位置。"),
                    Actions(Ui.Button("收下并布置",delegate{pending.Apply(app.Data);app.Remember("收到了"+sender+"送来的桌边礼物。");incoming=null;roomStatus="礼物已布置好。";giftStatus="已收到 "+sender+" 的礼物。";app.React("gift",pending.Message);Go("room");},true),
                        Ui.Button("暂时不收",delegate{incoming=null;giftStatus="已合上这份礼物。";RefreshPage();}))));
            }
            var senderInput=Ui.Input(giftSender??app.Data.GiftFrom);senderInput.MaxLength=32;
            var note=Ui.Input(giftDraft);note.MaxLength=180;note.AcceptsReturn=true;note.TextWrapping=TextWrapping.Wrap;note.MinHeight=72;
            senderInput.TextChanged+=delegate{giftSender=senderInput.Text;};note.TextChanged+=delegate{giftDraft=note.Text;};
            var status=Ui.Text(giftStatus,13,"#52625A");
            var export=Ui.Button("打包桌边礼物",delegate
            {
                if(string.IsNullOrWhiteSpace(note.Text)){giftStatus="先给朋友留一句话吧。";status.Text=giftStatus;note.Focus();return;}
                var save=new SaveFileDialog{Title="保存桌边礼物",Filter="桌边礼物|*.deskpet",DefaultExt=".deskpet",AddExtension=true,FileName="给你的桌边礼物.deskpet",OverwritePrompt=true};
                if(save.ShowDialog(this)!=true)return;
                try
                {
                    DeskGift gift=DeskGift.Capture(app.Data,senderInput.Text,note.Text);gift.Save(save.FileName);
                    app.Data.GiftFrom=gift.From;ActivitySave();giftStatus="礼物已打包，可以把 "+Path.GetFileName(save.FileName)+" 发给朋友。";status.Text=giftStatus;
                }
                catch(Exception e){giftStatus="保存失败。"+e.Message;status.Text=giftStatus;}
            },true);
            return Pile(13,Ui.Card(Pile(9,Heading("给朋友寄一个房间"),Copy("当前房间的配色、家具位置和你写的卡片会一起装进礼物包。把礼物文件发给朋友，对方打开「礼物邮局」即可拆开。"),
                Copy("署名（选填）"),senderInput,Copy("给朋友的话"),note,Actions(export),status)),
                Ui.Card(Pile(10,Heading("拆一份礼物"),Copy("选中朋友发来的 .deskpet 文件，先看卡片和房间，再决定是否收下。"),
                    Ui.Button("打开 .deskpet 礼物",delegate{var dlg=new OpenFileDialog{Title="打开桌边礼物",Filter="桌边礼物|*.deskpet",CheckFileExists=true};if(dlg.ShowDialog(this)==true)PreviewGift(dlg.FileName);}),
                    string.IsNullOrEmpty(app.Data.LastGift)?null:Copy("上次收到的卡片 · "+app.Data.LastGift))));
        }
        public bool PreviewGift(string path)
        {
            try { incoming=DeskGift.Read(path);giftStatus="";Go("gift");return true; }
            catch(Exception e) {incoming=null;giftStatus="无法打开礼物。"+e.Message;Go("gift");return false;}
        }
    }
}





