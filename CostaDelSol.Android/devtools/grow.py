# Android csproj 에 모자란 PC 파일을 이름으로 찾아 붙여 가며 타입 검사를 되풀이한다.
import sys,os,re,subprocess,glob
R=os.environ.get('REPO') or subprocess.run(['git','rev-parse','--show-toplevel'],capture_output=True,text=True).stdout.strip(); P=R+'/CostaDelSol.Android/CostaDelSol.Android.csproj'; T=os.path.dirname(os.path.abspath(__file__))
skip=set(sys.argv[1:])
NOLINK={'ShipRegistryDialog.cs','ImageShrinker.cs','HintService.cs','BookService.cs','DiscoveryService.cs','CityService.cs','NumericSpinner.cs','SkillGroupBox.cs','WorldMapWindow.cs','CityPicView.cs','EventAnimationPopup.cs','ConfirmDialog.cs','TalkDialog.cs','NoticeDialog.cs','CountDialog.cs','NumberPadDialog.cs','HintListDialog.cs','GameDialog.cs','TextInputDialog.cs','ShipNameDialog.cs','MoviePlayer.cs','DiscoveryClipPlayer.cs','ChoiceDialog.cs','GameMenu.cs','GameMenuHost.cs','App.xaml.cs','MainWindow.cs','WorldMapHost.cs','MapD3DRenderer.cs','WorldMapWindow.cs','WorldMapHost.cs','WorldMapContent.cs','ChromeTitleBar.cs','LoadingDialog.cs','AddOnHost.cs','AddOnApi.cs','AddOnFrame.cs','LuaSaved.cs','WindowTreeWindow.cs','WorldMapSurface.cs'}
allcs=[f for f in glob.glob(R+'/CdsHelper.*/**/*.cs',recursive=True) if '/obj/' not in f and '/bin/' not in f and '/ViewModels/' not in f and '/CdsHelper.Duel/' not in f and os.path.basename(f) not in NOLINK]
src={f:open(f,encoding='utf-8-sig').read() for f in allcs}
for it in range(60):
    x=open(P,encoding='utf-8').read()
    have=set(f.replace('\\','/') for f in re.findall(r'<Compile Include="\.\.\\([^"]+)"',x))
    out=subprocess.run(['python3','-I',T+'/check.py',P],capture_output=True,text=True,env={**os.environ,'MAXERR':'100000'}).stdout
    names=set(re.findall(r"error CS0246: The type or namespace name '(\w+)'",out))|set(re.findall(r"error CS0103: The name '(\w+)' does not exist",out))
    names|=set(re.findall(r"error CS0234: The type or namespace name '(\w+)' does not exist in the namespace",out))
    add=[]
    for m in sorted(names-skip):
        c=[os.path.relpath(f,R) for f,s in src.items() if re.search(r'\b(class|struct|record|enum|interface)\s+(struct\s+|class\s+)?%s\b'%m,s)]
        c=[f for f in c if f not in have]
        if c: add.append(c[0]); print('+',m,'->',c[0])
    if not add: break
    anchor='        <Compile Include="..\\CdsHelper.Game\\UI\\Views\\FrameArt.cs" Link="Shared\\CdsHelper.Game\\UI\\Views\\FrameArt.cs" />\n'
    x=x.replace(anchor,anchor+''.join('        <Compile Include="..\\%s" Link="Shared\\%s" />\n'%(f.replace('/','\\'),f.replace('/','\\')) for f in dict.fromkeys(add)))
    open(P,'w',encoding='utf-8').write(x)
print('\n'.join(sorted(set(l.split('cds-helper/')[-1][:230] for l in out.splitlines() if 'error' in l))))
