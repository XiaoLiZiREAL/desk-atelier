using System;
using System.Collections.Generic;
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
        readonly Image parkedProp=new Image { IsHitTestVisible=false,HorizontalAlignment=HorizontalAlignment.Center,
            VerticalAlignment=VerticalAlignment.Bottom,Stretch=Stretch.Uniform };
        readonly Dictionary<string,ImageSource> floorDrawings=new Dictionary<string,ImageSource>();

        long BridgeEnd { get { return bridgeSince+(Math.Abs(bridgeTo-bridgeFrom)+1)*BridgeFrameMs; } }

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
                ClearBridge();return false;
            }
            int step=(int)(Math.Max(0,now-bridgeSince)/BridgeFrameMs);
            ShowPose(bridgeFrames[bridgeFrom+step*Math.Sign(bridgeTo-bridgeFrom)]);
            return true;
        }
        BitmapSource[] StepFrames(int direction)
        {
            var steps=ActionFrames(character,"turnstep");
            if(steps==null || direction>=0)return steps;
            if(mirroredRole==character && mirroredSteps!=null)return mirroredSteps;
            mirroredRole=character;mirroredSteps=new BitmapSource[steps.Length];
            for(int i=0;i<steps.Length;i++)
            {
                var flip=new TransformedBitmap(steps[i],new ScaleTransform(-1,1));
                flip.Freeze();mirroredSteps[i]=flip;
            }
            return mirroredSteps;
        }
        bool HoldingCup(long elapsed)
        {
            if(performance!="tea")return false;
            if(character=="rem" || character=="kurisu" || character=="hitori")return true;
            int pose=ActionScore.For("tea").At(elapsed);
            return pose>=1 && pose<=4;
        }
        void SetPropAside(InterruptedActivity saved)
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
            else return;
            int from=bridgeKind=="pickup"?Math.Max(bridgeTo,bridgeFrom-(int)((clock.ElapsedMilliseconds-bridgeSince)/BridgeFrameMs)):saved.Grip;
            if(BeginBridge("setdown",ActionFrames(character,key),from,5))saved.Bridge=key;
        }
        ImageSource FloorDrawing(string key,BitmapSource finalPose)
        {
            if(!CompanionArchive.IsBuiltin(character))return null;
            string id=character+"|"+key;
            ImageSource drawing;
            if(floorDrawings.TryGetValue(id,out drawing))return drawing;
            // These rectangles in the authored final poses contain only the object resting by the shoes.
            int right=key=="penbridge" || key=="toolbridge"?80:character=="steve"?65:74;
            var area=new Int32Rect(0,165,right,43);
            var group=new DrawingGroup();
            using(var dc=group.Open())
            {
                dc.DrawRectangle(Brushes.Transparent,null,new Rect(0,0,192,208));
                dc.DrawImage(new CroppedBitmap(finalPose,area),new Rect(area.X,area.Y,area.Width,area.Height));
            }
            var image=new DrawingImage(group);image.Freeze();floorDrawings[id]=image;return image;
        }
        void BeginDragMotion()
        {
            long now=clock.ElapsedMilliseconds;
            var cup=independentPerformance && HoldingCup(now-performanceStart)?ActionFrames(character,"propbridge"):null;
            carriedPose=independentPerformance?sprite.Source as BitmapSource:null;
            ClearBridge();
            if(cup!=null)
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
