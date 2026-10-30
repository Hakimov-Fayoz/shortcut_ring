// ============================================================================
//  main.cpp (ClipRadialMenu) - менеджер буфера обмена с меню по зажатию кнопки мыши
//  Windows 10/11, только Win32 API + стандартная библиотека C++ (без сторонних либ)
//
//  КАК РАБОТАЕТ
//   * Программа живёт в фоне, без окна, с иконкой в трее.
//   * Зажали триггер (по умолчанию правая кнопка мыши) дольше задержки (150 мс)
//     -> рядом с курсором появляется меню из двух блоков:
//        слева  - превью выбранного элемента (скруглённый квадрат),
//        справа - список "таблеток" (сначала закреплённые с синей точкой,
//                 потом новые сверху).
//   * Курсор при появлении стоит в центре левого блока.
//   * Навели на пункт справа и отпустили триггер -> текст вставляется (Ctrl+V)
//     в активное поле. Отпустили вне пункта или нажали Esc -> ничего не вставится.
//   * Колесо мыши прокручивает список, пока меню открыто.
//   * Короткое нажатие кнопки-триггера (короче задержки) работает как обычно
//     (для правой кнопки - обычное контекстное меню, срабатывает при отпускании).
//   * Правый клик по иконке в трее: Включено / Настройки / Автозапуск / Выход.
//   * В настройках: кнопка-триггер (правая кнопка, Mouse 5, Mouse 4, Ctrl, Alt, Shift), задержка в мс,
//     список закреплённых фраз (добавить, изменить, удалить, порядок).
//
//  СБОРКА
//   MinGW:  g++ -O2 -mwindows main.cpp -o main.exe -luser32 -lgdi32 -lshell32 -ladvapi32
//           (работает и с -municode, и без него)
//   MSVC:   cl /EHsc /O2 /DUNICODE /D_UNICODE main.cpp
//
//  ЗАМЕЧАНИЕ: чтобы меню работало поверх окон, запущенных от администратора,
//  программу нужно тоже запускать от администратора (ограничение Windows).
//  Настройки хранятся в %APPDATA%\ClipRadialMenu\settings.txt (история буфера
//  на диск НЕ сохраняется - хранится только в памяти).
// ============================================================================

#ifndef UNICODE
#define UNICODE
#endif
#ifndef _UNICODE
#define _UNICODE
#endif
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef _WIN32_WINNT
#define _WIN32_WINNT 0x0601
#endif

#include <windows.h>
#include <shellapi.h>

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <fstream>
#include <string>
#include <vector>

#ifdef _MSC_VER
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")
#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "advapi32.lib")
#pragma comment(linker, "/SUBSYSTEM:WINDOWS")
// Подключаем современный вид элементов управления в окне настроек
#pragma comment(linker, "\"/manifestdependency:type='win32' name='Microsoft.Windows.Common-Controls' version='6.0.0.0' processorArchitecture='*' publicKeyToken='6595b64144ccf1df' language='*'\"")
#endif

// ----------------------------------------------------------------------------
//  Константы и типы
// ----------------------------------------------------------------------------

// Метка, которой помечаем СВОИ синтетические события, чтобы хуки их игнорировали
static const ULONG_PTR MAGIC = 0x43524D31;

static const size_t MAX_HISTORY = 16;      // сколько элементов истории хранить
static const wchar_t* APP_NAME = L"ClipRadialMenu";

// Варианты кнопки-триггера
enum Trigger { TRG_MOUSE5 = 0, TRG_CTRL = 1, TRG_ALT = 2, TRG_SHIFT = 3, TRG_MOUSE4 = 4, TRG_RIGHT = 5 };

// Состояния меню
enum MenuState {
    ST_IDLE,       // ничего не происходит
    ST_PENDING,    // триггер зажат, ждём окончания задержки
    ST_ACTIVE,     // меню на экране
    ST_CANCELLED   // меню отменено (Esc / комбинация клавиш), ждём отпускания триггера
};

// Пользовательские сообщения окон
enum {
    WM_TRAY       = WM_APP + 1,   // события иконки в трее
    WM_APP_REDRAW = WM_APP + 2,   // перерисовать меню
    WM_APP_PASTE  = WM_APP + 3    // вставить выбранный текст
};

// Идентификаторы таймеров
enum { T_SHOW = 1, T_PASTE = 2 };

// Идентификаторы команд трея
enum { ID_TOGGLE = 201, ID_SETTINGS = 202, ID_AUTOSTART = 203, ID_EXIT = 204 };

// Идентификаторы элементов окна настроек
enum {
    ID_TRIGGER = 101, ID_DELAY, ID_LIST, ID_TEXT,
    ID_ADD, ID_UPDATE, ID_DEL, ID_UP, ID_DOWN, ID_FROMCLIP
};

struct Config {
    bool enabled = true;                       // программа включена?
    int  delayMs = 150;                        // задержка перед показом меню
    int  trigger = TRG_RIGHT;                  // кнопка-триггер (по умолчанию правая кнопка мыши)
    std::vector<std::wstring> pinned;          // закреплённые фразы
};

struct ViewItem {                              // элемент, показанный в меню
    std::wstring text;
    bool pinned;
    int  w;                                    // ширина "таблетки" (считается при показе)
};

// Геометрия меню (в пикселях, уже с учётом DPI)
struct Layout {
    RECT preview;       // левый блок (квадрат)
    RECT list;          // область списка справа
    int  pillH, pitch;  // высота пункта и шаг между пунктами
    int  rows;          // видимых пунктов
    int  W, H;          // размер окна
};

// ----------------------------------------------------------------------------
//  Глобальные переменные
// ----------------------------------------------------------------------------
static Config g_cfg;
static std::vector<std::wstring> g_history;    // история буфера: [0] - самый новый
static std::vector<ViewItem> g_view;           // снимок списка на момент показа меню
static int  g_pinnedCount = 0;

static MenuState g_state = ST_IDLE;
static HWND  g_hMain = nullptr;                // скрытое главное окно
static HWND  g_hMenu = nullptr;                // окно меню
static HWND  g_hSettings = nullptr;            // окно настроек
static HHOOK g_hMouseHook = nullptr;
static HHOOK g_hKbHook = nullptr;

static int  g_hover = -1;                      // индекс элемента под курсором
static int  g_scroll = 0;                      // первый видимый элемент списка
static int  g_winX = 0, g_winY = 0;            // положение окна меню на экране
static int  g_dpi = 96;
static Layout g_L;
static bool g_redrawQueued = false;

static HFONT g_font = nullptr;                 // шрифт меню
static HFONT g_uiFont = nullptr;               // шрифт окна настроек
static HICON g_hIcon = nullptr;                // иконка трея

static ULONGLONG    g_ignoreClipUntil = 0;     // игнорировать изменения буфера до этого момента
static std::wstring g_pasteText;               // текст, который сейчас вставляем
static UINT g_msgTaskbarCreated = 0;
static UINT g_cfExclude = 0;                   // формат "не записывать в историю"

static std::vector<std::wstring> g_edit;       // рабочая копия закреплённых в окне настроек

// ----------------------------------------------------------------------------
//  Мелкие помощники
// ----------------------------------------------------------------------------

// Масштабирование размера под DPI монитора
static int S(int v) { return MulDiv(v, g_dpi, 96); }

static Layout MakeLayout() {
    Layout L;
    int pad = S(10), gap = S(8);
    int pv = S(240), listW = S(300);
    L.pillH = S(40);
    L.pitch = L.pillH + gap;
    L.rows = 6;
    int listH = L.rows * L.pitch - gap;
    // Список начинается на уровне верхней границы левого блока
    L.preview = { pad, pad, pad + pv, pad + pv };
    L.list = { pad + pv + S(20), pad, pad + pv + S(20) + listW, pad + listH };
    L.W = L.list.right + S(14) + pad;
    L.H = std::max(pv, listH) + pad * 2;
    return L;
}

static std::string ToUtf8(const std::wstring& w) {
    if (w.empty()) return std::string();
    int n = WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), nullptr, 0, nullptr, nullptr);
    std::string s(n, '\0');
    WideCharToMultiByte(CP_UTF8, 0, w.data(), (int)w.size(), &s[0], n, nullptr, nullptr);
    return s;
}

static std::wstring FromUtf8(const std::string& s) {
    if (s.empty()) return std::wstring();
    int n = MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), nullptr, 0);
    std::wstring w(n, L'\0');
    MultiByteToWideChar(CP_UTF8, 0, s.data(), (int)s.size(), &w[0], n);
    return w;
}

// Экранирование переводов строк для хранения одной фразы в одной строке файла
static std::wstring Esc(const std::wstring& s) {
    std::wstring r;
    for (wchar_t c : s) {
        if (c == L'\\') r += L"\\\\";
        else if (c == L'\n') r += L"\\n";
        else if (c == L'\r') r += L"\\r";
        else r += c;
    }
    return r;
}

static std::wstring Unesc(const std::wstring& s) {
    std::wstring r;
    for (size_t i = 0; i < s.size(); i++) {
        if (s[i] == L'\\' && i + 1 < s.size()) {
            wchar_t n = s[++i];
            if (n == L'n') r += L'\n';
            else if (n == L'r') r += L'\r';
            else r += n;
        } else r += s[i];
    }
    return r;
}

// Превращает многострочный текст в одну строку (для списков)
static std::wstring OneLine(const std::wstring& s, size_t maxLen = 200) {
    std::wstring r;
    for (wchar_t c : s) {
        if (r.size() >= maxLen) { r += L'\u2026'; break; }
        if (c == L'\r' || c == L'\n' || c == L'\t') {
            if (!r.empty() && r.back() != L' ') r += L' ';
        } else r += c;
    }
    return r;
}

// Для поля ввода Windows нужны переводы строк \r\n
static std::wstring ToCRLF(const std::wstring& s) {
    std::wstring r;
    for (size_t i = 0; i < s.size(); i++) {
        if (s[i] == L'\n' && (i == 0 || s[i - 1] != L'\r')) r += L"\r\n";
        else r += s[i];
    }
    return r;
}

static std::wstring GetText(HWND h) {
    int n = GetWindowTextLengthW(h);
    std::wstring s(n, L'\0');
    if (n) GetWindowTextW(h, &s[0], n + 1);
    return s;
}

// ----------------------------------------------------------------------------
//  Настройки: загрузка / сохранение (UTF-8, текстовый файл key=value)
// ----------------------------------------------------------------------------
static std::wstring SettingsPath() {
    wchar_t buf[MAX_PATH] = {};
    GetEnvironmentVariableW(L"APPDATA", buf, MAX_PATH);
    std::wstring dir = std::wstring(buf) + L"\\" + APP_NAME;
    CreateDirectoryW(dir.c_str(), nullptr);
    return dir + L"\\settings.txt";
}

static void LoadConfig() {
    std::ifstream f(SettingsPath().c_str(), std::ios::binary);
    if (!f) return;
    std::string line;
    while (std::getline(f, line)) {
        if (!line.empty() && line.back() == '\r') line.pop_back();
        size_t eq = line.find('=');
        if (eq == std::string::npos) continue;
        std::string key = line.substr(0, eq);
        std::wstring val = FromUtf8(line.substr(eq + 1));
        if (key == "delay")        g_cfg.delayMs = std::max(0, std::min(2000, _wtoi(val.c_str())));
        else if (key == "trigger") g_cfg.trigger = std::max(0, std::min(5, _wtoi(val.c_str())));
        else if (key == "enabled") g_cfg.enabled = (val != L"0");
        else if (key == "pinned")  g_cfg.pinned.push_back(Unesc(val));
    }
}

static void SaveConfig() {
    std::ofstream f(SettingsPath().c_str(), std::ios::binary | std::ios::trunc);
    if (!f) return;
    f << "delay=" << g_cfg.delayMs << "\n";
    f << "trigger=" << g_cfg.trigger << "\n";
    f << "enabled=" << (g_cfg.enabled ? 1 : 0) << "\n";
    for (auto& p : g_cfg.pinned) f << "pinned=" << ToUtf8(Esc(p)) << "\n";
}

// ----------------------------------------------------------------------------
//  Автозапуск (реестр HKCU\...\Run)
// ----------------------------------------------------------------------------
static const wchar_t* RUN_KEY = L"Software\\Microsoft\\Windows\\CurrentVersion\\Run";

static bool IsAutostart() {
    HKEY k;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, RUN_KEY, 0, KEY_READ, &k) != ERROR_SUCCESS) return false;
    bool ok = RegQueryValueExW(k, APP_NAME, nullptr, nullptr, nullptr, nullptr) == ERROR_SUCCESS;
    RegCloseKey(k);
    return ok;
}

static void SetAutostart(bool on) {
    HKEY k;
    if (RegOpenKeyExW(HKEY_CURRENT_USER, RUN_KEY, 0, KEY_WRITE, &k) != ERROR_SUCCESS) return;
    if (on) {
        wchar_t path[MAX_PATH] = {};
        GetModuleFileNameW(nullptr, path, MAX_PATH);
        std::wstring v = std::wstring(L"\"") + path + L"\"";
        RegSetValueExW(k, APP_NAME, 0, REG_SZ, (const BYTE*)v.c_str(), (DWORD)((v.size() + 1) * sizeof(wchar_t)));
    } else {
        RegDeleteValueW(k, APP_NAME);
    }
    RegCloseKey(k);
}

// ----------------------------------------------------------------------------
//  Работа с буфером обмена
// ----------------------------------------------------------------------------
static bool GetClipboardText(std::wstring& out) {
    if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return false;
    // Буфер может быть занят другим приложением - пробуем несколько раз
    for (int i = 0; i < 5; i++) {
        if (OpenClipboard(g_hMain)) {
            bool ok = false;
            HANDLE h = GetClipboardData(CF_UNICODETEXT);
            if (h) {
                const wchar_t* p = (const wchar_t*)GlobalLock(h);
                if (p) { out = p; ok = true; GlobalUnlock(h); }
            }
            CloseClipboard();
            return ok;
        }
        Sleep(10);
    }
    return false;
}

static bool SetClipboardText(const std::wstring& s) {
    for (int i = 0; i < 5; i++) {
        if (OpenClipboard(g_hMain)) {
            EmptyClipboard();
            HGLOBAL h = GlobalAlloc(GMEM_MOVEABLE, (s.size() + 1) * sizeof(wchar_t));
            if (h) {
                wchar_t* p = (wchar_t*)GlobalLock(h);
                memcpy(p, s.c_str(), (s.size() + 1) * sizeof(wchar_t));
                GlobalUnlock(h);
                SetClipboardData(CF_UNICODETEXT, h);
            }
            CloseClipboard();
            return h != nullptr;
        }
        Sleep(10);
    }
    return false;
}

// Вызывается при каждом изменении буфера обмена
static void OnClipboardUpdate() {
    if (!g_cfg.enabled) return;
    if (GetTickCount64() < g_ignoreClipUntil) return;        // это мы сами положили текст
    if (g_cfExclude && IsClipboardFormatAvailable(g_cfExclude)) return;  // менеджеры паролей и т.п.
    std::wstring t;
    if (!GetClipboardText(t)) return;
    if (t.empty() || t.size() > 100000) return;
    if (t.find_first_not_of(L" \t\r\n") == std::wstring::npos) return;  // только пробелы
    for (auto& p : g_cfg.pinned) if (p == t) return;         // закреплённое не дублируем
    auto it = std::find(g_history.begin(), g_history.end(), t);
    if (it != g_history.end()) g_history.erase(it);          // дубликат поднимаем наверх
    g_history.insert(g_history.begin(), t);                  // новые - всегда первые
    if (g_history.size() > MAX_HISTORY) g_history.resize(MAX_HISTORY);
}

// ----------------------------------------------------------------------------
//  Синтетический ввод
// ----------------------------------------------------------------------------
// Является ли выбранный триггер боковой кнопкой мыши, и какой именно
static bool IsMouseTrigger() { return g_cfg.trigger == TRG_MOUSE5 || g_cfg.trigger == TRG_MOUSE4 || g_cfg.trigger == TRG_RIGHT; }
static WORD TriggerXButton() { return g_cfg.trigger == TRG_MOUSE4 ? XBUTTON1 : XBUTTON2; }

static void SendKey(WORD vk, bool up) {
    INPUT in = {};
    in.type = INPUT_KEYBOARD;
    in.ki.wVk = vk;
    in.ki.wScan = (WORD)MapVirtualKeyW(vk, MAPVK_VK_TO_VSC);
    in.ki.dwFlags = up ? KEYEVENTF_KEYUP : 0;
    in.ki.dwExtraInfo = MAGIC;
    SendInput(1, &in, sizeof(INPUT));
}

static void SendCtrlV() {
    SendKey(VK_CONTROL, false);
    SendKey('V', false);
    SendKey('V', true);
    SendKey(VK_CONTROL, true);
}

// Повторяем короткий клик кнопки-триггера (правая / Back / Forward), который мы "съели" в хуке
static void ReplayTriggerClick() {
    INPUT in[2] = {};
    in[0].type = INPUT_MOUSE;
    in[0].mi.dwExtraInfo = MAGIC;
    if (g_cfg.trigger == TRG_RIGHT) {
        in[0].mi.dwFlags = MOUSEEVENTF_RIGHTDOWN;
        in[1] = in[0];
        in[1].mi.dwFlags = MOUSEEVENTF_RIGHTUP;
    } else {
        in[0].mi.dwFlags = MOUSEEVENTF_XDOWN;
        in[0].mi.mouseData = TriggerXButton();
        in[1] = in[0];
        in[1].mi.dwFlags = MOUSEEVENTF_XUP;
    }
    SendInput(2, in, sizeof(INPUT));
}

// Относится ли событие мыши к кнопке-триггеру; isDown сообщает, нажатие это или отпускание
static bool IsTriggerButtonMsg(WPARAM wp, const MSLLHOOKSTRUCT* ms, bool& isDown) {
    if (g_cfg.trigger == TRG_RIGHT) {
        if (wp == WM_RBUTTONDOWN) { isDown = true;  return true; }
        if (wp == WM_RBUTTONUP)   { isDown = false; return true; }
        return false;
    }
    if (g_cfg.trigger == TRG_MOUSE5 || g_cfg.trigger == TRG_MOUSE4) {
        if ((wp == WM_XBUTTONDOWN || wp == WM_XBUTTONUP) && HIWORD(ms->mouseData) == TriggerXButton()) {
            isDown = (wp == WM_XBUTTONDOWN);
            return true;
        }
    }
    return false;
}

// ----------------------------------------------------------------------------
//  Отрисовка меню
//  Окно "слоёное" (layered) с попиксельной прозрачностью. Фигуры (скруглённые
//  блоки) рисуем сами со сглаживанием, текст рисует GDI.
//  Фон блоков: RGB(32,32,32) с прозрачностью 50%.
// ----------------------------------------------------------------------------

// Ширина каждой "таблетки" по длине её текста
static void MeasureItems() {
    HDC dc = GetDC(nullptr);
    HGDIOBJ old = SelectObject(dc, g_font);
    int maxW = g_L.list.right - g_L.list.left;
    for (auto& it : g_view) {
        std::wstring t = OneLine(it.text, 80);
        SIZE sz = {};
        GetTextExtentPoint32W(dc, t.c_str(), (int)t.size(), &sz);
        int padL = it.pinned ? S(34) : S(14);
        it.w = std::min(maxW, std::max(S(60), (int)sz.cx + padL + S(14)));
    }
    SelectObject(dc, old);
    ReleaseDC(nullptr, dc);
}

// Определяем элемент под курсором (только если курсор над самой "таблеткой").
// Возвращает true, если выделение изменилось.
static bool UpdateHover(POINT pt) {
    const Layout& L = g_L;
    int rx = pt.x - g_winX, ry = pt.y - g_winY;
    int nh = -1;
    if (rx >= L.list.left && ry >= L.list.top && ry < L.list.bottom) {
        int rel = ry - L.list.top;
        int idx = g_scroll + rel / L.pitch;
        if (rel % L.pitch < L.pillH && idx < (int)g_view.size() && rx < L.list.left + g_view[idx].w)
            nh = idx;
    }
    if (nh != g_hover) { g_hover = nh; return true; }
    return false;
}

// Композитинг одного пикселя в premultiplied BGRA ("source over")
static inline void BlendPx(uint32_t& d, int r, int g, int b, int a) {
    if (a <= 0) return;
    int ia = 255 - a;
    uint32_t na = a + ((d >> 24) & 255) * ia / 255;
    uint32_t nr = r * a / 255 + ((d >> 16) & 255) * ia / 255;
    uint32_t ng = g * a / 255 + ((d >> 8) & 255) * ia / 255;
    uint32_t nb = b * a / 255 + (d & 255) * ia / 255;
    d = (na << 24) | (nr << 16) | (ng << 8) | nb;
}

// Скруглённый прямоугольник со сглаживанием (по расстоянию до границы).
// Если rad равен половине высоты - получается "таблетка", для квадрата - круг.
static void FillRound(uint32_t* buf, int W, int H, RECT rc, float rad, COLORREF col, int alpha) {
    int cr = GetRValue(col), cg = GetGValue(col), cb = GetBValue(col);
    float hx = (rc.right - rc.left) / 2.0f, hy = (rc.bottom - rc.top) / 2.0f;
    float cx = rc.left + hx, cy = rc.top + hy;
    for (int y = std::max<int>(0, rc.top); y < std::min<int>(H, rc.bottom); y++) {
        for (int x = std::max<int>(0, rc.left); x < std::min<int>(W, rc.right); x++) {
            float qx = std::fabs(x + 0.5f - cx) - (hx - rad);
            float qy = std::fabs(y + 0.5f - cy) - (hy - rad);
            float mx = std::max(qx, 0.0f), my = std::max(qy, 0.0f);
            float d = std::sqrt(mx * mx + my * my) + std::min(std::max(qx, qy), 0.0f) - rad;
            float cov = 0.5f - d;
            if (cov <= 0.0f) continue;
            if (cov > 1.0f) cov = 1.0f;
            BlendPx(buf[y * W + x], cr, cg, cb, (int)(alpha * cov + 0.5f));
        }
    }
}

static void RenderMenu() {
    if (g_state != ST_ACTIVE) return;
    const Layout& L = g_L;

    HDC screen = GetDC(nullptr);
    HDC mem = CreateCompatibleDC(screen);
    BITMAPINFO bi = {};
    bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth = L.W;
    bi.bmiHeader.biHeight = -L.H;               // top-down
    bi.bmiHeader.biPlanes = 1;
    bi.bmiHeader.biBitCount = 32;
    bi.bmiHeader.biCompression = BI_RGB;
    void* bits = nullptr;
    HBITMAP bmp = CreateDIBSection(screen, &bi, DIB_RGB_COLORS, &bits, nullptr, 0);
    HGDIOBJ oldBmp = SelectObject(mem, bmp);
    HGDIOBJ oldFont = SelectObject(mem, g_font);
    SetBkMode(mem, TRANSPARENT);

    // Цвета: фон RGB(32,32,32) с 50% прозрачности, синяя точка у закреплённых
    const COLORREF cBG = RGB(32, 32, 32), cHL = RGB(96, 96, 96), cBlue = RGB(0, 122, 255);
    const int aBG = 255, aHL = 255;

    // Слой 1: фигуры (отдельный прозрачный буфер).
    // Слой 2: текст рисуем GDI белым прямо в DIB (он служит маской покрытия),
    // потом накладываем поверх фигур.
    std::vector<uint32_t> shapes((size_t)L.W * L.H, 0);
    uint32_t* sp = shapes.data();

    // ---- Левый блок: превью ----
    FillRound(sp, L.W, L.H, L.preview, (float)S(34), cBG, aBG);
    RECT tr = L.preview;
    InflateRect(&tr, -S(18), -S(16));
    if (g_view.empty()) {
        SetTextColor(mem, RGB(170, 170, 170));
        DrawTextW(mem, L"История буфера пуста", -1, &tr, DT_WORDBREAK | DT_NOPREFIX);
    } else if (g_hover >= 0 && g_hover < (int)g_view.size()) {
        std::wstring t = g_view[g_hover].text;
        if (t.size() > 1500) { t.resize(1500); t += L'\u2026'; }
        SetTextColor(mem, RGB(255, 255, 255));
        DrawTextW(mem, t.c_str(), -1, &tr, DT_WORDBREAK | DT_NOPREFIX | DT_EXPANDTABS | DT_EDITCONTROL);
    } else {
        SetTextColor(mem, RGB(170, 170, 170));
        DrawTextW(mem, L"Наведите курсор на пункт справа и отпустите кнопку, чтобы вставить",
                  -1, &tr, DT_WORDBREAK | DT_NOPREFIX);
    }

    // ---- Правый блок: "таблетки" ----
    if (g_view.empty()) {
        RECT pill = { L.list.left, L.list.top, L.list.left + S(190), L.list.top + L.pillH };
        FillRound(sp, L.W, L.H, pill, L.pillH / 2.0f, cBG, aBG);
        RECT t = { pill.left + S(14), pill.top, pill.right - S(14), pill.bottom };
        SetTextColor(mem, RGB(170, 170, 170));
        DrawTextW(mem, L"Пусто", -1, &t, DT_SINGLELINE | DT_VCENTER | DT_NOPREFIX);
    }
    for (int i = 0; i < L.rows; i++) {
        int idx = g_scroll + i;
        if (idx >= (int)g_view.size()) break;
        int top = L.list.top + i * L.pitch;
        RECT pill = { L.list.left, top, L.list.left + g_view[idx].w, top + L.pillH };
        bool hl = (idx == g_hover);
        FillRound(sp, L.W, L.H, pill, L.pillH / 2.0f, hl ? cHL : cBG, hl ? aHL : aBG);

        int padL = S(14);
        if (g_view[idx].pinned) {                                  // синяя точка
            int cy = (pill.top + pill.bottom) / 2;
            RECT dot = { pill.left + S(10), cy - S(7), pill.left + S(24), cy + S(7) };
            FillRound(sp, L.W, L.H, dot, (float)S(7), cBlue, 255);
            padL = S(34);
        }
        RECT t = { pill.left + padL, pill.top, pill.right - S(14), pill.bottom };
        SetTextColor(mem, RGB(255, 255, 255));
        std::wstring line = OneLine(g_view[idx].text, 80);
        DrawTextW(mem, line.c_str(), -1, &t, DT_SINGLELINE | DT_VCENTER | DT_END_ELLIPSIS | DT_NOPREFIX);
    }

    // Тонкий индикатор прокрутки справа, если пунктов больше, чем влезает
    int total = (int)g_view.size();
    if (total > L.rows) {
        int trackH = L.list.bottom - L.list.top;
        int thumbH = std::max(S(24), trackH * L.rows / total);
        int y = L.list.top + (trackH - thumbH) * g_scroll / (total - L.rows);
        RECT th = { L.list.right + S(8), y, L.list.right + S(12), y + thumbH };
        FillRound(sp, L.W, L.H, th, (float)S(2), RGB(255, 255, 255), 140);
    }

    // ---- Склейка: текст (маска из зелёного канала) поверх фигур ----
    uint32_t* tp = (uint32_t*)bits;
    int n = L.W * L.H;
    for (int i = 0; i < n; i++) {
        uint32_t d = sp[i];
        int cov = (tp[i] >> 8) & 255;
        if (cov) BlendPx(d, 255, 255, 255, cov);
        tp[i] = d;
    }

    POINT ptSrc = { 0, 0 }, ptDst = { g_winX, g_winY };
    SIZE sz = { L.W, L.H };
    BLENDFUNCTION bf = { AC_SRC_OVER, 0, 255, AC_SRC_ALPHA };
    UpdateLayeredWindow(g_hMenu, screen, &ptDst, &sz, mem, &ptSrc, 0, &bf, ULW_ALPHA);

    SelectObject(mem, oldFont);
    SelectObject(mem, oldBmp);
    DeleteObject(bmp);
    DeleteDC(mem);
    ReleaseDC(nullptr, screen);
}

// Перерисовку делаем не в хуке (хук должен быть быстрым), а через сообщение
static void RequestRedraw() {
    if (!g_redrawQueued) {
        g_redrawQueued = true;
        PostMessageW(g_hMain, WM_APP_REDRAW, 0, 0);
    }
}

// ----------------------------------------------------------------------------
//  Показ / скрытие меню и управление состояниями
// ----------------------------------------------------------------------------
static void HideMenu() { if (g_hMenu) ShowWindow(g_hMenu, SW_HIDE); }

static void ShowMenu() {
    // Снимок списка: сначала закреплённые, затем история (новые сверху)
    g_view.clear();
    for (auto& p : g_cfg.pinned) g_view.push_back(ViewItem{ p, true, 0 });
    g_pinnedCount = (int)g_cfg.pinned.size();
    for (auto& h : g_history) {
        bool dup = false;
        for (auto& p : g_cfg.pinned) if (p == h) { dup = true; break; }
        if (!dup) g_view.push_back(ViewItem{ h, false, 0 });
    }
    MeasureItems();      // посчитать ширину каждой "таблетки"
    g_scroll = 0;
    g_hover = -1;

    // Окно позиционируем так, чтобы курсор оказался в центре левого блока
    const Layout& L = g_L;
    POINT cur; GetCursorPos(&cur);
    int cx = (L.preview.left + L.preview.right) / 2;
    int cy = (L.preview.top + L.preview.bottom) / 2;
    int x = cur.x - cx, y = cur.y - cy;

    // Не даём меню выйти за пределы рабочей области монитора
    MONITORINFO mi = {}; mi.cbSize = sizeof(mi);
    GetMonitorInfoW(MonitorFromPoint(cur, MONITOR_DEFAULTTONEAREST), &mi);
    int nx = std::max<int>(mi.rcWork.left, std::min<int>(x, mi.rcWork.right - L.W));
    int ny = std::max<int>(mi.rcWork.top,  std::min<int>(y, mi.rcWork.bottom - L.H));
    if (nx != x || ny != y) SetCursorPos(nx + cx, ny + cy);  // курсор всё равно в центре блока
    g_winX = nx; g_winY = ny;

    g_state = ST_ACTIVE;

    // Для Alt: "нажимаем" пустую клавишу, чтобы при отпускании Alt
    // Windows не активировала меню окна приложения
    if (g_cfg.trigger == TRG_ALT) { SendKey(0xE8, false); SendKey(0xE8, true); }

    // SWP_NOACTIVATE: фокус остаётся в текстовом поле, куда будем вставлять
    SetWindowPos(g_hMenu, HWND_TOPMOST, nx, ny, L.W, L.H, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    RenderMenu();
}

static void BeginPending() {
    g_state = ST_PENDING;
    SetTimer(g_hMain, T_SHOW, (UINT)std::max(10, g_cfg.delayMs), nullptr);
}

// Триггер отпущен до окончания задержки - это обычное нажатие
static void CancelPending() {
    KillTimer(g_hMain, T_SHOW);
    g_state = ST_IDLE;
}

// Отмена из-за комбинации клавиш/Esc: ждём, пока триггер отпустят
static void CancelCombo() {
    KillTimer(g_hMain, T_SHOW);
    if (g_state == ST_ACTIVE) HideMenu();
    g_state = ST_CANCELLED;
}

// Триггер отпущен при открытом меню
static void FinishMenu(bool paste) {
    HideMenu();
    int h = g_hover;
    g_state = ST_IDLE;
    if (paste && h >= 0 && h < (int)g_view.size()) {
        g_pasteText = g_view[h].text;
        PostMessageW(g_hMain, WM_APP_PASTE, 0, 0);   // вставка выполнится в основном цикле
    }
}

static void ResetAll() {
    KillTimer(g_hMain, T_SHOW);
    HideMenu();
    g_state = ST_IDLE;
}

// ----------------------------------------------------------------------------
//  Глобальные хуки мыши и клавиатуры
// ----------------------------------------------------------------------------
static bool IsTriggerKey(DWORD vk) {
    switch (g_cfg.trigger) {
        case TRG_CTRL:  return vk == VK_LCONTROL || vk == VK_RCONTROL || vk == VK_CONTROL;
        case TRG_ALT:   return vk == VK_LMENU || vk == VK_RMENU || vk == VK_MENU;
        case TRG_SHIFT: return vk == VK_LSHIFT || vk == VK_RSHIFT || vk == VK_SHIFT;
    }
    return false;
}

static LRESULT CALLBACK MouseProc(int code, WPARAM wp, LPARAM lp) {
    if (code < 0) return CallNextHookEx(nullptr, code, wp, lp);
    const MSLLHOOKSTRUCT* ms = (const MSLLHOOKSTRUCT*)lp;
    if (ms->dwExtraInfo == MAGIC || !g_cfg.enabled)       // свои события и выключенный режим пропускаем
        return CallNextHookEx(nullptr, code, wp, lp);

    // Для клавиатурных триггеров: клик/колесо во время ожидания = обычная комбинация (Ctrl+клик и т.д.)
    if (!IsMouseTrigger() && g_state == ST_PENDING &&
        (wp == WM_LBUTTONDOWN || wp == WM_RBUTTONDOWN || wp == WM_MBUTTONDOWN ||
         wp == WM_XBUTTONDOWN || wp == WM_MOUSEWHEEL))
        CancelCombo();

    if (wp == WM_MOUSEMOVE) {
        if (g_state == ST_ACTIVE && UpdateHover(ms->pt)) RequestRedraw();
        return CallNextHookEx(nullptr, code, wp, lp);
    }

    // Прокрутка списка колесом, пока меню открыто
    if (wp == WM_MOUSEWHEEL && g_state == ST_ACTIVE) {
        short delta = (short)HIWORD(ms->mouseData);
        int maxScroll = std::max(0, (int)g_view.size() - g_L.rows);
        g_scroll = std::max(0, std::min(maxScroll, g_scroll + (delta > 0 ? -1 : 1)));
        UpdateHover(ms->pt);
        RequestRedraw();
        return 1;                                         // колесо приложению не отдаём
    }

    // Кнопка мыши как триггер: правая / Mouse 5 (Forward) / Mouse 4 (Back)
    bool trgDown = false;
    if (IsMouseTrigger() && IsTriggerButtonMsg(wp, ms, trgDown)) {
        if (trgDown) {
            if (g_state == ST_IDLE) BeginPending();
            return 1;                                     // скрываем нажатие от приложений
        }
        switch (g_state) {
            case ST_PENDING:                              // короткий клик: возвращаем его системе
                CancelPending();
                ReplayTriggerClick();
                return 1;
            case ST_ACTIVE:                               // отпустили над элементом -> вставка
                UpdateHover(ms->pt);
                FinishMenu(true);
                return 1;
            case ST_CANCELLED:
                g_state = ST_IDLE;
                return 1;
            default: break;
        }
    }
    return CallNextHookEx(nullptr, code, wp, lp);
}

static LRESULT CALLBACK KbProc(int code, WPARAM wp, LPARAM lp) {
    if (code < 0) return CallNextHookEx(nullptr, code, wp, lp);
    const KBDLLHOOKSTRUCT* kb = (const KBDLLHOOKSTRUCT*)lp;
    if (kb->dwExtraInfo == MAGIC || !g_cfg.enabled)
        return CallNextHookEx(nullptr, code, wp, lp);

    bool down = (wp == WM_KEYDOWN || wp == WM_SYSKEYDOWN);
    bool up   = (wp == WM_KEYUP   || wp == WM_SYSKEYUP);

    // Esc отменяет меню (работает для любого триггера)
    if (g_state == ST_ACTIVE && kb->vkCode == VK_ESCAPE) {
        if (down) CancelCombo();
        return 1;
    }

    // Клавиатурные триггеры (Ctrl / Alt / Shift)
    if (!IsMouseTrigger()) {
        if (IsTriggerKey(kb->vkCode)) {
            if (down) {
                if (g_state == ST_IDLE) BeginPending();   // автоповтор клавиши игнорируем
            } else if (up) {
                if (g_state == ST_PENDING) CancelPending();
                else if (g_state == ST_ACTIVE) {
                    POINT p; GetCursorPos(&p);
                    UpdateHover(p);
                    FinishMenu(true);
                } else if (g_state == ST_CANCELLED) g_state = ST_IDLE;
            }
        } else if (down && (g_state == ST_PENDING || g_state == ST_ACTIVE)) {
            CancelCombo();                                // нажали другую клавишу: это шорткат, не меню
        }
    }
    return CallNextHookEx(nullptr, code, wp, lp);         // модификаторы никогда не "съедаем"
}

// ----------------------------------------------------------------------------
//  Иконка и трей
// ----------------------------------------------------------------------------

// Рисуем иконку программно (чтобы не нужен был .ico/.rc файл)
static HICON MakeIcon() {
    const int N = 32;
    BITMAPINFO bi = {};
    bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth = N;
    bi.bmiHeader.biHeight = -N;
    bi.bmiHeader.biPlanes = 1;
    bi.bmiHeader.biBitCount = 32;
    bi.bmiHeader.biCompression = BI_RGB;
    void* bits = nullptr;
    HDC dc = GetDC(nullptr);
    HBITMAP color = CreateDIBSection(dc, &bi, DIB_RGB_COLORS, &bits, nullptr, 0);
    ReleaseDC(nullptr, dc);
    uint32_t* px = (uint32_t*)bits;
    for (int y = 0; y < N; y++) {
        for (int x = 0; x < N; x++) {
            // скруглённый тёмный квадрат
            int cx = x < 7 ? 7 : (x > N - 8 ? N - 8 : x);
            int cy = y < 7 ? 7 : (y > N - 8 ? N - 8 : y);
            int dx = x - cx, dy = y - cy;
            uint32_t c = 0;                                   // прозрачно
            if (dx * dx + dy * dy <= 49) {
                c = 0xFF202020;
                if ((x - 10) * (x - 10) + (y - 10) * (y - 10) <= 16) c = 0xFF0078D7;   // синяя точка
                else if (y >= 8 && y <= 11 && x >= 17 && x <= 26)   c = 0xFFC8C8C8;    // строки-"список"
                else if (y >= 18 && y <= 21 && x >= 6 && x <= 26)   c = 0xFFA0A0A0;
                else if (y >= 24 && y <= 26 && x >= 6 && x <= 20)   c = 0xFFA0A0A0;
            }
            px[y * N + x] = c;
        }
    }
    HBITMAP mask = CreateBitmap(N, N, 1, 1, nullptr);
    ICONINFO ii = {};
    ii.fIcon = TRUE;
    ii.hbmMask = mask;
    ii.hbmColor = color;
    HICON ic = CreateIconIndirect(&ii);
    DeleteObject(mask);
    DeleteObject(color);
    return ic;
}

static void AddTrayIcon() {
    NOTIFYICONDATAW nid = {};
    nid.cbSize = sizeof(nid);
    nid.hWnd = g_hMain;
    nid.uID = 1;
    nid.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    nid.uCallbackMessage = WM_TRAY;
    nid.hIcon = g_hIcon;
    lstrcpynW(nid.szTip, L"Clip Radial Menu", ARRAYSIZE(nid.szTip));
    Shell_NotifyIconW(NIM_ADD, &nid);
}

static void RemoveTrayIcon() {
    NOTIFYICONDATAW nid = {};
    nid.cbSize = sizeof(nid);
    nid.hWnd = g_hMain;
    nid.uID = 1;
    Shell_NotifyIconW(NIM_DELETE, &nid);
}

static void OpenSettings();   // объявление, реализация ниже

static void ShowTrayMenu() {
    HMENU m = CreatePopupMenu();
    AppendMenuW(m, MF_STRING | (g_cfg.enabled ? MF_CHECKED : 0), ID_TOGGLE, L"Включено");
    AppendMenuW(m, MF_STRING, ID_SETTINGS, L"Настройки…");
    AppendMenuW(m, MF_STRING | (IsAutostart() ? MF_CHECKED : 0), ID_AUTOSTART, L"Запускать вместе с Windows");
    AppendMenuW(m, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(m, MF_STRING, ID_EXIT, L"Выход");
    POINT p; GetCursorPos(&p);
    SetForegroundWindow(g_hMain);          // без этого меню трея не закрывается по клику снаружи
    TrackPopupMenu(m, TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, p.x, p.y, 0, g_hMain, nullptr);
    PostMessageW(g_hMain, WM_NULL, 0, 0);
    DestroyMenu(m);
}

// ----------------------------------------------------------------------------
//  Окно настроек (создаётся кодом, без .rc ресурсов)
// ----------------------------------------------------------------------------
static HWND MkCtl(HWND parent, const wchar_t* cls, const wchar_t* text, DWORD style, DWORD ex,
                  int x, int y, int w, int h, int id) {
    HWND c = CreateWindowExW(ex, cls, text, WS_CHILD | WS_VISIBLE | style,
                             S(x), S(y), S(w), S(h), parent, (HMENU)(INT_PTR)id,
                             GetModuleHandleW(nullptr), nullptr);
    SendMessageW(c, WM_SETFONT, (WPARAM)g_uiFont, TRUE);
    return c;
}

static void RefreshPinnedList(HWND h, int select = -1) {
    HWND lb = GetDlgItem(h, ID_LIST);
    SendMessageW(lb, LB_RESETCONTENT, 0, 0);
    for (auto& s : g_edit) SendMessageW(lb, LB_ADDSTRING, 0, (LPARAM)OneLine(s, 80).c_str());
    if (select >= 0 && select < (int)g_edit.size()) SendMessageW(lb, LB_SETCURSEL, select, 0);
}

static LRESULT CALLBACK SettingsProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    switch (m) {
    case WM_CREATE: {
        MkCtl(h, L"STATIC", L"Кнопка вызова меню:", SS_LEFT, 0, 12, 16, 150, 20, -1);
        HWND cb = MkCtl(h, L"COMBOBOX", L"", CBS_DROPDOWNLIST | WS_TABSTOP | WS_VSCROLL, 0, 170, 12, 250, 200, ID_TRIGGER);
        SendMessageW(cb, CB_ADDSTRING, 0, (LPARAM)L"Mouse 5 (Forward)");
        SendMessageW(cb, CB_ADDSTRING, 0, (LPARAM)L"Ctrl");
        SendMessageW(cb, CB_ADDSTRING, 0, (LPARAM)L"Alt");
        SendMessageW(cb, CB_ADDSTRING, 0, (LPARAM)L"Shift");
        SendMessageW(cb, CB_ADDSTRING, 0, (LPARAM)L"Mouse 4 (Back)");
        SendMessageW(cb, CB_ADDSTRING, 0, (LPARAM)L"Правая кнопка мыши - по умолчанию");
        SendMessageW(cb, CB_SETCURSEL, g_cfg.trigger, 0);

        MkCtl(h, L"STATIC", L"Задержка перед показом:", SS_LEFT, 0, 12, 50, 155, 20, -1);
        MkCtl(h, L"EDIT", std::to_wstring(g_cfg.delayMs).c_str(), ES_NUMBER | ES_AUTOHSCROLL | WS_TABSTOP,
              WS_EX_CLIENTEDGE, 170, 46, 70, 24, ID_DELAY);
        MkCtl(h, L"STATIC", L"мс (по умолчанию 150)", SS_LEFT, 0, 248, 50, 200, 20, -1);

        MkCtl(h, L"STATIC", L"Закреплённые фразы (показываются первыми, с синей точкой):", SS_LEFT, 0, 12, 84, 496, 20, -1);
        MkCtl(h, L"LISTBOX", L"", LBS_NOTIFY | WS_VSCROLL | WS_TABSTOP, WS_EX_CLIENTEDGE, 12, 106, 496, 150, ID_LIST);

        MkCtl(h, L"STATIC", L"Текст выбранной / новой фразы:", SS_LEFT, 0, 12, 264, 300, 20, -1);
        MkCtl(h, L"EDIT", L"", ES_MULTILINE | ES_AUTOVSCROLL | ES_WANTRETURN | WS_VSCROLL | WS_TABSTOP,
              WS_EX_CLIENTEDGE, 12, 286, 496, 100, ID_TEXT);

        MkCtl(h, L"BUTTON", L"Добавить",    BS_PUSHBUTTON | WS_TABSTOP, 0,  12, 394,  84, 26, ID_ADD);
        MkCtl(h, L"BUTTON", L"Заменить",    BS_PUSHBUTTON | WS_TABSTOP, 0, 100, 394,  84, 26, ID_UPDATE);
        MkCtl(h, L"BUTTON", L"Удалить",     BS_PUSHBUTTON | WS_TABSTOP, 0, 188, 394,  84, 26, ID_DEL);
        MkCtl(h, L"BUTTON", L"Вверх",       BS_PUSHBUTTON | WS_TABSTOP, 0, 276, 394,  56, 26, ID_UP);
        MkCtl(h, L"BUTTON", L"Вниз",        BS_PUSHBUTTON | WS_TABSTOP, 0, 336, 394,  56, 26, ID_DOWN);
        MkCtl(h, L"BUTTON", L"Из буфера",   BS_PUSHBUTTON | WS_TABSTOP, 0, 396, 394, 112, 26, ID_FROMCLIP);

        MkCtl(h, L"BUTTON", L"Сохранить", BS_DEFPUSHBUTTON | WS_TABSTOP, 0, 316, 444, 90, 28, IDOK);
        MkCtl(h, L"BUTTON", L"Отмена",    BS_PUSHBUTTON | WS_TABSTOP,    0, 418, 444, 90, 28, IDCANCEL);

        g_edit = g_cfg.pinned;               // работаем с копией, применяем по "Сохранить"
        RefreshPinnedList(h);
        return 0;
    }
    case WM_COMMAND: {
        int id = LOWORD(w), ev = HIWORD(w);
        HWND lb = GetDlgItem(h, ID_LIST);
        HWND ed = GetDlgItem(h, ID_TEXT);
        int sel = (int)SendMessageW(lb, LB_GETCURSEL, 0, 0);

        if (id == ID_LIST && ev == LBN_SELCHANGE) {          // показать текст выбранной фразы
            if (sel >= 0 && sel < (int)g_edit.size()) SetWindowTextW(ed, ToCRLF(g_edit[sel]).c_str());
        } else if (id == ID_ADD) {
            std::wstring t = GetText(ed);
            if (t.find_first_not_of(L" \t\r\n") != std::wstring::npos) {
                g_edit.push_back(t);
                RefreshPinnedList(h, (int)g_edit.size() - 1);
            }
        } else if (id == ID_UPDATE) {
            std::wstring t = GetText(ed);
            if (sel >= 0 && t.find_first_not_of(L" \t\r\n") != std::wstring::npos) {
                g_edit[sel] = t;
                RefreshPinnedList(h, sel);
            }
        } else if (id == ID_DEL) {
            if (sel >= 0) {
                g_edit.erase(g_edit.begin() + sel);
                RefreshPinnedList(h, std::min(sel, (int)g_edit.size() - 1));
            }
        } else if (id == ID_UP) {
            if (sel > 0) { std::swap(g_edit[sel], g_edit[sel - 1]); RefreshPinnedList(h, sel - 1); }
        } else if (id == ID_DOWN) {
            if (sel >= 0 && sel + 1 < (int)g_edit.size()) { std::swap(g_edit[sel], g_edit[sel + 1]); RefreshPinnedList(h, sel + 1); }
        } else if (id == ID_FROMCLIP) {                       // подставить текущее содержимое буфера
            std::wstring t;
            if (GetClipboardText(t)) SetWindowTextW(ed, ToCRLF(t).c_str());
        } else if (id == IDOK) {                              // сохранить настройки
            int d = _wtoi(GetText(GetDlgItem(h, ID_DELAY)).c_str());
            g_cfg.delayMs = std::max(0, std::min(2000, d));
            int t = (int)SendMessageW(GetDlgItem(h, ID_TRIGGER), CB_GETCURSEL, 0, 0);
            g_cfg.trigger = std::max(0, std::min(5, t));
            g_cfg.pinned = g_edit;
            SaveConfig();
            ResetAll();                                       // на случай смены триггера
            DestroyWindow(h);
        } else if (id == IDCANCEL) {
            DestroyWindow(h);
        }
        return 0;
    }
    case WM_CLOSE:
        DestroyWindow(h);
        return 0;
    case WM_DESTROY:
        g_hSettings = nullptr;
        return 0;
    }
    return DefWindowProcW(h, m, w, l);
}

static void OpenSettings() {
    if (g_hSettings) {                                        // уже открыто - просто выводим вперёд
        ShowWindow(g_hSettings, SW_SHOW);
        SetForegroundWindow(g_hSettings);
        return;
    }
    RECT rc = { 0, 0, S(520), S(490) };
    DWORD style = WS_CAPTION | WS_SYSMENU | WS_MINIMIZEBOX;
    AdjustWindowRect(&rc, style, FALSE);
    int w = rc.right - rc.left, hgt = rc.bottom - rc.top;
    int x = (GetSystemMetrics(SM_CXSCREEN) - w) / 2;
    int y = (GetSystemMetrics(SM_CYSCREEN) - hgt) / 2;
    g_hSettings = CreateWindowExW(0, L"CRM_Settings", L"Clip Radial Menu - настройки", style,
                                  x, y, w, hgt, nullptr, nullptr, GetModuleHandleW(nullptr), nullptr);
    ShowWindow(g_hSettings, SW_SHOW);
    SetForegroundWindow(g_hSettings);
}

// ----------------------------------------------------------------------------
//  Окно меню (само ничего не рисует в WM_PAINT - используется UpdateLayeredWindow)
// ----------------------------------------------------------------------------
static LRESULT CALLBACK MenuProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    switch (m) {
    case WM_MOUSEACTIVATE: return MA_NOACTIVATE;   // клики по меню не забирают фокус
    case WM_ERASEBKGND:    return 1;
    case WM_PAINT: { PAINTSTRUCT ps; BeginPaint(h, &ps); EndPaint(h, &ps); return 0; }
    }
    return DefWindowProcW(h, m, w, l);
}

// ----------------------------------------------------------------------------
//  Главное (скрытое) окно: таймеры, буфер обмена, трей
// ----------------------------------------------------------------------------
static LRESULT CALLBACK MainProc(HWND h, UINT m, WPARAM w, LPARAM l) {
    switch (m) {
    case WM_CREATE:
        AddClipboardFormatListener(h);             // получать WM_CLIPBOARDUPDATE
        return 0;

    case WM_CLIPBOARDUPDATE:
        OnClipboardUpdate();
        return 0;

    case WM_TIMER:
        if (w == T_SHOW) {                          // задержка прошла - показываем меню
            KillTimer(h, T_SHOW);
            if (g_state == ST_PENDING) ShowMenu();
        } else if (w == T_PASTE) {                  // буфер уже заполнен - шлём Ctrl+V
            KillTimer(h, T_PASTE);
            SendCtrlV();
        }
        return 0;

    case WM_APP_REDRAW:
        g_redrawQueued = false;
        RenderMenu();
        return 0;

    case WM_APP_PASTE:
        // Кладём выбранный текст в буфер (не добавляя его заново в историю)
        g_ignoreClipUntil = GetTickCount64() + 500;
        SetClipboardText(g_pasteText);
        SetTimer(h, T_PASTE, 40, nullptr);          // небольшая пауза перед Ctrl+V
        return 0;

    case WM_TRAY:
        if (LOWORD(l) == WM_RBUTTONUP || LOWORD(l) == WM_CONTEXTMENU) ShowTrayMenu();
        else if (LOWORD(l) == WM_LBUTTONUP) OpenSettings();
        return 0;

    case WM_COMMAND:
        switch (LOWORD(w)) {
        case ID_TOGGLE:
            g_cfg.enabled = !g_cfg.enabled;
            if (!g_cfg.enabled) ResetAll();
            SaveConfig();
            break;
        case ID_SETTINGS:  OpenSettings(); break;
        case ID_AUTOSTART: SetAutostart(!IsAutostart()); break;
        case ID_EXIT:      DestroyWindow(h); break;
        }
        return 0;

    case WM_DESTROY:
        RemoveClipboardFormatListener(h);
        RemoveTrayIcon();
        PostQuitMessage(0);
        return 0;

    default:
        if (m == g_msgTaskbarCreated) AddTrayIcon();   // Explorer перезапустился - вернуть иконку
        break;
    }
    return DefWindowProcW(h, m, w, l);
}

// ----------------------------------------------------------------------------
//  Точка входа
// ----------------------------------------------------------------------------
int WINAPI wWinMain(HINSTANCE hInst, HINSTANCE, PWSTR, int) {
    // Только один экземпляр программы
    HANDLE mutex = CreateMutexW(nullptr, TRUE, L"Local\\ClipRadialMenu_SingleInstance");
    if (GetLastError() == ERROR_ALREADY_EXISTS) return 0;

    SetProcessDPIAware();                              // чёткий рендер на HiDPI экранах
    HDC dc = GetDC(nullptr);
    g_dpi = GetDeviceCaps(dc, LOGPIXELSX);
    ReleaseDC(nullptr, dc);
    g_L = MakeLayout();

    LoadConfig();

    g_font = CreateFontW(-S(16), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                         OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, ANTIALIASED_QUALITY,
                         DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
    g_uiFont = CreateFontW(-S(14), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET,
                           OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
                           DEFAULT_PITCH | FF_DONTCARE, L"Segoe UI");
    g_hIcon = MakeIcon();

    // Регистрация классов окон
    WNDCLASSEXW wc = {};
    wc.cbSize = sizeof(wc);
    wc.hInstance = hInst;
    wc.hCursor = LoadCursorW(nullptr, IDC_ARROW);

    wc.lpfnWndProc = MainProc;
    wc.lpszClassName = L"CRM_Main";
    RegisterClassExW(&wc);

    wc.lpfnWndProc = MenuProc;
    wc.lpszClassName = L"CRM_Menu";
    RegisterClassExW(&wc);

    wc.lpfnWndProc = SettingsProc;
    wc.lpszClassName = L"CRM_Settings";
    wc.hbrBackground = (HBRUSH)(COLOR_BTNFACE + 1);
    wc.hIcon = g_hIcon;
    RegisterClassExW(&wc);

    g_cfExclude = RegisterClipboardFormatW(L"ExcludeClipboardContentFromMonitorProcessing");
    g_msgTaskbarCreated = RegisterWindowMessageW(L"TaskbarCreated");

    // Скрытое главное окно (никогда не показывается)
    g_hMain = CreateWindowExW(0, L"CRM_Main", L"ClipRadialMenu", WS_POPUP, 0, 0, 0, 0,
                              nullptr, nullptr, hInst, nullptr);
    // Окно меню: поверх всего, без фокуса, без кнопки на панели задач, попиксельная прозрачность
    g_hMenu = CreateWindowExW(WS_EX_LAYERED | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE,
                              L"CRM_Menu", L"", WS_POPUP, 0, 0, g_L.W, g_L.H,
                              nullptr, nullptr, hInst, nullptr);

    AddTrayIcon();
    OnClipboardUpdate();                               // запомнить то, что уже лежит в буфере

    // Глобальные низкоуровневые хуки (потоку нужен цикл сообщений - он ниже)
    g_hMouseHook = SetWindowsHookExW(WH_MOUSE_LL, MouseProc, GetModuleHandleW(nullptr), 0);
    g_hKbHook    = SetWindowsHookExW(WH_KEYBOARD_LL, KbProc, GetModuleHandleW(nullptr), 0);

    // Главный цикл сообщений
    MSG msg;
    while (GetMessageW(&msg, nullptr, 0, 0) > 0) {
        if (g_hSettings && IsDialogMessageW(g_hSettings, &msg)) continue;   // Tab/Esc в настройках
        TranslateMessage(&msg);
        DispatchMessageW(&msg);
    }

    // Очистка
    if (g_hMouseHook) UnhookWindowsHookEx(g_hMouseHook);
    if (g_hKbHook) UnhookWindowsHookEx(g_hKbHook);
    DestroyWindow(g_hMenu);
    DestroyIcon(g_hIcon);
    DeleteObject(g_font);
    DeleteObject(g_uiFont);
    if (mutex) CloseHandle(mutex);
    return (int)msg.wParam;
}

// Для MinGW без -municode: обычная точка входа WinMain передаёт управление в wWinMain
#ifdef __MINGW32__
int WINAPI WinMain(HINSTANCE hInst, HINSTANCE hPrev, LPSTR, int nShow) {
    return wWinMain(hInst, hPrev, GetCommandLineW(), nShow);
}
#endif
