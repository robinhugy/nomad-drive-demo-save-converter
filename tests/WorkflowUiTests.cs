using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NomadSave;

internal static class WorkflowUiTests
{
    static readonly BindingFlags Fields=BindingFlags.Instance|BindingFlags.NonPublic;
    static readonly List<string> Checks=new List<string>();
    static void Check(bool value,string name){if(!value)throw new Exception(name);Checks.Add(name);}
    static T Field<T>(Form form,string name){return (T)form.GetType().GetField(name,Fields).GetValue(form);}
    static Button Find(Control parent,string name){foreach(Control c in parent.Controls){if(c is Button&&c.Name==name)return (Button)c;var found=Find(c,name);if(found!=null)return found;}return null;}
    static void Pump(Form form)
    {
        var time=System.Diagnostics.Stopwatch.StartNew();
        do{Application.DoEvents();Thread.Sleep(5);if(time.Elapsed.TotalSeconds>30)throw new Exception("UI operation timed out");}while(Field<bool>(form,"busy"));
        Application.DoEvents();SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
    }
    [STAThread]static int Main(string[] args)
    {
        try
        {
            string root=args[0],source=Path.Combine(root,"source"),output=Path.Combine(root,"output"),backup=Path.Combine(root,"backups");
            SaveOperations.TestGameRunning=false;
            foreach(var pair in new[]{new[]{"DefaultSaves",source},new[]{"DefaultEarlyAccessSaves",output},new[]{"DefaultBackups",backup}})
                typeof(SaveOperations).GetField(pair[0],BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,pair[1]);
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            using(var form=new ConverterWindow())
            {
                Field<TextBox>(form,"game").Text=Path.Combine(root,"game");form.ShowInTaskbar=false;form.Opacity=0;form.Show();Pump(form);
                var grid=Field<DataGridView>(form,"grid");var apply=Field<Button>(form,"apply");var preview=Field<Button>(form,"preview");var set=Find(form,"setAction");var details=Field<TextBox>(form,"details");
                Check(grid.Rows.Count==3,"Three fixture worlds listed");
                Check(!apply.Enabled&&grid.Rows.Cast<DataGridViewRow>().All(r=>!Convert.ToBoolean(r.Cells["Use"].Value)),"Startup selects nothing and cannot write");
                Check(apply.Text=="backup and create copies","Requested button text is exact");
                grid.CurrentCell=grid.Rows[0].Cells["World"];set.PerformClick();
                Check(details.Text.Contains("No worlds are checked")&&!Directory.Exists(output)&&!Directory.Exists(backup),"Highlighted row without checkbox does not set actions or create files");
                preview.PerformClick();Pump(form);Check(!apply.Enabled&&details.Text.Contains("Check at least one world"),"No-checkbox preview explains the missing selection");
                grid.Rows[0].Cells["Use"].Value=true;Field<ComboBox>(form,"action").SelectedIndex=1;set.PerformClick();
                Check(Convert.ToString(grid.Rows[0].Cells["Action"].Value)=="Enable multiplayer"&&Convert.ToString(grid.Rows[1].Cells["Action"].Value)=="Copy to Early Access","Set action updates only checked rows");
                Check(details.Text.Contains("Preview selected")&&!apply.Enabled,"Set action explains the next step without enabling writes");
                // An unreadable unchecked save must not participate in preview or copying.
                string ignored=Path.Combine(source,"slot_1","save.dat");File.WriteAllBytes(ignored,new byte[]{99,98,97});
                var originals=Directory.GetFiles(source,"*",SearchOption.AllDirectories).ToDictionary(p=>p,p=>SaveFormat.Sha(File.ReadAllBytes(p)));
                preview.PerformClick();Pump(form);
                
                Check(apply.Enabled&&details.Text.Contains("Previewed worlds: 1"),"Preview uses only checked worlds and ignores a corrupt unchecked world");
                Check(!Directory.Exists(output)&&!Directory.Exists(backup),"Successful preview writes neither saves nor backups");
                string report=details.Text;set.PerformClick();Check(apply.Enabled&&details.Text==report,"Repeating the same Set action preserves the successful preview");
                Field<ComboBox>(form,"action").SelectedIndex=2;Field<ComboBox>(form,"limit").SelectedItem="3";set.PerformClick();
                Check(!apply.Enabled&&Convert.ToString(grid.Rows[0].Cells["Limit"].Value)=="3","Changing a checked action invalidates the preview");
                preview.PerformClick();Pump(form);Check(apply.Enabled,"New successful preview enables copying again");
                using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(root,"workflow-ui.png"));}
                apply.PerformClick();Pump(form);
                Check(Directory.GetDirectories(output,"slot_*").Length==1,"Copy creates exactly one checked-world result");
                string created=Directory.GetDirectories(output,"slot_*")[0];var manifest=SaveFormat.Parse(SaveFormat.Decode(File.ReadAllBytes(Path.Combine(created,"save.dat"))).Entries["game.manifest"]);
                Check((manifest.Economy>>56)==3,"Created copy uses the selected player limit");
                Check(File.ReadAllText(Path.Combine(created,"meta.json")).Contains("(converted)"),"Created copy is named converted");
                Check(Directory.GetFiles(backup,"save.dat",SearchOption.AllDirectories).Length==1,"Backup contains only the checked source world");
                Check(originals.All(p=>File.Exists(p.Key)&&SaveFormat.Sha(File.ReadAllBytes(p.Key))==p.Value),"Every source file, including unchecked worlds, remains unchanged");
                Check(!apply.Enabled,"Completed copy requires a new preview before another write");
            }
            File.WriteAllText(Path.Combine(root,"ui-report.json"),Json.Write(Json.D("passed",Checks.Count,"checks",Checks)),SaveOperations.Utf8);
            Console.WriteLine("PASS: "+Checks.Count+" UI workflow and checked-only operation checks.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
