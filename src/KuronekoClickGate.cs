namespace DeskMate
{
    internal enum KuronekoTarget { None, Hair, Collar, Cuff, Hand, Manuscript, LeftShoe, RightShoe, Hem }
    internal enum KuronekoPose { None, Standing, Seated, Prone }

    // A token identifies a visual opportunity, not a frame: an invitation can
    // animate while it waits, but a second invitation cannot inherit its click.
    internal struct KuronekoHit
    {
        internal readonly KuronekoTarget Target;
        internal readonly KuronekoPose Pose;
        internal readonly int Opportunity;
        internal KuronekoHit(KuronekoTarget target,KuronekoPose pose,int opportunity)
        { Target=target;Pose=pose;Opportunity=opportunity; }
        internal bool Matches(KuronekoHit hit)
        {return Target!=KuronekoTarget.None && Target==hit.Target && Pose==hit.Pose && Opportunity==hit.Opportunity;}
    }

    internal sealed class KuronekoClickGate
    {
        KuronekoHit pressed, pending;
        long due=-1, expires;
        internal bool HasPending { get { return due>=0; } }
        internal void Begin(KuronekoHit hit) { pressed=hit; }
        internal void DropPress() { pressed=default(KuronekoHit); }
        internal void Cancel() { DropPress();pending=default(KuronekoHit);due=-1; }
        internal bool End(KuronekoHit released,long now,int doubleClickMs,long validUntil)
        {
            bool same=pressed.Matches(released);
            DropPress();
            if(!same || now>=validUntil)return false;
            pending=released;
            // An OS double-click can still be recognized at the exact boundary.
            due=now+System.Math.Max(0,doubleClickMs)+1;
            expires=validUntil;
            return true;
        }
        internal KuronekoTarget Tick(long now,KuronekoPose pose,int opportunity,bool available)
        {
            if(due<0)return KuronekoTarget.None;
            // The painted pixel was validated at press and release. After
            // release an advancing idle cel is not a new user action.
            if(!available || now>=expires || pending.Pose!=pose || pending.Opportunity!=opportunity){Cancel();return KuronekoTarget.None;}
            if(now<due)return KuronekoTarget.None;
            var target=pending.Target;
            pending=default(KuronekoHit);due=-1;
            return target;
        }
    }
}
