using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskMate
{
    public partial class SaveData
    {
        public string ActivityPace = "normal";
        public bool PetWelcomeSeen;
        public int MaidRoutine, LabRoutine, MinerRoutine, GuestRoutine;
    }

    // Hand-authored pose order and holds. Feet and contact points live in the drawings.
    internal sealed class ActionScore
    {
        readonly int[] poses, ends;
        public int Duration { get { return ends[ends.Length-1]; } }
        ActionScore(int[] poses,int[] holds)
        {
            this.poses=poses;ends=new int[holds.Length];
            for(int i=0;i<holds.Length;i++)ends[i]=holds[i]+(i==0?0:ends[i-1]);
        }
        public int At(long elapsed)
        {
            for(int i=0;i<ends.Length;i++)if(elapsed<ends[i])return poses[i];
            return poses[poses.Length-1];
        }
        static readonly Dictionary<string,ActionScore> scores=new Dictionary<string,ActionScore>{
            {"tea",new ActionScore(new[]{0,1,2,3,2,3,4,5},new[]{540,380,560,1180,340,870,570,720})},
            {"game",new ActionScore(new[]{0,1,2,3,4,5},new[]{570,660,670,600,940,730})},
            {"mine",new ActionScore(new[]{0,1,2,1,3,1,3,4,5},new[]{650,460,200,360,230,430,270,750,730})},
            {"focus",new ActionScore(new[]{0,1,2,3,2,3,4,2,3,4,5},new[]{640,550,600,1400,770,1350,980,630,1270,820,650})},
            {"rest",new ActionScore(new[]{0,1,2,3,2,3,4,5},new[]{590,420,900,2400,820,1800,440,670})},
            {"song",new ActionScore(new[]{0,1,2,3,1,4,2,5},new[]{160,230,210,290,160,260,280,260})}
        };
        internal static readonly string[] Names={"tea","game","song","focus","rest","mine"};
        public static ActionScore For(string key){return scores[key];}
        public static int SungPose(long elapsed,double fraction)
        {
            var score=scores["song"];
            if(fraction>.94)return 5;
            return score.At(Math.Max(0,elapsed)%score.Duration);
        }
    }

    public sealed partial class PetWindow
    {
        readonly Dictionary<string,BitmapSource[]> authored=new Dictionary<string,BitmapSource[]>();
        string performance="",schedulePace="";
        BitmapSource[] activePoses;
        long performanceStart,gameResultAt,worldTick;
        int hiddenBox,winningGuess;
        bool soloGame,independentPerformance;
        Border gameStage;
        readonly DeskChest[] boxArt=new DeskChest[3];
        readonly Button[] boxButtons=new Button[3];
        string lastDirection="";
        static readonly string[] maidDay={"tea","game","focus","rest","visit","tea","focus","game","rest"};
        static readonly string[] labDay={"focus","game","tea","rest","focus","visit","game","tea","rest"};
        static readonly string[] minerDay={"mine","game","tea","focus","rest","mine","visit","tea","game"};
        public string CurrentActivity { get { return visitor!=null?"visit":dancing?"song":performance; } }
        public bool OfferingGame { get { return soloGame && gameResultAt==0; } }

        internal BitmapSource[] ActionFrames(string id,string key)
        {
            string token=id+"|"+key;
            BitmapSource[] clips;
            if(authored.TryGetValue(token,out clips))return clips;
            string filename="motion-"+id+"-"+key+".png";
            string path=CompanionArchive.IsBuiltin(id)?app.Asset(filename):Path.Combine(app.UserFolder,"companions",id,key+".png");
            if(File.Exists(path))
            {
                try
                {
                    BitmapSource bitmap;
                    using(var file=File.OpenRead(path))bitmap=new PngBitmapDecoder(file,BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.OnLoad).Frames[0];
                    if(bitmap.CanFreeze)bitmap.Freeze();
                    if(bitmap.PixelWidth==1152 && bitmap.PixelHeight==208)
                    {
                        clips=new BitmapSource[6];
                        for(int i=0;i<6;i++) { var pose=new CroppedBitmap(bitmap,new Int32Rect(i*192,0,192,208));pose.Freeze();clips[i]=pose; }
                    }
                }
                catch(IOException) { }
                catch(NotSupportedException) { }
                catch(FileFormatException) { }
            }
            authored[token]=clips;return clips;
        }

        void ShowPose(BitmapSource pose)
        {
            currentRow=currentFrame=-1;
            if(sprite.Source!=pose)sprite.Source=pose;
        }
        bool BeginPerformance(string action,bool independent)
        {
            var sheet=ActionFrames(character,action);
            if(sheet==null)return false;
            StopPerformance();
            row=Idle;effectUntil=0;
            activePoses=sheet;performance=action;performanceStart=clock.ElapsedMilliseconds;
            independentPerformance=independent;
            ShowPose(sheet[0]);
            if(independent && action=="game")StartBoxScene();
            return true;
        }
        void StopPerformance()
        {
            if(gameStage!=null)gameStage.Visibility=Visibility.Collapsed;
            activePoses=null;performance="";soloGame=false;gameResultAt=0;
        }
        void FinishPerformance()
        {
            StopPerformance();SetEffect(Idle,0);ambientAt=NextAmbient();
        }
        internal void OpeningTool()
        {
            EndIndependentVisit();
            if(independentPerformance && activePoses!=null)FinishPerformance();
        }
        void ReleaseCharacterActions(string old)
        {
            if(old==null || CompanionArchive.IsBuiltin(old))return;
            foreach(string key in ActionScore.Names)authored.Remove(old+"|"+key);
        }
        bool AdvancePerformance(long now)
        {
            if(activePoses==null)return false;
            long t=Math.Max(0,now-performanceStart);
            if(soloGame)return AdvanceBoxScene(now,t);
            var score=ActionScore.For(performance);
            if(t>=score.Duration){FinishPerformance();return false;}
            ShowPose(activePoses[score.At(t)]);return true;
        }
        int RoutineIndex
        {
            get { return character=="rem"?app.Data.MaidRoutine:character=="kurisu"?app.Data.LabRoutine:character=="steve"?app.Data.MinerRoutine:app.Data.GuestRoutine; }
            set { if(character=="rem")app.Data.MaidRoutine=value;else if(character=="kurisu")app.Data.LabRoutine=value;else if(character=="steve")app.Data.MinerRoutine=value;else app.Data.GuestRoutine=value; }
        }
        public void RefreshRoutinePace()
        {
            schedulePace=app.Data.ActivityPace??"normal";
            int first=schedulePace=="lively"?9:schedulePace=="calm"?35:16;
            ambientAt=clock.ElapsedMilliseconds+1000*random.Next(first,first+10);
        }
        long NextAmbient()
        {
            int min=48,max=106;
            if(app.Data.ActivityPace=="lively"){min=23;max=48;}
            else if(app.Data.ActivityPace=="calm"){min=150;max=290;}
            return clock.ElapsedMilliseconds+1000*random.Next(min,max);
        }
        bool CompanionOccupied()
        {
            return app.Data.Quiet || !IsVisible || manuallyHidden || fullscreenHidden || visitor!=null || dragging || pressing ||
                (quickMenu!=null && quickMenu.IsOpen) || menu.Visible ||
                (app.Desk!=null && app.Desk.IsVisible) ||
                (app.Music!=null && app.Music.Playing);
        }
        void Ambient()
        {
            long now=clock.ElapsedMilliseconds;
            if(CompanionOccupied()){ambientAt=now+4500;return;}
            if(app.FocusActive && !app.FocusPaused)
            {
                if(!BeginPerformance("focus",false))SetEffect(Work,4200);
                ambientAt=now+36000;return;
            }
            var plan=character=="kurisu"?labDay:character=="steve"||ActionFrames(character,"mine")!=null?minerDay:maidDay;
            int ix=Math.Max(0,RoutineIndex)%plan.Length;
            string action=plan[ix];
            if(action=="visit") StartVisit(true);
            else if(!BeginPerformance(action,true))
            {
                // Legacy packs can still tell a story using their own authored poses.
                int pose=action=="game"?Wave:action=="rest"?Wait:Review;
                SetEffect(pose,Counts[pose]*FrameMs[pose]*2);
            }
            RoutineIndex=(ix+1)%plan.Length;
            string detail=action=="tea"?(character=="steve"?"打开水瓶，喝了几口水。":"自己端起茶杯，喝了几口茶。"):action=="game"?"在桌边悄悄藏好了"+Treasure+"。":
                action=="rest"?"搬来小凳，坐了一会儿。":action=="visit"?"请了一位伙伴来串门。":action=="mine"?"挥镐采下一块圆石。":
                character=="steve"?"摊开地图，查了查方位。":"翻开本子，写了点东西。";
            if(activePoses!=null || action=="visit")app.RememberActivity(app.CharacterName+detail);else app.Save();
            ambientAt=NextAmbient();
        }
        string Treasure { get { return character=="steve"?"一颗绿宝石":"一枚纽扣"; } }
        void StartBoxScene()
        {
            soloGame=true;hiddenBox=random.Next(3);winningGuess=-1;gameResultAt=0;lastDirection="";
            if(gameStage==null)
            {
                var slots=new StackPanel { Orientation=Orientation.Horizontal };
                for(int i=0;i<3;i++)
                {
                    int index=i;
                    var b=Ui.Button("",delegate{GuessBox(index);});
                    var art=new DeskChest();b.Width=39;b.Height=32;b.MinHeight=0;b.Padding=new Thickness(0);b.Margin=new Thickness(2,0,2,0);
                    b.Content=art;b.Background=Brushes.Transparent;b.BorderThickness=new Thickness(0);
                    System.Windows.Automation.AutomationProperties.SetName(b,"猜"+new[]{"左","中","右"}[i]+"边的盒子");
                    boxArt[i]=art;boxButtons[i]=b;slots.Children.Add(b);
                }
                gameStage=new Border { Background=null,Padding=new Thickness(0),
                    Child=slots,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Bottom };
                ((Grid)Content).Children.Add(gameStage);
            }
            gameStage.Visibility=Visibility.Visible;
            RenderBoxes(false,false);
        }
        void RenderBoxes(bool listen,bool reveal)
        {
            for(int i=0;i<3;i++)
            {
                bool correct=reveal && hiddenBox==i;
                boxButtons[i].IsHitTestVisible=listen;boxButtons[i].Focusable=listen;
                boxArt[i].Reveal(correct,character=="steve",winningGuess==i&&!correct);
            }
        }
        void GuessBox(int chosen)
        {
            long now=clock.ElapsedMilliseconds;
            if(!OfferingGame || now-performanceStart<2700 || now-performanceStart>=14000)return;
            winningGuess=chosen;gameResultAt=now;
            bool win=chosen==hiddenBox;
            app.Data.GameRounds++;
            if(win)app.Data.GamesWon++;
            app.Remember((win?"猜中了":"猜错了")+app.CharacterName+"藏"+(character=="steve"?"绿宝石":"纽扣")+"的盒子。");
            Say(win?"被你发现啦！":"揭晓了，在"+new[]{"左边","中间","右边"}[hiddenBox]+"。");
            RenderBoxes(false,true);
        }
        bool AdvanceBoxScene(long now,long t)
        {
            if(gameResultAt==0 && t>=14000)
            {gameResultAt=now;winningGuess=-1;RenderBoxes(false,true);}
            if(gameResultAt>0)
            {
                long after=now-gameResultAt;
                if(after>=2400){FinishPerformance();return false;}
                if(winningGuess>=0 && after<1300)
                {
                    int reaction=winningGuess==hiddenBox?Jump:Miss;
                    DrawFrame(reaction,(int)(after/FrameMs[reaction])%Counts[reaction]);return true;
                }
                ShowPose(activePoses[after<1100?4:5]);return true;
            }
            if(t>=2700 && lastDirection=="")
            {
                int excluded=(hiddenBox+1+random.Next(2))%3;
                lastDirection=new[]{"左边","中间","右边"}[excluded];
                Say("我藏好啦。给你个提示，不在"+lastDirection+"。");
                RenderBoxes(true,false);
            }
            int pose=t<2600?ActionScore.For("game").At(t):((t/1150)%3==0?4:3);
            ShowPose(activePoses[pose]);return true;
        }
    }
    internal sealed class DeskChest : FrameworkElement
    {
        bool open,emerald,wrong;
        public DeskChest(){Width=39;Height=32;}
        public void Reveal(bool opened,bool green,bool missed)
        {open=opened;emerald=green;wrong=missed;InvalidateVisual();}
        static void Shape(DrawingContext dc,string fill,string outline,string path)
        {dc.DrawGeometry(Ui.Color(fill),new Pen(Ui.Color(outline),1.15),Geometry.Parse(path));}
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawEllipse(Ui.Color("#550B1816"),null,new Point(20,29.5),17,2.2);
            string dark=emerald?"#4A3525":"#563E2C";
            Shape(dc,wrong?"#A7836B":"#C9965A",dark,"M3,14 L30,14 30,28 3,27 Z");
            Shape(dc,"#8D603B",dark,"M30,14 L36,10 36,24 30,28 Z");
            if(open)
            {
                Shape(dc,"#8C6A49",dark,"M4,13 L4,5 32,4 34,10 30,14 Z");
                Shape(dc,"#66503A",dark,"M4,14 L30,14 35,11 10,11 Z");
                if(emerald)Shape(dc,"#59CF87","#285A42","M19,5 L23,9 18,16 14,10 Z");
                else{dc.DrawEllipse(Ui.Color("#C96E5F"),new Pen(Ui.Color("#743D38"),1),new Point(18,10),4,3.6);
                     dc.DrawEllipse(Ui.Color("#F4CAB0"),null,new Point(17,9),.75,.75);dc.DrawEllipse(Ui.Color("#F4CAB0"),null,new Point(19,11),.75,.75);}
            }
            else
            {
                Shape(dc,"#E5BC80",dark,"M3,12 L9,8 35,8 30,13 3,15 Z");
                Shape(dc,"#BD8A53",dark,"M3,13 L30,13 30,17 3,17 Z");
            }
            dc.DrawRoundedRectangle(Ui.Color("#ECD7A0"),new Pen(Ui.Color(dark),.8),new Rect(15,15,4.5,6),1,1);
        }
    }
}
