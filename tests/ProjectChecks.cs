using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using DeskMate;

internal static partial class LifeChecks
{
    static PersonalProject Project(SaveData data,string role)
    {
        return role=="rem"?data.MaidProject:role=="hitori"?data.GuitarProject:role=="kurisu"?data.LabProject:data.MinerProject;
    }
    static void EndChapter()
    {
        AgeOffer(8000);PetCall("AdvancePerformance",PetNow());
    }
    static void CheckPropHandoff(string who,string activity,string bridge,int elapsed)
    {
        ChooseCharacter(who);
        var prior=StartAt(activity,elapsed);
        var sequence=Poses(who,bridge);
        PetCall("PlayPaperAction",false);
        Need(pet.CurrentActivity=="handoff" && Read<string>("bridgeKind")=="setdown" && sequence.Contains(pet.LiveSprite.Source),
            who+" lowers the current "+activity+" prop with its own artwork");
        var floor=Read<Image>("parkedProp");
        Until(()=>floor.Source!=null && Poses(who,"handoff").Contains(pet.LiveSprite.Source),1200,
            who+" leaves the same object on the floor throughout the paper handoff");
        EndChapter();
        Until(()=>Read<string>("bridgeKind")=="pickup" && floor.Source==null && sequence.Contains(pet.LiveSprite.Source),700,
            who+" retrieves the floor object from the matching strip");
        Until(()=>pet.CurrentActivity==activity && Object.ReferenceEquals(prior,pet.LiveSprite.Source),1100,
            who+" continues with the original "+activity+" prop and saved pose");
    }
    static void CheckProjectsAndTransitions()
    {
        foreach(string role in new[]{"rem","kurisu","hitori","steve"})
        {
            ChooseCharacter(role);
            foreach(string key in new[]{"projectstart","projectwork","projectfinish","propbridge","turnstep"})
            {
                var poses=Poses(role,key);
                Need(poses!=null && poses.Length==6 && poses.Select(p=>Hash(Pixels(p))).Distinct().Count()==6,
                    role+"/"+key+" contains six distinct full poses");
                if(role=="steve")CheckClassicSteve(key,poses);
            }
            var project=Project(app.Data,role);
            project.Stage=0;project.NextUtc=0;
            pet.GetType().GetProperty("RoutineIndex",Fields).SetValue(pet,3,null);
            PetCall("Ambient");
            Need(pet.CurrentActivity=="projectstart",role+" autonomously begins its own first chapter");
            PetCall("OpeningTool");
            Need(project.Stage==0 && (bool)PetCall("ResumePerformance",PetNow()),role+" resumes an unfinished chapter without advancing it");
            EndChapter();
            var saved=Project(Store.Load(),role);
            Need(saved.Stage==1 && saved.NextUtc>DateTime.UtcNow.AddMinutes(79).Ticks,role+" commits its first chapter and hours-scale deadline to disk");
            PetCall("Ambient");
            Need(!pet.CurrentActivity.StartsWith("project"),role+" fills the waiting time with its ordinary routine");
            PetCall("ForgetPerformance");
            // Reload a long-overdue save just like startup. Offline time cannot consume multiple chapters.
            project.NextUtc=DateTime.UtcNow.AddDays(-3).Ticks;
            app.Save();app.Data=Store.Load();project=Project(app.Data,role);
            PetCall("Ambient");
            Need(pet.CurrentActivity=="projectwork" && project.Stage==1,role+" returns after days away to the next unfinished chapter");
            EndChapter();
            saved=Project(Store.Load(),role);
            Need(saved.Stage==2 && saved.NextUtc>DateTime.UtcNow.AddMinutes(114).Ticks,role+" schedules the final chapter from the actual completion time");
            project.NextUtc=DateTime.UtcNow.AddSeconds(-1).Ticks;
            PetCall("Ambient");
            Need(pet.CurrentActivity=="projectfinish",role+" reaches its own outcome on the third chapter");
            EndChapter();saved=Project(Store.Load(),role);
            Need(saved.Stage==0 && new DateTime(saved.NextUtc,DateTimeKind.Utc).ToLocalTime().Date>DateTime.Today,
                role+" waits until a later local day to repeat its project");
        }
        ChooseCharacter("rem");
        StartAt("tea",1850);
        PetCall("PlayPaperAction",false);
        Need(pet.CurrentActivity=="handoff" && Read<string>("bridgeKind")=="setdown","a paper handoff first plays the cup-lowering strip");
        Until(()=>Read<string>("bridgeKind")==null && Poses("rem","handoff").Contains(pet.LiveSprite.Source),1300,"the note arrives after the cup is set aside");
        EndChapter();
        Until(()=>Read<string>("bridgeKind")=="pickup",650,"the pet first retrieves its cup when the note is put away");
        Until(()=>pet.CurrentActivity=="tea" && Poses("rem","tea").Contains(pet.LiveSprite.Source),1100,"the pickup blends back into the original tea activity");
        CheckPropHandoff("kurisu","pen","penbridge",810);
        CheckPropHandoff("steve","mine","toolbridge",780);
        CheckPropHandoff("hitori","tea","propbridge",100);
        CheckPropHandoff("rem","tea","propbridge",4570);
        ChooseCharacter("steve");StartAt("tea",100);PetCall("OpeningTool");
        Need(Read<string>("bridgeKind")==null,"Steve's empty first tea pose does not conjure a bottle");
        StartAt("mine",3480);PetCall("OpeningTool");
        Need(Read<string>("bridgeKind")==null,"Steve's block of harvested cobblestone does not turn back into a pickaxe");
        ChooseCharacter("kurisu");StartAt("pen",1670);PetCall("OpeningTool");
        Need(Read<string>("bridgeKind")==null,"a falling pen is not put away as if it remained in the hand");

        PetCall("ForgetPerformance");ResetPose();
        NativeRect bounds;GetWindowRect(petHandle,out bounds);
        SetPoint("pressWindow",bounds.Left,bounds.Top);
        Put("pressing",true);PetCall("UpdateDrag",11,0);
        Need(Read<string>("bridgeKind")=="startstep" && Read<int>("dragDirection")==1,"horizontal travel begins with turning the hips and stepping out");
        PetCall("UpdateDrag",7,0);
        Need(Read<int>("dragDirection")==-1 && Read<string>("bridgeKind")=="startstep","reversal is measured from the latest pointer position");
        pet.LiveSprite.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});
        Need(Read<string>("bridgeKind")=="stopstep","at release the moving feet visibly brake and settle");
        Until(()=>Read<string>("bridgeKind")==null,650,"the settling pose returns to the idle loop");
        SetWindowPos(petHandle,IntPtr.Zero,bounds.Left,bounds.Top,0,0,0x1|0x4|0x10);
    }
}
