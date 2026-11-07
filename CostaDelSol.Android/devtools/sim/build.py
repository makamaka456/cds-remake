import re,subprocess,os,glob,sys
HERE=os.path.dirname(os.path.abspath(__file__)); T=os.environ.get('TOOLS') or os.path.expanduser('~/cds-tools'); R=os.environ.get('REPO') or subprocess.run(['git','rev-parse','--show-toplevel'],capture_output=True,text=True).stdout.strip(); A=R+'/CostaDelSol.Android'; OUT=os.environ.get('SIMOUT') or T+'/sim'; os.makedirs(OUT,exist_ok=True)
x=open(A+'/CostaDelSol.Android.csproj',encoding='utf-8').read()
files=sorted(set([os.path.normpath(os.path.join(A,f.replace('\\','/'))) for f in re.findall(r'<Compile Include="([^"]+)"',x)]))
files+=[A+'/Map/WorldMapHost.cs',A+'/Map/WorldMapWindow.cs',A+'/Map/WorldMapWindow.Android.cs',A+'/Map/WorldMapWindow.Overlays.cs',A+'/Map/MapLayers.cs',A+'/Town/CityPicView.cs',A+'/Input/GameKey.cs',A+'/Game/PortSupply.cs',HERE+'/src/Fakes.cs',HERE+'/src/SimGraphics.cs',HERE+'/src/WpfDriver.cs',HERE+'/src/Qa.cs',A+'/Engine/WpfShim.cs',A+'/Input/GameKey.cs' if False else A+'/Engine/WpfShim.cs',HERE+'/src/Program.cs',A+'/Tools/ToolPages.cs',A+'/Engine/CityServiceShim.cs',A+'/Engine/AddOnShims.cs',A+'/Engine/OffscreenShims.cs',A+'/Engine/PackShims.cs']+glob.glob(A+'/Wpf/*.cs')
files=[f for f in dict.fromkeys(files) if not f.endswith('EventAnimationPopup.Gulls.cs')]   # 사건 연출은 Fakes 가 글로 남긴다
gu=HERE+'/src/GlobalUsings.cs'
open(gu,'w').write('\n'.join('global using global::%s;'%n for n in ['System','System.Collections.Generic','System.IO','System.Linq','System.Net.Http','System.Threading','System.Threading.Tasks']))
refs=[T+'/pkgs/mvvm/lib/netstandard2.1/CommunityToolkit.Mvvm.dll']+glob.glob(T+'/pkgs/microsoft.netcore.app.ref/ref/net10.0/*.dll')
args=[T+'/dn/dotnet',T+'/pkgs/microsoft.net.compilers.toolset/tasks/netcore/bincore/csc.dll','/nologo','/noconfig','/nostdlib+','/t:exe','/nullable:enable','/langversion:latest',
 '/out:'+OUT+'/sim.dll','/nowarn:CS8618,CS8600,CS8602,CS8603,CS8604,CS8625,CS1998,CS0414,CS0169,CS0649,CS8321,CS0067','/define:ANDROID','/analyzer:'+T+'/pkgs/mvvm/analyzers/dotnet/roslyn4.12/cs/CommunityToolkit.Mvvm.SourceGenerators.dll','/unsafe+']+['/r:'+r for r in refs]+[gu]+files
r=subprocess.run(args,capture_output=True,text=True)
errs=[l for l in r.stdout.splitlines() if 'error' in l]
print('\n'.join(sorted(set(l.split('cds-helper/')[-1][:220] for l in errs))[:60])); print('errors',len(errs))
open(OUT+'/sim.runtimeconfig.json','w').write('{"runtimeOptions":{"tfm":"net10.0","framework":{"name":"Microsoft.NETCore.App","version":"10.0.0"}}}')

import shutil; shutil.copy(T+'/pkgs/mvvm/lib/netstandard2.1/CommunityToolkit.Mvvm.dll', OUT+'/')
