"""Compile the pinned original LifeMeterBar float accumulation method in isolation."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--source', type=Path, required=True)
parser.add_argument('--work', type=Path, required=True)
parser.add_argument('--output', type=Path, default=Path(__file__).resolve().parents[1] / 'RhythmEngine.Tests/Reference/etterna-life.json')
args = parser.parse_args()
source_digest = hashlib.sha256(args.source.read_bytes()).hexdigest()
if source_digest != '1a3be12bcbdb0629519eaca7ade83137e6eadeeead8c710e70c9894b0df89088':
    raise ValueError('This harness only admits the pinned Etterna LifeMeterBar source.')
source = args.source.read_text(encoding='utf8')
begin = source.index('void\nLifeMeterBar::ChangeLife(float fDeltaLife)')
end = source.index('\nvoid\nLifeMeterBar::SetLife', begin)
method = source[begin:end]
work = args.work.resolve()
work.mkdir(parents=True, exist_ok=True)
program = r'''
#include <algorithm>
#include <iostream>
#include <iomanip>
#include <vector>
enum DrainType { DrainType_Normal, DrainType_NoRecover, DrainType_SuddenDeath };
struct Options { DrainType m_DrainType=DrainType_Normal; };
struct PlayerOptions { Options value; Options& GetSong(){return value;} };
struct PlayerState { PlayerOptions m_PlayerOptions; };
struct StageStats { bool m_bFailed=false; };
const float LIFE_MULTIPLIER=1;
#define CLAMP(v,lo,hi) v=std::clamp(v,float(lo),float(hi))
struct LifeMeterBar {
 PlayerState player;
 StageStats stats;
 PlayerState* m_pPlayerState=&player;
 StageStats* m_pPlayerStageStats=&stats;
 float m_fLifePercentage=.5f;
 float m_fLifeDifficulty=1;
 void AfterLifeChanged(){}
 void ChangeLife(float fDeltaLife);
};
'''+method+r'''
int main(){
 std::cout << std::setprecision(17) << "[";bool comma=false;
 const float changes[]={.008f,.008f,.004f,0,-.04f,-.08f,-.16f,.008f,-.08f,0};
 for(float difficulty:{.5f,1.f,2.f}){
  LifeMeterBar life;life.m_fLifeDifficulty=difficulty;
  for(int step=0;step<130;step++){
   int result=step<30?step%3:step<50?3+step%4:step%10;
   life.ChangeLife(changes[result]);
   if(life.m_fLifePercentage<=0)life.stats.m_bFailed=true;
   if(comma)std::cout<<",";comma=true;
   std::cout<<"{\"difficulty\":"<<difficulty<<",\"index\":"<<step<<",\"result\":"<<result<<",\"hp\":"<<double(life.m_fLifePercentage)<<",\"failed\":"<<(life.stats.m_bFailed?"true":"false")<<"}";
  }
 }
 std::cout<<"]";
}
'''
(work/'LifeReference.cpp').write_text(program,encoding='utf8')
vswhere=Path('C:/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe')
installation=subprocess.check_output([str(vswhere),'-latest','-products','*','-property','installationPath'],text=True).strip()
devcmd=Path(installation)/'Common7/Tools/VsDevCmd.bat'
(work/'build.cmd').write_text(f'@echo off\ncall "{devcmd}" -arch=x64 -host_arch=x64 >nul\nif errorlevel 1 exit /b 1\ncl /nologo /std:c++20 /EHsc /fp:precise LifeReference.cpp /Fe:LifeReference.exe\n',encoding='utf8')
subprocess.run(['cmd.exe','/d','/c','build.cmd'],cwd=work,check=True,stdout=subprocess.DEVNULL)
rows=json.loads(subprocess.check_output([str(work/'LifeReference.exe')],text=True))
args.output.write_text(json.dumps({'revision':'2d706147cc4bcf95a8ceb24dc4f4129be207f803','source_sha256':source_digest,
                                 'authority':'compiled original LifeMeterBar::ChangeLife(float), fallback metric coefficients; frame/notification callbacks excluded',
                                 'states':rows},indent=2)+'\n',encoding='utf8')
print(f'Generated {len(rows)} original Etterna life states.')
