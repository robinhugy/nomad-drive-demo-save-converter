using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

public sealed class ConverterWindow : Form
{
    readonly TextBox saves = new TextBox(), game = new TextBox(), newName = new TextBox();
    readonly CheckBox replace = new CheckBox(), cleanup = new CheckBox();
    readonly ComboBox players = new ComboBox();
    readonly DataGridView slots = new DataGridView();
    readonly TextBox details = new TextBox();
    readonly Label status = new Label();
    readonly ProgressBar progress = new ProgressBar();
    readonly Button convert = new Button(), refresh = new Button(), refreshList = new Button(), browseSaves = new Button(), browseGame = new Button();
    bool busy, filling, previewReady;
    Dictionary<string, object> current;

    public ConverterWindow()
    {
        Text = "Nomad Drive Save Converter " + BuildInfo.Version;
        using(var resource=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("NomadDriveSaveConverter.Icon"))
        {
            if(resource!=null)using(var icon=new Icon(resource,new Size(32,32)))Icon=(Icon)icon.Clone();
        }
        Font = new Font("Segoe UI", 10); Size = new Size(1040, 890); MinimumSize = new Size(880, 780);
        StartPosition = FormStartPosition.CenterScreen; AutoScaleMode = AutoScaleMode.Font;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 9 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 67));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        Controls.Add(layout);
        var header = new Panel { Dock = DockStyle.Fill };
        header.Controls.Add(new Label { Text = "Nomad Drive Save Converter", Font = new Font(Font.FontFamily, 18, FontStyle.Bold), AutoSize = true, Location = new Point(0,0) });
        header.Controls.Add(new Label { Text = "By robinhugy", Dock = DockStyle.Right, Width = 160, TextAlign = ContentAlignment.TopRight, Padding = new Padding(0,10,0,0) });
        header.Controls.Add(new Label { Text = "Convert legacy worlds or repair multiplayer in a copy. Originals are backed up.", AutoSize = true, Location = new Point(1,40) });
        layout.Controls.Add(header,0,0);
        var paths = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 95));
        paths.RowStyles.Add(new RowStyle(SizeType.Percent,50)); paths.RowStyles.Add(new RowStyle(SizeType.Percent,50));
        saves.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)+"Low", "Indie Devil", "Nomad Drive Demo", "EvilSave");
        game.Text = "";
        foreach(var box in new[]{saves,game}) { box.Dock=DockStyle.Fill; box.Margin=new Padding(3,6,3,3); }
        browseSaves.Text="Browse"; browseGame.Text="Browse";
        foreach(var b in new[]{browseSaves,browseGame}) b.Dock=DockStyle.Fill;
        paths.Controls.Add(new Label{Text="Save folder",AutoSize=true,Anchor=AnchorStyles.Left},0,0);
        paths.Controls.Add(new Label{Text="Game folder",AutoSize=true,Anchor=AnchorStyles.Left},0,1);
        paths.Controls.Add(saves,1,0); paths.Controls.Add(game,1,1); paths.Controls.Add(browseSaves,2,0); paths.Controls.Add(browseGame,2,1);
        browseSaves.Click += async (s,e)=>{ if(ChooseFolder(saves)) await AutomaticBatch(); };
        browseGame.Click += async (s,e)=>{ if(ChooseFolder(game)) await AutomaticBatch(); };
        layout.Controls.Add(paths,0,1);
        var tools=new Panel{Dock=DockStyle.Fill,Padding=new Padding(0,4,0,4)}; refresh.Text="Back up and convert all"; refresh.Width=230;refresh.Dock=DockStyle.Left;
        refresh.Click+=async(s,e)=>await AutomaticBatch(); tools.Controls.Add(refresh);
        refreshList.Text="Refresh list";refreshList.Width=120;refreshList.Dock=DockStyle.Left;
        refreshList.Click+=async(s,e)=>await RefreshSlots();tools.Controls.Add(refreshList);
        tools.Controls.Add(new Label{Text="Format 5 to 16 | Multiplayer repair",Width=340,Dock=DockStyle.Right,TextAlign=ContentAlignment.MiddleRight});
        layout.Controls.Add(tools,0,2);
        slots.Dock=DockStyle.Fill; slots.ReadOnly=true; slots.MultiSelect=false; slots.AllowUserToAddRows=false; slots.AllowUserToDeleteRows=false;
        slots.AllowUserToResizeRows=false; slots.RowHeadersVisible=false; slots.SelectionMode=DataGridViewSelectionMode.FullRowSelect;
        slots.BackgroundColor=SystemColors.Window; slots.AutoGenerateColumns=false; slots.RowTemplate.Height=29;
        slots.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="World",Name="Name",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});
        slots.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Last saved",Name="Date",Width=170});
        slots.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Format",Name="Format",Width=90});
        slots.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Objects",Name="Objects",Width=85});
        slots.SelectionChanged+=async(s,e)=>{if(!filling&&!busy) await Preview();};layout.Controls.Add(slots,0,3);
        slots.CellClick+=async(s,e)=>{if(!filling&&!busy&&e.RowIndex>=0)await Preview();};
        var options=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};
        options.RowStyles.Add(new RowStyle(SizeType.Percent,50));options.RowStyles.Add(new RowStyle(SizeType.Percent,50));
        replace.Text="Replace removed items with similar items where possible"; replace.Checked=true; replace.Dock=DockStyle.Fill;
        replace.CheckedChanged+=async(s,e)=>{if(!busy)await Preview();};options.Controls.Add(replace,0,0);
        cleanup.Text="Clean old world loot (also removes loose ground stashes; keeps vehicles and carried items)";
        cleanup.Dock=DockStyle.Fill;cleanup.Checked=false;
        cleanup.CheckedChanged+=async(s,e)=>{if(!busy)await Preview();};options.Controls.Add(cleanup,0,1);layout.Controls.Add(options,0,4);
        var naming=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1};naming.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,155));naming.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        naming.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));naming.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,190));
        naming.Controls.Add(new Label{Text="New world name",AutoSize=true,Anchor=AnchorStyles.Left},0,0);newName.Dock=DockStyle.Fill;newName.Margin=new Padding(3,6,3,3);naming.Controls.Add(newName,1,0);layout.Controls.Add(naming,0,5);
        naming.Controls.Add(new Label{Text="Player slots",AutoSize=true,Anchor=AnchorStyles.Left},2,0);
        players.DropDownStyle=ComboBoxStyle.DropDownList;players.Items.Add("Auto (multiplayer)");
        for(int count=2;count<=8;count++)players.Items.Add(count.ToString());players.SelectedIndex=0;
        players.Dock=DockStyle.Fill;players.Margin=new Padding(3,6,3,3);naming.Controls.Add(players,3,0);
        players.SelectedIndexChanged+=async(s,e)=>{if(!busy)await Preview();};
        details.Dock=DockStyle.Fill;details.Multiline=true;details.ReadOnly=true;details.ScrollBars=ScrollBars.Vertical;details.BackColor=SystemColors.Window;details.BorderStyle=BorderStyle.FixedSingle;details.Font=new Font("Segoe UI",10);layout.Controls.Add(details,0,6);
        var actions=new Panel{Dock=DockStyle.Fill,Padding=new Padding(0,8,0,8)};var open=new Button{Text="Open save folder",Width=215,Dock=DockStyle.Left};
        open.Click+=(s,e)=>{if(Directory.Exists(saves.Text))Process.Start(new ProcessStartInfo(saves.Text){UseShellExecute=true});};actions.Controls.Add(open);
        var licenses=new Button{Text="Licenses",Width=105,Dock=DockStyle.Left};
        licenses.Click+=(s,e)=>{
            using(var dialog=new Form{Text="Licenses and third-party notices",Size=new Size(850,650),StartPosition=FormStartPosition.CenterParent})
            using(var resource=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("NomadDriveSaveConverter.Notices"))
            using(var reader=new StreamReader(resource)){
                dialog.Controls.Add(new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,Font=new Font("Segoe UI",9),Text=WindowsLines(reader.ReadToEnd())});
                dialog.ShowDialog(this);
            }
        };actions.Controls.Add(licenses);
        convert.Text="Convert to new slot";convert.Width=245;convert.Dock=DockStyle.Right;convert.Enabled=false;
        convert.Click+=async(s,e)=>await ConvertSelected();actions.Controls.Add(convert);layout.Controls.Add(actions,0,7);
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2};footer.RowStyles.Add(new RowStyle(SizeType.Absolute,6));footer.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        progress.Dock=DockStyle.Fill;progress.Style=ProgressBarStyle.Continuous;footer.Controls.Add(progress,0,0);status.Dock=DockStyle.Fill;status.TextAlign=ContentAlignment.MiddleLeft;footer.Controls.Add(status,0,1);layout.Controls.Add(footer,0,8);
        Shown+=async(s,e)=>await AutomaticBatch();
        FormClosing+=(s,e)=>{if(busy){e.Cancel=true;status.Text="Please wait for the current operation to finish before closing.";}};
    }

    bool ChooseFolder(TextBox box)
    {
        using(var dialog=new FolderBrowserDialog{SelectedPath=Directory.Exists(box.Text)?box.Text:"",ShowNewFolderButton=false})
        { if(dialog.ShowDialog(this)!=DialogResult.OK)return false;box.Text=dialog.SelectedPath;return true; }
    }
    static string S(Dictionary<string,object> row,string key) {object value;return row.TryGetValue(key,out value)&&value!=null?Convert.ToString(value):"";}
    static string WindowsLines(string text) {return (text??"").Replace("\r\n","\n").Replace("\r","\n").Replace("\n",Environment.NewLine);}
    void ShowDetails(string text) {details.Text=WindowsLines(text);details.SelectionStart=0;details.ScrollToCaret();}
    void SetBusy(bool value,string message)
    {
        busy=value;foreach(Control control in new Control[]{saves,game,slots,replace,cleanup,players,newName,refresh,refreshList,browseSaves,browseGame})control.Enabled=!value;
        convert.Enabled=!value&&previewReady;progress.Style=value?ProgressBarStyle.Marquee:ProgressBarStyle.Continuous;
        if(message!=null)status.Text=message;
    }
    async Task<Dictionary<string,object>> RunEngine(params string[] args)
    {
        return await Task.Run(()=>NomadSave.ConverterEngine.Run(args));
    }
    async Task RefreshSlots(bool preview=true)
    {
        if(busy)return;previewReady=false;SetBusy(true,"Looking for saves...");
        try {
            var result=await RunEngine("--list","--output-root",saves.Text);filling=true;slots.Rows.Clear();current=null;
            if(string.IsNullOrWhiteSpace(game.Text)&&result.ContainsKey("locations"))game.Text=S((Dictionary<string,object>)result["locations"],"game");
            foreach(var value in (System.Collections.IEnumerable)result["slots"]){
                var row=(Dictionary<string,object>)value;
                double seconds=Convert.ToDouble(row["modified"]);string date=seconds>0?new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime().ToString("yyyy. MM. dd. HH:mm"):"Unreadable";
                int index=slots.Rows.Add(S(row,"name"),date,S(row,"format"),S(row,"objects"));slots.Rows[index].Tag=row;
            }
            filling=false;status.Text=slots.Rows.Count>0?"Select a world to convert or repair.":"No saves were found in this folder.";
            ShowDetails("Close the game before writing a converted copy.\nSelect a world to preview replacements and limitations.");
            foreach(DataGridViewRow row in slots.Rows){if(S((Dictionary<string,object>)row.Tag,"format")=="5"){slots.CurrentCell=row.Cells[0];break;}}
        } catch(Exception ex){ShowDetails(ex.Message);status.Text="The save list could not be read.";}
        finally{filling=false;SetBusy(false,null);}
        if(preview&&slots.Rows.Count>0)await Preview();
    }
    async Task AutomaticBatch()
    {
        if(busy)return;previewReady=false;SetBusy(true,"Finding saves, backing up and converting all eligible worlds...");
        Dictionary<string,object> result=null;
        try{
            var args=new List<string>{"--batch","--output-root",saves.Text};
            if(!string.IsNullOrWhiteSpace(game.Text)){args.Add("--game");args.Add(game.Text);}
            if(!replace.Checked)args.Add("--omit-missing");
            if(cleanup.Checked)args.Add("--clean-legacy-loot");
            if(players.SelectedIndex>0){args.Add("--players");args.Add(players.SelectedItem.ToString());}
            result=await RunEngine(args.ToArray());
            if(string.IsNullOrWhiteSpace(game.Text)&&result.ContainsKey("locations"))game.Text=S((Dictionary<string,object>)result["locations"],"game");
        }catch(Exception ex){ShowDetails(ex.Message);status.Text="Automatic conversion could not finish. Original saves are unchanged.";}
        finally{SetBusy(false,null);}
        if(result!=null){
            await RefreshSlots(false);ShowDetails(S(result,"reportText"));previewReady=false;convert.Enabled=false;
            status.Text="Automatic run complete. See the report for converted, repaired and unchanged worlds.";
        }
    }
    async Task Preview()
    {
        if(busy)return;previewReady=false;convert.Enabled=false;
        if(slots.CurrentRow==null)return;current=slots.CurrentRow.Tag as Dictionary<string,object>;
        if(current==null)return;
        newName.Text=System.Text.RegularExpressions.Regex.Replace(S(current,"name"),@"\s*\((?:converted|\u00e1talak\u00edtott)\)\s*$","",System.Text.RegularExpressions.RegexOptions.IgnoreCase)+" (converted)";
        if(current.ContainsKey("error")){ShowDetails(S(current,"error"));return;}
        string format=S(current,"format");
        if(format!="5"&&format!="16"){ShowDetails("This world format is not supported.");return;}
        convert.Text=cleanup.Checked?"Create cleaned copy":format=="16"?"Repair multiplayer in a copy":"Convert to new slot";
        SetBusy(true,"Checking objects, vehicles and paint...");
        try{
            var args=new List<string>{"--preview","--source",S(current,"path"),"--game",game.Text};if(!replace.Checked)args.Add("--omit-missing");
            if(cleanup.Checked)args.Add("--clean-legacy-loot");
            if(players.SelectedIndex>0){args.Add("--players");args.Add(players.SelectedItem.ToString());}
            var report=await RunEngine(args.ToArray());ShowDetails(S(report,"reportText"));previewReady=true;status.Text="Preview ready. The action button creates a separate save slot.";
        }catch(Exception ex){ShowDetails(ex.Message);status.Text="Preview could not be generated.";}
        finally{SetBusy(false,null);}
    }
    async Task ConvertSelected()
    {
        if(busy||!previewReady||current==null)return;
        SetBusy(true,"Backing up, converting and verifying...");
        try{
            var args=new List<string>{S(current,"format")=="16"?"--repair-multiplayer":"--convert","--source",S(current,"path"),"--output-root",saves.Text,"--game",game.Text,"--name",newName.Text};if(!replace.Checked)args.Add("--omit-missing");
            if(cleanup.Checked)args.Add("--clean-legacy-loot");
            if(players.SelectedIndex>0){args.Add("--players");args.Add(players.SelectedItem.ToString());}
            var report=await RunEngine(args.ToArray());ShowDetails(S(report,"reportText"));previewReady=false;
            status.Text="Created: "+S(report,"displayName")+". Select it from the game Load menu.";
        }catch(Exception ex){ShowDetails(ex.Message);status.Text="The operation did not complete. The original save is unchanged.";}
        finally{SetBusy(false,null);}
    }
    [STAThread] public static int Main(string[] args)
    {
        if(args.Length>0){
            return NomadSave.ConverterEngine.CommandLine(args);
        }
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new ConverterWindow());return 0;
    }
}
