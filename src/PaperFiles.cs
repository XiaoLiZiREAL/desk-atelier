using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace DeskMate
{
    public partial class StudioWindow
    {
        Grid writing,attachmentBar;
        Button paperFile,nextPaperFile;
        TextBlock fileName;
        int shownAttachment;

        void InstallPaperFiles()
        {
            stickySurface.AllowDrop=true;
            stickySurface.PreviewDragEnter+=FileDragOver;
            stickySurface.PreviewDragOver+=FileDragOver;
            stickySurface.PreviewDrop+=delegate(object sender,DragEventArgs e){
                if(!IsPaperFileDrop(e.Data))return;
                e.Handled=true;
                var effect=FileReferenceEffect(e.AllowedEffects);
                e.Effects=DragDropEffects.None;
                if(effect==DragDropEffects.None)return;
                try{
                    var paths=e.Data.GetData(DataFormats.FileDrop,false) as string[];
                    if(AttachPaperFiles(paths))e.Effects=effect;
                }catch(ExternalException){PaperFeedback("无法读取拖入的文件");}
            };
            attachmentBar=new Grid{Height=27,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(18,0,13,37)};
            attachmentBar.ColumnDefinitions.Add(new ColumnDefinition());
            attachmentBar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            attachmentBar.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
            paperFile=PaperAction("OpenPaperFile","打开附件",delegate{
                string path=SelectedPaperFile();
                if(path!=null){string issue=OpenReferencedFile(path);if(issue!=null)PaperFeedback(issue);}
            });
            var title=new DockPanel{LastChildFill=true};var clip=Ui.Icon("\uE723",13,Ui.Muted);
            clip.Margin=new Thickness(3,0,5,0);DockPanel.SetDock(clip,Dock.Left);title.Children.Add(clip);
            fileName=Ui.Text("",12,Ui.Ink);fileName.TextTrimming=TextTrimming.CharacterEllipsis;fileName.TextWrapping=TextWrapping.NoWrap;
            title.Children.Add(fileName);paperFile.Content=title;paperFile.HorizontalContentAlignment=HorizontalAlignment.Stretch;
            attachmentBar.Children.Add(paperFile);
            nextPaperFile=PaperAction("NextPaperFile","下一个附件",delegate{shownAttachment++;RefreshPaperFiles(stickyNote);});
            nextPaperFile.FontSize=11;nextPaperFile.Padding=new Thickness(4,0,4,0);
            Grid.SetColumn(nextPaperFile,1);attachmentBar.Children.Add(nextPaperFile);
            var remove=PaperAction("DetachPaperFile","从便签移出这个附件",RemovePaperFile);
            remove.Width=26;remove.Content=Ui.Icon("\uE711",10,Ui.Muted);
            Grid.SetColumn(remove,2);attachmentBar.Children.Add(remove);
            stickySurface.Children.Add(attachmentBar);
        }
        void FileDragOver(object sender,DragEventArgs e)
        {
            if(!IsPaperFileDrop(e.Data))return;
            e.Effects=FileReferenceEffect(e.AllowedEffects);e.Handled=true;
        }
        static bool IsPaperFileDrop(IDataObject data)
        {
            try{return data.GetDataPresent(DataFormats.FileDrop,false);}catch(ExternalException){return false;}
        }
        static DragDropEffects FileReferenceEffect(DragDropEffects allowed)
        {
            return (allowed&DragDropEffects.Link)!=0?DragDropEffects.Link:allowed&DragDropEffects.Copy;
        }
        internal bool AttachPaperFiles(IEnumerable<string> paths)
        {
            if(paths==null || paperTimePicker!=null)return false;
            var notes=app.Data.Notes;
            if(!notes.Contains(stickyNote) && notes.Count>=StickyCapacity){PaperFeedback("便签已满");return false;}
            var valid=new List<string>();
            var known=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if(stickyNote!=null)known.UnionWith(stickyNote.AttachmentPaths);
            int skipped=0;
            foreach(string path in paths){
                if(String.IsNullOrWhiteSpace(path) || !File.Exists(path) || !System.IO.Path.IsPathRooted(path)){skipped++;continue;}
                string full=System.IO.Path.GetFullPath(path);
                if(known.Add(full))valid.Add(full);
            }
            if(valid.Count==0){PaperFeedback(skipped>0?"请拖入可访问的文件":"这张纸已经带着这些文件");return false;}
            if(stickyNote==null || !notes.Contains(stickyNote)){
                stickyNote=new NoteItem{Text=stickyEditor.Text};
                notes.Insert(Math.Min(stickySlot,notes.Count),stickyNote);
            }
            shownAttachment=stickyNote.AttachmentPaths.Count;
            stickyNote.AttachmentPaths.AddRange(valid);
            stickySave.Stop();app.Save();UpdateStickyDetails();app.RefreshReturnedPaper();
            if(skipped>0)PaperFeedback("已加入文件，部分位置无法读取");
            return true;
        }
        internal void PaperFeedback(string message){stickyStatus.Text=message;stickyStatus.ToolTip=message;}
        string SelectedPaperFile()
        {
            return stickyNote==null || shownAttachment<0 || shownAttachment>=stickyNote.AttachmentPaths.Count?
                null:stickyNote.AttachmentPaths[shownAttachment];
        }
        void RefreshPaperFiles(NoteItem note)
        {
            int count=note==null?0:note.AttachmentPaths.Count;
            writing.Margin=new Thickness(20,45,19,count>0?66:38);
            attachmentBar.Visibility=count>0?Visibility.Visible:Visibility.Collapsed;
            if(count==0){shownAttachment=0;return;}
            shownAttachment%=count;
            string path=note.AttachmentPaths[shownAttachment];fileName.Text=System.IO.Path.GetFileName(path);
            paperFile.ToolTip=path;AutomationProperties.SetName(paperFile,"打开附件 · "+fileName.Text);
            nextPaperFile.Visibility=count>1?Visibility.Visible:Visibility.Collapsed;
            nextPaperFile.Content=(shownAttachment+1)+"/"+count+" ›";
        }
        void RemovePaperFile()
        {
            if(SelectedPaperFile()==null)return;
            stickyNote.AttachmentPaths.RemoveAt(shownAttachment);
            if(!stickyNote.HasContent){
                int index=app.Data.Notes.IndexOf(stickyNote);
                app.Data.Notes.Remove(stickyNote);SelectSticky(index,false);
            }else UpdateStickyDetails();
            stickySave.Stop();app.Save();app.RefreshReturnedPaper();
        }
        internal static string OpenReferencedFile(string path)
        {
            if(String.IsNullOrWhiteSpace(path) || !File.Exists(path) || !System.IO.Path.IsPathRooted(path))
                return "找不到原文件。可移出附件并重新拖入。";
            return OpenWindowsItem(path);
        }
    }
}
