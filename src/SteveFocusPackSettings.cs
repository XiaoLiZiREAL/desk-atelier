using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace DeskMate
{
    public partial class StudioWindow
    {
        bool changingStevePack;
        UIElement SteveFocusPackCard()
        {
            bool installed=app.SteveMining.Available;
            var install=Ui.Button(changingStevePack?"正在处理…":installed?"重新安装":"安装动画包",ChooseSteveFocusPack);
            install.IsEnabled=!changingStevePack;
            var row=new StackPanel{Orientation=Orientation.Horizontal};
            row.Children.Add(install);
            if(installed)
            {
                var remove=Ui.Button("移除",RemoveSteveFocusPack);
                remove.IsEnabled=!changingStevePack;
                remove.Margin=new Thickness(6,0,0,0);
                row.Children.Add(remove);
            }
            return Ui.Stack(Ui.Text(installed
                ?"已安装。选用史蒂夫并开始新的番茄钟，就能看到采矿动画。"
                :"未安装。不影响人物包或番茄钟。请选独立的 Steve-Focus ZIP，无需解压。",
                12,Ui.Muted),row);
        }

        void ChooseSteveFocusPack()
        {
            if(changingStevePack)return;
            var file=new Microsoft.Win32.OpenFileDialog{Title="安装史蒂夫专注动画包",Filter="专注动画 ZIP|*.zip",CheckFileExists=true,Multiselect=false};
            if(file.ShowDialog(this)==true){string path=file.FileName; ChangeSteveFocusPack(()=>app.SteveMining.Install(path),true);}
        }

        void RemoveSteveFocusPack()
        {
            if(changingStevePack)return;
            if(MessageBox.Show(this,"移除矿洞动画？史蒂夫人物、计时和记录都不会受影响。","史蒂夫专注动画",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)
                ChangeSteveFocusPack(app.SteveMining.Uninstall,false);
        }

        async void ChangeSteveFocusPack(Action change,bool install)
        {
            changingStevePack=true;RefreshPage();
            string error=null;
            try {await Task.Run(change);}
            catch(Exception ex){error=ex.GetBaseException().Message;}
            changingStevePack=false;
            if(app.Exiting)return;
            if(error==null && app.Pet!=null)app.Pet.RefreshSteveFocusPack();
            if(CurrentTool=="settings")RefreshPage();
            if(error!=null)
            {
                MessageBox.Show(this,error,"无法处理专注动画包",MessageBoxButton.OK,MessageBoxImage.Information);
                return;
            }
            ShowStatus(install?"已安装，下次开始专注时生效。":"已移除专注动画。");
        }
    }
}
