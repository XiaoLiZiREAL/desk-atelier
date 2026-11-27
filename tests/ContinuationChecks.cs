using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

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
        Need(Read<bool>("dragging") && Object.ReferenceEquals(cup,pet.LiveSprite.Source),"carrying a busy pet keeps its current prop in its hands");
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
