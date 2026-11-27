using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DeskMate;

internal static partial class LifeChecks
{
    static object PetCall(string method,params object[] args)
    {
        return pet.GetType().GetMethod(method,Fields).Invoke(pet,args);
    }
    static void ChooseCharacter(string id)
    {
        PetCall("ForgetPerformance");
        app.Data.Character=id;pet.Sync();ResetPose();
    }
    static BitmapSource StartAt(string name,int elapsed)
    {
        PetCall("ForgetPerformance");
        Need((bool)PetCall("BeginPerformance",name,true),"authored activity "+name+" starts");
        AgeOffer(elapsed);
        PetCall("AdvancePerformance",PetNow());
        return (BitmapSource)pet.LiveSprite.Source;
    }
    static void CheckContinuations()
    {
        app.Music.SetEnabled(false);app.Data.HideFullscreen=false;app.Data.Quiet=false;
        ChooseCharacter("rem");
        var cup=StartAt("tea",1850);
        pet.SetKaraoke("fixture","",0,true);
        Until(()=>pet.KaraokeMoving && Read<object>("interrupted")!=null,1100,"music takes over while the tea and its elapsed pose are remembered");
        Pump(450);
        pet.SetKaraoke("fixture","",0,false);
        Until(()=>pet.CurrentActivity=="tea",900,"a paused song releases the pet even with a paused caption visible");
        Need(Object.ReferenceEquals(pet.LiveSprite.Source,cup),"tea resumes with the same cup and the same pose");
        pet.ClearKaraoke();

        app.Open("focus");
        Need(pet.CurrentActivity!="tea" && Read<object>("interrupted")!=null,"opening a tool suspends its host's activity");
        Pump(420);Need(pet.CurrentActivity!="tea","the activity remains suspended while the tool is open");
        app.Desk.Hide();
        Until(()=>pet.CurrentActivity=="tea",1000,"closing the tool releases the saved activity");

        cup=StartAt("tea",1800);
        NativeRect bounds;GetWindowRect(petHandle,out bounds);
        SetPoint("pressWindow",bounds.Left,bounds.Top);Put("pressing",true);
        NativePoint mouse;
        if(GetCursorPos(out mouse))
        {
            SetPoint("pressPoint",mouse.X-8,mouse.Y);
            pet.LiveSprite.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,Environment.TickCount){RoutedEvent=Mouse.MouseMoveEvent});
        }
        else PetCall("UpdateDrag",8,0); // A noninteractive test desktop cannot read the physical cursor.
        Pump(220);
        Need(Read<bool>("dragging") && Object.ReferenceEquals(Poses("rem","propbridge")[2],pet.LiveSprite.Source),
            "dragging a pet while she drinks switches to the drawn two-handed grip on her cup");
        pet.LiveSprite.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});
        SetWindowPos(petHandle,IntPtr.Zero,bounds.Left,bounds.Top,0,0,0x1|0x4|0x10);
        Until(()=>pet.CurrentActivity=="tea",1000,"dropping the pet at a new location resumes its interrupted tea");
        Need(Object.ReferenceEquals(cup,pet.LiveSprite.Source),"the cup pose survives the drag and release");

        ChooseCharacter("kurisu");
        var pen=StartAt("pen",1640);
        PetCall("PlayPaperAction",false);
        Need(pet.CurrentActivity=="handoff" && Read<object>("interrupted")!=null,"Kurisu sets aside her pen to receive a note");
        pet.SetKaraoke("fixture","",0,true);
        Pump(200);Need(pet.CurrentActivity=="handoff" && !pet.KaraokeMoving,"the ongoing paper handoff has priority over singing");
        pet.ClearKaraoke();
        AgeOffer(8000);
        Until(()=>pet.CurrentActivity=="pen",1000,"after putting the note away, Kurisu returns to her pen");
        Need(Object.ReferenceEquals(pen,pet.LiveSprite.Source),"the pen performance continues from the saved pose");
        PetCall("PlayPaperAction",true);
        Need(pet.CurrentActivity=="returnnote","a due note uses the character's returning-paper performance");
        app.Data.Quiet=true;pet.Sync();
        Need(Read<object>("interrupted")==null && pet.CurrentActivity=="","quiet mode discards the held task");
        app.Data.Quiet=false;pet.Sync();

        StartAt("pen",740);PetCall("OpeningTool");
        var held=Read<object>("interrupted");
        held.GetType().GetField("InterruptedAt",Fields).SetValue(held,PetNow()-13*60*1000);
        Pump(160);
        Need(Read<object>("interrupted")==null && pet.CurrentActivity!="pen","long breaks discard an obsolete gesture");
        StartAt("pen",850);PetCall("OpeningTool");app.Data.Character="steve";pet.Sync();
        Need(Read<object>("interrupted")==null && pet.CurrentActivity!="pen","switching characters clears the former character's prop");

        ChooseCharacter("rem");
        StartAt("game",3300);
        int hidden=Read<int>("hiddenBox");string hint=Read<string>("lastDirection");
        PetCall("OpeningTool");
        Until(()=>pet.OfferingGame,1000,"the unattended chest game also resumes");
        Need(hidden==Read<int>("hiddenBox") && hint==Read<string>("lastDirection"),"resuming preserves the same hidden object and clue");
        PetCall("GuessBox",hidden);
        int wins=app.Data.GamesWon,rounds=app.Data.GameRounds;
        PetCall("OpeningTool");
        Until(()=>pet.CurrentActivity=="game",1000,"an interrupted reveal continues");
        Need(!pet.OfferingGame && app.Data.GamesWon==wins && app.Data.GameRounds==rounds,"replaying the reveal cannot award the round twice");

        VerifyNewRoutines("rem",new[]{"fold","wipe"});
        VerifyNewRoutines("kurisu",new[]{"pen","board"});
        VerifyNewRoutines("hitori",new[]{"pick","pickcatch","bow"});
        VerifyNewRoutines("steve",new[]{"fish","fishcatch","pillar","bowdraw","blockguard"});
        VerifyMutsumiRoutine();
        ChooseCharacter("hitori");StartAt("bow",400);
        PetCall("NoticedAudience",false,400L);
        Need(!(bool)PetCall("NoticedAudience",true,720L) && (bool)PetCall("NoticedAudience",true,1600L),
            "Bocchi only notices a mouse that approaches and stays until she looks up");
        PetCall("NoticedAudience",false,2000L);
        Need(!(bool)PetCall("NoticedAudience",true,4100L),"after the bow has finished, passing the cursor cannot interrupt it");
        ChooseCharacter("rem");
        pet.SetKaraoke("paused fixture","",.4,false);
        app.Data.MaidRoutine=2;DueSoon();
        Until(()=>pet.CurrentActivity=="fold" && pet.KaraokeVisible && !pet.KaraokeMoving,1200,
            "a visible paused caption allows the next scheduled activity to start from idle");
        pet.ClearKaraoke();
        CheckProjectsAndTransitions();
    }
    static void VerifyMutsumiRoutine()
    {
        ChooseCharacter("mutsumi");
        var left=(BitmapSource[])PetCall("StepFrames",-1);
        var atlas=Read<BitmapSource[][]>("frames");
        Need(left!=null && left[3]==atlas[2][2],"left turns use Mutsumi's original left-walking art and preserve her hairpin");
        var keys=new[]{"tune","mutepractice","stringwipe","game","leafcheck","listenbeat"};
        int[] slots={0,1,4,7,8,10};
        var garden=app.Data.CucumberProject;
        garden.Stage=2;garden.NextUtc=DateTime.UtcNow.AddHours(4).Ticks;
        for(int i=0;i<keys.Length;i++){
            string action=keys[i];
            var strip=Poses("mutsumi",action);
            Need(strip!=null && strip.Select(p=>Hash(Pixels(p))).Distinct().Count()==6,action+" has six different Mutsumi poses");
            app.Data.MutsumiRoutine=slots[i];PetCall("Ambient");
            Need(pet.CurrentActivity==action && strip.Contains(pet.LiveSprite.Source) && Store.Load().MutsumiRoutine==slots[i]+1,
                "Mutsumi autonomously starts "+action+" and persists her own cursor");
            if(action=="game"){
                PetCall("OpeningTool");PetCall("ResumePerformance",PetNow());
                Need(!pet.OfferingGame && pet.CurrentActivity=="game","Mutsumi's quiet pick game resumes without summoning the three chests");
            }
            PetCall("ForgetPerformance");
        }
        long later=DateTime.UtcNow.AddHours(4).Ticks;
        CheckLeafSlot(0,0,"focus","before planting there is no mature vine");
        CheckLeafSlot(1,later,"focus","the seedling gets time to grow before any trellis appears");
        CheckLeafSlot(0,later,"leafcheck","after harvest she can inspect the vine that remains");
        garden.Stage=0;garden.NextUtc=0;
        app.Data.MutsumiRoutine=14;PetCall("Ambient");
        Need(pet.CurrentActivity=="quietrest" && pet.LiveSprite.Source==Poses("mutsumi","rest")[0],"Mutsumi first gets her stool out to rest");
        AgeOffer(4200);PetCall("AdvancePerformance",PetNow());
        Need(Poses("mutsumi","quietrest").Contains(pet.LiveSprite.Source),"the long rest uses its own seated eyelid and hand drawings");
        AgeOffer(8600);PetCall("AdvancePerformance",PetNow());
        Need(pet.LiveSprite.Source==Poses("mutsumi","rest")[4],"Mutsumi stands up from the stool at the end");
        var prior=StartAt("tune",960);
        PetCall("PlayPaperAction",false);
        Need(Read<string>("bridgeKind")=="setdown" && Poses("mutsumi","guitarbridge").Contains(pet.LiveSprite.Source),
            "Mutsumi first dampens the strings before receiving the paper");
        Until(()=>Read<string>("bridgeKind")==null && Poses("mutsumi","handoff").Contains(pet.LiveSprite.Source) &&
            Read<System.Windows.Controls.Image>("parkedProp").Source!=null,1400,
            "the guitar stays on its stand while Mutsumi handles the note");
        EndChapter();
        Until(()=>Read<string>("bridgeKind")=="pickup",750,"Mutsumi retrieves the guitar before she continues");
        Until(()=>pet.CurrentActivity=="tune" && prior==pet.LiveSprite.Source,1200,"Mutsumi resumes at the interrupted tuning pose");
        pet.SetKaraoke("测试乐句","",.2,true);
        Until(()=>pet.KaraokeMoving && Read<object>("interrupted")!=null,800,"timed vocals take over Mutsumi's tuning");
        Need(Read<string>("bridgeKind")==null && Read<System.Windows.Controls.Image>("parkedProp").Source==null,
            "Mutsumi keeps the same guitar between tuning and accompanying a song");
        pet.ClearKaraoke();
        PetCall("ForgetPerformance");
        var motionType=typeof(PetWindow).Assembly.GetType("DeskMate.MusicMotion");
        foreach(string motion in new[]{"Listening","Intro","Singing","Interlude"}){
            PetCall("SetLyricMotion","测试乐句","",.2,Enum.Parse(motionType,motion),1.1);
            Need((bool)PetCall("AnimateKaraoke",PetNow()),motion+" renders to the pet window");
            string key=motion=="Listening"?"listenbeat":motion=="Singing"?"song":"mutepractice";
            Need(Poses("mutsumi",key).Contains(pet.LiveSprite.Source),motion+" uses Mutsumi's own "+key+" drawing");
        }
        pet.ClearKaraoke();
        CheckMusicPaper();
        CheckMutsumiOutfits();
    }
    static void CheckLeafSlot(int stage,long due,string expected,string reason)
    {
        var garden=app.Data.CucumberProject;
        garden.Stage=stage;garden.NextUtc=due;
        app.Data.MutsumiRoutine=8;PetCall("Ambient");
        Need(pet.CurrentActivity==expected && garden.Stage==stage,reason);
        PetCall("ForgetPerformance");
    }
    static void StartMutsumiMusic(string motion)
    {
        var type=typeof(PetWindow).Assembly.GetType("DeskMate.MusicMotion");
        PetCall("SetLyricMotion","测试乐句","",.2,Enum.Parse(type,motion),1.1);
        Until(()=>pet.KaraokeMoving && Read<string>("bridgeKind")==null,1500,"Mutsumi performs the "+motion+" phase");
    }
    static void CheckMusicPaper()
    {
        var guitar=Read<System.Windows.Controls.Image>("parkedMusicGuitar");
        var otherProp=Read<System.Windows.Controls.Image>("parkedProp");
        foreach(string phase in new[]{"Singing","Intro","Interlude"}){
            StartMutsumiMusic(phase);PetCall("PlayPaperAction",false);
            Need(Read<string>("bridgeKind")=="musicsetdown",phase+" leaves the active music pose through the guitar-lowering strip");
            Until(()=>guitar.Source!=null && Poses("mutsumi","handoff").Contains(pet.LiveSprite.Source),1300,"the music guitar remains on its stand during the handoff");
            if(phase=="Intro")pet.SetKaraoke("切换到另一首歌","",.7,true);
            EndChapter();
            Until(()=>Read<string>("bridgeKind")=="musicpickup" && guitar.Source==null,700,"Mutsumi retrieves the music guitar before playing again");
            if(phase=="Interlude"){
                PetCall("PlayPaperAction",true);
                Need(Read<string>("bridgeKind")=="musicsetdown","a second note reverses a pickup already in progress");
                Until(()=>guitar.Source!=null && Poses("mutsumi","returnnote").Contains(pet.LiveSprite.Source),1300,"the guitar stays parked during a due note too");
                EndChapter();
            }
            string resumed=phase=="Interlude"?"mutepractice":"song";
            Until(()=>pet.KaraokeMoving && Poses("mutsumi",resumed).Contains(pet.LiveSprite.Source),1500,"the music resumes at the current phase");
            pet.ClearKaraoke();
        }
        StartMutsumiMusic("Listening");PetCall("PlayPaperAction",false);
        Need(Read<string>("bridgeKind")==null && guitar.Source==null,"track-only listening receives paper with empty hands");
        pet.ClearKaraoke();EndChapter();
        foreach(string activity in new[]{"tea","tune"}){
            var previous=StartAt(activity,activity=="tea"?1850:960);
            StartMutsumiMusic("Singing");PetCall("PlayPaperAction",false);
            Until(()=>guitar.Source!=null && Poses("mutsumi","handoff").Contains(pet.LiveSprite.Source),1300,"singing yields the guitar to handle paper over "+activity);
            if(activity=="tea")Need(otherProp.Source!=null,"the earlier cup and the guitar have independent places on the floor");
            pet.ClearKaraoke();EndChapter();
            Until(()=>pet.CurrentActivity==activity && previous==pet.LiveSprite.Source,2000,"a stopped song restores the original "+activity+" pose");
            Need((guitar.Source!=null)==(activity=="tea") && otherProp.Source==null,
                activity=="tea"?"Mutsumi drinks with the guitar still parked separately":"Mutsumi holds the only guitar when tuning resumes");
            PetCall("ForgetPerformance");
        }
        foreach(string next in new[]{"practice","music","switch"}){
            StartMutsumiMusic("Singing");PetCall("PlayPaperAction",false);
            Until(()=>guitar.Source!=null && Poses("mutsumi","handoff").Contains(pet.LiveSprite.Source),1300,"the guitar is parked before the song changes");
            if(next=="music"){
                var type=typeof(PetWindow).Assembly.GetType("DeskMate.MusicMotion");
                PetCall("SetLyricMotion","仅曲名","",0d,Enum.Parse(type,"Listening"),0d);
            }else pet.ClearKaraoke();
            EndChapter();Pump(240);
            Need(guitar.Source!=null && Read<string>("bridgeKind")==null,"the guitar stays put after the note when her hands are empty");
            if(next=="practice"){
                app.Data.MutsumiRoutine=2;PetCall("Ambient");
                Need(pet.CurrentActivity=="tea" && guitar.Source!=null,"a quiet tea break leaves the instrument on its stand");
                ResetPose();app.Data.MutsumiRoutine=0;PetCall("Ambient");
                Need(Read<string>("bridgeKind")=="pickup" && guitar.Source==null,"the next tuning session retrieves that same guitar");
                Until(()=>pet.CurrentActivity=="tune" && Poses("mutsumi","tune").Contains(pet.LiveSprite.Source),1300,"the pickup leads directly into tuning");
            }else if(next=="music"){
                pet.SetKaraoke("新唱段","",.1,true);
                Until(()=>Read<string>("bridgeKind")=="musicpickup" && guitar.Source==null,700,"the next timed lyric reuses the standing guitar");
                Until(()=>pet.KaraokeMoving && Poses("mutsumi","song").Contains(pet.LiveSprite.Source),1300,"the guitar is held once during the new song");
                pet.ClearKaraoke();
            }else{
                app.Data.Character="rem";pet.Sync();
                Need(guitar.Source==null && !Read<bool>("musicGuitarParked"),"selecting another pet removes Mutsumi's stand");
                app.Data.Character="mutsumi";pet.Sync();
            }
            PetCall("ForgetPerformance");
        }
    }
    static void VerifyNewRoutines(string id,string[] actions)
    {
        ChooseCharacter(id);
        foreach(string key in actions.Concat(new[]{"handoff","returnnote"})){
            var poses=Poses(id,key);
            if(id=="steve")CheckClassicSteve(key,poses);
            else Need(poses!=null && poses.Length==6 && poses.Select(p=>Hash(Pixels(p))).Distinct().Count()==6,
                id+"/"+key+" loads six distinct packaged poses");
        }
        var savedRandom=Read<Random>("random");
        try
        {
            Put("random",new Random(42));
            var seen=new HashSet<string>();
            string field=id=="rem"?"MaidRoutine":id=="kurisu"?"LabRoutine":id=="hitori"?"GuitarRoutine":"MinerRoutine";
            var cursor=app.Data.GetType().GetField(field);
            int[] slots=id=="steve"?new[]{1,5,12,16}:id=="rem"?new[]{2,5}:new[]{1,5};
            // Exercise the positions in the actual routine. A fixed seed makes alternate outcomes reproducible.
            for(int i=0;i<80 && actions.Any(a=>!seen.Contains(a));i++)
            {
                cursor.SetValue(app.Data,slots[i%slots.Length]);
                PetCall("Ambient");seen.Add(pet.CurrentActivity);ResetPose();
            }
            Need(actions.All(seen.Contains),id+" can select every new activity and outcome without a user command");
        }
        finally{Put("random",savedRandom);}
    }
}
