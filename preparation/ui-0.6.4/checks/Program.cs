using YikaiLocal;
using System.Reflection;
using System.ComponentModel;
class Program
{
    static IEnumerable<Control> Desc(Control root){foreach(Control c in root.Controls){yield return c;foreach(var n in Desc(c))yield return n;}}
    static T Named<T>(Control root,string name) where T:Control=>Desc(root).OfType<T>().Single(c=>c.Name==name);
    static void Check(bool ok,string text){if(!ok)throw new Exception(text);Console.WriteLine("PASS "+text);}
    [STAThread] static int Main(){
        try{
            ApplicationConfiguration.Initialize();Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            var root=Path.Combine(Path.GetTempPath(),"yikai-select-"+Guid.NewGuid().ToString("N"));var settings=new Settings{Root=root};settings.Save();
            Directory.CreateDirectory("evidence");using var form=new MainForm(settings,new Runtime(settings),true);form.Show();Application.DoEvents();
            foreach(var lang in new[]{0,1,2}){
                Named<Choice>(form,"language").SelectedIndex=lang;Application.DoEvents();bool done=false;using var timer=new System.Windows.Forms.Timer{Interval=150};
                timer.Tick+=(_,_)=>{
                    var dialog=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f.Owner==form);if(dialog==null)return;timer.Stop();
                    var grid=Desc(dialog).OfType<DataGridView>().Single();var all=Named<Button>(dialog,"importSelectAll");var clear=Named<Button>(dialog,"importClearSelection");var count=Named<Label>(dialog,"importSelectionCount");
                    Check(!all.Enabled&&!clear.Enabled,"Empty list disables selection actions");
                    var rows=new BindingList<ImportCandidate>(Enumerable.Range(1,3).Select(i=>new ImportCandidate{Domain="project"+i+".yikai",TargetDomain="project"+i+".yikai",Folder=@"D:\phpstudy_pro\WWW\project"+i,Database=new(){Driver="mysql",Name="project"+i}}).ToList());grid.DataSource=rows;Application.DoEvents();
                    all.PerformClick();Check(rows.All(c=>c.Selected)&&!all.Enabled&&clear.Enabled,"Select all updates bound projects");
                    clear.PerformClick();Check(rows.All(c=>!c.Selected)&&all.Enabled&&!clear.Enabled,"Clear selection updates bound projects");
                    grid.CurrentCell=grid.Rows[0].Cells[0];grid.BeginEdit(false);grid.CurrentCell.Value=true;grid.CommitEdit(DataGridViewDataErrorContexts.Commit);grid.EndEdit();Application.DoEvents();
                    Check(rows[0].Selected&&!rows[1].Selected&&count.Text.Contains("1")&&all.Enabled&&clear.Enabled,"Single checkbox updates partial-selection state");
                    var header=typeof(DataGridView).GetMethod("OnColumnHeaderMouseClick",BindingFlags.NonPublic|BindingFlags.Instance)!;
                    header.Invoke(grid,[new DataGridViewCellMouseEventArgs(0,-1,5,5,new MouseEventArgs(MouseButtons.Left,1,5,5,0))]);Check(rows.All(c=>c.Selected),"Header click selects remaining projects");
                    header.Invoke(grid,[new DataGridViewCellMouseEventArgs(0,-1,5,5,new MouseEventArgs(MouseButtons.Left,1,5,5,0))]);Check(rows.All(c=>!c.Selected),"Header click clears all projects");
                    all.PerformClick();grid.Enabled=false;Check(!all.Enabled&&!clear.Enabled,"Selection unavailable while grid is busy");grid.Enabled=true;Check(clear.Enabled,"Selection returns after operation");
                    foreach(var size in new[]{dialog.Size,dialog.MinimumSize}){
                        dialog.Size=size;Application.DoEvents();
                        foreach(var control in new Control[]{all,clear,count})Check(control.Right<=control.Parent!.ClientSize.Width&&control.Bottom<=control.Parent.ClientSize.Height,"Toolbar fits "+lang+" "+size.Width+" "+control.Name);
                    }
                    dialog.Size=new Size(1100,780);using var image=new Bitmap(dialog.Width,dialog.Height);dialog.DrawToBitmap(image,new Rectangle(Point.Empty,dialog.Size));image.Save(Path.Combine("evidence","selection-"+lang+".png"));dialog.Close();done=true;
                };
                timer.Start();typeof(MainForm).GetMethod("ShowPhpStudyImport",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(form,null);
                var until=DateTime.UtcNow.AddSeconds(10);while(!done&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}Check(done,"Dialog closes "+lang);
            }
            return 0;
        }catch(Exception e){Console.WriteLine(e);return 1;}
    }
}
