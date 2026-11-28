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
        public int MaidRoutine, LabRoutine, MinerRoutine, GuitarRoutine, MutsumiRoutine, MutsumiStageRoutine, GuestRoutine;
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
        ActionScore(params int[] holds):this(new[]{0,1,2,3,4,5},holds) {}
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
            {"song",new ActionScore(new[]{0,1,2,3,1,4,2,5},new[]{160,230,210,290,160,260,280,260})},
            {"pick",new ActionScore(620,260,750,400,930,670)},
            {"pickcatch",new ActionScore(620,260,350,430,670,820)},
            {"bow",new ActionScore(660,540,710,1050,640,860)},
            {"fold",new ActionScore(new[]{0,1,2,3,4,2,3,5},new[]{740,560,660,580,940,570,660,780})},
            {"wipe",new ActionScore(new[]{0,1,2,3,2,3,4,3,5},new[]{730,560,520,530,460,520,1150,650,680})},
            {"pen",new ActionScore(new[]{0,1,2,1,2,3,4,5},new[]{670,340,320,300,290,390,750,880})},
            {"board",new ActionScore(new[]{0,1,2,1,2,3,4,5},new[]{740,470,550,440,480,1500,580,900})},
            {"fish",new ActionScore(new[]{0,1,2,3,2,3,4,5},new[]{650,440,1350,390,820,320,560,1300})},
            {"fishcatch",new ActionScore(new[]{0,1,2,3,2,3,4,5},new[]{650,440,1350,390,820,320,560,1750})},
            {"pillar",new ActionScore(610,490,580,1230,640,880)},
            {"bowdraw",new ActionScore(660,470,700,1100,540,680)},
            {"blockguard",new ActionScore(600,460,570,980,480,710)},
            {"handoff",new ActionScore(390,460,800,500,500,550)},
            {"returnnote",new ActionScore(420,450,600,530,1380,720)},
            {"projectstart",new ActionScore(850,780,1100,850,1250,1100)},
            {"projectwork",new ActionScore(900,940,1050,870,1100,1320)},
            {"projectfinish",new ActionScore(780,820,950,850,1100,1950)},
            {"propbridge",new ActionScore(80,80,100,100,110,90)},
            {"turnstep",new ActionScore(80,90,100,110,120,140)},
            {"penbridge",new ActionScore(80,90,100,100,110,90)},
            {"toolbridge",new ActionScore(80,90,100,100,110,90)},
            {"tune",new ActionScore(480,960,460,670,950,800)},
            {"mutepractice",new ActionScore(new[]{0,1,2,3,2,3,4,1,2,3,4,5},new[]{620,480,250,290,240,300,560,420,260,310,560,800})},
            {"stringwipe",new ActionScore(new[]{0,1,2,3,2,3,4,5},new[]{650,560,720,650,620,670,830,780})},
            {"listenbeat",new ActionScore(690,540,600,580,630,730)},
            {"quietrest",new ActionScore(700,730,1500,1850,950,880)},
            {"duetphrase",new ActionScore(1050,670,750,1200,740,720)},
            {"duetleft",new ActionScore(1050,670,750,1200,740,720)},
            {"mortispose",new ActionScore(480,400,450,380,1550,760)},
            {"stageribbon",new ActionScore(520,570,820,780,600,730)},
            {"hatbow",new ActionScore(560,620,790,1240,690,820)},
            {"curtaincall",new ActionScore(580,640,920,1450,800,920)},
            {"guitarbridge",new ActionScore(90,90,90,90,90,90)},
            {"leafcheck",new ActionScore(640,650,880,1200,650,720)}
        };
        internal static readonly string[] Names=scores.Keys.ToArray();
        internal static readonly ActionScore TuneAfterBow=new ActionScore(new[]{1,2,3,2,5},new[]{400,560,520,500,680});
        // 0–5 are her partner-facing drawings; 6, 7, 8 play a downstroke, lift and second stroke.
        internal static readonly ActionScore RehearsalLead=new ActionScore(
            new[]{0,1,2,6,7,8,4,3,5},new[]{3550,500,550,320,180,600,900,3050,1550});
        // Includes the guest's entrance, the hesitation, the two-note demonstration, and Bocchi's answer.
        internal static readonly ActionScore RehearsalReply=new ActionScore(
            new[]{0,1,2,1,2,3,2,3,4,3,5},new[]{2300,450,370,3480,450,450,450,450,450,800,1550});
        internal static readonly ActionScore QuietRestWithStool=new ActionScore(
            new[]{0,1,2,3,4,5,6,7,8,9,10},new[]{540,540,600,700,730,1500,1850,950,880,600,650});
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
        static readonly string[] maidDay={"tea","game","fold","rest","visit","wipe","tea","focus","fold","game","rest","focus"};
        static readonly string[] labDay={"focus","pen","tea","game","rest","board","visit","pen","tea","focus","rest","game"};
        static readonly string[] minerDay={"mine","fish","tea","focus","game","pillar","visit","mine","rest","tea","game","focus",
                                          "bowdraw","fish","mine","rest","blockguard","tea","focus","mine"};
        static readonly string[] guitarDay={"focus","pick","tea","rest","game","bow","visit","focus","pick","tea","game","rest"};
        static readonly string[] mutsumiDay={"tune","mutepractice","tea","focus","stringwipe","rest","visit","game",
                                             "leafcheck","mutepractice","listenbeat","tea","tune","focus","quietrest","visit"};
        static readonly string[] stageDay={"hatbow","stageribbon","mutepractice","tea","curtaincall","focus","visit","curtaincall",
                                          "listenbeat","stageribbon","tune","rest","leafcheck","quietrest","hatbow","visit",
                                          "stringwipe","stageribbon","game","curtaincall"};
        static readonly string[] guestDay={"tea","game","focus","rest","visit","tea","focus","game","rest"};
        static readonly string[] guestMinerDay={"mine","game","tea","focus","rest","mine","visit","tea","game"};
        static readonly TimeSpan MaxInterruption=TimeSpan.FromMinutes(12);
        sealed class InterruptedActivity
        {
            internal string Name,Hint,Bridge;
            internal BitmapSource[] Poses;
            internal long Elapsed,InterruptedAt,RevealedFor;
            internal int HiddenBox,Guess,Grip;
            internal bool Tuning,EncoreLineSaid;
        }
        InterruptedActivity interrupted;
        BitmapSource carriedPose;
        bool bowTuning,cursorNearBow,bowApproached,encoreLineSaid;
        bool PerformingPaper { get { return performance=="handoff" || performance=="returnnote"; } }
        bool UsesMutsumiActions { get { return character=="mutsumi" || (!CompanionArchive.IsBuiltin(character) && ActionFrames(character,"tune")!=null); } }
        bool UsesStageActions { get { return currentArtId=="mutsumi-stage" || (!CompanionArchive.IsBuiltin(character) && ActionFrames(character,"hatbow")!=null); } }
        public string CurrentActivity { get { return visitor!=null?"visit":dancing?"song":bridgeKind=="pickup"?"pickup":
            (app.FocusActive && focusCue.Started?"focus":performance); } }
        public bool OfferingGame { get { return soloGame && gameResultAt==0; } }

        internal BitmapSource[] ActionFrames(string id,string key)
        {
            return ReadActionArt(app.CharacterArtId(id),CompanionArchive.IsBuiltin(id),key);
        }
        BitmapSource[] ReadActionArt(string artId,bool builtin,string key)
        {
            string token=artId+"|"+key;
            BitmapSource[] clips;
            if(authored.TryGetValue(token,out clips))return clips;
            string filename="motion-"+artId+"-"+key+".png";
            string path=builtin?app.Asset(filename):Path.Combine(app.UserFolder,"companions",artId,key+".png");
            if(File.Exists(path))
            {
                try
                {
                    BitmapSource bitmap;
                    using(var file=File.OpenRead(path))bitmap=new PngBitmapDecoder(file,BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.OnLoad).Frames[0];
                    if(bitmap.CanFreeze)bitmap.Freeze();
                    const int celCount=6;
                    if(bitmap.PixelWidth==1152 && bitmap.PixelHeight==208*((celCount+5)/6))
                    {
                        clips=new BitmapSource[celCount];
                        for(int i=0;i<celCount;i++) { var pose=new CroppedBitmap(bitmap,new Int32Rect(i%6*192,i/6*208,192,208));pose.Freeze();clips[i]=pose; }
                    }
                }
                catch(IOException) { }
                catch(NotSupportedException) { }
                catch(FileFormatException) { }
            }
            authored[token]=clips;

            return clips;
        }

        void ShowPose(BitmapSource pose)
        {
            currentRow=currentFrame=-1;
            PresentFrame(pose);
        }
        bool BeginPerformance(string action,bool independent)
        {
            EndDesktopScene(true);
            var sheet=ActionFrames(character,action);
            if(sheet==null)return false;
            if(action=="quietrest"){
                var stool=ActionFrames(character,"rest");
                if(stool!=null)sheet=new[]{stool[0],stool[1],stool[2],sheet[0],sheet[1],sheet[2],
                    sheet[3],sheet[4],sheet[5],stool[4],stool[5]};
            }
            InterruptPerformance();
            row=Idle;effectUntil=0;
            activePoses=sheet;performance=action;
            bool pickingUp=IsGuitarActivity(action) && TakeStandingGuitar("pickup");
            if(pickingUp)pickupElapsed=0;
            performanceStart=LoweringProp || pickingUp?BridgeEnd:clock.ElapsedMilliseconds;
            independentPerformance=independent;
            if(action=="bow")cursorNearBow=IsPointerNearPet();
            if(!LoweringProp && !pickingUp)ShowPose(sheet[0]);
            if(independent && action=="game" && !UsesMutsumiActions)StartBoxScene();
            return true;
        }
        void StopPerformance()
        {
            if(gameStage!=null)gameStage.Visibility=Visibility.Collapsed;
            activePoses=null;performance="";soloGame=false;gameResultAt=0;
            independentPerformance=false;bowTuning=false;bowApproached=false;encoreLineSaid=false;
        }
        void InterruptPerformance(bool keepGuitar=false)
        {
            if(activePoses!=null && independentPerformance)
            {
                long now=clock.ElapsedMilliseconds;
                long elapsed=bridgeKind=="pickup"?pickupElapsed:Math.Max(0,now-performanceStart);
                interrupted=new InterruptedActivity {
                    Name=performance,Poses=activePoses,Elapsed=elapsed,InterruptedAt=now,
                    HiddenBox=hiddenBox,Guess=winningGuess,Hint=lastDirection,
                    RevealedFor=gameResultAt==0?-1:now-gameResultAt,Tuning=bowTuning,EncoreLineSaid=encoreLineSaid
                };
                if(!dragging)SetPropAside(interrupted,keepGuitar);
            }
            StopPerformance();
        }
        void ForgetPerformance()
        {
            interrupted=null;parkedProp.Source=null;parkedMusicGuitar.Source=null;musicGuitarParked=false;
            carriedPose=null;ClearBridge();StopPerformance();
        }
        bool ResumePerformance(long now)
        {
            if(interrupted==null || CompanionOccupied() || (app.FocusActive && !app.FocusPaused))return false;
            var saved=interrupted;interrupted=null;
            performance=saved.Name;activePoses=saved.Poses;independentPerformance=true;
            performanceStart=now-saved.Elapsed;row=Idle;effectUntil=0;bowTuning=saved.Tuning;encoreLineSaid=saved.EncoreLineSaid;
            bool takingGuitar=IsGuitarActivity(performance) && TakeStandingGuitar("pickup");
            if(takingGuitar || (saved.Bridge!=null && BeginBridge("pickup",ActionFrames(character,saved.Bridge),5,saved.Grip)))
            {
                parkedProp.Source=null;pickupElapsed=saved.Elapsed;
                performanceStart=BridgeEnd-saved.Elapsed;
                return true;
            }
            if(performance=="bow")cursorNearBow=IsPointerNearPet();
            if(performance=="game" && !UsesMutsumiActions)
            {
                soloGame=true;hiddenBox=saved.HiddenBox;winningGuess=saved.Guess;lastDirection=saved.Hint;
                gameResultAt=saved.RevealedFor<0?0:now-saved.RevealedFor;
                gameStage.Visibility=Visibility.Visible;
                RenderBoxes(saved.Elapsed>=2700 && gameResultAt==0,gameResultAt!=0);
            }
            return AdvancePerformance(now);
        }
        internal void PlayPaperAction(bool returning)
        {
            if(disposed || app.Data.Quiet || !IsVisible || manuallyHidden || fullscreenHidden)return;
            EndVisit();
            bool guitarInHand=UsesMutsumiActions && dancing && PlayingGuitarToMusic;
            StopSongMovement();
            ParkMusicGuitar(guitarInHand);
            if(!BeginPerformance(returning?"returnnote":"handoff",false))
                SetEffect(returning?Wave:Review,1600);
            RefreshKaraokeOverlay();
        }
        void FinishPerformance()
        {
            bool paper=PerformingPaper;
            CompleteProjectChapter();
            StopPerformance();SetEffect(Idle,0);ambientAt=NextAmbient();
            if(paper)RefreshKaraokeOverlay();
        }
        internal void OpeningTool()
        {
            EndDesktopScene(true);
            EndIndependentVisit();
            if(independentPerformance)InterruptPerformance();
        }
        void ReleaseCharacterActions(string old)
        {
            if(old==null || (old!="mutsumi" && CompanionArchive.IsBuiltin(old)))return;
            foreach(string key in ActionScore.Names){authored.Remove(old+"|"+key);floorDrawings.Remove(old+"|"+key);}
        }
        bool AdvancePerformance(long now)
        {
            if(activePoses==null)return false;
            long t=Math.Max(0,now-performanceStart);
            if(soloGame)return AdvanceBoxScene(now,t);
            if(performance=="bow" && !bowTuning)
            {
                if(NoticedAudience(IsPointerNearPet(),t))
                {
                    var tuning=ActionFrames(character,"focus");
                    if(tuning!=null){activePoses=tuning;bowTuning=true;performanceStart=now;t=0;}
                }
            }
            var score=bowTuning?ActionScore.TuneAfterBow:ActionScore.For(performance);
            if(performance=="quietrest" && activePoses.Length==11)score=ActionScore.QuietRestWithStool;
            if(t>=score.Duration){FinishPerformance();return false;}
            int pose=score.At(t);
            ShowPose(activePoses[pose]);
            if(performance=="mortispose" && pose==4 && !encoreLineSaid){
                encoreLineSaid=true;Say("全都不会弹！");
            }
            return true;
        }
        bool NoticedAudience(bool near,long elapsed)
        {
            bowApproached=near && (bowApproached || !cursorNearBow);
            cursorNearBow=near;
            return bowApproached && elapsed>=1450 && elapsed<3800;
        }
        int RoutineIndex
        {
            get {
                switch(character) {
                    case "rem": return app.Data.MaidRoutine;
                    case "kurisu": return app.Data.LabRoutine;
                    case "steve": return app.Data.MinerRoutine;
                    case "hitori": return app.Data.GuitarRoutine;
                    case "mutsumi": return currentArtId=="mutsumi-stage"?app.Data.MutsumiStageRoutine:app.Data.MutsumiRoutine;
                    default: return app.Data.GuestRoutine;
                }
            }
            set {
                switch(character) {
                    case "rem": app.Data.MaidRoutine=value;break;
                    case "kurisu": app.Data.LabRoutine=value;break;
                    case "steve": app.Data.MinerRoutine=value;break;
                    case "hitori": app.Data.GuitarRoutine=value;break;
                    case "mutsumi":
                        if(currentArtId=="mutsumi-stage")app.Data.MutsumiStageRoutine=value;
                        else app.Data.MutsumiRoutine=value;
                        break;
                    default: app.Data.GuestRoutine=value;break;
                }
            }
        }
        long desktopSceneDue=long.MaxValue, desktopSceneSince=-1;
        BitmapSource[] desktopScenePoses;
        string desktopSceneArt;
        bool ShowingDesktopScene { get { return desktopSceneSince>=0; } }

        void ScheduleDesktopScene(bool first)
        {
            desktopSceneDue=clock.ElapsedMilliseconds+1000L*
                WindowPerchMotion.VignetteDelaySeconds(app.Data.ActivityPace,first,random.Next(100000));
        }

        bool TryStartDesktopScene(long now)
        {
            long ripeBatch=0;
            if(currentArtId=="mutsumi")
            {
                var project=app.Data.ProjectFor("mutsumi");
                if(!ProjectPlantTimeline.MayShowCucumberScene(project.Stage,project.NextUtc,DateTime.UtcNow.Ticks,
                    app.Data.CucumberFruitSceneUtc))return false;
                ripeBatch=project.NextUtc;
            }
            if((ripeBatch==0 && now<desktopSceneDue) || perchTarget!=IntPtr.Zero || CompanionOccupied() || PerformingPaper ||
                (app.FocusActive && !app.FocusPaused) || !WindowPerchMotion.Supports(currentArtId))return false;
            if(desktopSceneArt!=currentArtId || desktopScenePoses==null)
            {
                desktopScenePoses=LoadWindowPerchPoses(currentArtId,PerchMode.Scene);
                desktopSceneArt=currentArtId;
            }
            ScheduleDesktopScene(false);
            if(desktopScenePoses==null)return false;
            desktopSceneSince=now;
            if(ripeBatch>0){app.Data.CucumberFruitSceneUtc=ripeBatch;app.Save();}
            backpackButton.Visibility=Visibility.Collapsed;
            parkedProp.Visibility=parkedMusicGuitar.Visibility=Visibility.Collapsed;
            ShowPose(desktopScenePoses[0]);
            // Let this vignette yield naturally to the rest of the usual routine.
            ambientAt=Math.Max(ambientAt,now+WindowPerchMotion.SceneDuration(currentArtId)+1500);
            return true;
        }

        bool AdvanceDesktopScene(long now)
        {
            if(!ShowingDesktopScene)return false;
            if(perchTarget!=IntPtr.Zero || desktopSceneArt!=currentArtId || CompanionOccupied() ||
                PerformingPaper || (app.FocusActive && !app.FocusPaused && !HasFocusCostume))
            {EndDesktopScene(true);return false;}
            if(currentArtId=="mutsumi")
            {
                var project=app.Data.ProjectFor("mutsumi");
                if(!ProjectPlantTimeline.HasRipeCucumber(project.Stage,project.NextUtc,DateTime.UtcNow.Ticks))
                {EndDesktopScene(true);return false;}
            }
            int index=WindowPerchMotion.DesktopFrameAt(now-desktopSceneSince,currentArtId);
            if(index<0){EndDesktopScene(true);return false;}
            ShowPose(desktopScenePoses[index]);
            return true;
        }

        void EndDesktopScene(bool restore)
        {
            if(!ShowingDesktopScene)return;
            desktopSceneSince=-1;
            RefreshBagAttachment();
            if(restore && !perchAligned && !disposed)
            {
                row=Idle;
                shownSince=clock.ElapsedMilliseconds;
                effectUntil=0;
                DrawFrame(Idle,0);
            }
        }

        public void RefreshRoutinePace()
        {
            schedulePace=app.Data.ActivityPace??"normal";
            int first=schedulePace=="lively"?9:schedulePace=="calm"?35:16;
            ambientAt=clock.ElapsedMilliseconds+1000*random.Next(first,first+10);
            ScheduleDesktopScene(true);
        }
        long NextAmbient()
        {
            int min=48,max=106;
            if(app.Data.ActivityPace=="lively"){min=23;max=48;}
            else if(app.Data.ActivityPace=="calm"){min=150;max=290;}
            return clock.ElapsedMilliseconds+1000*random.Next(min,max);
        }
        bool ToolOpen()
        {
            return (quickMenu!=null && quickMenu.IsOpen) || menu.Visible ||
                (app.Desk!=null && app.Desk.IsVisible) ||
                (app.NotesDesk!=null && app.NotesDesk.IsVisible && !app.NotesDesk.KeepsStickyOpen);
        }
        bool MusicPlaying { get { return (app.Music!=null && app.Music.Playing) || (lyricPlaying && KaraokeVisible); } }
        bool CompanionOccupied()
        {
            return app.Data.Quiet || !IsVisible || manuallyHidden || fullscreenHidden || visitor!=null || dragging || pressing ||
                pendingClickAt>0 || ToolOpen() || MusicPlaying;
        }
        string[] DailyRoutine()
        {
            switch(character)
            {
                case "hitori":return guitarDay;
                case "mutsumi":return currentArtId=="mutsumi-stage"?stageDay:mutsumiDay;
                case "kurisu":return labDay;
                case "steve":return minerDay;
                case "rem":return maidDay;
            }
            if(UsesStageActions)return stageDay;
            if(UsesMutsumiActions)return mutsumiDay;
            if(ActionFrames(character,"pick")!=null)return guitarDay;
            if(ActionFrames(character,"pen")!=null)return labDay;
            if(ActionFrames(character,"fish")!=null)return minerDay;
            if(ActionFrames(character,"fold")!=null)return maidDay;
            return ActionFrames(character,"mine")!=null?guestMinerDay:guestDay;
        }
        void Ambient()
        {
            long now=clock.ElapsedMilliseconds;
            if(CompanionOccupied()){ambientAt=now+4500;return;}
            if(app.FocusActive && !app.FocusPaused)
            {
                if(HasFocusCostume)return;
                if(!BeginPerformance("focus",false))SetEffect(Work,4200);
                ambientAt=now+36000;return;
            }
            if(TryStartProject())return;
            var plan=DailyRoutine();
            int ix=Math.Max(0,RoutineIndex)%plan.Length;
            string action=plan[ix];
            if(UsesStageActions){
                if(action=="curtaincall" && random.Next(5)==0 && ActionFrames(character,"mortispose")!=null)action="mortispose";
                if((action=="curtaincall" || action=="hatbow" || action=="stageribbon") && ActionFrames(character,action)==null)action="listenbeat";
            }
            if(action=="leafcheck" && character=="mutsumi" && !HasTrellisedCucumber())action="focus";
            if(action=="pick" && random.Next(3)==0 && ActionFrames(character,"pickcatch")!=null)action="pickcatch";
            if(action=="fish" && random.Next(4)==0 && ActionFrames(character,"fishcatch")!=null)action="fishcatch";
            if(action=="visit") StartVisit(true);
            else if(!BeginPerformance(action,true))
            {
                // Legacy packs can still tell a story using their own authored poses.
                int pose=action=="game"?Wave:action=="rest"?Wait:Review;
                SetEffect(pose,Counts[pose]*FrameMs[pose]*2);
            }
            RoutineIndex=(ix+1)%plan.Length;
            app.Save();
            ambientAt=NextAmbient();
        }
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
                gameStage.IsVisibleChanged+=delegate { PositionAttachments(); };
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
                boxArt[i].Reveal(correct,character,winningGuess==i&&!correct);
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
            app.Save();
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
                Say((character=="hitori"?"拨片藏好了。不在":"我藏好啦。给你个提示，不在")+lastDirection+"。");
                RenderBoxes(true,false);
            }
            int pose=t<2600?ActionScore.For("game").At(t):((t/1150)%3==0?4:3);
            ShowPose(activePoses[pose]);return true;
        }
    }
    internal sealed class DeskChest : FrameworkElement
    {
        bool open,wrong;
        string character;
        public DeskChest(){Width=39;Height=32;}
        public void Reveal(bool opened,string who,bool missed)
        {open=opened;character=who;wrong=missed;InvalidateVisual();}
        static void Shape(DrawingContext dc,string fill,string outline,string path)
        {dc.DrawGeometry(Ui.Color(fill),new Pen(Ui.Color(outline),1.15),Geometry.Parse(path));}
        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawEllipse(Ui.Color("#550B1816"),null,new Point(20,29.5),17,2.2);
            string dark=character=="steve"?"#4A3525":"#563E2C";
            Shape(dc,wrong?"#A7836B":"#C9965A",dark,"M3,14 L30,14 30,28 3,27 Z");
            Shape(dc,"#8D603B",dark,"M30,14 L36,10 36,24 30,28 Z");
            if(open)
            {
                Shape(dc,"#8C6A49",dark,"M4,13 L4,5 32,4 34,10 30,14 Z");
                Shape(dc,"#66503A",dark,"M4,14 L30,14 35,11 10,11 Z");
                if(character=="steve")Shape(dc,"#59CF87","#285A42","M19,5 L23,9 18,16 14,10 Z");
                else if(character=="hitori")Shape(dc,"#6BC2C6","#315D68","M14,6 Q18,3 23,6 Q25,9 19,16 Q14,13 13,9 Z");
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
