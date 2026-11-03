# Android 프로젝트를 csc 로 타입 검사한다 (실제 APK 빌드는 CI).
import sys, re, subprocess, os, glob
T=os.environ.get('TOOLS') or os.path.expanduser('~/cds-tools')   # setup-toolchain.sh 가 꾸린 폴더
proj=sys.argv[1]; pdir=os.path.dirname(os.path.abspath(proj))
xml=open(proj,encoding='utf-8-sig').read()
files=[os.path.join(pdir,f) for f in glob.glob('**/*.cs',root_dir=pdir,recursive=True) if not f.startswith(('obj/','bin/','devtools/'))]
for inc in re.findall(r'<Compile Include="([^"]+)"',xml):
    files.append(os.path.normpath(os.path.join(pdir,inc.replace('\\','/'))))
glob_using=os.path.join(T,'GlobalUsings.g.cs')
open(glob_using,'w').write('\n'.join('global using global::%s;'%n for n in
  ['System','System.Collections.Generic','System.IO','System.Linq','System.Net.Http','System.Threading','System.Threading.Tasks',
   ]+([] if 'Using Remove="Android.Widget"' in xml else ['Android.App','Android.Widget']))+'\nglobal using Bundle = global::Android.OS.Bundle;\n')
refs=[T+'/pkgs/mvvm/lib/netstandard2.1/CommunityToolkit.Mvvm.dll']+glob.glob(T+'/pkgs/microsoft.netcore.app.ref/ref/net10.0/*.dll')+glob.glob(T+'/pkgs/microsoft.android.ref.36/ref/net10.0/*.dll')
unsafe='/unsafe+' if 'AllowUnsafeBlocks' in xml else ''
args=[T+'/dn/dotnet',T+'/pkgs/microsoft.net.compilers.toolset/tasks/netcore/bincore/csc.dll','/nologo','/noconfig','/nostdlib+','/t:library',
      '/nullable:enable','/langversion:latest','/out:'+T+'/out.dll','/nowarn:CS8618,CS8600,CS8602,CS8603,CS8604,CS8625,CS8765,CS8767,CS8764,CS8610,CS8619,CS8601,CS8620,CS1998',
      '/define:ANDROID','/analyzer:'+T+'/pkgs/mvvm/analyzers/dotnet/roslyn4.12/cs/CommunityToolkit.Mvvm.SourceGenerators.dll']+([unsafe] if unsafe else [])+['/r:'+r for r in refs]+[glob_using]+files
r=subprocess.run(args,capture_output=True,text=True)
out=[l for l in r.stdout.splitlines() if "error" in l or ": warning" in l]
errs=[l for l in out if "error" in l]
print('\n'.join(out[:int(os.environ.get('MAXERR','150'))])); print('errors:',len(errs),'exit',r.returncode)
