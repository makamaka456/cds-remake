# 대용품으로 다시 만든 PC 클래스 — 공개 멤버 변화 (f4c2d62 → v1.1.65)
대용품(Engine/DialogShims.cs 등)이 PC 클래스와 같은 이름·인자라야 링크한 코드가 컴파일된다. 아래가 바뀌면 대용품도 맞춘다.

## ChoiceDialog
- 새로: `int Ask(Window owner, string title, IReadOnlyList<string> rows, string? cancel = "취소", int dim = -1, Window? under = null)`
- 없어지거나 바뀜: `int Ask(Window owner, string title, IReadOnlyList<string> rows, string cancel = "취소", int dim = -1)`

## ConfirmDialog
- 새로: `Window Hold(Window owner, string text, string? title = null)`

## GameMenu
- 새로: `prop Ring`
- 새로: `record GameMenuRow(string Text, Action? Run = null, BandStyle? Style = null, bool Dim = false, bool Grey = false)`
- 없어지거나 바뀜: `record GameMenuRow(string Text, Action? Run = null, BandStyle? Style = null, bool Dim = false)`

## GameMenuHost
- 변화 없음

## CountDialog
- 새로: `int NextShare(int now, int max)`

## NoticeDialog
- 새로: `void Notes(System.Windows.Window owner, string text, string title)`

## NumberPadDialog
- 변화 없음

## TalkDialog
- 변화 없음

## HintListDialog
- **v1.1.65 에 파일이 없음**(CdsHelper.Game/UI/Views/HintListDialog.cs) — 이름 바뀜·삭제 확인

## GameDialog
- 변화 없음

## TextInputDialog
- 변화 없음

## ShipNameDialog
- 새로: `string Settle(Window owner, string current)`
- 새로: `string? Ask(Window owner, string current)`
- 새로: `string? Ask(Window owner, string current, IReadOnlyList<string> names, string heading, int maxLength)`
- 없어지거나 바뀜: `string? Ask(Window owner, string current, bool mustName = false)`

## FlowArrows
- 변화 없음

## WeatherView
- 변화 없음

## MapD3DRenderer
- 새로: `(float X, float Y, float DirX, float DirY, float Strength, float Tick)`
- 새로: `byte[] ReadBack(ID3D11Texture2D texture, int width, int height)`
- 새로: `prop CityArtReady`
- 새로: `prop CitySprites`
- 새로: `prop FlowAmount`
- 새로: `prop HiResSea`
- 새로: `prop LandDetail`
- 새로: `prop PixelFilter`
- 새로: `prop SeaBrightness`
- 새로: `prop SeaEffect`
- 새로: `prop SeaTime`
- 새로: `prop ShipBob`
- 새로: `prop ShipFx`
- 새로: `prop SmoothClouds`
- 새로: `prop Software`
- 새로: `prop WaterPaletteReady`
- 새로: `void SetCityArt(uint[] atlas, int atlasW, int atlasH, (int X, int Y, ushort[] Block, int Width)`
- 새로: `void SetCityHiRes(uint[] atlas, int atlasW, int atlasH, float[] info, int count)`
- 새로: `void SetCitySprites(bool on)`
- 새로: `void SetHiddenCities(IEnumerable<int> hidden)`
- 새로: `void SetRoute(ReadOnlySpan<RouteDraw> points, float width = 1, float opacity = 1)`
- 새로: `void SetSeaDepth(byte[] depth)`
- 새로: `void SetShipHi(uint[]? premultiplied, int side)`
- 새로: `void SetTileKinds(byte[] kinds)`
- 새로: `void SetWake(ReadOnlySpan<(float X, float Y, float Age)`
- 새로: `void SetWaterPalette(byte[] water)`
- 새로: `void UpdateWorld(byte[] world)`
- 없어지거나 바뀜: `void SetRoute(ReadOnlySpan<RouteDraw> points)`

## GameShipReader
- 변화 없음
