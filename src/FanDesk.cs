// iMac Fan Desk - GPL-2.0-only. SMC backend: RPMac v1.10.1 (see Smc.cs, LICENSE, NOTICE).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using RPMac;

class Sample {
    public DateTime Time = DateTime.Now;
    public FanInfo Fan;
    public double Cpu, Gpu;
    public string Mode;
}
class FanPlot : Control {
    public readonly List<Sample> Samples = new List<Sample>();
    public FanPlot() { DoubleBuffered=true; BackColor=Color.FromArgb(22,29,40); }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);
        var g=e.Graphics; g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var area=new Rectangle(58,25,Math.Max(1,Width-85),Math.Max(1,Height-65));
        double max=Samples.Count==0 ? 3000 : Math.Max(3000,Samples.Max(s=>Math.Max(s.Fan.Actual,s.Fan.Target))+150);
        using(var grid=new Pen(Color.FromArgb(46,57,73)))
        using(var text=new SolidBrush(Color.FromArgb(170,185,204))) {
            for(int n=0;n<=4;n++) { int y=area.Bottom-area.Height*n/4; g.DrawLine(grid,area.Left,y,area.Right,y); g.DrawString((max*n/4).ToString("0"),Font,text,2,y-8); }
            g.DrawString("RPM · останні 4 хвилини",Font,text,area.Left,3);
            g.DrawString("Синій — фактичні   ·   Жовтий — ціль SMC",Font,text,area.Left,Height-27);
        }
        if(Samples.Count<2) return;
        DateTime end=Samples[Samples.Count-1].Time;
        for(int series=0;series<2;series++) {
            using(var pen=new Pen(series==0?Color.FromArgb(74,180,255):Color.FromArgb(255,202,95),2)) {
                PointF? previous=null;
                foreach(var s in Samples) {
                    double value=series==0?s.Fan.Actual:s.Fan.Target;
                    if(Double.IsNaN(value)) {previous=null;continue;}
                    var point=new PointF(area.Right-(float)((end-s.Time).TotalSeconds/240*area.Width),area.Bottom-(float)(value/max*area.Height));
                    if(previous.HasValue) g.DrawLine(pen,previous.Value,point);
                    previous=point;
                }
            }
        }
    }
}
class FanDesk : Form {
    Label reading=new Label(), temperatures=new Label(), status=new Label(), range=new Label();
    ComboBox mode=new ComboBox(); NumericUpDown rpm=new NumericUpDown(), cool=new NumericUpDown(), hot=new NumericUpDown();
    CheckBox emergency=new CheckBox(); Button apply=new Button(), maxButton=new Button(), autoButton=new Button();
    FanPlot plot=new FanPlot(); System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    bool busy, closing, controlling;
    string currentMode="Спостереження", pending=null, logPath;
    int desired=2000, low=55, high=80, fanIndex=0;
    bool boost=true;
    DateTime commandTime=DateTime.MinValue;
    double lastSent=Double.NaN;
    static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
    static readonly Color Ink=Color.FromArgb(225,234,245);
    public FanDesk(bool startMax) {
        Text="iMac Fan Desk — керування та діагностика"; Size=new Size(1010,790); MinimumSize=new Size(920,750);
        StartPosition=FormStartPosition.CenterScreen; BackColor=Color.FromArgb(15,21,31); ForeColor=Ink;
        Font=new Font("Segoe UI",10); AutoScaleMode=AutoScaleMode.Dpi;
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=9};
        foreach(int h in new[]{45,52,32,36,64,50,42}) layout.RowStyles.Add(new RowStyle(SizeType.Absolute,h));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,62));
        Controls.Add(layout);
        var title=new Label {Text="Керування вентилятором iMac",Font=new Font("Segoe UI",21,FontStyle.Bold),Dock=DockStyle.Fill}; layout.Controls.Add(title);
        reading.Text="Читання SMC…"; reading.Font=new Font("Segoe UI",21,FontStyle.Bold); reading.Dock=DockStyle.Fill; layout.Controls.Add(reading);
        temperatures.Dock=DockStyle.Fill; temperatures.Text="CPU / GPU: очікування датчиків"; layout.Controls.Add(temperatures);
        range.Dock=DockStyle.Fill; range.Text="Режим спостереження: команди не надсилаються."; layout.Controls.Add(range);
        var commands=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,Padding=new Padding(0,7,0,0)};
        mode.DropDownStyle=ComboBoxStyle.DropDownList; mode.Width=205;
        mode.Items.AddRange(new object[]{"Ручні оберти","За температурою"}); mode.SelectedIndex=0;
        rpm.Minimum=0; rpm.Maximum=20000; rpm.Value=2000; rpm.Increment=100; rpm.Width=100;
        Style(apply,"Застосувати",145); Style(maxButton,"МАКСИМУМ",160); Style(autoButton,"Системне Auto",165);
        commands.Controls.AddRange(new Control[]{mode,rpm,apply,maxButton,autoButton}); layout.Controls.Add(commands);
        var curve=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};
        curve.Controls.Add(new Label {Text="Темп. крива: мінімум при",AutoSize=true,Padding=new Padding(0,5,0,0)});
        SetupTemp(cool,55); SetupTemp(hot,80); curve.Controls.Add(cool);
        curve.Controls.Add(new Label {Text="°C → максимум при",AutoSize=true,Padding=new Padding(0,5,0,0)}); curve.Controls.Add(hot);
        curve.Controls.Add(new Label {Text="°C (за гарячішим CPU/GPU)",AutoSize=true,Padding=new Padding(0,5,0,0)}); layout.Controls.Add(curve);
        emergency.Text="Підсилення: Max від 85°C; при втраті температур у власному режимі — Max";
        emergency.AutoSize=true; emergency.Checked=true; layout.Controls.Add(emergency);
        plot.Dock=DockStyle.Fill; layout.Controls.Add(plot);
        status.Dock=DockStyle.Fill; status.ForeColor=Color.FromArgb(168,188,213); status.Text="Вихід із власного режиму повертає системне Auto. Автозапуск Windows не змінюється."; layout.Controls.Add(status);
        apply.Click+=delegate { if(cool.Value>=hot.Value) {MessageBox.Show("Верхня температура має бути більшою за нижню.");return;} Queue(mode.SelectedIndex==0?"Ручні оберти":"За температурою"); };
        maxButton.Click+=delegate {Queue("Максимум");}; autoButton.Click+=delegate {Queue("Системне Auto");};
        timer.Interval=2000; timer.Tick+=async delegate {await Poll();};
        Shown+=async delegate { await Poll(); if(startMax) Queue("Максимум"); timer.Start(); };
        FormClosing+=OnClosing;
        string logs=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"logs"); Directory.CreateDirectory(logs);
        logPath=Path.Combine(logs,"fans-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv");
        File.WriteAllText(logPath,"time,mode,fan,actual_rpm,target_rpm,min_rpm,max_rpm,cpu_c,gpu_c,forced\r\n");
    }
    void Style(Button b,string text,int width) {b.Text=text;b.Width=width;b.Height=37;b.FlatStyle=FlatStyle.Flat;b.BackColor=Color.FromArgb(40,64,91);b.ForeColor=Ink;}
    void SetupTemp(NumericUpDown n,int value) {n.Minimum=35;n.Maximum=95;n.Value=value;n.Width=65;}
    void Queue(string request) {
        pending=request; desired=(int)rpm.Value; low=(int)cool.Value; high=(int)hot.Value; boost=emergency.Checked;
        status.Text="Запит: "+request+". Очікування зчитування SMC…";
        if(!busy) { var ignored=Poll(); }
    }
    static bool ValidTemp(double t) {return !Double.IsNaN(t)&&t>0&&t<130;}
    static void StopOtherController() {
        foreach(var p in Process.GetProcessesByName("RPMac")) {
            p.Kill(); if(!p.WaitForExit(5000)) throw new Exception("Не вдалося зупинити RPMac. Керування не перехоплено.");
            p.Dispose();
        }
    }
    async Task Poll() {
        if(busy||closing) return; busy=true;
        string request=pending; pending=null;
        int requestedRpm=desired, tLow=low, tHigh=high; bool useBoost=boost;
        try {
            Sample sample=await Task.Run(delegate {
                if(!Smc.WritesAllowed && !Smc.Validate()) throw new Exception("SMC недоступний: "+Smc.SafetyReason);
                if(request!=null) {StopOtherController(); currentMode=request; controlling=true; lastSent=Double.NaN; commandTime=DateTime.Now;}
                if(controlling && Process.GetProcessesByName("RPMac").Length>0) throw new Exception("RPMac запустився паралельно. Натисни потрібний режим, щоб перехопити керування.");
                var fans=Smc.GetFans(); if(fans.Count!=1) throw new Exception("Ця збірка розрахована на iMac з одним вентилятором; виявлено: "+fans.Count);
                var f=fans[0]; fanIndex=f.Index;
                if(Double.IsNaN(f.Actual)||Double.IsNaN(f.Target)||Double.IsNaN(f.Min)||Double.IsNaN(f.Max)||f.Max<=f.Min) throw new Exception("Некоректні дані обертів SMC.");
                double cpu=Smc.ReadTemp("TCXc"), gpu=Smc.ReadTemp("TG0D");
                if(controlling) {
                    bool sensorsOk=ValidTemp(cpu)&&ValidTemp(gpu);
                    bool own=currentMode!="Системне Auto";
                    double temp=sensorsOk?Math.Max(cpu,gpu):Double.NaN;
                    double target=f.Target;
                    if(own && (!sensorsOk || (useBoost && temp>=85))) target=f.Max;
                    else if(currentMode=="Максимум") target=f.Max;
                    else if(currentMode=="Ручні оберти") target=requestedRpm;
                    else if(currentMode=="За температурою") target=f.Min+(f.Max-f.Min)*Math.Max(0,Math.Min(1,(temp-tLow)/(tHigh-tLow)));
                    if(currentMode=="Системне Auto") { if(request!=null) Smc.SetFanAuto(f.Index); }
                    else {
                        target=Math.Max(f.Min,Math.Min(f.Max,target));
                        if(Double.IsNaN(lastSent)||Math.Abs(lastSent-target)>=20||Math.Abs(f.Target-target)>40||!f.Forced) {
                            Smc.SetFanRpm(f.Index,target); lastSent=target;
                        }
                    }
                    f=Smc.GetFans()[0];
                }
                return new Sample {Fan=f,Cpu=cpu,Gpu=gpu,Mode=currentMode};
            });
            var fan=sample.Fan;
            reading.Text=String.Format("{0:0} об/хв   ·   ціль SMC: {1:0}",fan.Actual,fan.Target);
            temperatures.Text="CPU (TCXc): "+Temp(sample.Cpu)+"     GPU (TG0D): "+Temp(sample.Gpu)+"     Режим: "+sample.Mode;
            range.Text=String.Format("Діапазон SMC: {0:0}–{1:0} об/хв    ·    примусове керування: {2}",fan.Min,fan.Max,fan.Forced?"так":"ні");
            rpm.Minimum=(decimal)Math.Ceiling(fan.Min);rpm.Maximum=(decimal)Math.Floor(fan.Max);
            plot.Samples.Add(sample); plot.Samples.RemoveAll(s=>(sample.Time-s.Time).TotalSeconds>240);plot.Invalidate();
            File.AppendAllText(logPath,String.Join(",",new[]{sample.Time.ToString("o"),sample.Mode,fan.Index.ToString(),Num(fan.Actual),Num(fan.Target),Num(fan.Min),Num(fan.Max),Num(sample.Cpu),Num(sample.Gpu),fan.Forced?"1":"0"})+"\r\n");
            bool mismatch=controlling&&currentMode!="Системне Auto"&&(DateTime.Now-commandTime).TotalSeconds>15&&Math.Abs(fan.Actual-fan.Target)>Math.Max(200,fan.Target*0.15);
            status.ForeColor=mismatch?Color.Orange:Color.FromArgb(168,188,213);
            status.Text=mismatch?"УВАГА: фактичні оберти не досягають цілі. Перевір журнал і потік повітря.":"Оновлено "+DateTime.Now.ToString("HH:mm:ss")+" · Журнал: "+Path.GetFileName(logPath)+"\r\n"+(controlling?"Закриття → системне Auto.":"Спостереження: закриття не змінює SMC.")+" Новий автозапуск не додається.";
        } catch(Exception ex) {
            reading.Text="Немає надійних даних SMC";status.Text=ex.Message;status.ForeColor=Color.Orange;
        } finally {busy=false;}
    }
    static string Temp(double t) {return ValidTemp(t)?t.ToString("0.0")+" °C":"немає даних";}
    static string Num(double d) {return Double.IsNaN(d)?"":d.ToString("0.0",Inv);}
    async void OnClosing(object sender,FormClosingEventArgs e) {
        if(closing) return; e.Cancel=true; timer.Stop(); closing=true; Enabled=false;
        while(busy) await Task.Delay(100);
        try {
            if(controlling) await Task.Run(delegate { Smc.SetFanAuto(fanIndex); var f=Smc.GetFans(); if(f.Count==1&&f[0].Forced) throw new Exception("SMC усе ще повідомляє примусове керування."); });
            Smc.Cleanup(); FormClosing-=OnClosing; Close();
        } catch(Exception ex) {
            closing=false;Enabled=true;timer.Start(); MessageBox.Show("Не вдалося підтвердити повернення Auto: "+ex.Message+"\nВікно залишається відкритим.");
        }
    }
    [STAThread] static int Main(string[] args) {
        bool created; using(var mutex=new Mutex(true,"Local\\iMacFanDesk_controller",out created)) {
            if(!created) {MessageBox.Show("iMac Fan Desk вже працює.");return 1;}
            Process.GetCurrentProcess().PriorityClass=ProcessPriorityClass.Normal;
            if(args.Length>0&&(args[0]=="--probe"||args[0]=="--verify-max")) {
                string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"probe.txt");
                try {
                    if(!Smc.Validate()) throw new Exception(Smc.SafetyReason);
                    var f=Smc.GetFans();var lines=new List<string>();
                    lines.Add("Hardware: "+Smc.HardwareName);lines.Add("Validation: "+Smc.SafetyReason);
                    foreach(var fan in f) lines.Add(String.Format(Inv,"Fan {0}: actual={1}, target={2}, min={3}, max={4}, forced={5}",fan.Index,fan.Actual,fan.Target,fan.Min,fan.Max,fan.Forced));
                    lines.Add("CPU TCXc="+Num(Smc.ReadTemp("TCXc")));lines.Add("GPU TG0D="+Num(Smc.ReadTemp("TG0D")));
                    if(args[0]=="--verify-max") {
                        if(f.Count!=1) throw new Exception("Expected exactly one fan");
                        StopOtherController();
                        Smc.SetFanMax(f[0].Index);
                        Thread.Sleep(10000);
                        var after=Smc.GetFans()[0];
                        lines.Add(String.Format(Inv,"AFTER MAX: actual={0}, target={1}, max={2}, forced={3}",after.Actual,after.Target,after.Max,after.Forced));
                        if(!after.Forced||Double.IsNaN(after.Actual)||Double.IsNaN(after.Target)||Double.IsNaN(after.Max)||Math.Abs(after.Actual-after.Target)>200||Math.Abs(after.Target-after.Max)>50) throw new Exception("Max readback did not converge");
                        lines.Add("MAX command and actual RPM readback: PASS");
                    }
                    File.WriteAllLines(path,lines);Smc.Cleanup();return 0;
                } catch(Exception ex) {File.WriteAllText(path,"ERROR: "+ex);return 2;}
            }
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new FanDesk(args.Contains("--max")));return 0;
        }
    }
}
