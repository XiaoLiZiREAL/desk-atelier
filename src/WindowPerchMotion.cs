using System;

namespace DeskMate
{
    internal enum PerchMode { Edge, Scene }

    internal static class WindowPerchMotion
    {
        internal const double EdgeY=132.0/208.0;
        static readonly string[] Roles={"hitori","rem","kurisu","mutsumi","mutsumi-stage","steve","kuroneko"};
        static readonly int[][] EdgeMs={
            new[]{600,570,260,330,480,1130},new[]{540,550,640,360,590,710},new[]{490,380,820,380,440,620},
            new[]{400,350,420,360,510,780},new[]{600,390,630,480,360,610},
            new[]{160,105,95,95,87,200,95,95,95,150,110,100,180,100,110,160},new[]{360,430,620,440,530,750}};
        // A different pace for each story: legible entrance, a held payoff, and
        // a readable exit. Keep the original source cels sharp instead of blending.
        static readonly int[][] SceneMs={
            new[]{760,620,590,1520,760,1050},new[]{800,650,850,1400,820,780},new[]{750,680,650,1470,830,820},
            new[]{810,770,1240,960,730,690},new[]{760,750,690,1380,860,860},new int[0],
            new[]{680,520,680,420,580,3400,510,480,360,310,420,540}};
        internal static int PoseCount(string art,PerchMode mode)
        {
            if(!Supports(art))return 0;
            if(art=="kuroneko" && mode==PerchMode.Scene)return 12;
            if(art!="steve")return 6;
            return mode==PerchMode.Edge?16:85;
        }
        internal static string SpriteFile(string art,PerchMode mode)
        {
            return (mode==PerchMode.Edge?"window-edge-":"window-scene-")+art+".png";
        }
        internal static int FrameAt(long milliseconds,string art,PerchMode mode)
        {
            int index=Array.IndexOf(Roles,art);
            if(index<0)return 0;
            long now=Math.Max(0,milliseconds);
            if(mode==PerchMode.Scene && index==5)return (int)((now/60)%85);
            int[] score=mode==PerchMode.Edge?EdgeMs[index]:SceneMs[index];
            return Step(now%Total(score),score);
        }
        internal static int ActivityDuration(string art)
        {
            int index=Array.IndexOf(Roles,art);
            return index<0?0:Total(EdgeMs[index]);
        }

        internal static int SceneDuration(string art)
        {
            int index=Array.IndexOf(Roles,art);
            return index<0?0:index==5?85*60:Total(SceneMs[index]);
        }

        // Non-window scenes play once. Cancellation is owned by the desk routine,
        // not by window geometry or focus.
        internal static int DesktopFrameAt(long elapsed,string art)
        {
            int duration=SceneDuration(art);
            return duration<=0 || elapsed>=duration?-1:FrameAt(elapsed,art,PerchMode.Scene);
        }

        internal static int VignetteDelaySeconds(string pace,bool first,int variation)
        {
            int min,max;
            if(pace=="lively"){min=first?10:48;max=first?17:85;}
            else if(pace=="calm"){min=first?45:210;max=first?81:361;}
            else{min=first?18:95;max=first?33:171;}
            return min+Math.Max(0,variation)%(max-min);
        }

        internal static bool Supports(string art)
        {
            return art=="hitori" || art=="rem" || art=="kurisu" || art=="mutsumi" || art=="mutsumi-stage" || art=="steve" || art=="kuroneko";
        }
        static int Total(int[] durations)
        {
            int sum=0;
            foreach(int duration in durations)sum+=duration;
            return sum;
        }
        static int Step(long milliseconds,int[] durations)
        {
            for(int step=0;step<durations.Length;step++)
            {
                if(milliseconds<durations[step])return step;
                milliseconds-=durations[step];
            }
            return durations.Length-1;
        }
    }
    internal struct SeatPose
    {
        internal readonly bool Activity, Lounge, Swing;
        internal readonly int Frame;
        internal SeatPose(bool activity,int frame,bool lounge=false,bool swing=false){Activity=activity;Frame=frame;Lounge=lounge;Swing=swing;}
    }

    // The window coordinates and the pet's mood clock are deliberately independent.
    // A held neutral cel is the normal state; a moment may be skipped after sleep.
    internal sealed class WindowSeatRoutine
    {
        readonly bool vanilla;
        readonly Random random;
        readonly string role;
        readonly int mood;

        readonly bool canLounge,canSwing;
        int eventKind=-1,lastKind=-1,visits,untilActivity;
        bool proneNext;
        long start,next,nextLounge,previous=-1;
        internal static readonly int[] SideCels={22,23,24,25,26,25,27,23,22};
        internal static readonly int[] SideMs={230,270,330,2900,5650,2700,330,260,220};
        internal static readonly int[,] SideCadence={
            {340,290,250,3000,5900,2300,260,250,250},  // hesitates before curling
            {210,280,420,2400,6600,2250,375,270,215},  // settles calmly into sleep
            {265,295,275,3300,4700,3250,275,250,255},  // lingers in the elbow lean
            {300,355,410,3900,4450,2800,370,285,275},  // drops shoulder gently, rests with cheek on her sleeve
            {280,330,310,2700,6100,2450,300,245,225}};
        internal static int SideDuration(int frame,int mood)
        {
            return mood<0?SideMs[frame]:SideCadence[mood,frame];
        }
        internal static readonly int[] ProneCels={12,9,13,16,17,18,17,19,20,19,21,19,20,21,19,21,20,19,20,21,19,17,16,15,13,9,12};
        internal static readonly int[] ProneMs={185,160,205,220,1750,1100,590,800,280,390,310,650,280,310,480,310,280,730,300,330,580,990,230,230,190,155,215};
        internal static readonly int[] ProneRates={94,112,103,137,88};
        // The eight accented beats are hand-scored, not the same swing sped up:
        // sleeve-peek flutter, sleepy pauses, even pencil-tap, slow toe lift,
        // and a syncopated response to the stage outfit's hat gesture.
        internal static readonly int[,] ProneAccents={
            {20,21,20,21,21,20,20,21},
            {20,19,21,19,20,19,21,19},
            {20,21,20,21,20,21,20,21},
            {21,19,20,19,21,19,20,19},
            {21,20,21,21,20,20,21,20}};
        internal static readonly int[] AccentSlots={8,10,12,13,15,16,18,19};
        internal static int ProneFrame(int frame,int mood)
        {
            int beat=Array.IndexOf(AccentSlots,frame);
            return mood<0 || beat<0?ProneCels[frame]:ProneAccents[mood,beat];
        }
        internal static int ProneDuration(int frame,int mood)
        {
            int cel=ProneCels[frame];
            int rate=mood<0?100:ProneRates[mood];
            return cel>=19?ProneMs[frame]*rate/100:ProneMs[frame];
        }

        // Whole-cel alternating swing. The true neutral between feet is a
        // visible pause, so there is never a two-foot kick at the changeover.
        internal static readonly int[] SwingCels={-1,0,1,0,-1,2,3,2,-1,0,1,0,-1,2,3,2,-1,0,1,0,-1,2,3,2,-1};
        internal static readonly int[,] SwingCadence={
            {230,130,120,130,310,150,120,155}, // Bocchi hesitates between small lifts
            {150,170,215,170,150,170,215,170}, // Rem is gentle and even
            {150,105,210,110,180,200,145,160}, // Kurisu marks an uneven beat
            {260,180,150,215,330,165,140,220}, // uniform Mutsumi: patient, light
            {145,125,245,145,260,150,183,190}}; // stage costume: subtly syncopated
        internal static readonly int[] SwingLead={0,0,2,0,2};
        internal const int SwingPoseCount=4;
        internal static int SwingFrame(int index,int mood)
        {
            int frame=SwingCels[index];
            return frame<0 || mood<0?frame:(frame+SwingLead[mood])%SwingPoseCount;
        }
        internal static int SwingDuration(int index,int mood)
        {
            return index==SwingCels.Length-1?235:SwingCadence[mood<0?1:mood,index%8];
        }
        // She lies all the way down with cheek supported by folded forearms.
        // The flat pelvis and chest stay fixed while her small feet move.
        internal static readonly int[] StageProneCels={-1,12,13,16,17,18,19,20,21,19,20,21,19,20,21,18,17,16,11,14,15,-1};
        internal static readonly int[] StageProneMs={230,235,285,300,400,2100,1320,370,360,790,390,450,820,410,420,950,280,240,290,340,290,230};
        static readonly int[][] Cels={new[]{1},new[]{0,2,2,0},new[]{0,3,3,0},new[]{0,4,0,4,0},new[]{0,5,5,0}};
        static readonly int[][] Lengths={new[]{130},new[]{100,220,210,140},new[]{110,240,210,150},
            new[]{120,190,180,190,200},new[]{120,235,215,160}};
        static readonly int[] VanillaChoices={1,2,4};
        readonly bool kuronekoProne;
        static readonly int[] KuronekoSideHolds={240,370,420,5500,420,690};
        static readonly int[] KuronekoProneHolds={340,350,380,3300,600,1350,270,310,530};
        static readonly int[] KuronekoProneFrames={6,7,8,9,10,9,8,7,11};
        static readonly int[] KuronekoSwingFrames={-1,0,1,2,3,0,-1,4,5,6,7,4,-1};
        static readonly int[] KuronekoSwingHolds={360,115,140,225,135,125,420,115,135,215,150,115,540};
        internal static SeatPose KuronekoPauseAt(long t,bool prone)
        {
            int[] holds=prone?KuronekoProneHolds:KuronekoSideHolds;
            for(int i=0;i<holds.Length;i++)
            {
                if(t<holds[i])return new SeatPose(false,prone?KuronekoProneFrames[i]:i,true);
                t-=holds[i];
            }
            return new SeatPose(false,0);
        }
        internal static SeatPose KuronekoSwingAt(long t,out bool ended)
        {
            ended=false;
            for(int i=0;i<KuronekoSwingHolds.Length;i++)
            {
                int frame=KuronekoSwingFrames[i], hold=KuronekoSwingHolds[i];
                if(t<hold)return frame<0?new SeatPose(false,0):new SeatPose(false,frame,false,true);
                t-=hold;
            }
            ended=true;return new SeatPose(false,0);
        }
        internal WindowSeatRoutine(string role,int seed,long now,bool haveLounge=false,bool haveSwing=false,bool haveKuronekoProne=false)
        {
            this.role=role;
            mood=Array.IndexOf(new[]{"hitori","rem","kurisu","mutsumi","mutsumi-stage"},role);
            vanilla=role=="steve";
            canLounge=haveLounge && !vanilla;
            canSwing=haveSwing && !vanilla;
            kuronekoProne=haveKuronekoProne;
            random=new Random(seed);
            Reset(now);
        }
        internal void Reset(long now)
        {
            eventKind=lastKind=-1;visits=0;
            untilActivity=random.Next(3,6);
            previous=-1;
            next=now+random.Next(3100,5101);
            if(canLounge){nextLounge=now+random.Next(35000,60001);proneNext=random.Next(2)==1;}
        }
        internal void InterruptForGesture(long now)
        {
            eventKind=-1;next=now+3400;previous=now;
        }
        internal SeatPose At(long now,bool quiet,bool holdNewActivity=false)
        {
            if(previous>=0 && (now<previous || now-previous>12500))Reset(now);
            previous=now;
            if(quiet && eventKind>=5){eventKind=-1;next=now+6000;}
            if(eventKind<0 && now>=next && !holdNewActivity)
            {
                if(canLounge && !quiet && now>=nextLounge)
                {
                    eventKind=proneNext && (role!="kuroneko" || kuronekoProne)?7:6;
                    proneNext=!proneNext;
                    nextLounge=now+random.Next(72000,116001);
                }
                else if(!quiet && visits>=untilActivity)
                {
                    eventKind=5;visits=0;untilActivity=random.Next(3,6);
                }
                else
                {
                    int choice;
                    do{choice=vanilla?VanillaChoices[random.Next(VanillaChoices.Length)]:random.Next(Cels.Length);}
                    while(choice==lastKind);
                    lastKind=eventKind=choice;
                    if(!quiet)visits++;
                }
                start=now;
            }
            if(eventKind<0)return new SeatPose(false,0);
            long t=now-start;
            if(eventKind>=6)
            {
                if(role=="kuroneko")
                {
                    SeatPose pause=KuronekoPauseAt(t,eventKind==7);
                    if(pause.Lounge)return pause;
                    eventKind=-1;next=now+3500;
                    return new SeatPose(false,0);
                }
                bool stageProne=eventKind==7 && mood==4;
                int[] cels=eventKind==6?SideCels:stageProne?StageProneCels:ProneCels;
                for(int i=0;i<cels.Length;i++)
                {
                    int ms=stageProne?StageProneMs[i]:eventKind==7?ProneDuration(i,mood):SideDuration(i,mood);
                    int frame=eventKind==7 && !stageProne?ProneFrame(i,mood):cels[i];
                    if(t<ms)return frame<0?new SeatPose(false,0):new SeatPose(false,frame,true);
                    t-=ms;
                }
            }
            else if(eventKind==3 && canSwing)
            {
                if(role=="kuroneko")
                {
                    bool ended;
                    SeatPose swing=KuronekoSwingAt(t,out ended);
                    if(!ended)return swing;
                }
                else for(int i=0;i<SwingCels.Length;i++)
                {
                    int ms=SwingDuration(i,mood),frame=SwingFrame(i,mood);
                    if(t<ms)return frame<0?new SeatPose(false,0):new SeatPose(false,frame,false,true);
                    t-=ms;
                }
            }
            else if(eventKind==5)
            {
                if(t<WindowPerchMotion.ActivityDuration(role))
                    return new SeatPose(true,WindowPerchMotion.FrameAt(t,role,PerchMode.Edge));
            }
            else
            {
                int[] lengths=Lengths[eventKind],cels=Cels[eventKind];
                for(int i=0;i<lengths.Length;i++)
                {
                    if(t<lengths[i])return new SeatPose(false,cels[i]);
                    t-=lengths[i];
                }
            }
            eventKind=-1;
            next=now+(quiet?random.Next(6500,10501):random.Next(3800,8501));
            return new SeatPose(false,0);
        }
    }
}
