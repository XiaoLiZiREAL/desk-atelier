using System;
using DeskMate;
static class KuronekoStoryChecks {
 static void Need(bool b,string why){if(!b)throw new InvalidOperationException(why);}
 static void Main() {
  var story=new KuronekoStory();
  story.Start(100);int first=story.Episode;
  Need(story.Beat==ManuscriptBeat.Approach && !story.Accept(100),"approach does not accept early");
  long offer=100+KuronekoStory.ApproachMs;
  story.Step(offer,true);Need(story.Beat==ManuscriptBeat.Offer,"intro reaches offer");
  Need(story.Accept(offer+300) && !story.Accept(offer+301) && story.Beat==ManuscriptBeat.Share,"single acceptance and no replay");
  long endShare=story.Until;
  story.Step(endShare,true);Need(story.Beat==ManuscriptBeat.Withdraw,"share resolves");
  story.Step(endShare+KuronekoStory.WithdrawMs,true);Need(story.Beat==ManuscriptBeat.None,"share returns idle");
  story.Start(9000);Need(story.Episode!=first,"a new invitation cannot reuse previous target");
  story.Step(story.Until,true);long expires=story.Until;
  Need(!story.Accept(expires),"expires exactly on time");
  Need(!story.Accept(expires-1),"a backwards clock cannot reopen an expired offer");
  story.Step(expires,true);Need(story.Beat==ManuscriptBeat.Withdraw,"ignored offer withdraws");
  Need(story.WithdrawDuration==KuronekoStory.WithdrawMs+KuronekoStory.LowerMs,"ignored upright manuscript lowers before closing");
  story.Step(expires+story.WithdrawDuration,true);Need(story.Beat==ManuscriptBeat.None,"ignored offer goes back to idle");
  story.Start(20000);story.Step(story.Until,true);story.Step(23000,false);
  Need(story.Beat==ManuscriptBeat.None && !story.Accept(23000),"occlusion/focus cancels an invitation");
  int shown=0,prev=-1;
  for(int t=0;t<KuronekoStory.ApproachMs;t+=17){
   int i=KuronekoStory.EntryCel(t);Need(i>=prev,"entry order");prev=i;shown|=1<<i;
  }
  Need(shown==255,"all settling and signed manuscript entry poses visible");
  shown=0;prev=-1;
  for(int t=0;t<KuronekoStory.WithdrawMs;t+=17){
   int i=KuronekoStory.ExitCel(t);Need(i>=prev,"exit order");prev=i;shown|=1<<(i+1);
  }
  Need(shown==127,"close pose then every forward-authored exit cel");
  Need(KuronekoStory.ExitCel(KuronekoStory.CloseBookMs-1)==-1 && KuronekoStory.ExitCel(KuronekoStory.CloseBookMs)==0,"no cut before book is closed");
  Need(KuronekoGestureTiming.Frame(KuronekoTarget.Cuff,839)==5 &&
       KuronekoGestureTiming.Frame(KuronekoTarget.Cuff,840)==6,"sleeve averts gaze before looking back");
  Need(KuronekoGestureTiming.Frame(KuronekoTarget.Cuff,1619)==11 &&
       KuronekoGestureTiming.Frame(KuronekoTarget.Cuff,1620)==-1,"sleeve settles before returning to idle");
  Need(KuronekoChoreography.IsHandPixel(181,200,225,255) &&
       !KuronekoChoreography.IsHandPixel(232,235,237,255) &&
       !KuronekoChoreography.IsHandPixel(28,31,37,255) &&
       !KuronekoChoreography.IsHandPixel(60,86,125,60),"visible fingers are not a white cuff, skirt, or transparent margin");
  foreach(KuronekoTarget target in new[]{KuronekoTarget.Cuff,KuronekoTarget.Hair,KuronekoTarget.Collar,
     KuronekoTarget.Hand,KuronekoTarget.LeftShoe,KuronekoTarget.RightShoe}){
   int observed=0;
   for(int t=0;t<2100;t+=25){
     int i=KuronekoGestureTiming.Frame(target,t);
     if(i>=0)observed|=1<<i;
   }
   int expected=target==KuronekoTarget.LeftShoe || target==KuronekoTarget.RightShoe?255:4095;
   Need(observed==expected,"all authored gesture frames are displayed: "+target);
  }
  Need(KuronekoStory.OfferCel(5000,-1)==1,"unnoticed invitation stays composed");
  Need(KuronekoStory.OfferCel(800,0)==2 && KuronekoStory.OfferCel(1300,500)==3 &&
       KuronekoStory.OfferCel(1800,1000)==4 && KuronekoStory.OfferCel(2500,1700)==5,
       "real manuscript hover produces a one-shot shy glance and recovery");
  Need(KuronekoStory.LiftCel(2199)==-1 && KuronekoStory.LiftCel(2200)==0 &&
       KuronekoStory.LiftCel(2919)==2 && KuronekoStory.LowerCel(0)==3 &&
       KuronekoStory.LowerCel(539)==5 && KuronekoStory.LowerCel(540)==-1,"all separately drawn lift and lower cels play in order");
  Need(KuronekoChoreography.SeatedNote(1000,true)==2 && KuronekoChoreography.SeatedNote(1609,true)==2 &&
       KuronekoChoreography.SeatedNote(2390,true)==-1,"seated note return does not act flustered without an approach");
  Need(KuronekoChoreography.SeatedNote(899,true,900)==2 && KuronekoChoreography.SeatedNote(900,true,900)==3 &&
       KuronekoChoreography.SeatedNote(1179,true,900)==3 && KuronekoChoreography.SeatedNote(1180,true,900)==4 &&
       KuronekoChoreography.SeatedNote(1960,true,900)==5,"cursor approach gives one pullback, recovery and quiet exit");
  Need(!KuronekoChoreography.CanNoticeNote(619) && !KuronekoChoreography.CanNoticeNote(1400) &&
       KuronekoChoreography.SeatedNote(1600,true,1400)==2,"missed offers do not get a delayed embarrassed gesture");
  Need(KuronekoChoreography.Listening(500,false)==0 && KuronekoChoreography.Listening(500,true)==1 &&
       KuronekoChoreography.Listening(3010,false)==0,"ordinary listening lingers; instrumental moves earlier and loops");
  Need(KuronekoChoreography.Singing(0,.2)==0 && KuronekoChoreography.Singing(420,.2)==2 &&
       KuronekoChoreography.Singing(420,.95)==5,"voiced cels never carry past the end of a sung line");
  var gaze=new KuronekoGaze();
  for(int i=0;i<16;i++) {
   double rad=i*Math.PI/8;
   Need(KuronekoGaze.Sector(60*Math.Sin(rad),-60*Math.Cos(rad))==i,"direction matches the authored clockwise row order");
  }
  Need(gaze.Look(0,0,-80,152,true)==-1 && gaze.Look(459,0,-80,152,true)==-1 &&
       gaze.Look(460,0,-80,152,true)==0,"glance waits instead of following immediately");
  Need(gaze.Look(500,80,0,152,true)==0 && gaze.Look(665,80,0,152,true)==4,"direction only changes after a steady 165ms");
  Need(gaze.Look(2101,80,0,152,true)==-1 && gaze.Look(2200,80,0,152,false)==-1 &&
       gaze.Look(2300,80,0,152,true)==-1,"glance ends and focus lock resets dwell");
  Console.WriteLine("Kuroneko manuscript state, forward exit and sleeve timing checks passed.");
 }
}
