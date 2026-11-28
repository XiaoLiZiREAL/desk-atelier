namespace DeskMate
{
    internal static class ProjectPlantTimeline
    {
        // Stage 2 means the vine has been tied, not necessarily that fruit is present.
        // NextUtc is the first mature/harvest-ready time. Completing harvest returns
        // to stage 0, where the old vines may remain but their fruit is gone.
        internal static bool HasRipeCucumber(int stage,long readyUtc,long nowUtc)
        {
            return stage==2 && readyUtc>0 && nowUtc>=readyUtc;
        }

        internal static bool MayShowCucumberScene(int stage,long readyUtc,long nowUtc,long shownReadyUtc)
        {
            return HasRipeCucumber(stage,readyUtc,nowUtc) && shownReadyUtc!=readyUtc;
        }
    }
}
