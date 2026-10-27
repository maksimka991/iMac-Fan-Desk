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
        double ceiling=Samples.Count==0?3000:Math.Ceiling(Math.Max(3000,Samples.Max(s=>Math.Max(s.Fan.Actual,s.Fan.Max))*1.08)/500)*500;
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
    public CurveChart(){DoubleBuffered=true;ResizeRedraw=true;BackColor=Color.FromArgb(23,31,43);}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;if(Width<160||Height<130)return;
        var r=new Rectangle(24,48,Width-48,Height-110);
        using(var b=new SolidBrush(Color.FromArgb(170,188,209))) {
            g.DrawString("КРИВА 55 → 80°C",Font,b,16,14);g.DrawString(Max.ToString("0")+" RPM",Font,b,r.Left,r.Top-23);g.DrawString(Min.ToString("0")+" RPM",Font,b,r.Left,r.Bottom+9);
            g.DrawString("55°C",Font,b,r.Left,r.Bottom+31);g.DrawString("80°C",Font,b,r.Right-36,r.Bottom+31);
        }
        using(var p=new Pen(Color.FromArgb(72,186,255),3))g.DrawLine(p,r.Left,r.Bottom,r.Right,r.Top);
        if(DeskPolicy.TemperatureValid(Temperature)){
            float t=(float)Math.Max(0,Math.Min(1,(Temperature-55)/25));
            using(var b=new SolidBrush(Color.FromArgb(246,198,90)))g.FillEllipse(b,r.Left+t*r.Width-6,r.Bottom-t*r.Height-6,12,12);
        }
    }
}
