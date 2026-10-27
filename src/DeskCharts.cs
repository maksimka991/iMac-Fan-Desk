// GPL-2.0-only
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

class HistoryChart : Control {
    public readonly List<Reading> Samples=new List<Reading>();
    static readonly Color Muted=Color.FromArgb(153,172,194);
    public HistoryChart() { DoubleBuffered=true;BackColor=Color.FromArgb(23,31,43);ResizeRedraw=true; }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        if(Width<200||Height<130)return;
        var r=new Rectangle(64,48,Width-131,Height-100);
        double ceiling=Samples.Count==0?3600:Math.Ceiling(Math.Max(3000,Samples.Max(s=>Math.Max(Math.Max(s.Fan.Actual,s.Fan.Target),s.Fan.Max))*1.08)/500)*500;
        using(var brush=new SolidBrush(Muted))using(var pen=new Pen(Color.FromArgb(43,55,73))) {
            g.DrawString("ОБЕРТИ ТА ТЕМПЕРАТУРА · 6 хвилин",Font,brush,16,14);
            for(int i=0;i<=4;i++) {int y=r.Bottom-r.Height*i/4;g.DrawLine(pen,r.Left,y,r.Right,y);g.DrawString((ceiling*i/4).ToString("0"),Font,brush,9,y-8);g.DrawString((20+80*i/4)+"°",Font,brush,r.Right+10,y-8);}
            for(int i=0;i<=3;i++){int x=r.Left+r.Width*i/3;g.DrawLine(pen,x,r.Top,x,r.Bottom);g.DrawString(i==3?"зараз":("−"+(6-i*2)+" хв"),Font,brush,x-20,r.Bottom+9);}
            g.DrawString("RPM",Font,brush,10,r.Top-23);g.DrawString("°C",Font,brush,r.Right+10,r.Top-23);
        }
        if(Samples.Count<2)return;
        DateTime end=Samples[Samples.Count-1].Time;
        var colors=new[]{Color.FromArgb(72,186,255),Color.FromArgb(246,198,90),Color.FromArgb(244,126,130)};
        var previousClip=g.Clip;g.SetClip(r);
        for(int line=0;line<3;line++)using(var pen=new Pen(colors[line],line==0?2.6f:1.8f)) {
            if(line==1)pen.DashStyle=DashStyle.Dash;
            PointF? previous=null;
            foreach(var sample in Samples) {
                double v=line==0?sample.Fan.Actual:line==1?sample.Fan.Target:sample.Temperature;
                if(!DeskPolicy.Valid(v)){previous=null;continue;}
                double fraction=line==2?(v-20)/80:v/ceiling;
                var point=new PointF(r.Right-(float)((end-sample.Time).TotalSeconds/360*r.Width),r.Bottom-(float)(fraction*r.Height));
                if(previous.HasValue)g.DrawLine(pen,previous.Value,point);previous=point;
            }
        }
        g.Clip=previousClip;previousClip.Dispose();
        string[] names={"Фактичні RPM","Ціль SMC","Обраний датчик"};
        for(int i=0;i<3;i++)using(var brush=new SolidBrush(colors[i])){int x=20+i*190;g.FillEllipse(brush,x,Height-19,7,7);g.DrawString(names[i],Font,brush,x+13,Height-25);}
    }
}
class CurveChart : Control {
    public double Temperature=Double.NaN, Min=1200,Max=2700;
    public CurveSettings Curve=new CurveSettings();
    public CurveChart(){DoubleBuffered=true;ResizeRedraw=true;BackColor=Color.FromArgb(23,31,43);}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;if(Width<160||Height<130)return;
        var r=new Rectangle(24,48,Width-48,Height-110);
        var c=Curve??new CurveSettings();
        double minTemp=Math.Min(45,c.Temps[0]-5), maxTemp=Math.Max(95,c.Temps[3]+5);
        double minRpm=Math.Min(Min,c.Rpms[0]), maxRpm=Math.Max(c.Extended?3600:Max,c.EffectiveCeiling(Max));
        if(maxRpm<=minRpm) maxRpm=minRpm+1500;
        using(var b=new SolidBrush(Color.FromArgb(170,188,209)))using(var grid=new Pen(Color.FromArgb(43,55,73))) {
            g.DrawString("КРИВА ТЕМПЕРАТУР",Font,b,16,14);
            g.DrawString(maxRpm.ToString("0")+" RPM",Font,b,r.Left,r.Top-23);
            g.DrawString(minRpm.ToString("0")+" RPM",Font,b,r.Left,r.Bottom+9);
            g.DrawString(c.Temps[0].ToString("0")+"°C",Font,b,r.Left,r.Bottom+31);
            g.DrawString(c.Temps[3].ToString("0")+"°C",Font,b,r.Right-36,r.Bottom+31);
            if(c.Extended&&Max>minRpm&&Max<maxRpm) {
                float ySmc=r.Bottom-(float)((Max-minRpm)/(maxRpm-minRpm)*r.Height);
                grid.DashStyle=DashStyle.Dot;
                g.DrawLine(grid,r.Left,ySmc,r.Right,ySmc);
            }
        }
        Func<double,double,PointF> toPt=(t,rpm)=>{
            float tx=(float)Math.Max(0,Math.Min(1,(t-minTemp)/(maxTemp-minTemp)));
            float ty=(float)Math.Max(0,Math.Min(1,(rpm-minRpm)/(maxRpm-minRpm)));
            return new PointF(r.Left+tx*r.Width,r.Bottom-ty*r.Height);
        };
        var pts=new List<PointF>();
        pts.Add(toPt(minTemp,c.Target(minTemp,Min,Max,false)));
        for(int i=0;i<4;i++) pts.Add(toPt(c.Temps[i],c.Target(c.Temps[i],Min,Max,false)));
        pts.Add(toPt(maxTemp,c.Target(maxTemp,Min,Max,false)));
        using(var p=new Pen(Color.FromArgb(72,186,255),3)) g.DrawLines(p,pts.ToArray());
        using(var nodeBrush=new SolidBrush(Color.FromArgb(109,206,255))) {
            for(int i=1;i<=4;i++) g.FillEllipse(nodeBrush,pts[i].X-4,pts[i].Y-4,8,8);
        }
        if(DeskPolicy.TemperatureValid(Temperature)){
            double curRpm=c.Target(Temperature,Min,Max,false);
            var curPt=toPt(Temperature,curRpm);
            using(var b=new SolidBrush(Color.FromArgb(246,198,90)))g.FillEllipse(b,curPt.X-6,curPt.Y-6,12,12);
        }
    }
}
