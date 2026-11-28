using System;
using System.Runtime.InteropServices;

namespace DeskMate
{
    internal static class WindowPerchGeometryChecks
    {
        static int count;
        static void Need(bool ok,string reason)
        {
            if(!ok)throw new Exception(reason);
            count++;
        }
        static bool Align(PerchRect host,PerchRect pet,PerchRect work,double fraction,double scale,int padding,out int x,out int y)
        {return WindowPerchGeometry.TryAlign(host,pet,work,fraction,scale,padding,out x,out y);}
        [StructLayout(LayoutKind.Sequential)]
        struct DisplayPoint {public int X,Y;}
        [StructLayout(LayoutKind.Sequential)]
        struct DisplayRect {public int Left,Top,Right,Bottom;}
        delegate bool DisplayEnumerator(IntPtr display,IntPtr dc,ref DisplayRect rect,IntPtr data);
        [DllImport("user32.dll")]
        static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,DisplayEnumerator callback,IntPtr data);
        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromPoint(DisplayPoint point,uint flags);

        public static void Main(string[] args)
        {
            if(Array.IndexOf(args,"--windows-displays")>=0)
            {
                int displays=0;
                DisplayEnumerator check=delegate(IntPtr expected,IntPtr dc,ref DisplayRect r,IntPtr data)
                {
                    var middle=new DisplayPoint{X=r.Left+(r.Right-r.Left)/2,Y=r.Top+(r.Bottom-r.Top)/2};
                    Need(MonitorFromPoint(middle,0)==expected,"real Windows monitor lookup selects expected handle");
                    displays++;
                    return true;
                };
                Need(EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,check,IntPtr.Zero) && displays>0,
                    "native monitor enumeration");
                Console.WriteLine(displays+" live Windows displays checked (no mouse or foreground automation)");
            }
            var work=new PerchRect(0,0,1920,1040);
            var host=new PerchRect(300,390,1100,900);
            var pet=new PerchRect(709,152,931,400);
            double f;
            int x,y;
            Need(WindowPerchGeometry.TryDrop(host,pet,work,1,84,out f)&&Math.Abs(f-.65)<1e-9,"drop records fractional seat");
            Need(Align(host,pet,work,f,1,84,out x,out y)&&x==709&&y+pet.Height==389,"keep title bar entirely below pet HWND");
            var moved=new PerchRect(400,470,1200,980);
            Need(Align(moved,pet,work,f,1,84,out x,out y)&&x==809&&y==221,"follow moving window");
            var wider=new PerchRect(400,470,1400,980);
            Need(Align(wider,pet,work,f,1,84,out x,out y)&&x==939,"follow window width at same fraction");
            Need(!WindowPerchGeometry.TryDrop(host,new PerchRect(709,200,931,448),work,1,84,out f),"no distant attraction");
            Need(!WindowPerchGeometry.TryDrop(host,new PerchRect(200,150,422,398),work,1,84,out f),"no corner attraction");
            Need(!Align(new PerchRect(0,0,1920,1040),pet,work,.65,1,84,out x,out y),"maximized window has no headroom");
            Need(Align(new PerchRect(50,175,450,800),pet,work,.65,1,84,out x,out y)&&y<0&&y+84>=0,"offscreen transparent gutter allowed");
            Need(!Align(new PerchRect(50,175,450,800),pet,work,.65,1,0,out x,out y),"no clipping actual art");
            var left=new PerchRect(-1920,-120,0,950);
            var leftHost=new PerchRect(-1500,410,-300,830);
            var scaledPet=new PerchRect(-900,38,-567,410);
            Need(Align(leftHost,scaledPet,left,.65,1.5,126,out x,out y)&&x==-886&&y+scaledPet.Height==408,"mixed-DPI geometry on negative monitor");
            Need(Align(new PerchRect(-1900,410,-300,830),scaledPet,left,0,1.5,126,out x,out y)&&x==-1920,"keep leftmost pet on monitor");
            Need(Align(new PerchRect(-1200,410,-15,830),scaledPet,left,1,1.5,126,out x,out y)&&x+scaledPet.Width==0,"keep rightmost pet on monitor");
            Need(!Align(new PerchRect(500,390,560,900),pet,work,.5,1,84,out x,out y),"reject narrow host");
            Need(!Align(host,pet,work,double.NaN,1,84,out x,out y),"reject invalid anchor");
            Need(!Align(host,pet,work,.5,double.PositiveInfinity,84,out x,out y),"reject invalid DPI");
            Need(!Align(new PerchRect(300,1050,1100,1300),pet,work,.5,1,84,out x,out y),"perch cannot sit below working area");
            Need(WindowPerchMotion.Supports("mutsumi-stage") && WindowPerchMotion.Supports("steve") && !WindowPerchMotion.Supports("imported-pack"),"outfit and role eligibility is explicit");
            Need(WindowPerchGeometry.TryDropFraction(host,pet,1,out f),"drop attraction independent of selected artwork");
            Need(WindowPerchGeometry.TryAlignEdge(host,pet,work,.50,1,84,164,out x,out y) && y+84+104==391,
                "real seat contact is one pixel inside title-bar edge; legs overlap");
            Need(WindowPerchGeometry.TryAlignEdge(host,pet,work,1,1,84,164,out x,out y) && x+pet.Width<=host.Right-146,
                "right-edge seat cannot enter caption buttons");
            Need(WindowPerchGeometry.TryAlignEdge(host,pet,work,0,1,84,164,out x,out y) && x>=host.Left+34,
                "left-edge seat cannot enter caption icon");
            Need(!WindowPerchGeometry.TryAlignEdge(new PerchRect(0,0,1920,1040),pet,work,.5,1,84,164,out x,out y),
                "maximized host rejects clipped character");
            Need(!WindowPerchGeometry.TryAlignEdge(new PerchRect(300,300,620,900),pet,work,.5,1,84,164,out x,out y),
                "narrow window cannot cover caption buttons");
            Need(WindowPerchGeometry.TryAlignEdge(leftHost,scaledPet,left,.5,1.5,126,246,out x,out y) &&
                x<0 && y+126+156==411,"mixed DPI scales the exact off-primary contact coordinate");
            Need(WindowPerchGeometry.TryAlignScene(host,pet,work,.5,1,84,164,out x,out y) && y+248-13==389,
                "vignette floor aligns without transparent gap");
            Need(!WindowPerchGeometry.TryAlignScene(host,pet,work,.5,1,84,double.NaN,out x,out y),
                "vignette rejects invalid art size");
            Need(WindowPerchMotion.FrameAt(84*60,"steve",PerchMode.Scene)==84 &&
                WindowPerchMotion.FrameAt(85*60,"steve",PerchMode.Scene)==0,"Steve staircase 85 frames");
            Need(WindowPerchMotion.PoseCount("steve",PerchMode.Edge)==16 &&
                WindowPerchMotion.PoseCount("rem",PerchMode.Edge)==6 &&
                WindowPerchMotion.PoseCount("mutsumi-stage",PerchMode.Scene)==6 &&
                WindowPerchMotion.PoseCount("custom-series",PerchMode.Edge)==0,"atlases keyed by art and full outfit");
            foreach(string role in new[]{"hitori","rem","kurisu","mutsumi","mutsumi-stage","steve"})
            {
                foreach(PerchMode mode in new[]{PerchMode.Edge,PerchMode.Scene})
                {
                    int limit=WindowPerchMotion.PoseCount(role,mode);
                    bool[] visited=new bool[limit];
                    for(long t=0;t<12000;t+=17)
                    {
                        int frame=WindowPerchMotion.FrameAt(t,role,mode);
                        if(frame<0 || frame>=limit)throw new Exception("invalid frame address "+role);
                        visited[frame]=true;
                    }
                    Need(Array.IndexOf(visited,false)==-1,"every authored frame reachable "+role+" "+mode);
                    Need(WindowPerchMotion.FrameAt(long.MaxValue,role,mode)<limit,"extreme time safe");
                }
            }
            var grace=new PerchFocusGrace();
            Need(!grace.Expired(100,true,450),"host remains attached");
            Need(!grace.Expired(115,false,450) && !grace.Expired(420,false,450),"short popup does not move or hide pet");
            Need(!grace.Expired(425,true,450) && !grace.Expired(500,false,450),"returning to host resets the full grace interval");
            Need(!grace.Expired(949,false,450) && grace.Expired(950,false,450),"other app triggers desktop fallback only after sustained focus");
            grace.Reset();
            Need(!grace.Expired(1500,false,450),"explicit reset clears stale timing");
            Need(!WindowPerchGeometry.NeedsMove(800,390,801,389,true)
                && WindowPerchGeometry.NeedsMove(800,390,802,390,true),"one-pixel DWM noise ignored, real move follows");
            double m1=WindowPerchGeometry.HostFraction(host,300,1),m2=WindowPerchGeometry.HostFraction(wider,300,1);
            Need(Math.Abs(m1*host.Width-m2*wider.Width)<1e-9,"resizing host does not scale absolute anchor");
            Need(Math.Abs(WindowPerchGeometry.HostFraction(leftHost,300,1.5)*leftHost.Width-450)<1e-9,
                "anchor uses local physical scale when crossing monitors");
            var dualHost=new PerchRect(-1480,340,-480,850);
            Need(WindowPerchGeometry.HostAnchorX(dualHost,400,1.5)==-880,"negative monitor anchor in physical coordinates");
            var straddle=new PerchRect(-360,340,1020,850);
            Need(WindowPerchGeometry.HostAnchorX(straddle,100,1)==-260 &&
                WindowPerchGeometry.HostAnchorX(straddle,500,1)==140,"wide host selects anchor monitor, not dominant monitor");
            var paint=WindowPerchGeometry.PaintedArea(pet,640,230,84,152);
            Need(paint.Left==675 && paint.Right==827 && paint.Top==314 && paint.Bottom==478,
                "invisible margin and notification gutter do not count as the sprite for cover checks");
            var overlapping=new PerchRect(600,300,1000,700);
            var adjacent=new PerchRect(paint.Right,314,1200,478);
            Need(!WindowPerchGeometry.ForegroundCoversPet(false,true,overlapping,paint),
                "an app on the other display does not unseat the character");
            Need(!WindowPerchGeometry.ForegroundCoversPet(true,true,adjacent,paint),"same-display non-overlap does not disturb seat");
            Need(!WindowPerchGeometry.ForegroundCoversPet(true,false,overlapping,paint),"missing DWM sample does not unseat pet");
            Need(WindowPerchGeometry.ForegroundCoversPet(true,true,overlapping,paint),"actual same-monitor cover can yield");
            grace.Reset();
            for(int tick=0;tick<120;tick++)
            {
                bool otherDisplay=tick%4!=0;
                var front=otherDisplay?overlapping:adjacent;
                bool covers=WindowPerchGeometry.ForegroundCoversPet(!otherDisplay,true,front,paint);
                Need(!grace.Expired(tick*50,!covers,450),"alternating active monitor and same-monitor noncover cannot yield");
            }
            grace.Reset();
            for(int k=0;k<36;k++)
            {
                bool covers=k%7<5;
                Need(!grace.Expired(k*85,!covers,450),"short sustained cover, then click on other screen resets grace");
            }
            grace.Reset();
            Need(!grace.Expired(20,false,450) && grace.Expired(470,false,450) &&
                 !grace.Expired(471,true,450),"intentional same-display overlap falls back then restores");
            var maxed=new PerchRect(0,0,1920,1040);
            foreach(double dpi in new[]{1.0,1.25,1.5,1.75,2.0})
            {
                var scaled=new PerchRect(0,0,(int)Math.Round(222*dpi),(int)Math.Round(248*dpi));
                int pad=(int)Math.Round(84*dpi);
                double sh=scaled.Height-pad;
                Need(!WindowPerchGeometry.TryAlignEdge(maxed,scaled,work,.5,dpi,pad,sh,out x,out y),
                    "maximized without headroom reliably falls back");
                Need(WindowPerchGeometry.TryAlignEdge(new PerchRect(200,520,1580,1010),scaled,work,
                    .5,dpi,pad,sh,out x,out y),"restore returns valid geometry at simulated scale");
            }
            // Reproduce the reported order: a stationary host on monitor A, a
            // long-lived click/focus on B, null foreground then A; reach all six
            // Rem frames with one uninterrupted timer and one screen-space X.
            grace.Reset();
            int fixedX;
            Need(WindowPerchGeometry.TryAlignEdge(dualHost,scaledPet,left,.5,1.5,126,246,out fixedX,out y),
                "frozen test host on the left monitor");
            bool[] remPhases=new bool[6];
            for(int t=0;t<=7200;t+=50)
            {
                bool otherDisplay=t>=300 && t<4800;
                bool hasForeground=t<4800 || t>=5500;
                bool occlusion=WindowPerchGeometry.ForegroundCoversPet(!otherDisplay,hasForeground && otherDisplay,
                    overlapping,paint);
                Need(!grace.Expired(t,!occlusion,450),"cross-screen focus never unseats Rem");
                Need(WindowPerchGeometry.TryAlignEdge(dualHost,scaledPet,left,.5,1.5,126,246,out x,out y) &&
                    !WindowPerchGeometry.NeedsMove(fixedX,y,x,y,true),"stationary host retains exact screen-space X");
                remPhases[WindowPerchMotion.FrameAt(t,"rem",PerchMode.Edge)]=true;
            }
            Need(Array.IndexOf(remPhases,false)<0,"Rem loop continues on both screens with no phase reset");
            var back=new PerchRect(300,390,1100,900);
            var frontElsewhere=new PerchRect(0,455,800,900);
            Need(WindowPerchGeometry.EdgeIsExposed(back,620,620,new[]{frontElsewhere}),
                "visible inactive top edge wins even when front window overlaps lower part");
            var frontCoversEdge=new PerchRect(590,330,830,500);
            Need(!WindowPerchGeometry.EdgeIsExposed(back,620,620,new[]{frontCoversEdge}),
                "hidden inactive top edge does not accept drop");
            Need(!WindowPerchGeometry.EdgeIsExposed(back,580,650,new[]{frontCoversEdge}),
                "clamped seat cannot sneak behind an overlapping window");
            Need(WindowPerchGeometry.EdgeIsExposed(back,550,550,new[]{frontCoversEdge}),
                "exposed section of partially covered window still takes drop");
            Need(WindowPerchGeometry.EdgeIsExposed(leftHost,-720,-720,new[]{host}),
                "active right-screen app does not block left-screen drop");
            Need(WindowPerchGeometry.DropDistance(back,pet)<WindowPerchGeometry.DropDistance(wider,pet),
                "nearest eligible window edge outranks a distant one");
            var edgeTouch=new PerchRect(paint.Right-10,paint.Top,paint.Right+130,paint.Bottom);
            Need(!WindowPerchGeometry.ForegroundMostlyCoversPet(true,true,edgeTouch,paint),
                "small popup overlap does not suspend a perch");
            Need(WindowPerchGeometry.ForegroundMostlyCoversPet(true,true,overlapping,paint),
                "foreground covering the sprite can hide it without changing position");
            Need(PerchPresentationPolicy.Resolve(true,false,true,false,true)==PerchPresentation.Covered,
                "cover is a distinct state, not desktop relocation");
            Need(PerchPresentationPolicy.Resolve(true,false,true,false,false)==PerchPresentation.Seated,
                "uncover resumes the current seat");
            Need(PerchPresentationPolicy.Resolve(false,false,true,false,false)==PerchPresentation.HoldLastPlacement,
                "temporary bad geometry keeps old seat");
            Need(PerchPresentationPolicy.Resolve(false,false,true,true,false)==PerchPresentation.DesktopFallback,
                "lasting lack of safe room returns to desktop");
            Need(PerchPresentationPolicy.Resolve(true,true,true,false,false)==PerchPresentation.DesktopFallback,
                "real minimize is not a normal transient occlusion");
            var rectGrace=new PerchFocusGrace();
            Need(!rectGrace.Expired(0,false,850) && !rectGrace.Expired(700,false,850) &&
                !rectGrace.Expired(720,true,850),"drag and transient invalid bounds under 850 ms avoid bouncing");
            foreach(string role in new[]{"hitori","rem","kurisu","mutsumi","mutsumi-stage","steve"})
            {
                int duration=WindowPerchMotion.SceneDuration(role);
                Need(WindowPerchMotion.DesktopFrameAt(0,role)==0,"desktop vignette starts at first frame");
                Need(WindowPerchMotion.DesktopFrameAt(duration-1,role)==WindowPerchMotion.PoseCount(role,PerchMode.Scene)-1,
                    "desktop vignette reaches final distinct frame");
                Need(WindowPerchMotion.DesktopFrameAt(duration,role)==-1 &&
                    WindowPerchMotion.DesktopFrameAt(long.MaxValue,role)==-1,"desk vignette ends rather than looping");
                Need(WindowPerchMotion.SpriteFile(role,PerchMode.Edge).StartsWith("window-edge-") &&
                    WindowPerchMotion.SpriteFile(role,PerchMode.Scene).StartsWith("window-scene-"),"independent window and scene assets");
            }
            Need(WindowPerchMotion.DesktopFrameAt(0,"custom")==-1,"unsupported custom pack does not enter desktop vignette");
            Need(WindowPerchMotion.VignetteDelaySeconds("normal",true,0)==18 &&
                WindowPerchMotion.VignetteDelaySeconds("normal",true,14)==32 &&
                WindowPerchMotion.VignetteDelaySeconds("normal",false,0)==95 &&
                WindowPerchMotion.VignetteDelaySeconds("normal",false,75)==170,"normal desktop cadence");
            foreach(string role in new[]{"hitori","rem","kurisu","mutsumi","mutsumi-stage","steve"})
            {
                var routine=new WindowSeatRoutine(role,1984,0);
                Need(routine.At(0,false).Frame==0 && !routine.At(3000,false).Activity,"new seats start with an actual pause");
                int neutral=0,total=0,activities=0;
                bool[] micro=new bool[6];
                for(int t=3020;t<=180000;t+=20)
                {
                    var pose=routine.At(t,false);
                    if(pose.Activity)
                    {
                        activities++;
                        Need(pose.Frame<WindowPerchMotion.PoseCount(role,PerchMode.Edge),"native activity bound");
                    }
                    else
                    {
                        if(pose.Frame==0)neutral++;
                        micro[pose.Frame]=true;
                    }
                    total++;
                }
                Need(neutral*10>=total*7 && activities>0,"neutral dominates; special vignette appears occasionally");
                Need(micro[2] && micro[3] && micro[5],"left, right and individual quiet gestures are sampled");
                if(role=="steve")Need(!micro[1] && !micro[4],"Steve never blinks or bends legs in original rig");
                else Need(micro[1] && micro[4],"eyes blink and feet swing as distinct native poses");
                var quiet=new WindowSeatRoutine(role,1984,0);
                for(int t=0;t<90000;t+=90)
                    if(quiet.At(t,true).Activity)throw new Exception("quiet should not play full activity");
                Need(quiet.At(300000,true).Frame==0,"resume from sleep resets to still without spamming catch-up");
            }
            foreach(string role in new[]{"hitori","rem","kurisu","mutsumi","mutsumi-stage"})
            {
                int duration=WindowPerchMotion.SceneDuration(role);
                Need(duration>=5000 && duration<=6000,"desktop vignette has a readable multi-second pace");
                Need(WindowPerchMotion.DesktopFrameAt(476,role)==0 &&
                    WindowPerchMotion.DesktopFrameAt(duration-475,role)==5,"entrance and ending each hold rather than flash");
            }
            Need(WindowPerchMotion.SceneDuration("steve")==5100,"keep the accepted Steve timing");
            const long fruitReady=1000000;
            Need(!ProjectPlantTimeline.HasRipeCucumber(0,0,fruitReady),"no fruit on first empty/seedling stage");
            Need(!ProjectPlantTimeline.HasRipeCucumber(1,fruitReady-1,fruitReady),"seedling/vine phase is not ripe");
            Need(!ProjectPlantTimeline.HasRipeCucumber(2,fruitReady,fruitReady-1),"trellised but still growing");
            Need(!ProjectPlantTimeline.HasRipeCucumber(2,0,fruitReady),"invalid or old timestamp is not ripe");
            Need(ProjectPlantTimeline.MayShowCucumberScene(2,fruitReady,fruitReady,0),"show exactly at the ready threshold");
            Need(!ProjectPlantTimeline.MayShowCucumberScene(2,fruitReady,fruitReady+1,fruitReady),"once per plant cycle");
            Need(!ProjectPlantTimeline.HasRipeCucumber(0,fruitReady+1,fruitReady+5),"after harvest cannot display ripe fruit");
            Need(ProjectPlantTimeline.MayShowCucumberScene(2,fruitReady+10,fruitReady+11,fruitReady),"next batch can show once ripe");
            foreach(string role in new[]{"hitori","rem","kurisu","mutsumi","mutsumi-stage"})
            {
                var routine=new WindowSeatRoutine(role,7193,0,true,true);
                bool side=false,prone=false,footA=false,footB=false,returned=false,before=false;
                bool[] sittingFeet=new bool[WindowSeatRoutine.SwingPoseCount];
                int lounging=0;
                for(int t=0;t<270000;t+=50)
                {
                    var pose=routine.At(t,false);
                    if(t<35000 && pose.Lounge)throw new Exception("lounging cannot replace the normal initial seat");
                    if(pose.Swing)sittingFeet[pose.Frame]=true;
                    if(pose.Lounge)
                    {
                        if(pose.Activity || pose.Frame<0 || pose.Frame>=28)throw new Exception("Bad lounge cel");
                        lounging++;
                        if(pose.Frame==26)side=true;
                        if(pose.Frame==(role=="mutsumi-stage"?18:17))prone=true;
                        if(pose.Frame==20)footA=true;
                        if(pose.Frame==21)footB=true; // stage: the separately drawn stand-up transfer
                    }
                    if(before && !pose.Lounge && !pose.Activity && pose.Frame==0)returned=true;
                    before=pose.Lounge;
                }
                Need(side && prone && footA && footB && returned,"both short lounge stories play and return to seated");
                Need(lounging*5<(270000/50),"lounging is occasional, not a perpetual idle replacement");
                Need(Array.IndexOf(sittingFeet,false)<0,"sitting foot motion uses all four new easing cels");
            }
            Need(WindowSeatRoutine.SideCels[0]==22 && WindowSeatRoutine.SideCels[1]==23 &&
                Array.IndexOf(WindowSeatRoutine.SideCels,27)>Array.IndexOf(WindowSeatRoutine.SideCels,26) &&
                WindowSeatRoutine.SideCels[WindowSeatRoutine.SideCels.Length-1]==22,"side rest enters and exits through intermediate cels");
            Need(WindowSeatRoutine.ProneCels[0]>=12 && WindowSeatRoutine.ProneCels[1]==9 &&
                WindowSeatRoutine.ProneCels[WindowSeatRoutine.ProneCels.Length-1]>=12,"prone rest eases in and back up");
            foreach(string role in new[]{"rem","steve"})
            {
                var r=new WindowSeatRoutine(role,20,0,true,true);
                for(int t=0;t<300000;t+=75)
                    {
                    var pose=r.At(t,role!="steve");
                    if(pose.Lounge || (role=="steve" && pose.Swing))throw new Exception("No unsupported Steve or quiet lounging");
                    }
                Need(true,"Steve vanilla and quiet-mode safeguards preserved");
            }
            var editorOverlay=new PerchRect(560,380,1140,950);
            var otherScreenForeground=new PerchRect(-1780,180,-650,900);
            Need(WindowPerchGeometry.BlocksWindowSeat(editorOverlay,host,paint,720),
                "support hidden by top window even if most of the pet head is outside it");
            Need(!WindowPerchGeometry.BlocksWindowSeat(otherScreenForeground,host,paint,720),
                "clicking another screen alone does not obscure support");
            Need(!WindowPerchGeometry.BlocksWindowSeat(edgeTouch,host,paint,720),
                "a sliver cannot falsely hide the pet");
            var fastOcclusion=new PerchFocusGrace();
            bool blocked=WindowPerchGeometry.BlocksWindowSeat(editorOverlay,host,paint,720);
            Need(!fastOcclusion.Expired(0,!blocked,75) && fastOcclusion.Expired(100,!blocked,75),
                "quick response to real covering, not focus debounce");
            bool blockedAfterClick=WindowPerchGeometry.BlocksWindowSeat(otherScreenForeground,host,paint,720) ||
                WindowPerchGeometry.BlocksWindowSeat(editorOverlay,host,paint,720);
            Need(PerchPresentationPolicy.Resolve(true,false,true,false,blockedAfterClick)==PerchPresentation.Covered,
                "background editor still blocks after moving focus to secondary screen");
            Need(!fastOcclusion.Expired(120,true,75) &&
                PerchPresentationPolicy.Resolve(true,false,true,false,false)==PerchPresentation.Seated,
                "uncover resumes same seat without delay or desktop relocation");
            Need(Array.IndexOf(WindowSeatRoutine.ProneCels,17)>=0 &&
                Array.IndexOf(WindowSeatRoutine.ProneCels,19)>Array.IndexOf(WindowSeatRoutine.ProneCels,17),
                "prone at rest visibly precedes feet-up phase");
            int[] swing=WindowSeatRoutine.SwingCels;
            Need(swing[0]==-1 && swing[swing.Length-1]==-1,
                "use exactly the ordinary neutral seat on swing entrance and exit");
            for(int role=0;role<5;role++)
            {
                int duration=0;
                for(int i=0;i<swing.Length;i++)duration+=WindowSeatRoutine.SwingDuration(i,role);
                Need(duration>=3500 && duration<=5900,"alternating natural cycles remain unhurried");
                int lastFoot=-1;
                for(int i=0;i+4<swing.Length;i+=4)
                {
                    int ease=WindowSeatRoutine.SwingFrame(i+1,role),peak=WindowSeatRoutine.SwingFrame(i+2,role);
                    Need(WindowSeatRoutine.SwingFrame(i,role)==-1 && WindowSeatRoutine.SwingFrame(i+4,role)==-1,
                          "always put both feet down before changing side");
                    Need(ease/2==peak/2 && peak!=ease && WindowSeatRoutine.SwingFrame(i+3,role)==ease,
                          "one foot at a time, on the same drawing when easing back");
                    Need(lastFoot!=peak/2,"the other foot goes next");
                    lastFoot=peak/2;
                }
            }
            int[] stage=WindowSeatRoutine.StageProneCels;
            Need(stage.Length==WindowSeatRoutine.StageProneMs.Length && stage[0]==-1 && stage[stage.Length-1]==-1 &&
                Array.IndexOf(stage,17)<Array.IndexOf(stage,18) && Array.IndexOf(stage,18)<Array.IndexOf(stage,20) &&
                Array.LastIndexOf(stage,21)>Array.LastIndexOf(stage,20) &&
                stage[1]==12 && stage[2]==13 && stage[stage.Length-4]==11 && stage[stage.Length-3]==14 && stage[stage.Length-2]==15,
                 "stage lowers into folded forearms, rests flat, lifts feet only, then returns");
            foreach(int frame in stage)Need(frame==-1 || (frame>=11 && frame<=21),"no old mixed art in stage prone");
            int sideHeld=0,sideTransfer=0;
            for(int i=0;i<WindowSeatRoutine.SideMs.Length;i++)
                if(WindowSeatRoutine.SideCels[i]==25 || WindowSeatRoutine.SideCels[i]==26)
                    sideHeld+=WindowSeatRoutine.SideMs[i];
                else sideTransfer+=WindowSeatRoutine.SideMs[i];
            Need(sideHeld>=11000 && sideTransfer<1900,"extend rest, not the transition");
            var rhythms=new System.Collections.Generic.HashSet<string>();
            for(int role=0;role<5;role++)
            {
                int held=0;
                for(int i=0;i<WindowSeatRoutine.SideCels.Length;i++)
                    if(WindowSeatRoutine.SideCels[i]==25 || WindowSeatRoutine.SideCels[i]==26)
                        held+=WindowSeatRoutine.SideDuration(i,role);
                Need(held>=11000 && held<=12000,"each character still has a long side-rest hold");
                var cue=new System.Text.StringBuilder();
                int lifted=0;
                for(int i=0;i<WindowSeatRoutine.ProneCels.Length;i++)
                {
                    int p=WindowSeatRoutine.ProneFrame(i,role);
                    Need(p>=0 && p<22,"prone signature uses valid raster");
                    if(p>=19)lifted+=WindowSeatRoutine.ProneDuration(i,role);
                    cue.Append(p).Append(',');
                }
                Need(lifted>=4800 && lifted<=8500,"extended, unhurried foot swings");
                Need(rhythms.Add(cue.ToString()),"each outfit plays a distinct prone sequence");
            }
            Console.WriteLine(count+" window perch geometry and motion checks passed");
        }
    }
}
