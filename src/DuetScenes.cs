using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskMate
{
    public enum HitoriVisitTurn { FromOldSave, Pick, TeaAfterPick, Rehearsal, TeaAfterRehearsal }

    public partial class SaveData
    {
        // Completed stories alternate with the shorter tea visit, independently for each pair.
        public int DuetTeaTurns;
        public HitoriVisitTurn HitoriVisit;
    }

    public sealed partial class PetWindow
    {
        sealed class DuetScene
        {
            public string Guest;
            public bool Pick, MirrorBocchi, MirrorKurisu, SteveFacesRight;
            public BitmapSource[] Bocchi, Bow, Steve, RemIn, RemOut, KurisuIn, KurisuOut, MutsumiPhrase;
            public readonly Image Prop=new Image { IsHitTestVisible=false, Stretch=Stretch.Fill, RenderTransformOrigin=new Point(.5,.5) };
            public readonly RotateTransform Spin=new RotateTransform();
            public readonly TranslateTransform HostStep=new TranslateTransform();
            public Transform PreviousTransform;
        }

        int DuetTurn()
        {
            // Read the old pair flag before another host can spend it. After migration
            // only a completed visit with Bocchi as the host advances her four-stage cycle.
            if(app.Data.HitoriVisit==HitoriVisitTurn.FromOldSave)
                app.Data.HitoriVisit=(app.Data.DuetTeaTurns&1)!=0?HitoriVisitTurn.TeaAfterPick:HitoriVisitTurn.Pick;
            switch(character){
                case "hitori":return app.Data.HitoriVisit==HitoriVisitTurn.Rehearsal || app.Data.HitoriVisit==HitoriVisitTurn.TeaAfterRehearsal?4:1;
                case "steve":return 1;
                case "mutsumi":return 4;
                case "rem":case "kurisu":return 2;
                default:return 0;
            }
        }
        bool HitoriTeaIsDue
        {
            get { return app.Data.HitoriVisit==HitoriVisitTurn.TeaAfterPick || app.Data.HitoriVisit==HitoriVisitTurn.TeaAfterRehearsal; }
        }
        void CompleteVisit(VisitScene scene)
        {
            int pair=DuetTurn();
            if(!scene.Independent || pair==0)return;
            if(character=="hitori"){
                if(scene.Duet==null && !HitoriTeaIsDue)return;
                if(scene.Duet==null)app.Data.HitoriVisit=pair==1?HitoriVisitTurn.Rehearsal:HitoriVisitTurn.Pick;
                else app.Data.HitoriVisit=pair==1?HitoriVisitTurn.TeaAfterPick:HitoriVisitTurn.TeaAfterRehearsal;
            }
            if(scene.Duet==null)app.Data.DuetTeaTurns &= ~pair;
            else app.Data.DuetTeaTurns |= pair;
            app.Save();
        }
        BitmapSource[] DuetStrip(string name)
        {
            var path=app.Asset("duet-"+name+".png");
            if(!File.Exists(path))return null;
            var atlas=Ui.Bitmap(path);
            if(atlas.PixelWidth!=1152 || atlas.PixelHeight!=208)return null;
            var cells=new BitmapSource[6];
            for(int i=0;i<cells.Length;i++){
                var cell=new CroppedBitmap(atlas,new Int32Rect(192*i,0,192,208));cell.Freeze();cells[i]=cell;
            }
            return cells;
        }
        DuetScene PrepareDuet(double side)
        {
            int pair=DuetTurn();
            if(pair==0 || (character=="hitori"?HitoriTeaIsDue:(app.Data.DuetTeaTurns & pair)!=0))return null;
            var d=new DuetScene {Pick=pair==1};
            if(d.Pick){
                d.Guest=character=="hitori"?"steve":"hitori";
                // Bocchi drops the pick out of her right hand. Keep that hand facing the visitor.
                d.MirrorBocchi=character=="hitori"?side>0:side<0;
                d.SteveFacesRight=!d.MirrorBocchi;
                d.Bocchi=DuetStrip("hitori");d.Bow=ActionFrames("hitori","bow");
                d.Steve=DuetStrip(d.SteveFacesRight?"steve-right":"steve-left");
                if(d.Bocchi==null || d.Bow==null || d.Steve==null || !File.Exists(app.Asset("duet-pick.png")))return null;
                d.Prop.Source=Ui.Bitmap(app.Asset("duet-pick.png"));
                d.Prop.Width=sprite.Width*14/192;d.Prop.Height=sprite.Height*15/208;
            }else if(pair==4){
                d.Guest=character=="mutsumi"?"hitori":"mutsumi";
                bool partnerOnRight=character=="mutsumi"?side>0:side<0;
                var phrase=ActionFrames("mutsumi",partnerOnRight?"duetphrase":"duetleft");
                var picking=ActionFrames("mutsumi","mutepractice");
                d.Bocchi=ActionFrames("hitori","focus");
                if(phrase==null || picking==null || d.Bocchi==null)return null;
                d.MutsumiPhrase=new[]{phrase[0],phrase[1],phrase[2],phrase[3],phrase[4],phrase[5],
                                      picking[2],picking[1],picking[3]};
            }else{
                d.Guest=character=="rem"?"kurisu":"rem";
                d.MirrorKurisu=character=="kurisu"?side>0:side<0;
                d.RemIn=ActionFrames("rem","handoff");d.RemOut=ActionFrames("rem","returnnote");
                d.KurisuIn=ActionFrames("kurisu","handoff");d.KurisuOut=ActionFrames("kurisu","returnnote");
                if(d.RemIn==null || d.RemOut==null || d.KurisuIn==null || d.KurisuOut==null)return null;
                var drawing=new DrawingGroup();
                using(var pen=drawing.Open()){
                    var outline=new Pen(Ui.Color("#9F823D"),1.2);
                    pen.DrawRoundedRectangle(Ui.Color("#FFE186"),outline,new Rect(1,1,28,19),1,1);
                    pen.DrawLine(outline,new Point(6,8),new Point(19,8));
                    pen.DrawLine(outline,new Point(6,12),new Point(16,12));
                }
                drawing.Freeze();d.Prop.Source=new DrawingImage(drawing);
                d.Prop.Width=sprite.Width*30/192;d.Prop.Height=sprite.Height*21/208;
            }
            d.Prop.RenderTransform=d.Spin;d.Prop.Visibility=Visibility.Hidden;
            d.PreviousTransform=sprite.RenderTransform;
            sprite.RenderTransform=d.HostStep;
            return d;
        }
        static double Fraction(double t,double start,double end)
        {
            return Math.Max(0,Math.Min(1,(t-start)/(end-start)));
        }
        static Point Between(Point from,Point to,double p,double lift)
        {
            return new Point(from.X+(to.X-from.X)*p,from.Y+(to.Y-from.Y)*p-4*lift*p*(1-p));
        }
        Point AtActor(double center,double frameX,double frameY,bool mirrored)
        {
            return new Point(center+((mirrored?192-frameX:frameX)-96)*sprite.Width/192,frameY*sprite.Height/208);
        }
        static void PlaceProp(DuetScene d,Point center,double angle)
        {
            d.Prop.Visibility=Visibility.Visible;
            Canvas.SetLeft(d.Prop,center.X-d.Prop.Width*.5);Canvas.SetTop(d.Prop,center.Y-d.Prop.Height*.5);
            d.Spin.Angle=angle;
        }
        static BitmapSource Facing(VisitScene scene,BitmapSource source,bool mirror)
        {
            if(!mirror)return source;
            BitmapSource flipped;
            if(!scene.Flips.TryGetValue(source,out flipped)){
                flipped=new TransformedBitmap(source,new ScaleTransform(-1,1));flipped.Freeze();scene.Flips[source]=flipped;
            }
            return flipped;
        }
        void DuoPose(VisitScene scene,bool host,BitmapSource source,bool mirror=false)
        {
            source=Facing(scene,source,mirror);
            if(host)ShowPose(source);else scene.Actor.Source=source;
        }
        BitmapSource StepFrame(VisitScene scene,bool host,double direction,double elapsed,double duration)
        {
            var steps=host?scene.HostSteps:scene.GuestSteps;
            if(steps!=null){
                bool mirror=direction<0;
                if(!host && mirror && scene.GuestLeftSteps!=null){steps=scene.GuestLeftSteps;mirror=false;}
                if(elapsed<.4)return Facing(scene,steps[Math.Min(3,(int)(Math.Max(0,elapsed)*10))],mirror);
                if(elapsed>=duration-.26)return Facing(scene,steps[elapsed<duration-.13?4:5],mirror);
            }
            int state=direction>0?Right:Leftward;
            return (host?frames:scene.Art)[state][(int)(Math.Max(0,elapsed)*1000/FrameMs[state])%Counts[state]];
        }
        void UpdateDuet(VisitScene scene,double t,double hostX,double near,double far)
        {
            var d=scene.Duet;
            // Hold the partners further apart than the seated tea scene so the shared item
            // is readable on the desktop and the two silhouettes never occlude the handoff.
            near=Math.Min(sprite.Width*.84,far*.85);
            double entry=1-Smooth(Fraction(t,0,1.8)),exit=Smooth(Fraction(t,11.2,14.3));
            double guestX=hostX+scene.Side*(near+(far-near)*(entry+exit));
            scene.Actor.Opacity=Math.Min(Smooth(t/.45),Smooth((14.3-t)/.55));
            d.Prop.Visibility=Visibility.Hidden;
            bool walking=t<1.8 || t>=11.2;
            if(d.Pick){
                bool steveHost=character=="steve";
                double towardBocchi=steveHost?scene.Side:-scene.Side;
                double stride=Math.Min(near*.23,sprite.Width*(steveHost?.12:.19));
                double travel=Smooth(Fraction(t,3.65,4.55))*(1-Smooth(Fraction(t,9.95,10.85)));
                double steveRest=steveHost?hostX:guestX;
                double steveX=steveRest+towardBocchi*stride*travel;
                if(steveHost)d.HostStep.X=steveX-hostX;else guestX=steveX;
                double bocchiX=steveHost?guestX:hostX;
                DrawPickExchange(scene,t,bocchiX,steveX,steveRest+towardBocchi*stride,steveHost,towardBocchi);
            }else if(d.MutsumiPhrase!=null)DrawGuitarRehearsal(scene,t);
            else DrawPaperExchange(scene,t,hostX,guestX);
            Canvas.SetLeft(scene.Actor,guestX-sprite.Width*.5);
            if(walking)DuoPose(scene,false,StepFrame(scene,false,t<1.8?-scene.Side:scene.Side,t<1.8?t:t-11.2,t<1.8?1.8:3.1));
        }
        void DrawGuitarRehearsal(VisitScene scene,double t)
        {
            var d=scene.Duet;
            bool mutsumiHost=character=="mutsumi";
            // Bocchi starts, then holds her fretting hand. Mutsumi plays two isolated notes
            // and mutes her strings until Bocchi has played the phrase back on her own.
            long elapsed=(long)Math.Round(t*1000);
            int mutsumi=ActionScore.RehearsalLead.At(elapsed);
            int bocchi=ActionScore.RehearsalReply.At(elapsed);
            // Both instruments are right handed even if the visitor enters from the other side.
            DuoPose(scene,mutsumiHost,d.MutsumiPhrase[mutsumi]);
            DuoPose(scene,!mutsumiHost,d.Bocchi[bocchi]);
        }
        void DrawPickExchange(VisitScene scene,double t,double bocchiX,double steveX,double pickupX,bool steveHost,double towardBocchi)
        {
            var d=scene.Duet;
            BitmapSource bocchi;
            if(t<8.45)bocchi=d.Bocchi[t<2.34?0:t<2.85?1:t<6.78?2:t<7.62?3:t<8.13?4:5];
            else bocchi=d.Bow[t<8.85?0:t<9.12?1:t<9.44?2:t<10.03?3:t<10.38?4:0];
            DuoPose(scene,!steveHost,bocchi,d.MirrorBocchi);

            int pose=t<2.72?0:t<4.55?1:t<5.32?2:t<5.65?1:t<6.26?3:t<6.60?4:t<7.0?5:t<8.3?1:0;
            if((t>=3.65 && t<4.55) || (t>=9.95 && t<10.85))
                DuoPose(scene,steveHost,StepFrame(scene,steveHost,t<4.55?towardBocchi:-towardBocchi,t<4.55?t-3.65:t-9.95,.9));
            else DuoPose(scene,steveHost,d.Steve[pose]);

            Point released=AtActor(bocchiX,75,77,d.MirrorBocchi);
            Point floor=AtActor(pickupX,96+towardBocchi*13,191,false);
            Point firstBounce=AtActor(bocchiX,38,190,d.MirrorBocchi);
            bool right=d.SteveFacesRight;
            Point pickup=AtActor(pickupX,right?67:72,right?143:133,false);
            if(t>=2.34 && t<2.77){
                double p=Fraction(t,2.34,2.77);
                var falling=Between(released,firstBounce,p,0);
                falling.Y=released.Y+(firstBounce.Y-released.Y)*p*p;
                PlaceProp(d,falling,p*120);
            }else if(t>=2.77 && t<3.65){
                double p=Fraction(t,2.77,3.65);
                var point=Between(firstBounce,floor,1-(1-p)*(1-p),0);
                point.Y-=Math.Abs(Math.Sin(p*Math.PI*2))*(1-p)*sprite.Height*.055;
                PlaceProp(d,point,120+450*p*towardBocchi);
            }else if(t>=3.65 && t<4.78)PlaceProp(d,floor,20);
            else if(t>=4.78 && t<5.12)
                PlaceProp(d,Between(floor,pickup,Smooth(Fraction(t,4.78,5.12)),0),12);
            else if(t>=5.12 && t<6.60){
                // These contact points are the projected hand pivots of the corresponding 1.8 frames.
                Point hand=pose==2?pickup:pose==1?AtActor(steveX,right?86:60,right?132:123,false):
                    pose==3?AtActor(steveX,right?85:63,right?132:124,false):AtActor(steveX,right?131:46,right?94:91,false);
                PlaceProp(d,hand,-19);
            }else if(t>=6.60 && t<7.62){
                var from=AtActor(steveX,right?131:46,right?94:91,false);
                var returned=AtActor(bocchiX,92,194,d.MirrorBocchi);
                double p=Fraction(t,6.60,7.35);
                PlaceProp(d,Between(from,returned,p,sprite.Height*.27),-19+p*320);
            }
        }
        void DrawPaperExchange(VisitScene scene,double t,double hostX,double guestX)
        {
            var d=scene.Duet;
            bool remHost=character=="rem";
            double remX=remHost?hostX:guestX,labX=remHost?guestX:hostX;
            BitmapSource lab=t<3.62?d.KurisuOut[t<1.95?0:t<2.38?1:t<2.82?2:t<3.22?3:4]:
                d.KurisuIn[t<7.68?0:t<7.99?1:t<8.45?2:t<8.83?3:t<9.55?4:5];
            BitmapSource rem=t>=4.53 && t<6.84
                ? (t<5.15?d.RemIn[t<4.85?1:2]:d.RemOut[t<5.56?2:t<6.02?3:t<6.49?5:4])
                : d.RemIn[t<1.9 || t>=8.85?5:0];
            DuoPose(scene,!remHost,lab,d.MirrorKurisu);
            DuoPose(scene,remHost,rem);
            // Join the loose sheet to the center of the paper in each adjacent hand-drawn
            // frame (rather than the actor's idle palm, which is extended further out).
            Point remHand=AtActor(remX,98,110,false);
            if(t>=3.62 && t<4.53){
                double p=Fraction(t,3.62,4.53);
                var paper=Between(AtActor(labX,91,102,d.MirrorKurisu),remHand,p,sprite.Height*.17);
                PlaceProp(d,paper,Math.Sin(p*Math.PI*2)*24);
            }else if(t>=6.84 && t<7.68){
                double p=Fraction(t,6.84,7.68);
                Point receive=AtActor(labX,67,94,d.MirrorKurisu);
                PlaceProp(d,Between(AtActor(remX,94,114,false),receive,p,sprite.Height*.10),Math.Sin(p*Math.PI)*-16);
            }
        }
    }
}
