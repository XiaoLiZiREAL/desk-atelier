using System;
using DeskMate;
static class FocusCompanionChecks
{
    static int checks;
    static void Eq(int got,int want,string label)
    { checks++;if(got!=want)throw new Exception(label+": got "+got+", expected "+want); }

    static void Main()
    {
        foreach(string art in new[]{"kurisu","rem","hitori","mutsumi","mutsumi-stage"})
        {
            var cue=new FocusCompanionCue();
            Eq(cue.Step(art,0,true,false),-1,art+" idle");
            cue.Start();
            Eq(cue.Step(art,0,false,false),-1,art+" waiting on previous movement");
            Eq(cue.Step(art,100,true,true),-1,art+" pause before arrival");
            Eq(cue.Step(art,200,true,false),0,art+" settle");
            int mask=0;
            for(int t=300;t<50000;t+=100)mask|=1<<cue.Step(art,t,true,false);
            Eq(mask,art=="kurisu"?31:art=="rem"?47:63,art+" every complete frame shown");
            int before=cue.Step(art,50000,true,false);
            Eq(cue.Step(art,50100,true,true),before,art+" paused");
            Eq(cue.Step(art,90000,true,true),before,art+" elapsed paused time ignored");
            Eq(cue.Step(art,91000,false,false),-1,art+" no painting during drag/menu");
            Eq(cue.Step(art,99000,true,false),before,art+" no replay after interruption");
            cue.Stop(false);
            Eq(cue.Step(art,100000,true,false),before,art+" cancel from current pose");
            Eq(cue.Step(art,100200,true,false),art=="rem" && before>=2?5:1,art+" pack away without success");
            Eq(cue.Step(art,100400,true,false),1,art+" pack away hold");
            Eq(cue.Step(art,100600,true,false),0,art+" settle after cancel");
            Eq(cue.Step(art,100800,true,false),0,art+" neutral hold");
            Eq(cue.Step(art,101000,true,false),-1,art+" depart");
            cue.Start();
            Eq(cue.Step(art,102000,true,false),0,art+" clean restart");
            cue.Stop(true);
            cue.Step(art,103000,true,false);
            Eq(cue.Step(art,103200,true,false),5,art+" quiet completion");
            Eq(cue.Step(art,103300,false,false),-1,art+" hidden exit never plays on return");
            Eq(cue.Step(art,109000,true,false),-1,art+" no stale animation");
        }
        var changing=new FocusCompanionCue();
        changing.Start();
        Eq(changing.Step("kurisu",0,true,false),0,"first role");
        Eq(changing.Step("mutsumi-stage",10000,true,false),0,"change outfit opens own scene");
        Eq(changing.Step("imported-pet",10100,true,false),-1,"custom pet has no built-in animation");
        changing.Stop(true);
        Eq(changing.Step("kurisu",10200,true,false),-1,"do not celebrate former role");
        var bridge=new FocusCompanionCue();
        bridge.Start();
        Eq(bridge.Step("rem",0,true,false,true),FocusCompanionCue.BridgeFirst+0,"desktop starts standing");
        Eq(bridge.Step("rem",200,true,false,true),FocusCompanionCue.BridgeFirst+0,"standing beat");
        Eq(bridge.Step("rem",400,true,false,true),FocusCompanionCue.BridgeFirst+2,"drawn frame between standing and sitting");
        bridge.Stop(false);
        Eq(bridge.Step("rem",400,true,false,true),FocusCompanionCue.BridgeFirst+2,"cancel while sitting down preserves pose");
        Eq(bridge.Step("rem",600,true,false,true),FocusCompanionCue.BridgeFirst+2,"cancel holds current pose");
        Eq(bridge.Step("rem",800,true,false,true),FocusCompanionCue.BridgeFirst+1,"cancellation tracks back toward standing");
        Eq(bridge.Step("rem",1000,true,false,true),FocusCompanionCue.BridgeFirst+0,"returns to original stance");
        Eq(bridge.Step("rem",1200,true,false,true),-1,"entry cancellation ends without a success beat");
        bridge.Start();
        Eq(bridge.Step("rem",1300,true,false,true),FocusCompanionCue.BridgeFirst+0,"standing in a new session");
        Eq(bridge.Step("rem",1500,true,false,false),0,"switching to ledge does not play a desktop stool");
        var mine=new FocusCompanionCue();mine.Start();
        var seen=new System.Collections.Generic.HashSet<int>();
        var shift=new MineShift(0);
        int firstCut=shift.SectionStart(1);
        for(int t=0;t<firstCut;t+=25)seen.Add(mine.Step("steve",t,true,false));
        int expected=shift.Variant(0)%6==0?220:236;
        Eq(Math.Min(seen.Count,expected),expected,"complete authored approach and exit, with the appropriate quiet poses");
        Eq(shift.SectionStart(MineShift.Sections),1800000,"complete thirty minute shift");
        long now=firstCut+200;
        int frozen=mine.Step("steve",now,true,true);
        Eq(mine.Step("steve",now+2000,true,true),frozen,"no further strikes while paused");
        mine.Stop(false);
        Eq(mine.Step("steve",now+2100,true,false),frozen,"cancel preserves mining pose");
        Eq(mine.Step("steve",now+2300,true,false),frozen,"no fake block break on cancel");
        Eq(mine.Step("steve",now+2500,true,false),-1,"clean mining exit");
        var oreTypes=new System.Collections.Generic.HashSet<string>();
        for(int i=0;i<1800;i++)
        {
            string a=FocusCompanionCue.MineOreAt(i,23), b=FocusCompanionCue.MineOreAt(i+1,23);
            oreTypes.Add(a);
            if(a!="stone" && b!="stone")throw new Exception("Missing pair atlas");
            if(a!=FocusCompanionCue.MineOreAt(i,23))throw new Exception("Ore changed while paused");
            if(a!="stone" && !FocusCompanionCue.MineSuffix(i-1,23).EndsWith("-to-"+a))
                throw new Exception("Current ore differs from previous reveal");
        }
        Eq(oreTypes.Count,8,"all eight Java 1.8 mining block types reachable");
        Console.WriteLine("Pass: "+checks+" focus companion timeline checks.");
    }
}
