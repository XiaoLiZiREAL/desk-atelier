using System;

namespace DeskMate
{
    internal enum ManuscriptBeat { None, Approach, Offer, Share, Withdraw }

    internal sealed class KuronekoGaze
    {
        int shown=-1,candidate=-1;
        long since=-1,candidateSince=-1;
        internal void Reset(){shown=candidate=-1;since=candidateSince=-1;}
        internal static int Sector(double dx,double dy)
        {
            return ((int)Math.Round(Math.Atan2(dx,-dy)*8/Math.PI)+16)%16;
        }
        internal int Look(long now,double dx,double dy,double spriteWidth,bool available)
        {
            double squared=dx*dx+dy*dy, outer=Math.Max(100,spriteWidth*1.23);
            if(!available || squared<24*24 || squared>outer*outer){Reset();return -1;}
            if(since<0 || now<since){Reset();since=now;}
            int angle=Sector(dx,dy);
            if(angle!=candidate){candidate=angle;candidateSince=now;}
            if(now-since<460 || now-since>2100)return -1;
            if(shown<0 || now-candidateSince>=165)shown=candidate;
            return shown;
        }
    }

    internal static class KuronekoChoreography
    {
        static readonly int[] ReceiveNote={240,220,350,220,240,240};
        static readonly int[] Listen={660,265,400,160,285,390,310,540};
        static readonly int[] Instrumental={420,190,260,130,215,310,220,410};
        static readonly int[] Song={245,150,220,170,155,275};
        internal static int Singing(long elapsed,double lineProgress)
        {
            return lineProgress>=.94?5:At(Math.Max(0,elapsed),Song,true);
        }
        internal static bool IsHandPixel(int b,int g,int r,int alpha)
        {
            return alpha>=112 && r-b>=16 && g-b>=4 && r>=92;
        }
        internal static bool CanNoticeNote(long elapsed)
        {
            return elapsed>=620 && elapsed<1400;
        }
        internal static int SeatedNote(long elapsed,bool returning,long noticedAt=-1)
        {
            if(!returning)return At(elapsed,ReceiveNote,false);
            if(elapsed<0 || elapsed>=2390)return -1;
            if(elapsed<300)return 0;
            if(elapsed<620)return 1;
            if(elapsed>=1960)return 5;
            if(!CanNoticeNote(noticedAt) || elapsed<noticedAt)return 2;
            return elapsed-noticedAt<280?3:4;
        }
        internal static int Listening(long elapsed,bool instrumental)
        {
            return At(elapsed,instrumental?Instrumental:Listen,true);
        }
        static int At(long elapsed,int[] holds,bool loop)
        {
            if(elapsed<0)return -1;
            if(loop) {
                int duration=0;
                for(int i=0;i<holds.Length;i++)duration+=holds[i];
                elapsed%=duration;
            }
            for(int i=0;i<holds.Length;i++){if(elapsed<holds[i])return i;elapsed-=holds[i];}
            return -1;
        }
    }

    internal static class KuronekoGestureTiming
    {
        static readonly int[] Cuff={105,100,95,110,150,280,185,110,100,105,110,170};
        static readonly int[] Collar={110,95,105,125,165,135,105,260,110,100,105,175};
        static readonly int[] Hair={105,100,110,120,150,250,135,150,110,110,105,165};
        static readonly int[] Hand={105,115,105,125,150,195,125,160,110,105,110,175};
        static readonly int[] Shoes={125,135,155,185,270,175,140,220};
        static readonly int[] Default={130,190,230,300,205,170};
        internal static int Frame(KuronekoTarget target,long elapsed)
        {
            if(elapsed<0)return -1;
            int[] dwells=target==KuronekoTarget.Cuff?Cuff:target==KuronekoTarget.Collar?Collar:
                target==KuronekoTarget.Hair?Hair:target==KuronekoTarget.Hand?Hand:
                target==KuronekoTarget.LeftShoe || target==KuronekoTarget.RightShoe?Shoes:Default;
            for(int i=0;i<dwells.Length;i++){if(elapsed<dwells[i])return i;elapsed-=dwells[i];}
            return -1;
        }
    }

    // Independent of Windows, artwork and wall clock so pause/expiration and
    // interaction exclusion can be tested without opening a desktop.
    internal sealed class KuronekoStory
    {
        internal const int InvitationMs=5800, ApproachMs=2920, WithdrawMs=2340;
        internal const int LiftStartMs=2200, LiftCelMs=240, LowerCelMs=180, LowerMs=3*LowerCelMs, ShareMs=2800+LowerMs;
        internal const int EntryCelMs=235, ExitCelMs=300, CloseBookMs=540;
        long until,previous;
        int episode;
        internal int Episode { get { return episode; } }
        internal long Until { get { return until; } }
        internal ManuscriptBeat Beat { get; private set; }
        internal bool Shared { get; private set; }
        internal int WithdrawDuration { get { return WithdrawMs+(Shared?0:LowerMs); } }
        internal static int EntryCel(long elapsed)
        {
            return (int)Math.Min(7,Math.Max(0,elapsed)/EntryCelMs);
        }
        // -1 is the closing-book pose. The exit is drawn in forward order.
        internal static int ExitCel(long elapsed)
        {
            return elapsed<CloseBookMs?-1:(int)Math.Min(5,(elapsed-CloseBookMs)/ExitCelMs);
        }
        internal static int OfferCel(long elapsed,long noticedFor)
        {
            // If there's no pointer interest, stay composed. A real hover
            // changes the gaze once; no looping blush or twitch.
            if(noticedFor<0)return elapsed<610?0:1;
            if(noticedFor<230)return 2;
            if(noticedFor<730)return 3;
            return noticedFor<1220?4:5;
        }
        internal static int LiftCel(long elapsed)
        {
            return elapsed<LiftStartMs?-1:(int)Math.Min(2,(elapsed-LiftStartMs)/LiftCelMs);
        }
        internal static int LowerCel(long elapsed)
        {
            return elapsed<0 || elapsed>=LowerMs?-1:3+(int)(elapsed/LowerCelMs);
        }
        internal void Start(long now)
        {
            episode++; previous=now; Shared=false; Beat=ManuscriptBeat.Approach; until=now+ApproachMs;
        }
        internal void Cancel(){Beat=ManuscriptBeat.None;until=0;}
        internal void Step(long now,bool canStay)
        {
            if(!canStay || now<previous){Cancel();return;}
            previous=now;
            if(Beat==ManuscriptBeat.None || now<until)return;
            if(Beat==ManuscriptBeat.Approach){Beat=ManuscriptBeat.Offer;until=now+InvitationMs;}
            else if(Beat==ManuscriptBeat.Offer || Beat==ManuscriptBeat.Share){Beat=ManuscriptBeat.Withdraw;until=now+WithdrawDuration;}
            else Cancel();
        }
        internal bool Accept(long now)
        {
            if(Beat!=ManuscriptBeat.Offer)return false;
            if(now<previous || now>=until){Step(now,true);return false;}
            previous=now;
            Shared=true;Beat=ManuscriptBeat.Share;until=now+ShareMs;return true;
        }
    }
}
