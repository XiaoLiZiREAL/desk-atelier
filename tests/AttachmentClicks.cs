using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms=System.Windows.Forms;

internal static partial class DismissalChecks
{
    const string DragFile="attachment-qa-only.txt";
    const string DropTarget="drop-target.txt";

    static void InstallTestDragSource(Forms.Form form,string folder)
    {
        string fixture=Path.Combine(folder,DragFile),targetPath=Path.Combine(folder,DropTarget);
        IntPtr permittedTarget=IntPtr.Zero;
        form.MouseDown+=delegate(object sender,Forms.MouseEventArgs e) {
            if(e.Button!=Forms.MouseButtons.Left || e.X<145 || !File.Exists(targetPath))return;
            permittedTarget=new IntPtr(Int64.Parse(File.ReadAllText(targetPath)));
            var data=new Forms.DataObject();
            data.SetFileDropList(new StringCollection {fixture});
            form.DoDragDrop(data,Forms.DragDropEffects.Copy);
        };
        form.QueryContinueDrag+=delegate(object sender,Forms.QueryContinueDragEventArgs e) {
            if(e.Action!=Forms.DragAction.Drop)return;
            XY point;
            if(!GetCursorPos(out point)||WindowFromPoint(point)!=permittedTarget)e.Action=Forms.DragAction.Cancel;
        };
    }
    static void CheckAttachments(IntPtr outside,string folder)
    {
        string file=Path.Combine(folder,DragFile);
        File.WriteAllText(file,"test drag and drop");
        var pet=new WindowInteropHelper(app.Pet).Handle;
        File.WriteAllText(Path.Combine(folder,DropTarget),pet.ToInt64().ToString());
        var source=new XY{X=250,Y=110};
        var destination=ExposedPoint(app.Pet.LiveSprite);
        if(WindowFromPoint(source)!=outside)throw new InvalidOperationException("Fixture drag source obstructed");
        SetCursorPos(source.X,source.Y);Pump(60);
        if(WindowFromPoint(source)!=outside)throw new InvalidOperationException("Fixture moved before drag");
        mouse_event(2u,0,0,0,UIntPtr.Zero);
        try
        {
            Pump(120);
            last=new XY{X=(int)destination.X,Y=(int)destination.Y};
            if(WindowFromPoint(last)!=pet)throw new InvalidOperationException("Test drop target obstructed");
            SetCursorPos(last.X,last.Y);Pump(220);
            XY actual;
            if(!GetCursorPos(out actual)||WindowFromPoint(actual)!=pet)throw new InvalidOperationException("Pointer left the test pet");
        }
        finally {mouse_event(4u,0,0,0,UIntPtr.Zero);}
        Pump(260);
        Check(app.Data.Bag.Any(item=>item.Target==file),"real cross-process OLE file drop reaches the pet's bag");
        var bag=Find<FrameworkElement>(app.Pet,e=>AutomationProperties.GetAutomationId(e)=="PetBackpack");
        ClickExposed(bag);
        Check(app.Desk.IsVisible&&app.Desk.CurrentTool=="bag","click exposed backpack on pet opens collected files");
        var filename=Find<TextBlock>(app.Desk,t=>t.Text==DragFile);
        Check(filename.IsVisible,"backpack shows the dropped file name");
        ClickElement(Find<Button>(app.Desk,b=>Object.Equals(b.Content,"移出")));
        Check(!app.Desk.IsVisible&&app.Data.Bag.Count==0&&!bag.IsVisible&&File.Exists(file),"removing the last item hides the bag and preserves the original file");
    }
    static Point ExposedPoint(FrameworkElement target)
    {
        var hwnd=new WindowInteropHelper(app.Pet).Handle;
        // Test the application's input hit tree as well as the actual native window.
        // Character art may cover part of the backpack strap.
        foreach(double y in new[]{.5,.25,.75,.1,.9})
        foreach(double x in new[]{.5,.25,.75,.1,.9})
        {
            var point=target.PointToScreen(new Point(target.ActualWidth*x,target.ActualHeight*y));
            var physical=new XY{X=(int)point.X,Y=(int)point.Y};
            var hit=app.Pet.InputHitTest(app.Pet.PointFromScreen(point)) as Visual;
            if(hit!=null&&(hit==target||target.IsAncestorOf(hit))&&WindowFromPoint(physical)==hwnd)return point;
        }
        throw new InvalidOperationException("No exposed test point on "+AutomationProperties.GetAutomationId(target));
    }
    static void ClickExposed(FrameworkElement target)
    {
        Click(new WindowInteropHelper(app.Pet).Handle,ExposedPoint(target),false);
    }
}
