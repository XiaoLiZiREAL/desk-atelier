using System;
using DeskMate;

static class KuronekoClickGateChecks
{
    static void Need(bool ok,string name){if(!ok)throw new InvalidOperationException(name);Console.WriteLine("PASS "+name);}
    static void Main()
    {
        var manuscript=new KuronekoHit(KuronekoTarget.Manuscript,KuronekoPose.Prone,1);
        var next=new KuronekoHit(KuronekoTarget.Manuscript,KuronekoPose.Prone,2);
        var seated=new KuronekoHit(KuronekoTarget.Manuscript,KuronekoPose.Seated,1);
        var empty=default(KuronekoHit);
        var gate=new KuronekoClickGate();
        Action first=()=>{gate.Begin(manuscript);gate.End(manuscript,100,450,1000);};
        first();
        Need(gate.Tick(550,manuscript.Pose,manuscript.Opportunity,true)==KuronekoTarget.None,"first release never consumes recognized double click");
        gate.Cancel(); // native second-press hook opens tool via established route
        Need(gate.Tick(550,manuscript.Pose,manuscript.Opportunity,true)==KuronekoTarget.None,"double click executes only the existing shortcut");
        first();
        Need(gate.Tick(551,manuscript.Pose,manuscript.Opportunity,true)==KuronekoTarget.Manuscript && gate.Tick(552,manuscript.Pose,manuscript.Opportunity,true)==KuronekoTarget.None,"single click accepts exactly once");
        gate.Begin(manuscript);
        gate.Cancel();
        Need(!gate.End(manuscript,100,450,1000),"drag or lost capture cannot become a click");
        first();
        Need(gate.Tick(101,manuscript.Pose,manuscript.Opportunity,false)==KuronekoTarget.None && gate.Tick(550,manuscript.Pose,manuscript.Opportunity,true)==KuronekoTarget.None,"cover, hidden role or notice invalidates pending action");
        first();
        Need(gate.Tick(550,next.Pose,next.Opportunity,true)==KuronekoTarget.None,"new invitation cannot inherit first click");
        first();
        Need(gate.Tick(550,seated.Pose,seated.Opportunity,true)==KuronekoTarget.None,"change of drawn pose cancels old target");
        gate.Begin(manuscript);
        gate.End(manuscript,100,450,500);
        Need(gate.Tick(550,manuscript.Pose,manuscript.Opportunity,true)==KuronekoTarget.None,"expired invitation cannot accept");
        gate.Begin(manuscript);
        Need(!gate.End(empty,100,450,1000),"release on transparent pixels cannot accept");
        var cuff=new KuronekoHit(KuronekoTarget.Cuff,KuronekoPose.Standing,0);
        gate.Begin(cuff);
        Need(gate.End(cuff,100,450,long.MaxValue),"opaque sleeve press and release accepted");
        Need(gate.Tick(280,KuronekoPose.Standing,0,true)==KuronekoTarget.None && gate.HasPending,
            "standing idle can replace its cel while a validated click waits");
        Need(gate.Tick(551,KuronekoPose.Standing,0,true)==KuronekoTarget.Cuff,"idle cel replacement no longer loses the click");
        gate.Begin(empty);
        Need(!gate.End(empty,100,450,1000),"transparent margins do not belong to any region");
    }
}
