using System;

namespace DeskMate
{
    public sealed class PersonalProject
    {
        // Stage is the next chapter to perform. Only a completed performance advances it.
        public int Stage;
        public long NextUtc;
    }

    public partial class SaveData
    {
        public PersonalProject MaidProject=new PersonalProject(), LabProject=new PersonalProject(),
            MinerProject=new PersonalProject(), GuitarProject=new PersonalProject(), CucumberProject=new PersonalProject();

        public long CucumberFruitSceneUtc;

        internal PersonalProject ProjectFor(string role)
        {
            switch(role)
            {
                case "rem": return MaidProject ?? (MaidProject=new PersonalProject());
                case "kurisu": return LabProject ?? (LabProject=new PersonalProject());
                case "steve": return MinerProject ?? (MinerProject=new PersonalProject());
                case "hitori": return GuitarProject ?? (GuitarProject=new PersonalProject());
                case "mutsumi": return CucumberProject ?? (CucumberProject=new PersonalProject());
                default: return null;
            }
        }
    }

    public sealed partial class PetWindow
    {
        static readonly string[] ProjectChapters={"projectstart","projectwork","projectfinish"};

        bool HasTrellisedCucumber()
        {
            var project=app.Data.ProjectFor("mutsumi");
            // Stage two follows tying the vine. After harvest, the mature vine remains
            // available until she actually completes the next seedling chapter.
            return project.Stage==2 || (project.Stage==0 && project.NextUtc>0);
        }

        bool TryStartProject()
        {
            var project=app.Data.ProjectFor(character);
            if(project==null)return false;
            if(project.Stage<0 || project.Stage>=ProjectChapters.Length)
                project.Stage=0;
            if(project.NextUtc<0 || project.NextUtc>DateTime.MaxValue.Ticks)
                project.NextUtc=0;
            if(project.NextUtc==0 && RoutineIndex!=3)return false;
            if(DateTime.UtcNow.Ticks<project.NextUtc)return false;
            if(!BeginPerformance(ProjectChapters[project.Stage],true))return false;
            ambientAt=NextAmbient();
            return true;
        }

        void CompleteProjectChapter()
        {
            if(!independentPerformance || Array.IndexOf(ProjectChapters,performance)<0)return;
            var project=app.Data.ProjectFor(character);
            if(project==null || project.Stage!=Array.IndexOf(ProjectChapters,performance))return;
            project.Stage=(project.Stage+1)%ProjectChapters.Length;
            if(project.Stage==0)
                project.NextUtc=DateTime.Today.AddDays(1).AddHours(9).AddMinutes(random.Next(61)).ToUniversalTime().Ticks;
            else if(character=="mutsumi")
                project.NextUtc=DateTime.UtcNow.AddMinutes(project.Stage==1?random.Next(150,211):random.Next(220,301)).Ticks;
            else
                project.NextUtc=DateTime.UtcNow.AddMinutes(project.Stage==1?random.Next(80,101):random.Next(115,151)).Ticks;
            app.Save();
        }
    }
}
