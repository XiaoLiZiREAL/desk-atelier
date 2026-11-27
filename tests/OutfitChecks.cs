using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using DeskMate;

internal static partial class LifeChecks
{
    static void ChangeOutfit(string outfit)
    {
        typeof(Studio).GetMethod("WearMutsumiOutfit",Fields).Invoke(app,new object[]{outfit});
    }
    static byte[] FirstDrawing(string resource)
    {
        using(var file=File.OpenRead(app.Asset(resource))){
            var image=new PngBitmapDecoder(file,BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.OnLoad).Frames[0];
            return Pixels(new CroppedBitmap(image,new Int32Rect(0,0,192,208)));
        }
    }
    static void CheckMutsumiOutfits()
    {
        ChooseCharacter("mutsumi");ChangeOutfit("uniform");
        app.Data.MutsumiRoutine=4;app.Data.MutsumiStageRoutine=0;
        var garden=app.Data.CucumberProject;
        garden.Stage=2;garden.NextUtc=DateTime.UtcNow.AddHours(2).Ticks;
        var uniform=Pixels(Read<BitmapSource[][]>("frames")[0][0]);
        StartAt("tune",960);PetCall("PlayPaperAction",false);
        ChangeOutfit("stage");
        var stage=Pixels(Read<BitmapSource[][]>("frames")[0][0]);
        Need(app.Data.Character=="mutsumi" && !stage.SequenceEqual(uniform) && stage.SequenceEqual(FirstDrawing("mutsumi-stage.png")),
            "changing clothes redraws the same role using the stage atlas");
        Need(Read<object>("interrupted")==null && Read<string>("bridgeKind")==null && Read<Image>("parkedProp").Source==null,
            "changing clothes releases old gestures and props");
        PetCall("Ambient");
        Need(pet.CurrentActivity=="hatbow" && Pixels((BitmapSource)pet.LiveSprite.Source).SequenceEqual(FirstDrawing("motion-mutsumi-stage-hatbow.png")),
            "the stage schedule opens with its own hat and bow drawings");
        ChangeOutfit("uniform");PetCall("Ambient");
        Need(pet.CurrentActivity=="stringwipe" && app.Data.MutsumiRoutine==5 && app.Data.MutsumiStageRoutine==1,
            "the school routine continues where she left it");
        ChangeOutfit("stage");PetCall("Ambient");
        Need(pet.CurrentActivity=="stageribbon" && app.Data.MutsumiStageRoutine==2 && Store.Load().MutsumiOutfit=="stage",
            "returning in costume advances her own brooch routine");
        Need(garden==app.Data.CucumberProject && garden.Stage==2,"both clothes share the existing plant");
        foreach(string key in new[]{"tea","tune","song","listenbeat","mutepractice","guitarbridge","propbridge","handoff","returnnote",
                                     "turnstep","projectstart","projectwork","projectfinish","curtaincall","mortispose","duetphrase","duetleft"}){
            var poses=Poses("mutsumi",key);
            Need(poses!=null && Pixels(poses[0]).SequenceEqual(FirstDrawing("motion-mutsumi-stage-"+key+".png")),
                "stage "+key+" loads the chosen wardrobe's real drawing");
        }
        CheckPropHandoff("mutsumi","tea","propbridge",1780);
        CheckPropHandoff("mutsumi","tune","guitarbridge",960);
        StartMutsumiMusic("Singing");
        Need(Poses("mutsumi","song").Contains(pet.LiveSprite.Source),"the live music overlay uses her stage performance");
        pet.ClearKaraoke();PetCall("ForgetPerformance");
        var originalRandom=Read<Random>("random");
        try{
            Put("random",new Random(42));
            bool sawCurtain=false,sawEncore=false;
            for(int i=0;i<60 && !(sawCurtain && sawEncore);i++){
                PetCall("ForgetPerformance");app.Data.MutsumiStageRoutine=4;PetCall("Ambient");
                sawCurtain|=pet.CurrentActivity=="curtaincall";
                if(pet.CurrentActivity!="mortispose")continue;
                sawEncore=true;AgeOffer(550);PetCall("AdvancePerformance",PetNow());
                Need(!Read<bool>("encoreLineSaid"),"the catchphrase waits until her arms have risen");
                PetCall("OpeningTool");PetCall("ResumePerformance",PetNow());
                AgeOffer(2050);PetCall("AdvancePerformance",PetNow());
                Need(Read<bool>("encoreLineSaid") && Read<TextBlock>("words").Text=="全都不会弹！",
                    "Mortis says the exact line at the raised-arms pose");
                Pump(100);pet.UpdateLayout();
                Need(pet.LiveSprite.Source==Poses("mutsumi","mortispose")[4] && Read<TextBlock>("words").IsVisible,
                    "the rendered peak and visible caption coexist in the real window");
                CapturePet("mutsumi-stage-mortis");
                long spokenUntil=Read<long>("speechUntil");
                PetCall("OpeningTool");PetCall("ResumePerformance",PetNow());
                Need(Read<bool>("encoreLineSaid") && Read<long>("speechUntil")==spokenUntil,
                    "resuming the gesture does not repeat the line");
            }
            Need(sawCurtain && sawEncore,"the same stage slot can produce the normal curtain call and its rare encore");
        }finally{Put("random",originalRandom);}
        PetCall("ForgetPerformance");garden.Stage=1;garden.NextUtc=DateTime.UtcNow.AddSeconds(-1).Ticks;
        PetCall("Ambient");
        Need(pet.CurrentActivity=="projectwork" && Poses("mutsumi","projectwork").Contains(pet.LiveSprite.Source),
            "the stage costume can begin tending the same growing vine");
        ChangeOutfit("uniform");PetCall("Ambient");
        Need(pet.CurrentActivity=="projectwork" && garden.Stage==1,"switching garments reopens the unfinished chapter in the uniform");
        EndChapter();Need(garden.Stage==2,"finishing in the other garment advances the vine exactly once");
        app.Data.MutsumiRoutine=4;PetCall("Ambient");
        Need(pet.CurrentActivity=="stringwipe" && !Read<bool>("encoreLineSaid"),"the uniform follows her ordinary practice schedule");
        PetCall("ForgetPerformance");
    }
}
