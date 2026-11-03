using Android.App;
using Android.Widget;
namespace CostaDelSol.Droid.Input;

/// <summary>
/// 대항해시대3(PC 판)이 쓰는 키보드 키. 패드 버튼·화면 단추·가상 키보드가 이 가운데 하나를 "누른다" —
/// 에뮬레이터처럼 게임은 늘 키보드 키를 받는다.
/// </summary>
public enum GameKey
{
    Escape, Enter, Space,
    Up, Down, Left, Right,
    D0, D1, D2, D3, D4, D5, D6, D7, D8, D9,
    V, D, W, C,
    PageUp, PageDown, F5, Backspace,
    Plus, Minus,

    // ── 나머지 자판(일반 컴퓨터 키보드). 위의 이름은 저장해 둔 매핑과 맞물려 있으니 바꾸지 않고 뒤에 잇는다. ──
    A, B, E, F, G, H, I, J, K, L, M, N, O, P, Q, R, S, T, U, X, Y, Z,
    F1, F2, F3, F4, F6, F7, F8, F9, F10, F11, F12,
    Tab, CapsLock, Shift, Ctrl, Alt,
    Insert, Delete, Home, End,
    Grave, BracketLeft, BracketRight, Backslash, Semicolon, Apostrophe, Comma, Period, Slash,
}

/// <summary>키 이름과 원본에서의 쓰임 — 매핑 화면과 「대항해시대3 단축키」 창이 같이 쓴다.</summary>
public static class GameKeys
{
    public static string Name(GameKey key) => key switch
    {
        GameKey.Escape => "ESC",
        GameKey.Enter => "엔터",
        GameKey.Space => "스페이스",
        GameKey.Up => "↑",
        GameKey.Down => "↓",
        GameKey.Left => "←",
        GameKey.Right => "→",
        >= GameKey.D0 and <= GameKey.D9 => ((int)key - (int)GameKey.D0).ToString(),
        GameKey.PageUp => "PgUp",
        GameKey.PageDown => "PgDn",
        GameKey.Backspace => "지우기",
        GameKey.Plus => "=",
        GameKey.Minus => "-",
        GameKey.CapsLock => "Caps",
        GameKey.Insert => "Ins",
        GameKey.Delete => "Del",
        GameKey.Grave => "`",
        GameKey.BracketLeft => "[",
        GameKey.BracketRight => "]",
        GameKey.Backslash => "\\",
        GameKey.Semicolon => ";",
        GameKey.Apostrophe => "'",
        GameKey.Comma => ",",
        GameKey.Period => ".",
        GameKey.Slash => "/",
        _ => key.ToString(),
    };

    /// <summary>원본(PC 판)에서 그 키가 하는 일. 이 판에 아직 없는 쓰임도 원본 그대로 적는다.</summary>
    public static string Use(GameKey key) => key switch
    {
        GameKey.Escape => "취소 · 창 닫기",
        GameKey.Enter => "결정",
        GameKey.Space => "결정 · 여럿 고르기 · 일기토 전환 · 낚시 줄 놓기 · 미궁 되돌리기",
        GameKey.Up or GameKey.Down => "목록 이동 · 도시 건물 고르기",
        GameKey.Left or GameKey.Right => "목록 이동 · 도시 건물 고르기 · 낚시 · 포커",
        GameKey.D0 => "커맨드 창(바다 · 도시)",
        GameKey.D5 => "닻 내리기/올리기",
        GameKey.D1 => "숫자판 조타 남서 · 일기토/교역소 첫째",
        GameKey.D2 => "숫자판 조타 남 · 일기토/교역소 둘째",
        GameKey.D3 => "숫자판 조타 남동 · 일기토/교역소 셋째",
        GameKey.D4 => "숫자판 조타 서",
        GameKey.D6 => "숫자판 조타 동",
        GameKey.D7 => "숫자판 조타 북서",
        GameKey.D8 => "숫자판 조타 북",
        GameKey.D9 => "숫자판 조타 북동",
        GameKey.V => "저장",
        GameKey.D => "발견물 지도",
        GameKey.W => "발견물 지도에서 바람 화살표",
        GameKey.C => "발견물 지도에서 해류 화살표",
        GameKey.PageUp => "해전 전황(제독·함대) · 미궁 위층 · 연대기 앞쪽",
        GameKey.PageDown => "해전 선박 정보 · 미궁 아래층 · 연대기 뒤쪽",
        GameKey.F5 => "교역소 다시 읽기",
        GameKey.Backspace => "숫자 입력 지우기",
        GameKey.Plus => "발견물 지도 확대",
        GameKey.Minus => "발견물 지도 축소",
        _ => "",
    };

    /// <summary>「대항해시대3 단축키」 창의 줄들 — 원본 조작을 화면별로.</summary>
    public static readonly (string Group, (string Keys, string Use)[] Rows)[] Shortcuts =
    [
        ("바다", [
            ("숫자판 1~9", "그 방향으로 뱃머리를 돌린다 (8 북 · 2 남 · 4 서 · 6 동)"),
            ("5", "닻 내리기/올리기"),
            ("0", "커맨드 창"),
            ("마우스 커서", "커서 쪽으로 뱃머리를 돌린다"),
            ("왼쪽 클릭", "닻 내리기/올리기"),
            ("오른쪽 클릭", "커맨드 창"),
            ("ESC", "열린 커맨드·정보 창 닫기"),
            ("V", "저장 (앱 추가)"),
            ("D", "발견물 지도 (앱 추가)"),
            ("휠", "지도 확대·축소 (앱 추가)"),
        ]),
        ("도시", [
            ("방향키", "건물을 차례로 고른다"),
            ("엔터 · 스페이스", "고른 건물에 들어갈지 묻는다"),
            ("0 · 오른쪽 클릭", "도시 커맨드 창"),
            ("ESC", "열린 창 닫기 (도시를 나가는 것은 항구의 출항뿐)"),
        ]),
        ("대화상자 · 목록", [
            ("↑ ↓ (← →)", "줄 옮기기"),
            ("엔터 · 스페이스", "결정"),
            ("ESC · 오른쪽 클릭", "취소 · 닫기"),
            ("스페이스", "여럿 고르는 목록에서 고르기/풀기"),
            ("숫자 · 지우기", "숫자 입력 창"),
        ]),
        ("미니게임 · 특수 화면", [
            ("1 · 2 · 3, 스페이스", "일기토"),
            ("← →, ↓ · 엔터 · 스페이스", "낚시 (조종, 줄 놓기)"),
            ("← →, 엔터 · 스페이스", "포커"),
            ("방향키, PgUp · PgDn, 스페이스", "미궁 (이동, 층, 되돌리기)"),
            ("PgUp · PgDn", "해전 전황 · 선박 정보"),
            ("1~3, 엔터, ESC, F5", "교역소"),
            ("← →, PgUp · PgDn, ESC", "연대기 · 백과사전 쪽 넘기기"),
        ]),
        ("발견물 지도 (앱 추가)", [
            ("방향키", "지도 옮기기"),
            ("＋ · －", "확대 · 축소"),
            ("W · C", "바람 · 해류 화살표"),
        ]),
    ];
}
