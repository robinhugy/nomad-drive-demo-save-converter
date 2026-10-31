using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using NomadSave;

public sealed class ConverterWindow : Form
{
    readonly TextBox source=new TextBox(), target=new TextBox(), game=new TextBox(), details=new TextBox();
    readonly ComboBox profile=new ComboBox(), action=new ComboBox(), limit=new ComboBox();
    readonly CheckBox replace=new CheckBox(), cleanup=new CheckBox();
    readonly DataGridView grid=new DataGridView(); readonly Label status=new Label();
    readonly Button preview=new Button(), apply=new Button(); readonly ProgressBar progress=new ProgressBar();
    readonly List<Control> inputs=new List<Control>(); bool busy, filling; Dictionary<string,object> previewPlan;
    static readonly string[] Actions={"Copy to Early Access","Enable multiplayer","Set player limit"};
    public ConverterWindow()
    {
        Text="Nomad Drive Save Converter "+BuildInfo.Version; Font=new Font("Segoe UI",10); Size=new Size(1220,930); MinimumSize=new Size(1000,820); StartPosition=FormStartPosition.CenterScreen; AutoScaleMode=AutoScaleMode.Font;
        using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("NomadDriveSaveConverter.Icon"))
        {if(stream!=null)using(var icon=new Icon(stream,new Size(32,32)))Icon=(Icon)icon.Clone();}
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=9}; Controls.Add(layout);
        foreach(float height in new float[]{68,142,40,250,45,64})layout.RowStyles.Add(new RowStyle(SizeType.Absolute,height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,46)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,36));
        var header=new Panel{Dock=DockStyle.Fill}; header.Controls.Add(new Label{Text="Nomad Drive Save Converter",AutoSize=true,Font=new Font(Font.FontFamily,18,FontStyle.Bold)});
        header.Controls.Add(new Label{Text="By robinhugy",Dock=DockStyle.Right,Width=160,TextAlign=ContentAlignment.TopRight});
        header.Controls.Add(new Label{Text="Choose worlds and an action for each. Every result is a new (converted) save; originals are backed up.",AutoSize=true,Location=new Point(0,40)});layout.Controls.Add(header,0,0);
        var paths=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=3}; paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150)); paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,175)); paths.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); paths.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,95));
        profile.DropDownStyle=ComboBoxStyle.DropDownList;profile.Items.AddRange(new object[]{"Demo + Early Access","Demo","Early Access","Custom folder"});profile.SelectedIndex=0;
        target.Text=SaveOperations.DefaultEarlyAccessSaves;game.Text=SaveOperations.DiscoverGame(true)??"";source.Text="Both standard save folders";source.ReadOnly=true;
        paths.Controls.Add(Label("Source saves"),0,0);paths.Controls.Add(profile,1,0);paths.Controls.Add(source,2,0);
        paths.Controls.Add(Label("Destination saves"),0,1);paths.Controls.Add(target,1,1);paths.SetColumnSpan(target,2);
        paths.Controls.Add(Label("Early Access game"),0,2);paths.Controls.Add(game,1,2);paths.SetColumnSpan(game,2);
        foreach(var box in new Control[]{profile,source,target,game}){box.Dock=DockStyle.Fill;box.Margin=new Padding(3,8,3,3);inputs.Add(box);}
        var browseSource=Button("Browse",()=>{string before=source.Text;if(Choose(source)){string selected=source.Text;profile.SelectedIndex=3;source.Text=selected;source.ReadOnly=false;}else source.Text=before;});paths.Controls.Add(browseSource,3,0);
        paths.Controls.Add(Button("Browse",()=>Choose(target)),3,1);paths.Controls.Add(Button("Browse",()=>Choose(game)),3,2);layout.Controls.Add(paths,0,1);
        profile.SelectedIndexChanged+=(s,e)=>{source.ReadOnly=profile.SelectedIndex!=3;source.Text=profile.SelectedIndex==0?"Both standard save folders":profile.SelectedIndex==1?SaveOperations.DefaultSaves:profile.SelectedIndex==2?SaveOperations.DefaultEarlyAccessSaves:"";InvalidatePreview();};
        foreach(var box in new[]{source,target,game})box.TextChanged+=(s,e)=>InvalidatePreview();
        source.TextChanged+=(s,e)=>{if(!busy){grid.Rows.Clear();status.Text="Source changed. Click Find / refresh saves.";}};
        var listing=new FlowLayoutPanel{Dock=DockStyle.Fill}; var refresh=Button("Find / refresh saves",async()=>await RefreshWorlds());refresh.Width=175;listing.Controls.Add(refresh);
        listing.Controls.Add(Button("Select all",()=>{foreach(DataGridViewRow row in grid.Rows)if(!((Dictionary<string,object>)row.Tag).ContainsKey("error"))row.Cells["Use"].Value=true;InvalidatePreview();}));
        listing.Controls.Add(Button("Clear selection",()=>{foreach(DataGridViewRow row in grid.Rows)row.Cells["Use"].Value=false;InvalidatePreview();}));
        listing.Controls.Add(new Label{Text="Nothing is converted automatically.",AutoSize=true,Padding=new Padding(15,7,0,0)});layout.Controls.Add(listing,0,2);
        grid.Dock=DockStyle.Fill;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.AllowUserToResizeRows=false;grid.RowHeadersVisible=false;grid.BackgroundColor=SystemColors.Window;grid.AutoGenerateColumns=false;grid.RowTemplate.Height=30;grid.EditMode=DataGridViewEditMode.EditOnEnter;
        grid.Columns.Add(new DataGridViewCheckBoxColumn{Name="Use",HeaderText="Select",Width=60});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="World",HeaderText="World",ReadOnly=true,AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Profile",HeaderText="Source",ReadOnly=true,Width=100});
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Current",HeaderText="Current limit",ReadOnly=true,Width=100});
        var actionColumn=new DataGridViewComboBoxColumn{Name="Action",HeaderText="Action",Width=205,FlatStyle=FlatStyle.Flat};actionColumn.Items.AddRange(Actions);grid.Columns.Add(actionColumn);
        var limitColumn=new DataGridViewComboBoxColumn{Name="Limit",HeaderText="New limit",Width=90,FlatStyle=FlatStyle.Flat};for(int i=1;i<=8;i++)limitColumn.Items.Add(i.ToString());grid.Columns.Add(limitColumn);
        grid.Columns.Add(new DataGridViewTextBoxColumn{Name="Saved",HeaderText="Last saved",ReadOnly=true,Width=145});
        grid.CurrentCellDirtyStateChanged+=(s,e)=>{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};grid.CellValueChanged+=(s,e)=>{if(!filling){if(e.RowIndex>=0&&e.ColumnIndex==grid.Columns["Action"].Index&&grid.Rows[e.RowIndex].Tag!=null)UpdateLimit(grid.Rows[e.RowIndex]);InvalidatePreview();}};grid.DataError+=(s,e)=>{e.ThrowException=false;};inputs.Add(grid);layout.Controls.Add(grid,0,3);
        var bulk=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,5,0,0)};bulk.Controls.Add(new Label{Text="For checked worlds:",AutoSize=true,Padding=new Padding(0,6,5,0)});
        action.DropDownStyle=ComboBoxStyle.DropDownList;action.Items.AddRange(Actions);action.SelectedIndex=0;action.Width=205;bulk.Controls.Add(action);inputs.Add(action);
        limit.DropDownStyle=ComboBoxStyle.DropDownList;for(int i=1;i<=8;i++)limit.Items.Add(i.ToString());limit.SelectedItem="4";limit.Width=65;bulk.Controls.Add(limit);inputs.Add(limit);
        var set=Button("Set action",()=>{grid.EndEdit();foreach(DataGridViewRow row in grid.Rows)if(Convert.ToBoolean(row.Cells["Use"].Value??false)){row.Cells["Action"].Value=action.SelectedItem.ToString();if(action.SelectedIndex==2)row.Cells["Limit"].Value=limit.SelectedItem.ToString();else UpdateLimit(row);}InvalidatePreview();});set.Width=120;bulk.Controls.Add(set);layout.Controls.Add(bulk,0,4);
        var options=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false};
        replace.Text="Replace removed legacy items with similar items where possible";replace.Checked=true;replace.AutoSize=true;
        cleanup.Text="Clean old imported world loot (also removes loose ground stashes; requires the original legacy save)";cleanup.AutoSize=true;
        foreach(var box in new[]{replace,cleanup}){options.Controls.Add(box);inputs.Add(box);box.CheckedChanged+=(s,e)=>InvalidatePreview();}layout.Controls.Add(options,0,5);
        details.Multiline=true;details.ReadOnly=true;details.ScrollBars=ScrollBars.Vertical;details.Dock=DockStyle.Fill;details.BackColor=SystemColors.Window;layout.Controls.Add(details,0,6);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(0,6,0,0)};
        preview.Text="Preview selected";preview.Width=170;preview.Height=32;preview.Click+=async(s,e)=>await Run(false);actions.Controls.Add(preview);inputs.Add(preview);
        apply.Text="Back up and create copies";apply.Width=245;apply.Height=32;apply.Enabled=false;apply.Click+=async(s,e)=>await Run(true);actions.Controls.Add(apply);
        var open=Button("Open destination",()=>{if(Directory.Exists(target.Text))Process.Start(new ProcessStartInfo(target.Text){UseShellExecute=true});});open.Width=170;actions.Controls.Add(open);
        actions.Controls.Add(Button("Licenses",()=>{using(var dialog=new Form{Text="Licenses",Size=new Size(800,600),StartPosition=FormStartPosition.CenterParent})using(var stream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("NomadDriveSaveConverter.Notices"))using(var reader=new StreamReader(stream)){dialog.Controls.Add(new TextBox{Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,Text=Lines(reader.ReadToEnd())});dialog.ShowDialog(this);}}));layout.Controls.Add(actions,0,7);
        var footer=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));progress.Dock=DockStyle.Fill;status.Dock=DockStyle.Fill;status.TextAlign=ContentAlignment.MiddleLeft;footer.Controls.Add(progress,0,0);footer.Controls.Add(status,1,0);layout.Controls.Add(footer,0,8);
        Shown+=async(s,e)=>await RefreshWorlds();FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};
    }
    Label Label(string text){return new Label{Text=text,AutoSize=true,Anchor=AnchorStyles.Left};}
    Button Button(string text,Action run){var b=new Button{Text=text,AutoSize=true,Height=31};b.Click+=(s,e)=>run();inputs.Add(b);return b;}
    bool Choose(TextBox box){using(var d=new FolderBrowserDialog{SelectedPath=Directory.Exists(box.Text)?box.Text:"",ShowNewFolderButton=false}){if(d.ShowDialog(this)!=DialogResult.OK)return false;box.Text=d.SelectedPath;return true;}}
    static string Lines(string s){return(s??"").Replace("\r\n","\n").Replace("\r","\n").Replace("\n",Environment.NewLine);}
    void InvalidatePreview(){previewPlan=null;apply.Enabled=false;status.Text="Choose worlds, set actions, then preview.";}
    void UpdateLimit(DataGridViewRow row)
    {
        bool enabled=Convert.ToString(row.Cells["Action"].Value)==Actions[2];var cell=(DataGridViewComboBoxCell)row.Cells["Limit"];
        cell.ReadOnly=!enabled;cell.DisplayStyle=enabled?DataGridViewComboBoxDisplayStyle.DropDownButton:DataGridViewComboBoxDisplayStyle.Nothing;cell.Style.BackColor=enabled?SystemColors.Window:SystemColors.Control;
        if(!enabled){var info=row.Tag as Dictionary<string,object>;if(info!=null&&!info.ContainsKey("error"))cell.Value=(Convert.ToString(row.Cells["Action"].Value)==Actions[1]?Math.Max(2,Convert.ToInt32(info["players"])):Convert.ToInt32(info["players"])).ToString();}
    }
    void Busy(bool value,string message){busy=value;foreach(var c in inputs)c.Enabled=!value;apply.Enabled=!value&&previewPlan!=null;progress.Style=value?ProgressBarStyle.Marquee:ProgressBarStyle.Continuous;if(message!=null)status.Text=message;}
    async Task RefreshWorlds()
    {
        if(busy)return;InvalidatePreview();var roots=profile.SelectedIndex==0?new[]{SaveOperations.DefaultSaves,SaveOperations.DefaultEarlyAccessSaves}:new[]{source.Text};Busy(true,"Reading saves...");
        try{var rows=await Task.Run(()=>SelectedOperations.ListWorlds(roots));filling=true;grid.Rows.Clear();foreach(var r in rows){double seconds=Convert.ToDouble(r["modified"]);string date=seconds>0?new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(seconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm"):"Unreadable";int n=grid.Rows.Add(false,Json.S(r,"name"),Json.S(r,"profile"),Json.S(r,"players"),Actions[0],"4",date);grid.Rows[n].Tag=r;UpdateLimit(grid.Rows[n]);if(r.ContainsKey("error")){grid.Rows[n].ReadOnly=true;grid.Rows[n].DefaultCellStyle.ForeColor=Color.DarkRed;grid.Rows[n].Cells["World"].ToolTipText=Json.S(r,"error");}}details.Text="Copy to Early Access keeps the current single-player / multiplayer setting.\r\nEnable multiplayer uses at least 2 slots and keeps a higher existing limit.\r\nSet player limit uses the New limit column: 1 = single-player, 2–8 = multiplayer.\r\nLowering a limit retains every saved player profile and inventory.\r\nOld world-loot cleanup is optional; it can remove loose ground stashes.\r\nClose the game before creating copies.";status.Text=rows.Count+" world(s) found. Select only the ones you want to change.";}
        catch(Exception ex){details.Text=ex.Message;status.Text="Could not read the save list.";}finally{filling=false;Busy(false,null);}
    }
    Dictionary<string,object> MakePlan()
    {
        grid.EndEdit();var selections=new List<object>();foreach(DataGridViewRow row in grid.Rows){if(!Convert.ToBoolean(row.Cells["Use"].Value??false))continue;var info=(Dictionary<string,object>)row.Tag;if(info.ContainsKey("error"))throw new ConversionError(Json.S(info,"error"));string a=Convert.ToString(row.Cells["Action"].Value);selections.Add(Json.D("source",Json.S(info,"slot"),"expectedSha256",Json.S(info,"sha256"),"action",a==Actions[0]?"migrate":a==Actions[1]?"multiplayer":"players","players",a==Actions[2]?(object)int.Parse(Convert.ToString(row.Cells["Limit"].Value)):null));}
        return Json.D("game",game.Text,"outputRoot",target.Text,"backupRoot",SaveOperations.DefaultBackups,"replaceMissing",replace.Checked,"cleanLegacyLoot",cleanup.Checked,"selections",selections);
    }
    async Task Run(bool write)
    {
        if(busy)return;Dictionary<string,object> plan;
        try{plan=write?previewPlan:MakePlan();if(plan==null)return;}catch(Exception ex){details.Text=ex.Message;return;}
        Busy(true,write?"Backing up, creating copies and verifying...":"Checking selected worlds...");
        try{var result=await Task.Run(()=>SelectedOperations.Execute(plan,write));details.Text=Lines(Json.S(result,"reportText"));details.SelectionStart=0;details.ScrollToCaret();previewPlan=!write&&!Json.Items(result["errors"]).Any()?plan:null;status.Text=write?"Created "+result["created"]+" new world(s). Originals are unchanged; see the report for any errors.":"Preview ready. Check the report, then create the selected copies.";}
        catch(Exception ex){previewPlan=null;details.Text=ex.Message;status.Text="The operation did not complete. Original saves are unchanged.";}finally{Busy(false,null);}
    }
    [STAThread]public static int Main(string[] args){if(args.Length>0)return ConverterEngine.CommandLine(args);Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new ConverterWindow());return 0;}
}
