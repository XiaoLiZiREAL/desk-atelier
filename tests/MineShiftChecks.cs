using System;
using System.Collections.Generic;
using DeskMate;
static class MineShiftChecks
{
    static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
    static void Main(string[] args)
    {
        for(int seed=0;seed<200;seed++)
        {
            var shift=new MineShift(seed);
            var seen=new HashSet<int>();
            int previous=-1,quiet=0,rare=0,count=0;
            var shortest=int.MaxValue;var longest=0;
            Need(shift.SectionStart(MineShift.Sections)==MineShift.DurationMs,"route must end at exactly 30 minutes");
            for(int section=0;section<MineShift.Sections;section++)
            {
                int t=shift.SectionStart(section), length=shift.SectionStart(section+1)-t, exit=length-MineShift.ExitMs;
                shortest=Math.Min(length,shortest);longest=Math.Max(length,longest);
                Need(length>=12500 && length<=42000,"pace bounds");
                var cels=new HashSet<int>();
                Need(shift.Frame(t)==0 && shift.Frame(t+length-1)==MineShift.CelCount-1,"cut must finish behind rock and start at entry");
                int v=shift.Variant(t);
                if(previous>=0)Need(v/6!=previous/6,"same cave repeated in adjacent sections");
                for(int j=1;j<=8 && section>=j;j++)
                    Need(v!=shift.Variant(shift.SectionStart(section-j)),"scene reused too soon");
                if(v%6<2)quiet++;if(v%6==5)rare++;count++;
                for(int f=0;f<length;f+=50)
                {
                    int cel=shift.Frame(t+f);
                    cels.Add(cel);
                    Need(shift.Preload(t+f)==(length-f<=MineShift.PrefetchMs?shift.Variant(t+length):-1),"prefetch must follow the actual cut");
                    Need(cel>=0 && cel<MineShift.CelCount,"cel out of range");
                    Need(v==shift.Variant(t+f),"vein changed within a cave");
                    if(f>=exit)Need(cel>=MineShift.ExitFirst,"walk into next passage omitted");
                    if(f>=9000 && f<exit)Need(cel>=MineShift.QuietFirst && cel<MineShift.ExitFirst,"quiet pose out of phase");

                }
                int expected=v%6==0?220:v%6==1?236:252;
                Need(cels.Count>=expected && cels.Count<=Math.Min(252,expected+1),"authored approach, quiet moves or exit missing");
                previous=v;seen.Add(v);
            }
            Need(count==72 && seen.Count>=15,"not enough variety for 30m");
            Need(longest-shortest>=8000,"walking and mining should not feel paced to the same beat");
            Need(quiet>=12 && rare<=1,"needs quiet scenes and restrained rare drops");
        }
        var route=new MineShift(23);
        for(int j=0;j<72;j++)
            {
            int start=route.SectionStart(j);
            Console.WriteLine("{0:D2}:{1:D2} {2} {3:F2}s",start/60000,start/1000%60,MineShift.Catalog[route.Variant(start)],(route.SectionStart(j+1)-start)/1000.0);
        }
        Console.WriteLine("200 full 30-minute runs passed.");
    }
}
