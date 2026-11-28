using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskMate
{
    public sealed partial class PetWindow
    {
        const int BridgeFrameMs=90;
        BitmapSource[] bridgeFrames,mirroredSteps;
        string bridgeKind,mirroredRole;
        int bridgeFrom,bridgeTo,previousDragX,dragDirection;
        long bridgeSince;
        long pickupElapsed;
        bool musicGuitarParked;
        readonly Image parkedProp=new Image { IsHitTestVisible=false,HorizontalAlignment=HorizontalAlignment.Center,
            VerticalAlignment=VerticalAlignment.Bottom,Stretch=Stretch.Uniform };
        // Her cup may already be parked when music starts. The stage needs a separate
        // place for the guitar while both of her hands are occupied by a sheet of paper.
        readonly Image parkedMusicGuitar=new Image { IsHitTestVisible=false,HorizontalAlignment=HorizontalAlignment.Center,
            VerticalAlignment=VerticalAlignment.Bottom,Stretch=Stretch.Uniform };
        readonly Dictionary<string,ImageSource> floorDrawings=new Dictionary<string,ImageSource>();

        long BridgeEnd { get { return bridgeSince+(Math.Abs(bridgeTo-bridgeFrom)+1)*BridgeFrameMs; } }
        bool LoweringProp { get { return bridgeKind=="setdown" || bridgeKind=="musicsetdown"; } }
        bool MusicPaperGuitar { get { return bridgeKind=="musicsetdown" || bridgeKind=="musicpickup" || musicGuitarParked; } }

        bool BeginBridge(string kind,BitmapSource[] sheet,int from,int to)
        {
            if(sheet==null || app.Data.Quiet)return false;
            bridgeKind=kind;bridgeFrames=sheet;bridgeFrom=from;bridgeTo=to;
            bridgeSince=clock.ElapsedMilliseconds;ShowPose(sheet[from]);return true;
        }
        void ClearBridge()
        {
            bridgeKind=null;bridgeFrames=null;
        }
        bool AdvanceBridge(long now)
        {
            if(bridgeFrames==null)return false;
            if(now>=BridgeEnd)
            {
                if(bridgeKind=="setdown" && interrupted!=null && interrupted.Bridge!=null)
                    parkedProp.Source=FloorDrawing(interrupted.Bridge,bridgeFrames[5]);
                if(bridgeKind=="musicsetdown"){
                    musicGuitarParked=true;parkedMusicGuitar.Source=FloorDrawing("guitarbridge",bridgeFrames[5]);
                }
                ClearBridge();return false;
            }
            int step=(int)(Math.Max(0,now-bridgeSince)/BridgeFrameMs);
            ShowPose(bridgeFrames[bridgeFrom+step*Math.Sign(bridgeTo-bridgeFrom)]);
            return true;
        }
        static bool IsGuitarActivity(string key)
        {
            return key=="tune" || key=="mutepractice" || key=="stringwipe";
        }
        void ParkMusicGuitar(bool wasPlaying)
        {
            if(!wasPlaying && bridgeKind!="musicpickup")return;
            int from=bridgeKind=="musicpickup"?Math.Max(0,5-(int)((clock.ElapsedMilliseconds-bridgeSince)/BridgeFrameMs)):0;
            BeginBridge("musicsetdown",ActionFrames(character,"guitarbridge"),from,5);
        }
        bool TakeStandingGuitar(string kind)
        {
            if(!musicGuitarParked || !BeginBridge(kind,ActionFrames(character,"guitarbridge"),5,0))return false;
            musicGuitarParked=false;parkedMusicGuitar.Source=null;
            if(interrupted!=null && interrupted.Bridge=="guitarbridge"){interrupted.Bridge=null;parkedProp.Source=null;}
            return true;
        }
        BitmapSource[] StepFrames(int direction)
        {
            var steps=ActionFrames(character,"turnstep");
            if(steps==null || direction>=0)return steps;
            if(mirroredRole==currentArtId && mirroredSteps!=null)return mirroredSteps;
            if(character=="mutsumi"){
                mirroredRole=currentArtId;mirroredSteps=MutsumiLeftSteps(frames);
                return mirroredSteps;
            }
            mirroredRole=currentArtId;mirroredSteps=new BitmapSource[steps.Length];
            for(int i=0;i<steps.Length;i++)
            {
                var flip=new TransformedBitmap(steps[i],new ScaleTransform(-1,1));
                flip.Freeze();mirroredSteps[i]=flip;
            }
            return mirroredSteps;
        }
        // Keep Mutsumi's hairpin and school crest on their drawn sides when she turns.
        static BitmapSource[] MutsumiLeftSteps(BitmapSource[][] atlas)
        {
            var walk=atlas[Leftward];
            return new[]{atlas[Idle][0],walk[0],walk[1],walk[2],walk[7],atlas[Idle][0]};
        }
        bool HoldingCup(long elapsed)
        {
            if(performance!="tea")return false;
            if(character=="rem" || character=="kurisu" || character=="hitori" || character=="mutsumi")return true;
            int pose=ActionScore.For("tea").At(elapsed);
            return pose>=1 && pose<=4;
        }
        void SetPropAside(InterruptedActivity saved,bool keepGuitar)
        {
            int pose=ActionScore.For(performance).At(saved.Elapsed);
            string key;
            if(HoldingCup(saved.Elapsed))
            {
                key="propbridge";
                saved.Grip=pose==0 || pose==5?2:pose==1 || pose==4?1:0;
            }
            else if(character=="steve" && performance=="mine" && pose!=5)key="toolbridge";
            else if(character=="kurisu" && performance=="pen" && pose!=2 && pose!=3)
            {
                key="penbridge";saved.Grip=pose==5?1:0;
            }
            else if(!keepGuitar && IsGuitarActivity(performance) && UsesMutsumiActions)
                key="guitarbridge";
            else return;
            int from=bridgeKind=="pickup"?Math.Max(bridgeTo,bridgeFrom-(int)((clock.ElapsedMilliseconds-bridgeSince)/BridgeFrameMs)):saved.Grip;
            if(BeginBridge("setdown",ActionFrames(character,key),from,5))saved.Bridge=key;
        }
        ImageSource FloorDrawing(string key,BitmapSource finalPose)
        {
            string artId=currentArtId;
            string id=artId+"|"+key;
            ImageSource drawing;
            if(floorDrawings.TryGetValue(id,out drawing))return drawing;
            if(!CompanionArchive.IsBuiltin(character)){
                if(key!="guitarbridge")return null;
                // Edited packs often keep the original guitar stand. Its crop is only
                // valid if the last cel retained the same visible pixels and layout.
                bool matched=false;
                foreach(string originalId in new[]{"mutsumi","mutsumi-stage"}){
                    var original=ReadActionArt(originalId,true,key);
                    if(original==null || !SamePropPose(original[5],finalPose))continue;
                    artId=originalId;finalPose=original[5];matched=true;break;
                }
                if(!matched){floorDrawings[id]=null;return null;}
            }
            // The authored last pose leaves the object separate from the body. Preserve its
            // original scale and location while the hands are busy with a note.
            int right=key=="penbridge" || key=="toolbridge"?80:character=="steve"?65:74;
            var area=key=="guitarbridge"?new Int32Rect(120,61,61,138):new Int32Rect(0,165,right,43);
            // The stage stand opens wider near her shoes. Split at the skirt hem to
            // include both feet of the stand without copying the brim or cape.
            var pieces=key=="guitarbridge" && artId=="mutsumi-stage"
                ?new[]{new Int32Rect(124,73,57,66),new Int32Rect(110,139,71,62)}:new[]{area};
            var group=new DrawingGroup();
            using(var dc=group.Open())
            {
                dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,192,208));
                foreach(var piece in pieces)
                    dc.DrawImage(new CroppedBitmap(finalPose,piece),new Rect(piece.X,piece.Y,piece.Width,piece.Height));
            }
            var image=new DrawingImage(group);image.Freeze();floorDrawings[id]=image;return image;
        }
        static bool SamePropPose(BitmapSource a,BitmapSource b)
        {
            if(a.PixelWidth!=b.PixelWidth || a.PixelHeight!=b.PixelHeight)return false;
            int stride=a.PixelWidth*4;
            var left=new byte[stride*a.PixelHeight];var right=new byte[left.Length];
            new FormatConvertedBitmap(a,PixelFormats.Pbgra32,null,0).CopyPixels(left,stride,0);
            new FormatConvertedBitmap(b,PixelFormats.Pbgra32,null,0).CopyPixels(right,stride,0);
            return left.SequenceEqual(right);
        }
        void BeginDragMotion()
        {
            long now=clock.ElapsedMilliseconds;
            var cup=independentPerformance && HoldingCup(now-performanceStart)?ActionFrames(character,"propbridge"):null;
            carriedPose=independentPerformance?sprite.Source as BitmapSource:null;
            ClearBridge();
            var novel=IsKuroneko && (independentPerformance && (performance=="focus" || performance.StartsWith("project",StringComparison.Ordinal)))?
                ActionFrames(character,"authorcarry"):null;
            if(novel!=null)
            {
                carriedPose=novel[2];
                BeginBridge("holdbook",novel,0,2);
            }
            else if(cup!=null)
            {
                carriedPose=cup[2];
                BeginBridge("holdcup",cup,1,2);
            }
            previousDragX=0;dragDirection=0;
        }
        void UpdateDragMotion(int dx)
        {
            int direction=Math.Sign(dx-previousDragX);previousDragX=dx;
            if(direction==0 && dragDirection!=0)return;
            if(direction==0)direction=1;
            if(dragDirection==direction)return;
            dragDirection=direction;
            row=direction<0?Leftward:Right;shownSince=clock.ElapsedMilliseconds;
            if(carriedPose==null)BeginBridge("startstep",StepFrames(direction),0,3);
        }
        void FinishDragMotion()
        {
            bool carried=carriedPose!=null;
            carriedPose=null;ClearBridge();
            ClampCurrent();RememberPosition();
            if(!carried && BeginBridge("stopstep",StepFrames(dragDirection),4,5))
            {
                row=Idle;effectUntil=0;return;
            }
            if(!ResumePerformance(clock.ElapsedMilliseconds))SetEffect(Idle,0);
        }
    }
}
