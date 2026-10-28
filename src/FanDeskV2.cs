// iMac Fan Desk 0.6 - GPL-2.0-only. SMC module: RPMac v1.10.1.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;
using RPMac;

class MiniOverlay : Form {
    readonly Label tempLabel=new Label(), rpmLabel=new Label();
    bool dragging; Point dragStart;
    public Action<Point> PositionChanged;
    public MiniOverlay() {
        FormBorderStyle=FormBorderStyle.None;
        ShowInTaskbar=false;
        TopMost=true;
        StartPosition=FormStartPosition.Manual;
        Size=new Size(184,42);
        BackColor=Color.FromArgb(18,26,38);
        ForeColor=FanDeskV2.Ink;
        Opacity=0.90;
        DoubleBuffered=true;
        var row=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(6,4,6,4),BackColor=Color.Transparent};
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,46));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,54));
        tempLabel.Text="— °C";
        tempLabel.Font=new Font("Segoe UI",13,FontStyle.Bold);
        tempLabel.ForeColor=Color.FromArgb(244,143,145);
        tempLabel.Dock=DockStyle.Fill;
        tempLabel.TextAlign=ContentAlignment.MiddleCenter;
        rpmLabel.Text="— RPM";
        rpmLabel.Font=new Font("Segoe UI",13,FontStyle.Bold);
        rpmLabel.ForeColor=Color.FromArgb(72,186,255);
        rpmLabel.Dock=DockStyle.Fill;
        rpmLabel.TextAlign=ContentAlignment.MiddleCenter;
        row.Controls.Add(tempLabel,0,0);
        row.Controls.Add(rpmLabel,1,0);
        Controls.Add(row);
        foreach(Control c in new Control[]{this,row,tempLabel,rpmLabel}) {
            c.MouseDown+=OnDown;
            c.MouseMove+=OnMove;
            c.MouseUp+=OnUp;
            c.Cursor=Cursors.SizeAll;
        }
        Paint+=delegate(object s,PaintEventArgs e) {
            using(var pen=new Pen(Color.FromArgb(56,78,108),1))
                e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);
        };
    }
    protected override bool ShowWithoutActivation { get { return true; } }
    void OnDown(object s,MouseEventArgs e) { if(e.Button==MouseButtons.Left){dragging=true;dragStart=e.Location;} }
    void OnMove(object s,MouseEventArgs e) {
        if(!dragging)return;
        Location=new Point(Location.X+e.X-dragStart.X,Location.Y+e.Y-dragStart.Y);
    }
    void OnUp(object s,MouseEventArgs e) {
        if(!dragging)return;
        dragging=false;
        if(PositionChanged!=null)PositionChanged(Location);
    }
    public void UpdateValues(double temp,double rpm) {
        tempLabel.Text=DeskPolicy.TemperatureValid(temp)?temp.ToString("0")+"°C":"—";
        tempLabel.ForeColor=DeskPolicy.TemperatureValid(temp)&&temp>=80?Color.FromArgb(255,120,105):Color.FromArgb(244,143,145);
        rpmLabel.Text=DeskPolicy.Valid(rpm)?rpm.ToString("0")+" RPM":"—";
    }
}

class FanDeskV2 : Form {
    const string Version="0.7";
    readonly Button maxButton=new Button(),curveButton=new Button(),overlayButton=new Button(),applyCurveButton=new Button(),resetCurveButton=new Button();
    readonly NumericUpDown[] tempInputs=new NumericUpDown[4], rpmInputs=new NumericUpDown[4];
    readonly CheckBox extendedCheck=new CheckBox();
    readonly Label editorState=new Label();
    bool maximum=DeskFiles.Maximum, overlayOn=DeskFiles.OverlayVisible;
    MiniOverlay overlay; ToolStripMenuItem trayOverlayItem;
    readonly Label tempCard=new Label(), rpmCard=new Label(), targetCard=new Label(), status=new Label(), curveLabel=new Label();
    readonly ComboBox sensorBox=new ComboBox();
    readonly HistoryChart history=new HistoryChart();readonly CurveChart curve=new CurveChart();
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    NotifyIcon tray;Icon currentIcon; RegisteredWaitHandle showRegistration;
    bool busy,exiting,initialized,populating,preview,startInTray;int mismatches;
    string selectedKey=DeskFiles.Sensor,logPath;List<SensorChoice> knownSensors=new List<SensorChoice>();
    double lastSent=Double.NaN,cachedMax=Double.NaN,lastTemp=Double.NaN,lastRpm=Double.NaN;int fanIndex;
    public static readonly Color Bg=Color.FromArgb(15,22,32),PanelColor=Color.FromArgb(23,31,43),Ink=Color.FromArgb(230,237,246),Muted=Color.FromArgb(153,172,194);
    [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr handle);
    public FanDeskV2(bool hidden,bool designPreview) {
        preview=designPreview;startInTray=hidden;
        Text="iMac Fan Desk v"+Version;Size=new Size(1120,870);MinimumSize=new Size(1040,820);StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Segoe UI",10);BackColor=Bg;ForeColor=Ink;AutoScaleMode=AutoScaleMode.Dpi;
        var root=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24,20,24,18),ColumnCount=1,RowCount=7};
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,92));
        root.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,150));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        Controls.Add(root);
        var title=new Label{Text="iMac Fan Desk v"+Version,Font=new Font("Segoe UI",22,FontStyle.Bold),Dock=DockStyle.Fill};root.Controls.Add(title);
        var cards=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,Margin=new Padding(0,0,0,12)};
        for(int i=0;i<3;i++)cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,33.33f));
        MakeCard(cards,tempCard,"ТЕМПЕРАТУРА КЕРУВАННЯ",Color.FromArgb(244,143,145));MakeCard(cards,rpmCard,"ФАКТИЧНІ ОБЕРТИ",Color.FromArgb(72,186,255));MakeCard(cards,targetCard,"ЗАДАНІ ОБЕРТИ",Color.FromArgb(246,198,90));root.Controls.Add(cards);
        var charts=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=new Padding(0,0,0,12)};
        charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,72));charts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,28));
        history.Dock=DockStyle.Fill;history.Margin=new Padding(0,0,12,0);curve.Dock=DockStyle.Fill;curve.Margin=Padding.Empty;
        charts.Controls.Add(history);charts.Controls.Add(curve);root.Controls.Add(charts);
        var sensorRow=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty};sensorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,165));sensorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        sensorRow.Controls.Add(new Label{Text="Керувати за датчиком",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft});
        sensorBox.BackColor=PanelColor;sensorBox.ForeColor=Ink;sensorBox.FlatStyle=FlatStyle.Flat;sensorBox.Dock=DockStyle.Fill;sensorBox.DropDownStyle=ComboBoxStyle.DropDownList;sensorBox.Margin=new Padding(0,12,0,10);sensorRow.Controls.Add(sensorBox);root.Controls.Add(sensorRow);
        var modes=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,Margin=Padding.Empty};
        modes.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145));modes.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145));modes.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,175));modes.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        maxButton.Text="МАКСИМУМ";curveButton.Text="За кривою";
        foreach(var button in new[]{maxButton,curveButton,overlayButton}){button.Dock=DockStyle.Fill;button.FlatStyle=FlatStyle.Flat;button.ForeColor=Ink;button.Margin=new Padding(0,6,10,8);modes.Controls.Add(button);}
        maxButton.Click+=delegate{SetMode(true);};curveButton.Click+=delegate{SetMode(false);};
        overlayButton.Click+=delegate{SetOverlay(!overlayOn);};
        UpdateOverlayButtonStyle();
        curveLabel.Text="Крива керування";curveLabel.Dock=DockStyle.Fill;curveLabel.ForeColor=Muted;curveLabel.TextAlign=ContentAlignment.MiddleLeft;modes.Controls.Add(curveLabel);root.Controls.Add(modes);
        root.Controls.Add(BuildCurveEditor());
        status.Text="Підключення до SMC…";status.Dock=DockStyle.Fill;status.ForeColor=Muted;root.Controls.Add(status);
        LoadCurveToEditor(DeskFiles.Curve);
        sensorBox.SelectedIndexChanged+=delegate {
            var choice=sensorBox.SelectedItem as SensorChoice;if(populating||choice==null)return;
            try{DeskFiles.SaveSensor(choice.Key);selectedKey=choice.Key;lastSent=Double.NaN;history.Samples.Clear();history.Invalidate();status.Text="Датчик збережено. Застосування кривої…";}
            catch(Exception ex){DeskFiles.Error(ex);status.Text="Не вдалося зберегти датчик: "+ex.Message;}
        };
        if(!preview) {
            SetupTray();
            InitOverlay();
            string logs=Path.Combine(DeskFiles.Root,"logs");
            logPath=Path.Combine(logs,"fans-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv");
            File.WriteAllText(logPath,"time,sensor,temp_c,actual_rpm,target_rpm,max_rpm,cpu_c,gpu_c,forced,fallback,selection,mode\r\n");
            timer.Interval=2000;timer.Tick+=async delegate {await Poll();};
            SystemEvents.PowerModeChanged+=PowerChanged;
            Shown+=async delegate {if(startInTray)Hide();if(overlayOn&&overlay!=null)overlay.Show();await Poll();timer.Start();};
            Resize+=delegate{if(WindowState==FormWindowState.Minimized)Hide();};
        }
        FormClosing+=ClosingWindow;
    }
    void InitOverlay() {
        overlay=new MiniOverlay();
        var screen=Screen.PrimaryScreen.WorkingArea;
        int x=DeskFiles.OverlayX, y=DeskFiles.OverlayY;
        if(x==int.MinValue||y==int.MinValue||x<screen.Left-100||y<screen.Top-20||x>screen.Right-40||y>screen.Bottom-20) {
            x=screen.Right-overlay.Width-24;
            y=screen.Top+24;
        }
        overlay.Location=new Point(x,y);
        overlay.PositionChanged=loc=>DeskFiles.SaveOverlay(overlayOn,loc.X,loc.Y);
    }
    void SetOverlay(bool on) {
        overlayOn=on;
        Point loc=overlay!=null?overlay.Location:new Point(DeskFiles.OverlayX,DeskFiles.OverlayY);
        DeskFiles.SaveOverlay(overlayOn,loc.X,loc.Y);
        UpdateOverlayButtonStyle();
        if(overlay!=null) {
            overlay.UpdateValues(lastTemp,lastRpm);
            if(overlayOn) { if(!overlay.Visible) overlay.Show(); overlay.TopMost=true; }
            else overlay.Hide();
        }
    }
    void UpdateOverlayButtonStyle() {
        overlayButton.Text=overlayOn?"Міні-вікно: ВКЛ":"Міні-вікно: ВИКЛ";
        overlayButton.BackColor=overlayOn?Color.FromArgb(42,108,82):PanelColor;
        if(trayOverlayItem!=null) trayOverlayItem.Text=overlayOn?"Міні-вікно поверх усіх: ВКЛ":"Міні-вікно поверх усіх: ВИКЛ";
    }
    Control BuildCurveEditor() {
        var panel=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=PanelColor,ColumnCount=1,RowCount=3,Padding=new Padding(14,8,14,8),Margin=new Padding(0,0,0,8)};
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute,24));panel.RowStyles.Add(new RowStyle(SizeType.Absolute,53));panel.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        editorState.Text="КРИВА · усі зміни збережені";editorState.ForeColor=Muted;editorState.Dock=DockStyle.Fill;panel.Controls.Add(editorState);
        var points=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,Margin=Padding.Empty};
        string[] names={"ПОЧАТОК","ОСНОВНЕ ОХОЛОДЖЕННЯ","ПІДСИЛЕННЯ","ВЕРХНЯ ТОЧКА"};
        for(int i=0;i<4;i++) {
            points.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,25));
            var group=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,Margin=new Padding(0,0,8,0)};
            group.RowStyles.Add(new RowStyle(SizeType.Absolute,20));group.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            group.Controls.Add(new Label{Text=names[i],ForeColor=Muted,Font=new Font("Segoe UI",8),Dock=DockStyle.Fill});
            var row=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};
            var t=new NumericUpDown{Minimum=30,Maximum=110,Width=52,BackColor=Bg,ForeColor=Ink};
            var r=new NumericUpDown{Minimum=1200,Maximum=3600,Increment=50,Width=72,BackColor=Bg,ForeColor=Ink};
            tempInputs[i]=t;rpmInputs[i]=r;
            row.Controls.Add(t);row.Controls.Add(new Label{Text="°C →",AutoSize=true,Margin=new Padding(0,5,0,0)});row.Controls.Add(r);row.Controls.Add(new Label{Text="RPM",AutoSize=true,Margin=new Padding(0,5,0,0)});
            t.ValueChanged+=delegate{MarkCurveDirty();};r.ValueChanged+=delegate{MarkCurveDirty();};
            group.Controls.Add(row);points.Controls.Add(group);
        }
        panel.Controls.Add(points);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false,Margin=new Padding(0,6,0,0)};
        extendedCheck.Text="Розширені оберти до 3600 RPM";extendedCheck.AutoSize=true;extendedCheck.Margin=new Padding(0,7,18,0);extendedCheck.CheckedChanged+=delegate{MarkCurveDirty();};
        StyleSubButton(applyCurveButton,"Зберегти криву",140,Color.FromArgb(34,81,112));StyleSubButton(resetCurveButton,"Типові значення",150,Color.FromArgb(43,55,73));
        applyCurveButton.Click+=delegate{ApplyEditorCurve(false);};resetCurveButton.Click+=delegate{ApplyEditorCurve(true);};
        actions.Controls.Add(extendedCheck);actions.Controls.Add(applyCurveButton);actions.Controls.Add(resetCurveButton);panel.Controls.Add(actions);return panel;
    }
    void MarkCurveDirty(){if(populating)return;editorState.Text="КРИВА · є незбережені зміни";editorState.ForeColor=Color.FromArgb(246,198,90);applyCurveButton.Enabled=true;}
    void StyleSubButton(Button b,string text,int width,Color bg) {
        b.Text=text;b.Width=width;b.Height=30;b.FlatStyle=FlatStyle.Flat;b.BackColor=bg;b.ForeColor=Ink;b.Margin=new Padding(0,2,10,0);
    }
    void LoadCurveToEditor(CurveSettings c) {
        populating=true;
        for(int i=0;i<4;i++) {
            tempInputs[i].Value=(decimal)Math.Max(30,Math.Min(110,c.Temps[i]));
            rpmInputs[i].Value=(decimal)Math.Max(1200,Math.Min(3600,c.Rpms[i]));
        }
        extendedCheck.Checked=c.Extended;
        editorState.Text="КРИВА · усі зміни збережені";editorState.ForeColor=Muted;applyCurveButton.Enabled=false;
        curve.Curve=c.Clone();curve.Invalidate();
        populating=false;
    }
    void ApplyEditorCurve(bool resetDefault) {
        try {
            var next=resetDefault?new CurveSettings():new CurveSettings{
                Temps=tempInputs.Select(n=>(double)n.Value).ToArray(),
                Rpms=rpmInputs.Select(n=>(double)n.Value).ToArray(),
                Extended=extendedCheck.Checked
            };
            next.Validate();
            DeskFiles.SaveCurve(next);
            LoadCurveToEditor(next);
            lastSent=Double.NaN;
            status.ForeColor=Muted;
            status.Text=resetDefault?"Відновлено стандартну криву 55/80/85/90°C.":"Криву збережено та застосовано.";
            if(!busy){var ignored=Poll();}
        } catch(Exception ex) {
            DeskFiles.Error(ex);
            status.ForeColor=Color.Orange;
            status.Text="Помилка кривої: "+ex.Message;
            MessageBox.Show(ex.Message,"Некоректна крива",MessageBoxButtons.OK,MessageBoxIcon.Warning);
        }
    }
    void MakeCard(TableLayoutPanel parent,Label value,string caption,Color color) {
        var card=new TableLayoutPanel{Dock=DockStyle.Fill,BackColor=PanelColor,RowCount=2,Padding=new Padding(14,7,10,7),Margin=new Padding(0,0,10,0)};
        card.RowStyles.Add(new RowStyle(SizeType.Absolute,22));card.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        card.Controls.Add(new Label{Text=caption,ForeColor=Muted,Dock=DockStyle.Fill,Font=new Font("Segoe UI",9)});
        value.Text="—";value.Dock=DockStyle.Fill;value.Font=new Font("Segoe UI",24,FontStyle.Bold);value.ForeColor=color;card.Controls.Add(value);parent.Controls.Add(card);
    }
    void SetupTray() {
        var menu=new ContextMenuStrip();menu.Items.Add("Відкрити",null,delegate{ShowDesk();});
        trayOverlayItem=new ToolStripMenuItem(overlayOn?"Міні-вікно поверх усіх: ВКЛ":"Міні-вікно поверх усіх: ВИКЛ",null,delegate{SetOverlay(!overlayOn);});
        menu.Items.Add(trayOverlayItem);
        menu.Items.Add("МАКСИМУМ",null,delegate{SetMode(true);});menu.Items.Add("За кривою",null,delegate{SetMode(false);});
        menu.Items.Add("Журнали",null,delegate{Process.Start(Path.Combine(DeskFiles.Root,"logs"));});
        menu.Items.Add(new ToolStripSeparator());menu.Items.Add("Вийти → системне Auto",null,async delegate{await ExitDesk();});
        tray=new NotifyIcon{Icon=SystemIcons.Application,Text="iMac Fan Desk · підключення…",Visible=true,ContextMenuStrip=menu};
        tray.MouseClick+=delegate(object s,MouseEventArgs e){if(e.Button==MouseButtons.Left)ShowDesk();};
    }
    public void WatchShowEvent(EventWaitHandle signal){showRegistration=ThreadPool.RegisterWaitForSingleObject(signal,delegate{try{BeginInvoke((Action)ShowDesk);}catch{}},null,Timeout.Infinite,false);}
    void ShowDesk(){if(exiting)return;Show();WindowState=FormWindowState.Normal;Activate();}
    void SetMode(bool value){
        try{DeskFiles.SaveMode(value);maximum=value;lastSent=Double.NaN;status.Text=value?"Перемикання на постійний Max…":"Повернення до температурної кривої…";if(!busy){var ignored=Poll();}}
        catch(Exception ex){DeskFiles.Error(ex);status.Text="Не вдалося зберегти режим: "+ex.Message;}
    }
    void PowerChanged(object sender,PowerModeChangedEventArgs e){if(e.Mode==PowerModes.Resume)try{BeginInvoke((Action)delegate{initialized=false;lastSent=Double.NaN;});}catch{}}
    static void StopRPMac(){foreach(var p in Process.GetProcessesByName("RPMac")){using(p){p.Kill();if(!p.WaitForExit(5000))throw new Exception("RPMac не зупинився; одночасне керування заблоковано.");}}}
    static string SensorName(string key) {
        switch(key){case "TCXc":return "CPU — ядра / PECI";case "TC0P":return "CPU — поруч із процесором";case "TG0D":return "GPU — кристал";case "TG0P":return "GPU — поруч із чипом";case "TG1P":return "GPU — другий датчик";case "TM0P":return "Пам’ять — датчик 1";case "TM1P":return "Пам’ять — датчик 2";case "TA0P":return "Повітря на вході";case "TPCD":return "Живлення / PECI";default:return Smc.TempName(key);}
    }
    async Task Poll(){
        if(busy||exiting||preview)return;busy=true;string key=selectedKey;bool useMaximum=maximum;
        CurveSettings activeCurve=DeskFiles.Curve.Clone();
        try {
            Reading value=await Task.Run(delegate{
                if(!initialized){
                    StopRPMac();if(!Smc.Validate())throw new Exception("SMC: "+Smc.SafetyReason);
                    var keys=Smc.EnumTempKeys();knownSensors=keys.Distinct().Select(k=>new SensorChoice{Key=k,Name=SensorName(k)}).OrderBy(s=>s.Key=="TCXc"?0:s.Key=="TG0D"?1:2).ThenBy(s=>s.Name).ToList();
                    knownSensors.Insert(0,new SensorChoice{Key="ALL",Name="Найгарячіший датчик — усі доступні"});
                    knownSensors.Insert(0,new SensorChoice{Key="HOT",Name="Найгарячіший із CPU / GPU (рекомендовано)"});
                    initialized=true;lastSent=Double.NaN;
                }
                if(Process.GetProcessesByName("RPMac").Length>0)StopRPMac();
                var fans=Smc.GetFans();if(fans.Count!=1)throw new Exception("Очікувався один вентилятор iMac.");
                var f=fans[0];fanIndex=f.Index;
                if(!DeskPolicy.Valid(f.Actual)||!DeskPolicy.Valid(f.Target))throw new Exception("Некоректні показники RPM.");
                double cpu=Smc.ReadTemp("TCXc"),gpu=Smc.ReadTemp("TG0D"),temp;string actualKey=key;
                if(key=="HOT"||key=="ALL") {
                    var temperatures=new Dictionary<string,double>{{"TCXc",cpu},{"TG0D",gpu}};
                    if(key=="ALL")foreach(var sensor in knownSensors)if(sensor.Key.StartsWith("T")&&!temperatures.ContainsKey(sensor.Key))temperatures[sensor.Key]=Smc.ReadTemp(sensor.Key);
                    var hottest=DeskPolicy.Hottest(temperatures);temp=hottest.Value;actualKey=hottest.Key;
                    if(!DeskPolicy.TemperatureValid(cpu)||!DeskPolicy.TemperatureValid(gpu))temp=Double.NaN;
                } else temp=key=="TCXc"?cpu:key=="TG0D"?gpu:Smc.ReadTemp(key);
                double target=activeCurve.Target(temp,f.Min,f.Max,useMaximum);cachedMax=activeCurve.EffectiveCeiling(f.Max);
                if(Double.IsNaN(lastSent)||Math.Abs(lastSent-target)>=20||!f.Forced||Math.Abs(f.Target-target)>40){
                    Smc.SetFanCurveRpm(f.Index,target,activeCurve.Extended);lastSent=target;
                }
                f=Smc.GetFans()[0];
                if(!DeskPolicy.Valid(f.Actual)||!DeskPolicy.Valid(f.Target)||!f.Forced)throw new Exception("SMC не підтвердив керування.");
                return new Reading{Fan=f,Sensor=actualKey,Selection=key,Temperature=temp,Cpu=cpu,Gpu=gpu,Fallback=!DeskPolicy.TemperatureValid(temp),Maximum=useMaximum,Sensors=knownSensors};
            });
            UpdateReading(value,activeCurve);
            try{File.AppendAllText(logPath,String.Join(",",new[]{value.Time.ToString("o"),value.Sensor,DeskPolicy.Number(value.Temperature),DeskPolicy.Number(value.Fan.Actual),DeskPolicy.Number(value.Fan.Target),DeskPolicy.Number(value.Fan.Max),DeskPolicy.Number(value.Cpu),DeskPolicy.Number(value.Gpu),value.Fan.Forced?"1":"0",value.Fallback?"1":"0",value.Selection,value.Maximum?"max":"curve"})+"\r\n");}catch(Exception ex){DeskFiles.Error(ex);status.Text+=" · Журнал недоступний";}
        } catch(Exception ex){
            DeskFiles.Error(ex);status.ForeColor=Color.Orange;status.Text="Немає надійних даних SMC. "+ex.Message;tempCard.Text="—";rpmCard.Text="—";targetCard.Text="—";SetTray(Double.NaN,"Немає даних SMC");
            if(overlay!=null)overlay.UpdateValues(Double.NaN,Double.NaN);
            if(DeskPolicy.Valid(cachedMax))try{Smc.SetFanCurveRpm(fanIndex,cachedMax,activeCurve.Extended);}catch(Exception failure){DeskFiles.Error(failure);}
            initialized=false;
        }finally{busy=false;}
    }
    void UpdateReading(Reading value,CurveSettings activeCurve){
        if(sensorBox.Items.Count==0&&value.Sensors.Count>0){
            populating=true;foreach(var s in value.Sensors)sensorBox.Items.Add(s);
            var selected=value.Sensors.FirstOrDefault(s=>s.Key==selectedKey);
            if(selected==null){selected=value.Sensors.FirstOrDefault(s=>s.Key=="TCXc")??value.Sensors[0];DeskFiles.SaveSensor(selected.Key);selectedKey=selected.Key;lastSent=Double.NaN;}
            sensorBox.SelectedItem=selected;populating=false;
        }
        var f=value.Fan;
        lastTemp=value.Temperature;lastRpm=f.Actual;
        if(overlay!=null)overlay.UpdateValues(lastTemp,lastRpm);
        tempCard.Text=DeskPolicy.TemperatureValid(value.Temperature)?value.Temperature.ToString("0.0")+" °C":"—";rpmCard.Text=f.Actual.ToString("0")+" RPM";targetCard.Text=f.Target.ToString("0")+" RPM";
        curveLabel.Text=activeCurve.Extended?"Плавна крива · до "+activeCurve.Rpms[3].ToString("0")+" RPM":"Плавна крива · ліміт SMC "+f.Max.ToString("0")+" RPM";
        if(value.Maximum)curveLabel.Text="ПОСТІЙНИЙ MAX · "+activeCurve.EffectiveCeiling(f.Max).ToString("0")+" RPM (температура не знижує ціль)";
        maxButton.BackColor=value.Maximum?Color.FromArgb(145,86,24):PanelColor;curveButton.BackColor=value.Maximum?PanelColor:Color.FromArgb(34,81,112);
        UpdateOverlayButtonStyle();
        curve.Curve=activeCurve;curve.Temperature=value.Temperature;curve.Min=f.Min;curve.Max=f.Max;curve.Invalidate();
        history.Samples.Add(value);history.Samples.RemoveAll(s=>(value.Time-s.Time).TotalSeconds>360);history.Invalidate();
        mismatches=Math.Abs(f.Actual-f.Target)>Math.Max(250,f.Target*.15)?mismatches+1:0;
        status.ForeColor=value.Fallback||mismatches>=8?Color.Orange:Muted;
        status.Text=value.Fallback?"Датчик недоступний — задано Max до відновлення даних.":mismatches>=8?"Фактичні оберти не досягають цілі. Дивись графік і журнал.":"Керує "+value.Sensor+" · CPU "+FormatTemp(value.Cpu)+" · GPU "+FormatTemp(value.Gpu)+" · "+value.Time.ToString("HH:mm:ss")+" · × у трей";
        SetTray(value.Temperature,String.Format("{0} · {1} · {2:0} RPM",value.Maximum?"MAX":"Крива",value.Sensor,f.Actual));
        try{File.WriteAllLines(Path.Combine(DeskFiles.Root,"state.txt"),new[]{"version="+Version,"time="+value.Time.ToString("o"),"pid="+Process.GetCurrentProcess().Id,"windowVisible="+Visible,"trayVisible="+(tray!=null&&tray.Visible),"overlayVisible="+overlayOn,"selection="+value.Selection,"mode="+(value.Maximum?"max":"curve"),"extended="+activeCurve.Extended,"sensor="+value.Sensor,"temperature="+DeskPolicy.Number(value.Temperature),"actualRPM="+DeskPolicy.Number(f.Actual),"targetRPM="+DeskPolicy.Number(f.Target),"forced="+f.Forced,"fallback="+value.Fallback});}catch(Exception ex){DeskFiles.Error(ex);}
    }
    static string FormatTemp(double value){return DeskPolicy.TemperatureValid(value)?value.ToString("0")+"°C":"—";}
    void SetTray(double temp,string detail){
        if(tray==null)return;string text=DeskPolicy.TemperatureValid(temp)?Math.Round(temp).ToString():"!";
        using(var bitmap=new Bitmap(32,32))using(var g=Graphics.FromImage(bitmap))using(var font=new Font("Segoe UI",text.Length<3?23:17,FontStyle.Bold,GraphicsUnit.Pixel))using(var brush=new SolidBrush(DeskPolicy.TemperatureValid(temp)&&temp>=80?Color.FromArgb(255,133,117):Color.FromArgb(109,206,255)))using(var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}){
            g.Clear(Color.Transparent);g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;g.DrawString(text,font,brush,new RectangleF(-2,-2,36,35),format);
            IntPtr handle=bitmap.GetHicon();Icon icon;try{icon=(Icon)Icon.FromHandle(handle).Clone();}finally{DestroyIcon(handle);}
            var old=currentIcon;currentIcon=icon;tray.Icon=icon;if(old!=null)old.Dispose();
        }
        string tip="iMac Fan Desk · "+FormatTemp(temp)+" · "+detail;tray.Text=tip.Length>63?tip.Substring(0,63):tip;
    }
    void ClosingWindow(object sender,FormClosingEventArgs e){
        if(preview||exiting)return;
        if(e.CloseReason==CloseReason.WindowsShutDown||e.CloseReason==CloseReason.TaskManagerClosing){timer.Stop();DisposeTray();return;}
        e.Cancel=true;Hide();
    }
    async Task ExitDesk(){
        if(exiting)return;exiting=true;timer.Stop();
        try{
            while(busy)await Task.Delay(100);
            if(initialized||DeskPolicy.Valid(cachedMax))await Task.Run(delegate{Smc.SetFanAuto(fanIndex);var f=Smc.GetFans();if(f.Count==1&&f[0].Forced)throw new Exception("SMC не підтвердив Auto.");});
            Smc.Cleanup();DisposeTray();Close();
        }catch(Exception ex){DeskFiles.Error(ex);exiting=false;timer.Start();ShowDesk();MessageBox.Show("Не вдалося повернути системне Auto: "+ex.Message);}
    }
    void DisposeTray(){SystemEvents.PowerModeChanged-=PowerChanged;if(showRegistration!=null)showRegistration.Unregister(null);if(overlay!=null){overlay.Close();overlay.Dispose();overlay=null;}if(tray!=null){tray.Visible=false;tray.Dispose();tray=null;}if(currentIcon!=null){currentIcon.Dispose();currentIcon=null;}}
    public void SeedPreview(){
        sensorBox.Items.Add(new SensorChoice{Key="HOT",Name="Найгарячіший із CPU / GPU (рекомендовано)"});populating=true;sensorBox.SelectedIndex=0;populating=false;
        var c=DeskFiles.Curve;
        for(int i=0;i<180;i++){double temp=58+28*(0.5+0.5*Math.Sin(i/35.0));double tgt=c.Target(temp,1200,2700,false);history.Samples.Add(new Reading{Time=DateTime.Now.AddSeconds((i-179)*2),Temperature=temp,Fan=new FanInfo{Actual=tgt+35*Math.Sin(i/4.0),Target=tgt,Max=2700}});}
        tempCard.Text="82.5 °C";rpmCard.Text="2945 RPM";targetCard.Text="2950 RPM";curve.Curve=c;curve.Temperature=82.5;status.Text="Попередній перегляд · 4-точкова крива (55→80→85→90°C) + міні-вікно";
    }
    [STAThread]static int Main(string[] args){
        DeskFiles.Load();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Contains("--self-test")){try{DeskPolicy.SelfTest();File.WriteAllText(Path.Combine(DeskFiles.Root,"self-test.txt"),"PASS v0.7: 4-point curve, extended target override, overlay, hot GPU vs cool CPU, hot CPU, invalid readings, missing-sensor fallback, invalid limits");return 0;}catch(Exception ex){DeskFiles.Error(ex);return 2;}}
        if(args.Contains("--render-preview")){using(var form=new FanDeskV2(false,true)){form.SeedPreview();form.Show();Application.DoEvents();using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview.png"),ImageFormat.Png);}form.Close();}return 0;}
        bool first;using(var single=new Mutex(true,"Local\\iMacFanDesk_controller",out first))using(var show=new EventWaitHandle(false,EventResetMode.AutoReset,"Local\\iMacFanDesk_show")){
            if(!first){show.Set();return 0;}Process.GetCurrentProcess().PriorityClass=ProcessPriorityClass.Normal;
            using(var form=new FanDeskV2(args.Contains("--tray"),false)){form.WatchShowEvent(show);Application.Run(form);}return 0;
        }
    }
}
