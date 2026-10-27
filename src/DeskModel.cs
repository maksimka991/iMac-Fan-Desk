// GPL-2.0-only
using System;
using System.IO;
using System.Globalization;
using System.Collections.Generic;
using RPMac;

class SensorChoice {
    public string Key, Name;
    public override string ToString() { return Name+"  ·  "+Key; }
}
class Reading {
    public DateTime Time=DateTime.Now;
    public FanInfo Fan;
    public double Temperature, Cpu, Gpu;
    public string Sensor, Selection;
    public bool Fallback;
    public bool Maximum;
    public List<SensorChoice> Sensors;
}
static class DeskPolicy {
    public static bool Valid(double value) { return !Double.IsNaN(value)&&!Double.IsInfinity(value); }
    public static bool TemperatureValid(double value) { return Valid(value)&&value>0&&value<130; }
    public static double Target(double temperature,double min,double max,bool maximum=false) {
        if(!Valid(min)||!Valid(max)||min<0||max<=min||max>20000) throw new ArgumentException("Invalid fan limits");
        if(maximum||!TemperatureValid(temperature)) return max;
        return min+(max-min)*Math.Max(0,Math.Min(1,(temperature-55)/25));
    }
    public static string Number(double d) {return Valid(d)?d.ToString("0.0",CultureInfo.InvariantCulture):"";}
    public static KeyValuePair<string,double> Hottest(IDictionary<string,double> readings) {
        var result=new KeyValuePair<string,double>("",Double.NaN);
        foreach(var item in readings)if(TemperatureValid(item.Value)&&(!TemperatureValid(result.Value)||item.Value>result.Value))result=item;
        return result;
    }
    public static void SelfTest() {
        CurveSettings.Test();
        if(Target(40,1200,2700)!=1200||Target(55,1200,2700)!=1200||Target(67.5,1200,2700)!=1950||Target(80,1200,2700)!=2700||Target(100,1200,2700)!=2700||Target(Double.NaN,1200,2700)!=2700) throw new Exception("Curve/fallback test failed");
        bool rejected=false;try {Target(60,2700,1200);}catch(ArgumentException){rejected=true;}
        if(!rejected) throw new Exception("Invalid hardware limits were accepted");
        if(Target(40,1200,2700,true)!=2700||Target(55,1200,2700,false)!=1200)throw new Exception("Max/curve switching failed");
        var hottest=Hottest(new Dictionary<string,double>{{"TCXc",74.3},{"TG0D",94}});
        if(hottest.Key!="TG0D"||Target(hottest.Value,1200,2700)!=2700)throw new Exception("Hot GPU was ignored");
        hottest=Hottest(new Dictionary<string,double>{{"TCXc",90},{"TG0D",60},{"bad",Double.NaN}});
        if(hottest.Key!="TCXc")throw new Exception("Hot CPU was ignored");
        hottest=Hottest(new Dictionary<string,double>{{"bad",Double.NaN}});
        if(Target(hottest.Value,1200,2700)!=2700)throw new Exception("Missing-sensor fallback failed");
    }
}
static class DeskFiles {
    public static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"iMacFanDesk");
    public static string Sensor="HOT";
    public static bool Maximum;
    public static CurveSettings Curve=new CurveSettings();
    public static void SaveCurve(CurveSettings c){string path=Path.Combine(Root,"curve.txt"),tmp=path+".tmp";File.WriteAllText(tmp,c.Serialize());if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);Curve=c;}
    public static void Load() {
        Directory.CreateDirectory(Root);Directory.CreateDirectory(Path.Combine(Root,"logs"));
        try{string cp=Path.Combine(Root,"curve.txt");if(File.Exists(cp))Curve=CurveSettings.Parse(File.ReadAllText(cp));}catch(Exception ex){Error(ex);Curve=new CurveSettings();}
        string path=Path.Combine(Root,"sensor.txt");
        if(File.Exists(path)) {string key=File.ReadAllText(path).Trim();if(key=="HOT"||key=="ALL"||(key.Length==4&&key.StartsWith("T"))) Sensor=key;}
        string mode=Path.Combine(Root,"mode.txt");Maximum=File.Exists(mode)&&File.ReadAllText(mode).Trim()=="max";
    }
    public static void SaveSensor(string key) {
        string path=Path.Combine(Root,"sensor.txt"), temp=path+".tmp";
        File.WriteAllText(temp,key); if(File.Exists(path)) File.Replace(temp,path,null);else File.Move(temp,path);
        Sensor=key;
    }
    public static void SaveMode(bool maximum) {
        string path=Path.Combine(Root,"mode.txt"),temp=path+".tmp";
        File.WriteAllText(temp,maximum?"max":"curve");if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);Maximum=maximum;
    }
    public static void Error(Exception ex) {try {File.AppendAllText(Path.Combine(Root,"errors.log"),DateTime.Now.ToString("o")+" "+ex+Environment.NewLine);}catch{}}
}

// User-editable, monotonic piecewise-linear curve. Values above SMC max require opt-in.
class CurveSettings {
    public double[] Temps={55,80,85,90};
    public double[] Rpms={1200,2700,3200,3600};
    public bool Extended=true;
    public CurveSettings Clone() {
        return new CurveSettings {
            Temps=(double[])Temps.Clone(),
            Rpms=(double[])Rpms.Clone(),
            Extended=Extended
        };
    }
    public void Validate() {
        if(Temps.Length!=4||Rpms.Length!=4)throw new ArgumentException("Потрібні чотири точки.");
        for(int i=0;i<4;i++) {
            if(!DeskPolicy.Valid(Temps[i])||Temps[i]<30||Temps[i]>110||!DeskPolicy.Valid(Rpms[i])||Rpms[i]<1200||Rpms[i]>3600)throw new ArgumentException("Температура: 30–110°C; оберти: 1200–3600 RPM.");
            if(i>0&&(Temps[i]<=Temps[i-1]||Rpms[i]<Rpms[i-1]))throw new ArgumentException("Температури мають зростати, оберти не можуть спадати.");
        }
    }
    public double EffectiveCeiling(double smcMax) {
        Validate();
        return Extended?Math.Max(smcMax,Rpms[3]):smcMax;
    }
    public double Target(double temp,double min,double max,bool maximum) {
        Validate();DeskPolicy.Target(temp,min,max);
        double ceiling=EffectiveCeiling(max);
        double rpm=ceiling;
        if(!maximum&&DeskPolicy.TemperatureValid(temp)) {
            if(temp<=Temps[0]) rpm=Rpms[0];
            else {
                rpm=Rpms[3];
                for(int i=1;i<4;i++) {
                    if(temp<=Temps[i]) {rpm=Rpms[i-1]+(Rpms[i]-Rpms[i-1])*Math.Max(0,(temp-Temps[i-1])/(Temps[i]-Temps[i-1]));break;}
                }
            }
        }
        return Math.Max(min,Math.Min(Extended?3600:max,rpm));
    }
    public string Summary(double smcMax) {
        return String.Format("{0:0}°C→{1:0} · {2:0}°C→{3:0} · {4:0}°C→{5:0} · {6:0}°C→{7:0} RPM ({8})",
            Temps[0],Rpms[0],Temps[1],Extended?Rpms[1]:Math.Min(smcMax,Rpms[1]),
            Temps[2],Extended?Rpms[2]:Math.Min(smcMax,Rpms[2]),
            Temps[3],Extended?Rpms[3]:Math.Min(smcMax,Rpms[3]),
            Extended?"розширений ліміт до 3600":"ліміт SMC "+smcMax.ToString("0"));
    }
    public string Serialize() {
        Validate();var lines=new List<string>{Extended?"extended=1":"extended=0"};
        for(int i=0;i<4;i++)lines.Add(Temps[i].ToString(CultureInfo.InvariantCulture)+","+Rpms[i].ToString(CultureInfo.InvariantCulture));
        return String.Join("\n",lines);
    }
    public static CurveSettings Parse(string text) {
        var lines=text.Trim().Split('\n');if(lines.Length!=5)throw new ArgumentException("Некоректний файл кривої.");
        var c=new CurveSettings();string flag=lines[0].Trim();if(flag!="extended=0"&&flag!="extended=1")throw new ArgumentException("Некоректний режим.");c.Extended=flag=="extended=1";
        for(int i=0;i<4;i++){var parts=lines[i+1].Trim().Split(',');if(parts.Length!=2)throw new ArgumentException("Некоректна точка.");c.Temps[i]=Double.Parse(parts[0],CultureInfo.InvariantCulture);c.Rpms[i]=Double.Parse(parts[1],CultureInfo.InvariantCulture);}c.Validate();return c;
    }
    public static void Test() {
        var c=new CurveSettings{Extended=true};
        if(c.Target(80,1200,2700,false)!=2700||c.Target(85,1200,2700,false)!=3200||c.Target(90,1200,2700,false)!=3600||c.Target(82.5,1200,2700,false)!=2950||c.Target(40,1200,2700,false)!=1200||c.Target(Double.NaN,1200,2700,false)!=3600)throw new Exception("Extended curve failed");
        c.Extended=false;if(c.Target(95,1200,2700,false)!=2700)throw new Exception("SMC limit failed");
        if(Parse(c.Serialize()).Rpms[2]!=3200)throw new Exception("Settings roundtrip failed");
        c.Temps[2]=70;bool rejected=false;try{c.Validate();}catch(ArgumentException){rejected=true;}if(!rejected)throw new Exception("Invalid curve accepted");
    }
}
