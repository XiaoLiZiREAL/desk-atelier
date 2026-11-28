using System;
using System.Windows;

namespace DeskMate
{
    public sealed partial class PetWindow
    {
        readonly FocusCompanionCue focusCue=new FocusCompanionCue();
        MineFrameCache mineFrames;
        string FocusMateCostume { get { return perchAligned?"focusmate":"focusmate-desk"; } }
        bool HasFocusCostume { get { return FocusCompanionCue.Supports(currentArtId) &&
            (currentArtId=="steve"?System.IO.File.Exists(app.Asset("motion-steve-focuscave-torch-pass.dca")):
             ActionFrames(character,FocusMateCostume)!=null); } }
        bool testPhoneWave;
        internal void FocusSessionStarted()
        {
            focusCue.Start(random.Next());
            EndIndependentVisit();
            // An unfinished game invitation is not a scene that should block
            // focus; ordinary finite authored movements keep their ending.
            if(OfferingGame)StopPerformance();
            testPhoneWave=random.Next(5)==0;
        }
        internal void FocusSessionEnded(bool completed)
        {
            focusCue.Stop(completed);
            ambientAt=NextAmbient();
        }

        bool PresentFocusCompanion(long now,bool ready)
        {
            // A persisted timer may already be running when the pet is opened.
            // On exit, never replay the celebration on a role that didn't attend.
            if(app.FocusActive && !focusCue.Pending)FocusSessionStarted();
            if(!app.FocusActive && focusCue.Pending)focusCue.Stop(false);
            bool available=focusCue.Pending && HasFocusCostume && IsVisible && !manuallyHidden &&
                !fullscreenHidden && !perchCovered && !pressing && !dragging && visitor==null &&
                !app.Data.Quiet && !MusicPlaying && !PerformingPaper &&
                (quickMenu==null || !quickMenu.IsOpen) && !menu.Visible;
            var entry=focusCue.Pending && !perchAligned && HasFocusCostume?ActionFrames(character,"focusmate-entry"):null;
            int frame=focusCue.Step(currentArtId,now,available && ready,app.FocusPaused,entry!=null);
            if(frame<0)return false;
            string costume=FocusMateCostume;
            if(currentArtId=="steve")
            {
                if(mineFrames==null)mineFrames=new MineFrameCache(app.Asset);
                var bank=mineFrames.Get(focusCue.MineVariant);
                if(bank==null)return false;
                System.Windows.Media.Imaging.BitmapSource scene;
                try{scene=bank.Draw(frame);}
                catch(System.IO.InvalidDataException){return false;}
                if(perchAligned)backpackButton.Visibility=Visibility.Collapsed;
                parkedProp.Visibility=parkedMusicGuitar.Visibility=Visibility.Collapsed;
                if(!Object.ReferenceEquals(sprite.Source,scene))ShowPose(scene);
                if(focusCue.MinePreload>=0 && !app.FocusPaused)mineFrames.Get(focusCue.MinePreload);
                return true;
            }
            var poses=ActionFrames(character,costume);
            if(frame>=FocusCompanionCue.BridgeFirst)
            {
                if(entry==null)return false;
                poses=entry;
                frame-=FocusCompanionCue.BridgeFirst;
            }
            else if(perchAligned && currentArtId=="kurisu" && frame==5 && focusCue.ReachedEnd && testPhoneWave)
                poses=ActionFrames(character,costume+"-result")??poses;
            if(poses==null || frame>=poses.Length)return false;
            if(perchAligned)backpackButton.Visibility=Visibility.Collapsed;
            parkedProp.Visibility=parkedMusicGuitar.Visibility=Visibility.Collapsed;
            if(!Object.ReferenceEquals(sprite.Source,poses[frame]))ShowPose(poses[frame]);

            return true;
        }
    }
}
