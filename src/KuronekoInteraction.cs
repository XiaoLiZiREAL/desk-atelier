using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace DeskMate
{
    public sealed partial class PetWindow
    {
        readonly KuronekoClickGate kuronekoClicks=new KuronekoClickGate();
        readonly KuronekoStory kuronekoStory=new KuronekoStory();
        readonly KuronekoGaze kuronekoGaze=new KuronekoGaze();
        struct KuronekoZone
        {
            internal readonly int X,Y,W,H;
            internal readonly KuronekoTarget Target;
            internal KuronekoZone(KuronekoTarget target,int x,int y,int w,int h){Target=target;X=x;Y=y;W=w;H=h;}
            internal bool Contains(int x,int y){return x>=X && y>=Y && x<X+W && y<Y+H;}
        }
        readonly Dictionary<string,List<KuronekoZone>> kuronekoZones=new Dictionary<string,List<KuronekoZone>>();
        readonly byte[] kuronekoPixel=new byte[4];
        BitmapSource kuronekoPixelFrame,kuronekoPixelSource;
        BitmapSource[] kuronekoClip, kuronekoTransition, kuronekoExit, kuronekoNote, kuronekoOffer, kuronekoBook;
        long kuronekoClipStart,kuronekoNextInvite,kuronekoNextTap,kuronekoLastRender=-1,kuronekoNoteStart;
        bool kuronekoOwnRelease,kuronekoMapsRead;
        bool kuronekoNoteReturning;
        bool kuronekoNotePointerInside;
        long kuronekoNoteNotice=-1;
        KuronekoTarget kuronekoClipTarget;
        long kuronekoNoticeAt=-1;
        string kuronekoLayout;
        bool IsKuroneko {get{return currentArtId=="kuroneko";}}

        bool KuronekoMayInteract()
        {
            return IsKuroneko && IsVisible && !manuallyHidden && !fullscreenHidden && !perchCovered &&
                !app.Data.Quiet && !app.FocusActive && !MusicPlaying && !PerformingPaper &&
                visitor==null && !dragging && !KuronekoToolsOpen() && bridgeKind==null;
        }
        bool KuronekoToolsOpen()
        {
            // A visible settings card is where people select the character,
            // and does not need to veto clicks actually delivered to the pet.
            return (quickMenu!=null && quickMenu.IsOpen) || menu.Visible ||
                (app.Desk!=null && app.Desk.IsVisible && app.Desk.CurrentTool!="home" && app.Desk.CurrentTool!="settings") ||
                (app.NotesDesk!=null && app.NotesDesk.IsVisible && !app.NotesDesk.KeepsStickyOpen);
        }
        void ResetKuroneko()
        {
            ClearKuronekoStoryArt();kuronekoGaze.Reset();
            kuronekoNote=null;kuronekoLayout=null;
            ReleaseKuronekoPixels();
            if(perchPoseArt=="kuroneko")
            {
                perchPoses=perchStillPoses=perchLoungePoses=perchSwingPoses=null;
                perchPoseArt=null;
            }
            if(desktopSceneArt=="kuroneko")
            {
                desktopScenePoses=null;
                desktopSceneArt=null;
            }
            kuronekoMapsRead=false;kuronekoZones.Clear();
            kuronekoLastRender=-1;kuronekoNextTap=0;kuronekoNextInvite=clock.ElapsedMilliseconds+60000;
        }
        void ReleaseKuronekoPixels(){kuronekoPixelFrame=kuronekoPixelSource=null;}
        void ClearKuronekoStoryArt()
        {
            kuronekoClicks.Cancel();kuronekoStory.Cancel();
            kuronekoClip=kuronekoTransition=kuronekoExit=kuronekoOffer=kuronekoBook=null;
            kuronekoNoticeAt=-1;
        }
        void ClearKuronekoMoment()
        {
            if(!IsKuroneko)return;
            ClearKuronekoStoryArt();kuronekoGaze.Reset();kuronekoNote=null;kuronekoLayout=null;ReleaseKuronekoPixels();
        }
        BitmapSource[] KuronekoFrames(string key)
        {
            // Load a single clip only when it is needed. There is no persistent
            // atlas owner once the active scene loses its final BitmapSource.
            bool standing=key=="stand-hair" || key=="stand-collar" || key=="stand-cuff" || key=="stand-hand";
            bool shoes=key=="seat-leftshoe" || key=="seat-rightshoe";
            return LoadPerchAtlas("kuroneko-interaction-"+key+".png",standing?12:shoes?8:6);
        }
        void ReadKuronekoZones()
        {
            if(kuronekoMapsRead)return;
            kuronekoMapsRead=true;
            string path=app.Asset("kuroneko-zones.txt");
            try{
                if(!File.Exists(path) || new FileInfo(path).Length>65536)return;
                foreach(string line in File.ReadLines(path))
                {
                    var fields=line.Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries);
                    int x,y,w,h;KuronekoTarget part;
                    if(fields.Length!=6 || !Enum.TryParse(fields[1],false,out part) || !Enum.IsDefined(typeof(KuronekoTarget),part) || part==KuronekoTarget.None ||
                        !int.TryParse(fields[2],NumberStyles.None,CultureInfo.InvariantCulture,out x) ||
                        !int.TryParse(fields[3],NumberStyles.None,CultureInfo.InvariantCulture,out y) ||
                        !int.TryParse(fields[4],NumberStyles.None,CultureInfo.InvariantCulture,out w) ||
                        !int.TryParse(fields[5],NumberStyles.None,CultureInfo.InvariantCulture,out h) ||
                        x>=192 || y>=208 || w<1 || h<1 || w>192-x || h>208-y)continue;
                    List<KuronekoZone> regions;
                    if(!kuronekoZones.TryGetValue(fields[0],out regions))
                    {
                        if(kuronekoZones.Count>=64)continue;
                        regions=new List<KuronekoZone>();kuronekoZones.Add(fields[0],regions);
                    }
                    if(regions.Count<24) regions.Add(new KuronekoZone(part,x,y,w,h));
                }
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        string CurrentKuronekoLayout()
        {
            if(kuronekoStory.Beat!=ManuscriptBeat.None)
                return kuronekoStory.Beat==ManuscriptBeat.Offer?kuronekoLayout:null;
            if(kuronekoClip!=null)return null;
            if(perchAligned)return kuronekoLayout;
            if(activePoses==null && row==Idle && !ShowingDesktopScene) return "standing";
            return null;
        }
        KuronekoHit FindKuronekoHit(Point p)
        {
            if(!KuronekoMayInteract())return default(KuronekoHit);
            string layout=CurrentKuronekoLayout();
            if(layout==null || sprite.ActualWidth<=0 || sprite.ActualHeight<=0)return default(KuronekoHit);
            var frame=sprite.Source as BitmapSource;
            if(frame==null)return default(KuronekoHit);
            if(layout=="seated")
            {
                int swing=perchSwingPoses==null?-1:Array.IndexOf(perchSwingPoses,frame);
                if(swing>=0)layout="swing-"+swing.ToString(CultureInfo.InvariantCulture);
                else{
                    int cel=perchStillPoses==null?-1:Array.IndexOf(perchStillPoses,frame);
                    if(cel<0 && perchPoses!=null)cel=Array.IndexOf(perchPoses,frame);
                    if(cel<0)return default(KuronekoHit);
                    layout="seated-"+cel.ToString(CultureInfo.InvariantCulture);
                }
            }
            // WPF Uniform stretch can letterbox as the window is scaled.
            double scale=Math.Min(sprite.ActualWidth/frame.PixelWidth,sprite.ActualHeight/frame.PixelHeight);
            double fw=frame.PixelWidth*scale,fh=frame.PixelHeight*scale;
            double u=(p.X-(sprite.ActualWidth-fw)/2)/fw,v=(p.Y-(sprite.ActualHeight-fh)/2)/fh;
            if(u<0 || v<0 || u>=1 || v>=1)return default(KuronekoHit);
            ReadKuronekoZones();
            List<KuronekoZone> map;
            if(!kuronekoZones.TryGetValue(layout,out map))return default(KuronekoHit);
            if(frame!=kuronekoPixelFrame){kuronekoPixelFrame=frame;kuronekoPixelSource=PetSprite.Readable(frame);}
            kuronekoPixelSource.CopyPixels(new Int32Rect((int)(u*frame.PixelWidth),(int)(v*frame.PixelHeight),1,1),kuronekoPixel,4,0);
            if(kuronekoPixel[3]<32)return default(KuronekoHit);
            KuronekoTarget target=KuronekoTarget.None;
            int mx=(int)(192*u),my=(int)(208*v);
            bool skin=KuronekoChoreography.IsHandPixel(kuronekoPixel[0],kuronekoPixel[1],kuronekoPixel[2],kuronekoPixel[3]);
            for(int i=map.Count-1;i>=0;i--)if(map[i].Contains(mx,my) && (map[i].Target!=KuronekoTarget.Hand || skin)){target=map[i].Target;break;}
            if(target==KuronekoTarget.None)return default(KuronekoHit);
            bool offer=kuronekoStory.Beat==ManuscriptBeat.Offer;
            return new KuronekoHit(target,offer?KuronekoPose.Prone:perchAligned?KuronekoPose.Seated:KuronekoPose.Standing,offer?kuronekoStory.Episode:0);
        }
        void TickKuronekoClick(long now)
        {
            if(!IsKuroneko || !kuronekoClicks.HasPending || pressing)return;
            bool offer=kuronekoStory.Beat==ManuscriptBeat.Offer;
            string layout=CurrentKuronekoLayout();
            var pose=offer?KuronekoPose.Prone:layout=="seated"?KuronekoPose.Seated:layout=="standing"?KuronekoPose.Standing:KuronekoPose.None;
            var target=kuronekoClicks.Tick(now,pose,offer?kuronekoStory.Episode:0,KuronekoMayInteract());
            if(target==KuronekoTarget.None || now<kuronekoNextTap)return;
            if(target==KuronekoTarget.Manuscript && kuronekoStory.Accept(now)){ReleaseKuronekoPixels();kuronekoNextTap=now+2500;return;}
            if(kuronekoStory.Beat!=ManuscriptBeat.None)return;
            string name=(perchAligned?"seat-":"stand-")+target.ToString().ToLowerInvariant();
            BitmapSource[] art=KuronekoFrames(name);
            if(art==null)return;
            if(perchAligned && windowSeatRoutine!=null && (target==KuronekoTarget.LeftShoe || target==KuronekoTarget.RightShoe))
                windowSeatRoutine.InterruptForGesture(now);
            kuronekoClip=art;kuronekoClipStart=now;kuronekoClipTarget=target;kuronekoNextTap=now+300;
        }
        bool QueueKuronekoSeatedNote(bool returning)
        {
            if(!IsKuroneko || !perchAligned || perchCovered)return false;
            // The actual note was handled upstream. Don't play a second full-body
            // scene over a running focus session or fall back to a standing cue.
            if(app.FocusActive && !app.FocusPaused){kuronekoNote=null;return true;}
            var art=KuronekoFrames(returning?"seat-return-note":"seat-note");
            if(art==null)return false;
            kuronekoNote=art;kuronekoNoteReturning=returning;
            kuronekoNoteNotice=-1;kuronekoNotePointerInside=KuronekoNotePointerInFan();
            kuronekoNoteStart=clock.ElapsedMilliseconds;
            return true;
        }
        bool RenderKuronekoNote(long now)
        {
            if(kuronekoNote==null || !IsKuroneko || !perchAligned || perchCovered)return false;
            if(app.FocusActive && !app.FocusPaused){kuronekoNote=null;return false;}
            long elapsed=now-kuronekoNoteStart;
            if(kuronekoNoteReturning && kuronekoNoteNotice<0)
            {
                bool inside=KuronekoNotePointerInFan();
                if(inside && !kuronekoNotePointerInside && KuronekoChoreography.CanNoticeNote(elapsed))kuronekoNoteNotice=elapsed;
                kuronekoNotePointerInside=inside;
            }
            int frame=KuronekoChoreography.SeatedNote(elapsed,kuronekoNoteReturning,kuronekoNoteNotice);
            if(frame<0){kuronekoNote=null;return false;}
            ShowPose(kuronekoNote[frame]);return true;
        }
        bool KuronekoNotePointerInFan()
        {
            if(!sprite.IsVisible || sprite.ActualWidth<=0 || sprite.ActualHeight<=0)return false;
            Point p=Mouse.GetPosition(sprite);
            double scale=Math.Min(sprite.ActualWidth/192.0,sprite.ActualHeight/208.0);
            double x=(p.X-(sprite.ActualWidth-192*scale)/2)/scale;
            double y=(p.Y-(sprite.ActualHeight-208*scale)/2)/scale;
            return x>=16 && x<=62 && y>=63 && y<=103;
        }
        bool RenderKuroneko(long now,bool seated,bool neutral,bool shoes=false)
        {
            if(!IsKuroneko)return false;
            if(kuronekoLastRender>=0 && (now<kuronekoLastRender || now-kuronekoLastRender>12000))ClearKuronekoMoment();
            kuronekoLastRender=now;
            if(!KuronekoMayInteract()){ClearKuronekoMoment();return false;}
            if(seated && kuronekoStory.Beat==ManuscriptBeat.None)kuronekoLayout=shoes?"seated":null;
            TickKuronekoClick(now);
            if(kuronekoClip!=null && kuronekoStory.Beat==ManuscriptBeat.None)
            {
                int frame=KuronekoGestureTiming.Frame(kuronekoClipTarget,now-kuronekoClipStart);
                if(frame>=0 && frame<kuronekoClip.Length){ShowPose(kuronekoClip[frame]);return true;}
                kuronekoClip=null;
            }
            if(!seated)
            {
                if(kuronekoStory.Beat!=ManuscriptBeat.None)ClearKuronekoMoment();
                return false;
            }
            var before=kuronekoStory.Beat;
            if(before==ManuscriptBeat.None && neutral && !pressing && !kuronekoClicks.HasPending && now>=kuronekoNextInvite && !ToolOpen() && IsPointerNearPet())
            {
                kuronekoNextInvite=now+random.Next(150000,280001);
                ReadKuronekoZones();
                if(kuronekoZones.ContainsKey("offer"))
                {
                    var story=KuronekoFrames("manuscript");
                    var entry=KuronekoFrames("manuscript-entry");
                    var exit=KuronekoFrames("manuscript-exit");
                    if(story!=null && entry!=null && exit!=null)
                    {
                        kuronekoClip=story;kuronekoTransition=entry;kuronekoExit=exit;
                        kuronekoOffer=KuronekoFrames("manuscript-offer");
                        kuronekoBook=kuronekoOffer==null?null:KuronekoFrames("manuscript-book");
                        kuronekoStory.Start(now);
                    }
                }
            }
            kuronekoStory.Step(now,true);
            switch(kuronekoStory.Beat)
            {
                case ManuscriptBeat.Approach:
                    kuronekoLayout=null;
                    long elapsedEntry=KuronekoStory.ApproachMs-(kuronekoStory.Until-now);
                    int liftCel=KuronekoStory.LiftCel(elapsedEntry);
                    if(liftCel>=0 && kuronekoBook!=null){ShowPose(kuronekoBook[liftCel]);return true;}
                    int entryCel=KuronekoStory.EntryCel(elapsedEntry);
                    ShowPose(entryCel<6?kuronekoTransition[entryCel]:kuronekoClip[entryCel-6]);
                    return true;
                case ManuscriptBeat.Offer:
                    kuronekoLayout=kuronekoOffer==null || !kuronekoZones.ContainsKey("offer-book")?"offer":"offer-book";
                    if(kuronekoOffer==null){ShowPose(kuronekoClip[2]);return true;}
                    long elapsedOffer=KuronekoStory.InvitationMs-(kuronekoStory.Until-now);
                    if(before==ManuscriptBeat.Offer && kuronekoNoticeAt<0 && elapsedOffer>=240 &&
                        FindKuronekoHit(Mouse.GetPosition(sprite)).Target==KuronekoTarget.Manuscript)
                        kuronekoNoticeAt=now;
                    int offerCel=KuronekoStory.OfferCel(elapsedOffer,kuronekoNoticeAt<0?-1:now-kuronekoNoticeAt);
                    ShowPose(kuronekoOffer[offerCel]);return true;
                case ManuscriptBeat.Share:
                    kuronekoLayout=null;
                    int lowerToShare=KuronekoStory.LowerCel(KuronekoStory.ShareMs-(kuronekoStory.Until-now));
                    ShowPose(lowerToShare>=0 && kuronekoBook!=null?kuronekoBook[lowerToShare]:kuronekoClip[kuronekoStory.Until-now>1150?3:4]);
                    return true;
                case ManuscriptBeat.Withdraw:
                    kuronekoLayout=null;
                    long elapsedExit=kuronekoStory.WithdrawDuration-(kuronekoStory.Until-now);
                    if(!kuronekoStory.Shared)
                    {
                        int lowerToClose=KuronekoStory.LowerCel(elapsedExit);
                        if(lowerToClose>=0 && kuronekoBook!=null){ShowPose(kuronekoBook[lowerToClose]);return true;}
                        elapsedExit-=KuronekoStory.LowerMs;
                    }
                    int exitCel=KuronekoStory.ExitCel(elapsedExit);
                    ShowPose(exitCel<0?kuronekoClip[5]:kuronekoExit[exitCel]);
                    return true;
                default:
                    if(before!=ManuscriptBeat.None){ClearKuronekoStoryArt();ReleaseKuronekoPixels();}
                    kuronekoLayout=shoes?"seated":null;return false;
            }
        }
    }
}
