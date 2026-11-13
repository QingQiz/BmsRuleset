"""Execute pinned upstream window implementations in isolated build directories.

No reference checkout is built or modified. The generated fixture is consumed by
NUnit without needing Java or upstream repositories at test time.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import re

parser = argparse.ArgumentParser()
parser.add_argument('--beatoraja', type=Path, required=True)
parser.add_argument('--osu', type=Path, required=True)
parser.add_argument('--work', type=Path, required=True)
parser.add_argument('--open2jam', type=Path)
parser.add_argument('--etterna-header', type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
fixtures = root / 'RhythmEngine.Tests' / 'Reference'
fixtures.mkdir(exist_ok=True)
args.work.mkdir(parents=True, exist_ok=True)
manifest_path = fixtures / 'source-manifest.json'
manifest = [entry for entry in json.loads(manifest_path.read_text(encoding='utf8')) if 'fixture' in entry] if manifest_path.exists() else []


def copy_source(source, destination, repository):
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, destination)
    manifest.append({'repository': repository, 'path': str(source.relative_to(repository)),
                     'revision': subprocess.check_output(['git', '-C', str(repository), 'rev-parse', 'HEAD'], text=True).strip(),
                     'sha256': hashlib.sha256(source.read_bytes()).hexdigest()})


java_root = args.work / 'beatoraja'
java_root.mkdir(exist_ok=True)
java_source = args.beatoraja / 'src/bms/player/beatoraja/play/JudgeProperty.java'
copy_source(java_source, java_root / 'bms/player/beatoraja/play/JudgeProperty.java', args.beatoraja)
java_runner = r'''
import bms.player.beatoraja.play.JudgeProperty;
public class WindowReference {
 public static void main(String[] args) {
  System.out.print("["); boolean comma=false;
  for(int rank: new int[]{25,50,75,100,125}) {
   var window=new JudgeProperty.JudgeWindow(JudgeProperty.SEVENKEYS,rank,new int[]{100,100,100});
   for(var type: JudgeProperty.NoteType.values()) {
    if(comma) System.out.print(","); comma=true;
    System.out.print("{\"rank\":"+rank+",\"type\":\""+type+"\",\"windows\":[");
    int count=(type==JudgeProperty.NoteType.NOTE || type==JudgeProperty.NoteType.SCRATCH)?5:4;
    for(int i=0;i<count;i++) {
     if(i>0) System.out.print(",");
     System.out.print("["+(-window.getTime(type,i,true))+","+(-window.getTime(type,i,false))+"]");
    }
    System.out.print("]}");
   }
  }
  System.out.print("]");
 }
}
'''
(java_root / 'WindowReference.java').write_text(java_runner, encoding='utf8')
subprocess.run(['javac', '-d', str(java_root), str(java_root/'bms/player/beatoraja/play/JudgeProperty.java'), str(java_root/'WindowReference.java')], check=True)
java_output = subprocess.check_output(['java', '-cp', str(java_root), 'WindowReference'], text=True)
(fixtures/'beatoraja-windows.json').write_text(json.dumps(json.loads(java_output), indent=2)+'\n', encoding='utf8')

gauge_path=args.beatoraja/'src/bms/player/beatoraja/play/GaugeProperty.java'
copy_source(gauge_path,java_root/'bms/player/beatoraja/play/GaugeProperty.java',args.beatoraja)
groove_path=args.beatoraja/'src/bms/player/beatoraja/play/GrooveGauge.java'
copy_source(groove_path,java_root/'GrooveGauge.source.txt',args.beatoraja)
groove=groove_path.read_text(encoding='utf8')
start=groove.index('public static final class Gauge')
gauge_classes=groove[start:groove.rfind('}')]
(java_root/'bms/player/beatoraja/play/GrooveGauge.java').write_text('package bms.player.beatoraja.play; import bms.model.BMSModel; import com.badlogic.gdx.math.MathUtils; import bms.player.beatoraja.play.GaugeProperty.GaugeElementProperty; public class GrooveGauge { public enum ClearType {REFERENCE} '+gauge_classes+'}',encoding='utf8')
math_path=java_root/'com/badlogic/gdx/math/MathUtils.java';math_path.parent.mkdir(parents=True,exist_ok=True)
math_path.write_text('package com.badlogic.gdx.math; public class MathUtils {public static float clamp(float v,float min,float max){if(v<min)return min;if(v>max)return max;return v;}}',encoding='utf8')
model_root=java_root/'bms/model';model_root.mkdir(parents=True,exist_ok=True)
(model_root/'BMSModel.java').write_text('package bms.model; public record BMSModel(double total,int count){public double getTotal(){return total;} public int getTotalNotes(){return count;}}',encoding='utf8')
(java_root/'GaugeReference.java').write_text(r'''
import bms.model.BMSModel;
import bms.player.beatoraja.play.GaugeProperty;
public class GaugeReference {
 public static void main(String[]args){
  var rows=new java.util.ArrayList<String>();
  for(boolean lr2:new boolean[]{false,true})for(int index:new int[]{0,1,2,3,4,5,6,7,8})for(double total:new double[]{130,260})for(int count:new int[]{2,100,1000}){
   var p=(lr2?GaugeProperty.LR2:GaugeProperty.SEVENKEYS).values[index];
   var model=new BMSModel(total,count);var changes=new java.util.ArrayList<String>();
   for(float value:p.value) changes.add(Float.toString(p.modifier==null?value:p.modifier.modify(value,model)));
   rows.add("{\"lr2\":"+lr2+",\"gauge\":"+index+",\"total\":"+total+",\"count\":"+count+",\"minimum\":"+p.min+",\"initial\":"+p.init+",\"border\":"+p.border+",\"changes\":["+String.join(",",changes)+"]}");
  }
  System.out.print("["+String.join(",",rows)+"]");
 }
}
''',encoding='utf8')
subprocess.run(['javac','-d',str(java_root),str(math_path),str(model_root/'BMSModel.java'),str(java_root/'bms/player/beatoraja/play/GrooveGauge.java'),str(java_root/'bms/player/beatoraja/play/GaugeProperty.java'),str(java_root/'GaugeReference.java')],check=True)
gauge_output=subprocess.check_output(['java','-cp',str(java_root),'GaugeReference'],text=True)
(fixtures/'beatoraja-gauges.json').write_text(json.dumps(json.loads(gauge_output),indent=2)+'\n',encoding='utf8')

(java_root/'GaugeFlowReference.java').write_text(r'''
import bms.model.BMSModel;
import bms.player.beatoraja.play.GaugeProperty;
import bms.player.beatoraja.play.GrooveGauge;
public class GaugeFlowReference {
 public static void main(String[]args){
  var rows=new java.util.ArrayList<String>();
  for(int index=0;index<9;index++)for(double total:new double[]{130,260}){
   var gauge=new GrooveGauge.Gauge(new BMSModel(total,1000),GaugeProperty.SEVENKEYS.values[index],null);
   var states=new java.util.ArrayList<String>();
   for(int step=0;step<120;step++){
    int judge=step<30?step%3:step<65?3+step%3:step%2;
    float rate=(step%5==0&&(judge==1||judge==3))?0.5f:1;
    gauge.update(judge,rate);
    states.add("{\"judge\":"+judge+",\"rate\":"+rate+",\"value\":"+Double.toString(gauge.getValue())+",\"qualified\":"+gauge.isQualified()+"}");
   }
   rows.add("{\"gauge\":"+index+",\"total\":"+total+",\"count\":1000,\"states\":["+String.join(",",states)+"]}");
  }
  System.out.print("["+String.join(",",rows)+"]");
 }
}
''',encoding='utf8')
subprocess.run(['javac','-cp',str(java_root),'-d',str(java_root),str(java_root/'GaugeFlowReference.java')],check=True)
gauge_flow=subprocess.check_output(['java','-cp',str(java_root),'GaugeFlowReference'],text=True)
(fixtures/'beatoraja-gauge-flow.json').write_text(json.dumps(json.loads(gauge_flow),indent=2)+'\n',encoding='utf8')

mania_root = args.work / 'mania'
mania_root.mkdir(exist_ok=True)
copy_source(args.osu/'osu.Game.Rulesets.Mania/Scoring/ManiaHitWindows.cs', mania_root/'ManiaHitWindows.cs', args.osu)
score_source=args.osu/'osu.Game.Rulesets.Mania/Scoring/ManiaScoreProcessor.cs'
copy_source(score_source,mania_root/'ManiaScoreProcessor.source.txt',args.osu)
score_text=score_source.read_text(encoding='utf8')


def method(source, declaration):
    begin=source.index(declaration);opening=source.index('{',begin);depth=1;end=opening+1
    while depth:
        depth += (source[end]=='{')-(source[end]=='}');end+=1
    return source[begin:end]


methods='\n'.join(method(score_text,name) for name in ['protected override double ComputeTotalScore(', 'protected override double GetComboScoreChange(',
                                                       'public override int GetBaseScoreForResult(', 'private int getBaseComboScoreForResult('])
(mania_root/'ScoreOracle.cs').write_text(r'''
using System.Text.Json;
using osu.Game.Rulesets.Scoring;
public class Number { public double Value; }
public class JudgementResult { public HitResult Type; public int ComboAfterJudgement; }
public class ScoreBase {
 public Number Accuracy=new();
 public virtual int GetBaseScoreForResult(HitResult r) => r switch {HitResult.Perfect=>300,HitResult.Great=>300,HitResult.Good=>200,HitResult.Ok=>100,HitResult.Meh=>50,_=>0};
 protected virtual double ComputeTotalScore(double a,double b,double c)=>0;
 protected virtual double GetComboScoreChange(JudgementResult r)=>0;
}
public class ManiaScoreReference:ScoreBase {
 const double combo_base=4;
 METHODS
 public double Combo(HitResult r,int c)=>GetComboScoreChange(new JudgementResult {Type=r,ComboAfterJudgement=c});
 public double Total(double combo,double progress)=>ComputeTotalScore(combo,progress,0);
}
public static class ScoreOracle {
 public static void Generate(){
  HitResult[] sequence=[HitResult.Perfect,HitResult.Great,HitResult.Good,HitResult.Ok,HitResult.Meh,HitResult.Miss,HitResult.Perfect,HitResult.Perfect];
  var r=new ManiaScoreReference();var rows=new List<object>();double comboPortion=0,points=0;int combo=0;
  var maximum=Enumerable.Range(1,sequence.Length).Sum(i=>r.Combo(HitResult.Perfect,i));
  for(int i=0;i<sequence.Length;i++){
   var grade=sequence[i];combo=grade==HitResult.Miss?0:combo+1;points+=r.GetBaseScoreForResult(grade);comboPortion+=r.Combo(grade,combo);
   r.Accuracy.Value=points/((i+1)*305);
   rows.Add(new {result=grade.ToString().ToLowerInvariant(),score=Math.Round(r.Total(comboPortion/maximum,(i+1.0)/sequence.Length)),accuracy=r.Accuracy.Value,combo});
  }
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"lazer-score.json"),JsonSerializer.Serialize(rows));
 }
}
'''.replace('METHODS',methods),encoding='utf8')
(mania_root/'Directory.Build.props').write_text('<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings></PropertyGroup></Project>', encoding='utf8')
(mania_root/'Reference.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup></Project>', encoding='utf8')
# Only the upstream class's framework-facing types are replaced. Window logic is
# compiled unmodified from the pinned source; the shim implements DifficultyRange.
(mania_root/'FrameworkShim.cs').write_text(r'''
namespace osu.Game.Beatmaps {
 public record DifficultyRange(double Low,double Mid,double High);
 public interface IBeatmapDifficultyInfo {
  public static double DifficultyRange(double d,DifficultyRange r) => d>5?r.Mid+(r.High-r.Mid)*(d-5)/5:r.Mid+(r.Mid-r.Low)*(d-5)/5;
 }
}
namespace osu.Game.Rulesets.Scoring {
 public enum HitResult { Miss,Meh,Ok,Good,Great,Perfect }
 public abstract class HitWindows {
  public abstract bool IsHitResultAllowed(HitResult r);
  public abstract void SetDifficulty(double d);
  public abstract double WindowFor(HitResult r);
 }
}
''', encoding='utf8')
(mania_root/'Program.cs').write_text(r'''
using System.Text.Json;
using osu.Game.Rulesets.Mania.Scoring;
using osu.Game.Rulesets.Scoring;
var rows=new List<object>();
ScoreOracle.Generate();
foreach(var classic in new[]{false,true})
foreach(var convert in new[]{false,true})
foreach(var od in new[]{0.0,4.0,4.5,5.0,8.0,10.0})
foreach(var rate in new[]{0.75,1.0,1.5})
foreach(var difficulty in new[]{1.0,1.4}) {
 var w=new ManiaHitWindows { ClassicModActive=classic,IsConvert=convert,SpeedMultiplier=rate,DifficultyMultiplier=difficulty };
 w.SetDifficulty(od);
 rows.Add(new {classic,convert,od,rate,difficulty,windows=new[]{HitResult.Perfect,HitResult.Great,HitResult.Good,HitResult.Ok,HitResult.Meh,HitResult.Miss}.Select(w.WindowFor).ToArray()});
}
Console.WriteLine(JsonSerializer.Serialize(rows));
''', encoding='utf8')
subprocess.run(['dotnet','build',str(mania_root/'Reference.csproj'),'-c','Release','--nologo'], check=True, stdout=subprocess.DEVNULL)
mania_output=subprocess.check_output(['dotnet',str(mania_root/'bin/Release/net10.0/Reference.dll')],text=True)
(fixtures/'mania-windows.json').write_text(json.dumps(json.loads(mania_output),indent=2)+'\n',encoding='utf8')
(fixtures/'lazer-score.json').write_text(json.dumps(json.loads((mania_root/'bin/Release/net10.0/lazer-score.json').read_text()),indent=2)+'\n',encoding='utf8')
(fixtures/'source-manifest.json').write_text(json.dumps(manifest,indent=2,default=str)+'\n',encoding='utf8')
print('Generated 20 beatoraja tables and',len(json.loads(mania_output)),'mania tables from upstream source.')

if args.open2jam:
    o2root=args.work/'open2jam'
    o2root.mkdir(exist_ok=True)
    render=args.open2jam/'src/org/open2jam/render/Render.java'
    copy_source(render,o2root/'Render.source.txt',args.open2jam)
    source=render.read_text(encoding='utf8')
    start=source.index('public JudgmentResult handleJudgment(')
    end=source.index('/* play a sample */',start)
    method=source[start:end]
    runner=r'''
import java.util.*;
public class O2Reference {
 enum JudgmentResult {COOL,GOOD,BAD,MISS}
 static class Counter {
  int number,limit=Integer.MAX_VALUE;
  int getNumber(){return number;} int getLimit(){return limit;}
  void setNumber(int n){number=Math.max(0,Math.min(limit,n));}
  void addNumber(int n){setNumber(number+n);} void subtractNumber(int n){setNumber(number-n);}
  void incNumber(){addNumber(1);} void resetNumber(){number=0;}
 }
 static class Entity { Entity copy(){return new Entity();} void setDead(boolean b){} }
 static class Skin { Map<String,Entity> getEntityMap(){var m=new HashMap<String,Entity>();for(int i=1;i<=5;i++)m.put("PILL_"+i,new Entity());return m;} }
 Counter jambar_entity=new Counter(),jamcombo_entity=new Counter(),lifebar_entity=new Counter(),score_entity=new Counter(),combo_entity=new Counter(),maxcombo_entity=new Counter();
 LinkedList<Entity> pills_draw=new LinkedList<>(); List<Entity> entities_matrix=new ArrayList<>(); Skin skin=new Skin();
 int consecutive_cools,rank=1;
 O2Reference(){jambar_entity.limit=50;lifebar_entity.limit=36000;lifebar_entity.number=36000;}
 METHOD
 public static void main(String[] args){
  var r=new O2Reference();var rows=new ArrayList<String>();
  for(int i=0;i<220;i++){
   var input=i<100?JudgmentResult.COOL:i%11==0?JudgmentResult.MISS:i%5==0?JudgmentResult.BAD:i%3==0?JudgmentResult.GOOD:JudgmentResult.COOL;
   var result=r.handleJudgment(input);
   if(result==JudgmentResult.COOL||result==JudgmentResult.GOOD)r.combo_entity.incNumber();else r.combo_entity.resetNumber();
   rows.add("{\"input\":\""+input+"\",\"result\":\""+result+"\",\"score\":"+r.score_entity.number+",\"jam\":"+r.jamcombo_entity.number+",\"progress\":"+r.jambar_entity.number+",\"pills\":"+r.pills_draw.size()+",\"streak\":"+r.consecutive_cools+",\"life\":"+r.lifebar_entity.number+",\"combo\":"+r.combo_entity.number+"}");
  }
  System.out.print("["+String.join(",",rows)+"]");
 }
}
'''.replace('METHOD',method)
    (o2root/'O2Reference.java').write_text(runner,encoding='utf8')
    subprocess.run(['javac','-d',str(o2root),str(o2root/'O2Reference.java')],check=True)
    output=subprocess.check_output(['java','-cp',str(o2root),'O2Reference'],text=True)
    (fixtures/'open2jam-scoring.json').write_text(json.dumps(json.loads(output),indent=2)+'\n',encoding='utf8')
    (fixtures/'source-manifest.json').write_text(json.dumps(manifest,indent=2,default=str)+'\n',encoding='utf8')
    print('Generated 220 scoring transitions by executing Open2Jam Render.handleJudgment.')

if args.etterna_header:
    native_root=(args.work/'etterna').resolve();native_root.mkdir(exist_ok=True)
    header=args.etterna_header.read_text(encoding='utf8')
    start=header.index('inline auto\nwerwerwerwerf')
    end=header.index('inline void\nwrap',start)
    functions=header[start:end]
    weights='\n'.join(re.findall(r'static const float wife3_\w+ = [^;]+;',header))
    (native_root/'WifeReference.cpp').write_text('#include <cmath>\n#include <iostream>\n#include <iomanip>\n'+weights+'\n'+functions+r'''
int main(){
 std::cout << std::setprecision(10) << "["; bool comma=false;
 for(float scale: {0.2F,0.33F,0.5F,0.66F,0.84F,1.F,1.33F,1.5F})
 for(float ms: {0.F,0.001F,4.999F,5.F,5.001F,10.F,16.666F,22.5F,45.F,64.999F,65.F,65.001F,80.F,90.F,120.F,134.999F,135.F,150.F,179.999F,180.F,200.F}){
  if(comma)std::cout << ",";comma=true;
  std::cout << "{\"scale\":" << scale << ",\"offset\":" << ms << ",\"points\":" << wife3(ms/1000.F,scale) << "}";
 }
 std::cout << "]";
}
''',encoding='utf8')
    vswhere=Path('C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe')
    installation=subprocess.check_output([str(vswhere),'-latest','-products','*','-property','installationPath'],text=True).strip()
    devcmd=Path(installation)/'Common7/Tools/VsDevCmd.bat'
    (native_root/'build.cmd').write_text(f'@echo off\ncall "{devcmd}" -arch=x64 -host_arch=x64 >nul\nif errorlevel 1 exit /b 1\ncl /nologo /std:c++20 /EHsc WifeReference.cpp /Fe:WifeReference.exe\n',encoding='utf8')
    subprocess.run(['cmd.exe','/d','/c','build.cmd'],cwd=native_root,check=True,stdout=subprocess.DEVNULL)
    output=subprocess.check_output([str(native_root/'WifeReference.exe')],text=True)
    (fixtures/'etterna-wife3.json').write_text(json.dumps(json.loads(output),indent=2)+'\n',encoding='utf8')
    manifest.append({'repository':'https://github.com/etternagame/etterna','path':'src/RageUtil/Utils/RageUtil.h',
                     'revision':'2d706147cc4bcf95a8ceb24dc4f4129be207f803','sha256':hashlib.sha256(args.etterna_header.read_bytes()).hexdigest()})
    (fixtures/'source-manifest.json').write_text(json.dumps(manifest,indent=2,default=str)+'\n',encoding='utf8')
    print('Generated 168 Wife3 samples by compiling the original C++ functions.')
