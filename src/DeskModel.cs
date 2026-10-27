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
    public static void Load() {
        Directory.CreateDirectory(Root);Directory.CreateDirectory(Path.Combine(Root,"logs"));
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
