namespace YikaiLocal;

public sealed partial class MainForm
{
    ProjectSplitView projectSplit=null!;
    bool restoringSidebar,draggingSidebar;
    ProjectSplitView CreateProjectSplit()
    {
        var split=new ProjectSplitView{Name="projectSplit",Dock=DockStyle.Fill,SplitterWidth=8,BackColor=Palette.Divider,Margin=Padding.Empty,Size=new Size(1200,650),SplitterDistance=360,Panel1MinSize=260,Panel2MinSize=780};
        projectSplit=split;
        split.SplitterMoving+=(_,_)=>{if(!restoringSidebar&&!building)draggingSidebar=true;};
        split.SplitterMoved+=(_,_)=>{if(!draggingSidebar||restoringSidebar||building)return;draggingSidebar=false;settings.SidebarWidth=split.SplitterDistance;settings.Save();};
        split.SizeChanged+=(_,_)=>{if(!building)RestoreSidebarWidth();};
        split.SetHint(serviceTips,T("拖动分界调整项目栏宽度","Drag to resize the project sidebar","境界をドラッグして幅を変更"));
        return split;
    }
    void RestoreSidebarWidth()
    {
        if(projectSplit==null||projectSplit.IsDisposed||restoringSidebar)return;
        var maximum=projectSplit.ClientSize.Width-projectSplit.Panel2MinSize-projectSplit.SplitterWidth;
        if(maximum<projectSplit.Panel1MinSize)return;
        restoringSidebar=true;
        try{projectSplit.SplitterDistance=Math.Clamp(settings.SidebarWidth,projectSplit.Panel1MinSize,maximum);}
        finally{restoringSidebar=false;}
    }
}
