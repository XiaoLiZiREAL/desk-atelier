using System;

namespace DeskMate
{
    // The animation earns no focus credit and never drives the timer. Only
    // time actually spent on screen advances the scene.
    internal sealed class FocusCompanionCue
    {
        static readonly string[] Roles={"kurisu","rem","hitori","mutsumi","mutsumi-stage","steve"};
        static readonly int[][] Frames={
            new[]{0,1,2,3,2,3,4,3},
            new[]{0,1,2,3,3,5,5,0},
            new[]{0,1,2,3,4,2,4,5},
            new[]{0,1,2,3,2,3,4,5},
            new[]{0,1,2,3,2,3,4,5},
            new[]{0,1,2,3,4,5}
        };
        static readonly int[][] Holds={
            new[]{550,540,1050,2300,4800,1800,1700,7300},
            new[]{800,680,1450,900,8800,1250,1000,12500},
            new[]{450,700,860,900,740,1550,1700,8800},
            new[]{680,680,820,1500,5550,1360,1000,7600},
            new[]{580,880,990,1630,5300,1460,1250,8900},
            new[]{400,210,155,280,500,4600}
        };
        MineShift mineShift=new MineShift(0);
        internal int MineVariant { get; private set; }
        internal int MinePreload { get; private set; }
        static readonly int[] EntryHolds={220,140,150,165,190,220};
        bool inSession, entered, exiting, completed, bridge;
        long previous=-1, elapsed, outTime;
        int held=-1, outFrom=-1, role=-1;
        internal static string MineOreAt(int cycle,int seed)
        {
            // A regular stone block between random draws keeps each physical
            // reveal and its next mine consistent and leaves ore uncommon.
            if((cycle&1)==0)return "stone";
            uint v=unchecked((uint)seed+0x9e3779b9U*(uint)((cycle+1)/2));
            v=unchecked((v^(v>>16))*0x7feb352dU);
            v=unchecked((v^(v>>15))*0x846ca68bU);
            int roll=(int)((v^(v>>16))%100);
            return roll<54?"stone":roll<71?"coal":roll<82?"iron":roll<88?"gold":
                roll<94?"redstone":roll<97?"lapis":roll<99?"diamond":"emerald";
        }
        internal static string MineSuffix(int cycle,int seed)
        {
            string now=MineOreAt(cycle,seed), next=MineOreAt(cycle+1,seed);
            return now=="stone" && next=="stone"?"":"-"+now+"-to-"+next;
        }
        internal static bool Supports(string art) { return Array.IndexOf(Roles,art)>=0; }
        internal bool Pending { get { return inSession || exiting; } }
        internal bool Started { get { return entered; } }
        internal int Cycle { get; private set; }
        internal bool ReachedEnd { get { return exiting && completed; } }
        internal const int BridgeFirst=256;

        internal void Start(int mineSeed=0)
        {
            mineShift=new MineShift(mineSeed);
            MineVariant=0; MinePreload=-1;
            inSession=true; entered=exiting=completed=false;
            previous=-1; elapsed=outTime=0; held=outFrom=-1; Cycle=0;
        }
        internal void Stop(bool success)
        {
            if(!inSession)return;
            inSession=false; exiting=entered; completed=success;
            outTime=0; outFrom=held; previous=-1;
        }
        internal int Step(string art,long now,bool ready,bool paused,bool hasBridge=false)
        {
            int index=Array.IndexOf(Roles,art);
            if(index!=role || (inSession && bridge!=hasBridge))
            {
                role=index; bridge=hasBridge; entered=exiting=false;
                elapsed=outTime=0; held=outFrom=-1; previous=-1; Cycle=0;
            }
            if(role<0 || !Pending || !ready)
            {
                previous=-1;
                if(!inSession){exiting=entered=false;held=-1;}
                return -1;
            }
            long delta=previous>=0 && now>=previous?Math.Min(now-previous,200):0;
            previous=now;
            if(exiting)
            {
                outTime+=delta;
                if(outTime<170)return outFrom;
                if(art=="steve")
                {
                    // Finishing the timer cannot break a block early or give an
                    // extra mining payoff. Leave the exact pose already reached.
                    if(outTime<370)return outFrom;
                    exiting=entered=false; return held=-1;
                }
                if(art=="rem" && outFrom>=2 && outFrom<BridgeFirst && outTime<350)return 5;
                if(bridge && outFrom>=BridgeFirst)
                {
                    int previousCell=outFrom-(int)((outTime-170)/160);
                    if(previousCell>=BridgeFirst)return previousCell;
                }
                else
                {
                    if(completed && outTime<970)return 5;
                    long close=outTime-(completed?970:170);
                    if(!bridge)
                    {
                        if(close<360)return 1;
                        if(close<730)return 0;
                    }
                    else
                    {
                        if(close<200)return 0;
                        int rise=BridgeFirst+EntryHolds.Length-1-(int)((close-200)/160);
                        if(rise>=BridgeFirst)return rise;
                    }
                }
                exiting=entered=false; return held=-1;
            }
            if(paused){previous=-1;return entered?held:-1;}
            entered=true;
            elapsed+=delta;
            long workTime=elapsed;
            if(bridge)
            {
                for(int cell=0;cell<EntryHolds.Length;cell++)
                {
                    if(workTime<EntryHolds[cell])return held=BridgeFirst+cell;
                    workTime-=EntryHolds[cell];
                }
            }
            if(art=="steve")
            {
                MineVariant=mineShift.Variant(workTime);
                MinePreload=mineShift.Preload(workTime);
                return held=mineShift.Frame(workTime);
            }
            int[] holds=Holds[role], frames=Frames[role];
            int loopStart=art=="rem"?0:2;
            long intro=loopStart==0?0:holds[0]+holds[1], body=0;
            for(int i=loopStart;i<holds.Length;i++)body+=holds[i];
            Cycle=workTime<intro?0:(int)((workTime-intro)/body);
            long t=workTime<intro?workTime:intro+(workTime-intro)%body;
            for(int i=0;i<holds.Length;i++)
            {
                if(t<holds[i])return held=frames[i];
                t-=holds[i];
            }
            return held=0;
        }
    }
}
