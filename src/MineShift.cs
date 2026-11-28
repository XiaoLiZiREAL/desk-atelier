using System;
using System.Collections.Generic;

namespace DeskMate
{
    // A seeded route plan makes a full Pomodoro one expedition rather than a
    // twelve-second loop. It is derived once; pause/resume never draw new ore.
    internal sealed class MineShift
    {
        internal const int DurationMs=1800000, Sections=72, CelCount=252, QuietFirst=180, QuietCels=16, ExitFirst=228;
        internal const int ExitMs=1200, PrefetchMs=2600;
        const int ApproachMs=QuietFirst*50;
        readonly int[] starts=new int[Sections+1];
        internal static readonly string[] Catalog={
            "torch-pass","torch-check","torch-coal","torch-red","torch-lapis","torch-diamond",
            "fork-pass","fork-check","fork-coal","fork-iron","fork-gold","fork-emerald",
            "stairs-pass","stairs-check","stairs-coal","stairs-iron","stairs-red","stairs-diamond"
        };
        readonly int[] route=new int[Sections];
        readonly int[] glanceOrder=new int[Sections];

        internal MineShift(int seed)
        {
            Random rnd=new Random(seed);
            // Rare find is possible but not guaranteed, never a reward tied to
            // the user completing any timer or waiting a prescribed duration.
            int rareSlot=rnd.Next(3)==0?rnd.Next(13,Sections):-1;
            int[] uses=new int[Catalog.Length];
            for(int i=0;i<route.Length;i++)
            {
                var choices=new List<int>();
                for(int k=0;k<Catalog.Length;k++)
                {
                    if(i>0 && k/6==route[i-1]/6)continue;
                    if((k%6==5)!=(i==rareSlot))continue;
                    bool used=false;
                    for(int j=Math.Max(0,i-8);j<i;j++)if(route[j]==k){used=true;break;}
                    if(used)continue;
                    choices.Add(k);
                }
                bool quiet=(i==0 || (i>=2 && route[i-1]%6>=2 && route[i-2]%6>=2));
                if(quiet && i!=rareSlot)
                {
                    var breakChoices=choices.FindAll(k=>k%6<2);
                    if(breakChoices.Count>0)choices=breakChoices;
                }
                int fewest=int.MaxValue;
                foreach(int k in choices)if(uses[k]<fewest)fewest=uses[k];
                choices.RemoveAll(k=>uses[k]!=fewest);
                route[i]=choices[rnd.Next(choices.Count)];
                uses[route[i]]++;
                glanceOrder[i]=rnd.Next(3);
            }

            // Walking past a wall is brief; working a vein or looking around in
            // an unfamiliar place is slower. Preserve a complete 30-minute route
            // without stretching the authored walk, pick strike or rock cut.
            int[] lengths=new int[Sections];
            var rhythm=new Random(unchecked(seed^0x51e4a476));
            int budget=DurationMs/50;
            for(int i=0;i<Sections;i++)
            {
                int action=route[i]%6;
                int intent=action==0?17:action==1?23:action==5?32:27;
                lengths[i]=intent*20+rhythm.Next(-65,66);
                budget-=lengths[i];
            }
            int mean=budget/Sections;
            int remainder=budget%Sections;
            for(int i=0;i<Sections;i++)
            {
                int tick=lengths[i]+mean+(i<Math.Abs(remainder)?Math.Sign(remainder):0);
                starts[i+1]=starts[i]+tick*50;
            }
        }

        internal int SectionStart(int section) { return starts[section]; }

        static int Clock(long elapsed) { return (int)(Math.Max(0,elapsed)%DurationMs); }
        int Section(int clock)
        {
            int i=Array.BinarySearch(starts,clock);
            return i>=0?i:~i-1;
        }

        internal int Preload(long elapsed)
        {
            int clock=Clock(elapsed), section=Section(clock);
            return starts[section+1]-clock<=PrefetchMs?route[(section+1)%Sections]:-1;
        }

        internal int Variant(long elapsed)
        {
            return route[Section(Clock(elapsed))];
        }
        internal int Frame(long elapsed)
        {
            int clock=Clock(elapsed), section=Section(clock);
            int t=clock-starts[section], exitAt=starts[section+1]-starts[section]-ExitMs;
            if(t<ApproachMs)return t/50;
            if(t>=exitAt)return ExitFirst+(t-exitAt)*(CelCount-ExitFirst)/ExitMs;
            int since=t-ApproachMs, window=exitAt-ApproachMs;
            int action=route[section]%6;
            int order=glanceOrder[section], actions=action==0?1:action==1?2:3;
            for(int i=0;i<actions;i++)
            {
                int moment=window*(i+1)/(actions+1)-QuietCels*35;
                int drift=since-moment;
                if(drift>=0 && drift<QuietCels*70)
                    return QuietFirst+((order+i)%3)*QuietCels+(int)(drift/70);
            }
            return QuietFirst;
        }
    }
}
