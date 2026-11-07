using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace StudyGate.App;

/// <summary>
/// 全局输入压制。WH_KEYBOARD_LL + WH_MOUSE_LL 装在**专用线程**并跑 message loop。
///
/// 三条必须懂的事实：
/// 1. 低级钩子的图标说明是"装在当前线程"，实际回调由安装线程的消息循环驱动。
///    所以钩子线程必须 GetMessage 泵消息，不能 Sleep；安装完成后立即 Stop 会立刻失效。
/// 2. 回调用 `(IntPtr)1` 吞键，`CallNextHookEx` 放行。吞掉的事件**不产生任何后续消息**，
///    连 WM_CHAR 都不会到，这正是 Esc/Alt+F4 打不穿窗口的原因。
/// 3. 委托必须是**静态字段**。若用局部变量或 lambda，GC 一旦回收，系统手里的函数指针
///    就悬空，表现为钩子时好时坏甚至整个桌面输入卡死。
///
/// 隐蔽口令入口：所有**未吞掉**的可见字符都调 onChar 回传（SPEC 第五节、CONTRACTS F）。
/// Ctrl+A 在这里被翻译成 '\u0001'，就是口令序列的前缀 —— 界面上不显示任何东西，
/// WPF 侧因为键盘消息已被钩子层放行，Ctrl+A 会正常落到文本框但被视图吞掉（视图不显示）。
/// </summary>
internal sealed class InputLock : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;

    private const uint WmKeyDown = 0x0100;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyUp = 0x0105;

    private const uint LButtonDown = 0x0201;
    private const uint RButtonDown = 0x0204;
    private const uint MButtonDown = 0x0207;
    private const uint XButtonDown = 0x020B;
    private const uint MouseWheel = 0x020A;
    private const uint MouseHWheel = 0x020E;

    private const uint LlkhfAltDown = 0x20;
    private const uint LlkhfExtended = 0x01;

    private const int VkBack = 0x08;
    private const int VkTab = 0x09;
    private const int VkReturn = 0x0D;
    private const int VkEscape = 0x1B;
    private const int VkSpace = 0x20;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkControl = 0x11;
    private const int VkShift = 0x10;
    private const int VkMenu = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;
    private const int VkF4 = 0x73;
    private const int VkF5 = 0x74;
    private const int VkF12 = 0x7B;

    private const uint WmQuit = 0x0012;

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "SetWindowsHookExW")]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out NativeMessage lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true, EntryPoint = "PostThreadMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint lpPoint);

    [DllImport("user32.dll", EntryPoint = "WindowFromPoint")]
    private static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    // ── 结构体（全部按 win-x64 布局）────────────────────────────────

    /// <summary>
    /// KBDLLHOOKSTRUCT：
    ///   DWORD vkCode(0) scanCode(4) flags(8) time(12)；ULONG_PTR dwExtraInfo(16)
    /// 4 个 DWORD 占 16 字节，dwExtraInfo 自然落在 8 字节边界，总长 24。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        internal uint VkCode;
        internal uint ScanCode;
        internal uint Flags;
        internal uint Time;
        internal UIntPtr DwExtraInfo;
    }

    /// <summary>
    /// MSLLHOOKSTRUCT：
    ///   POINT pt(x=0,y=4)；DWORD mouseData(8) flags(12) time(16)；ULONG_PTR dwExtraInfo(24)
    /// POINT 是 2 个 LONG = 8 字节，因此 mouseData 在偏移 8（这是最容易写错的地方，
    /// 64 位下也不会变 —— 结构里没有指针排在 mouseData 之前）。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal UIntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    /// <summary>
    /// MSG（win-x64）：
    ///   HWND(0,8) UINT message(8,4) pad(12,4) WPARAM(16,8) LPARAM(24,8) DWORD time(32,4) POINT pt(36,8)
    /// 总长 44 → 按 8 字节对齐补到 48。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        internal IntPtr Hwnd;
        internal uint Message;
        internal UIntPtr WParam;
        internal IntPtr LParam;
        internal uint Time;
        internal int PointX;
        internal int PointY;
    }

    // ── 实例状态 ────────────────────────────────────────────────

    private readonly Action<char> _onChar;

    // 钩子过程必须由字段持有：字段是 GC root，静态或实例字段都可以，但绝不能是局部变量。
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;

    private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);

    /// <summary>
    /// 句柄表。**必须加锁访问**：Stop() 在超时兜底路径上会从主线程直接 UnhookAll，
    /// 而钩子线程退出消息循环后也会调一次 —— 两个线程同时 foreach/Clear 一个 List
    /// 会抛 InvalidOperationException，且这发生在退出路径上，没人接得住。
    /// </summary>
    private readonly List<IntPtr> _hooks = new List<IntPtr>();
    private readonly object _hookLock = new object();

    private Thread? _thread;
    private uint _threadId;
    private int _failure;
    private volatile bool _disposed;

    /// <summary>只有本进程的窗口才放行鼠标；其它窗口（任务栏、其它应用）一律吞掉。</summary>
    private readonly uint _ownPid;

    internal InputLock(Action<char> onChar)
    {
        _onChar = onChar ?? throw new ArgumentNullException(nameof(onChar));
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
        _ownPid = (uint)Environment.ProcessId;
    }

    /// <summary>启动钩子线程并等它就位。返回 false = 钩子没装上（权限或系统策略）。</summary>
    internal bool Start()
    {
        if (_thread is not null)
        {
            return _failure == 0;
        }

        _thread = new Thread(HookThreadMain)
        {
            IsBackground = true,
            Name = "StudyGate.InputHook",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();

        // 装钩子是同步的，给它 2 秒；超时视为失败，调用方降级（仅锁当前会话）。
        return _ready.Wait(TimeSpan.FromSeconds(2)) && _failure == 0;
    }

    /// <summary>卸载并结束钩子线程。可重复调用。</summary>
    internal void Stop()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        uint tid = _threadId;
        if (tid != 0)
        {
            PostThreadMessage(tid, WmQuit, IntPtr.Zero, IntPtr.Zero);
        }

        _thread?.Join(TimeSpan.FromSeconds(1));

        // 兜底：线程没退出（消息循环被卡住）时从当前线程直接卸钩，钩子仍然立刻失效。
        UnhookAll();

        _ready.Dispose();
    }

    public void Dispose()
    {
        Stop();
    }

    // ── 线程主体 ──────────────────────────────────────────────

    private void HookThreadMain()
    {
        try
        {
            _threadId = GetCurrentThreadId();

            IntPtr module = GetModuleHandle(null);

            IntPtr kb = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, module, 0);
            IntPtr ms = SetWindowsHookEx(WhMouseLl, _mouseProc, module, 0);

            if (kb != IntPtr.Zero)
            {
                lock (_hookLock)
                {
                    _hooks.Add(kb);
                }
            }

            if (ms != IntPtr.Zero)
            {
                lock (_hookLock)
                {
                    _hooks.Add(ms);
                }
            }

            // 键盘钩子是刚需；鼠标钩子失败可以降级（只锁键不锁鼠标）。
            _failure = kb == IntPtr.Zero ? 1 : 0;
        }
        catch (Exception)
        {
            _failure = 1;
        }
        finally
        {
            _ready.Set();
        }

        // 消息循环：低级钩子的回调靠这里泵出来。GetMessage 返回 0 是 WM_QUIT，负数（!=0）是错误。
        while (true)
        {
            int rc = GetMessage(out NativeMessage msg, IntPtr.Zero, 0, 0);
            if (rc == 0 || rc < 0)
            {
                break;
            }
        }

        UnhookAll();
    }

    private void UnhookAll()
    {
        lock (_hookLock)
        {
            foreach (IntPtr hook in _hooks)
            {
                if (hook != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(hook);
                }
            }

            _hooks.Clear();
        }
    }

    // ── 回调 ──────────────────────────────────────────────────

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_disposed)
        {
            KbdLlHookStruct info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            uint msg = unchecked((uint)wParam.ToInt64());

            bool isDown = msg == WmKeyDown || msg == WmSysKeyDown;
            bool isUp = msg == WmKeyUp || msg == WmSysKeyUp;

            if (isDown || isUp)
            {
                if (ShouldSwallow((int)info.VkCode, info.Flags))
                {
                    return (IntPtr)1;
                }

                // 只处理按下，避免一个字符被回传两次（隐藏通道会把口令拆成 abbc）。
                if (isDown)
                {
                    char? ch = Translate((int)info.VkCode);
                    if (ch.HasValue)
                    {
                        try
                        {
                            _onChar(ch.Value);
                        }
                        catch (Exception)
                        {
                            // 回传链路出错绝不能把桌面输入卡死：吞掉异常继续放行。
                        }
                    }
                }
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    /// <summary>
    /// 吞键判定。Alt+F4 / Alt+Tab 靠 LLKHF_ALTDOWN 标志；Ctrl 组合靠实时键态 ——
    /// 低级钩子里 Ctrl 的按下事件可能已被系统拿去先处理，读 Async 键态比记账可靠。
    /// </summary>
    private static bool ShouldSwallow(int vk, uint flags)
    {
        bool alt = (flags & LlkhfAltDown) != 0 || IsDown(VkMenu);
        bool ctrl = IsDown(VkControl);
        bool shift = IsDown(VkShift);

        switch (vk)
        {
            case VkEscape:
                // Esc：单独按关闭；Ctrl+Esc 打开开始菜单；Ctrl+Shift+Esc 打开任务管理器。
                return true;

            case VkLWin:
            case VkRWin:
                return true;

            case VkF12:
                // F12 在多数环境打开调试器面板。
                return true;

            case VkF4:
                return alt;

            case VkTab:
                return alt;

            default:
                // Ctrl+Shift+Esc 的 Esc 已在上一个 case 被吞；这里只兜其他 Ctrl 组合穿透，
                // 不额外拦，避免误伤 Ctrl+A（\u0001 口令前缀）。
                _ = ctrl || shift;
                return false;
        }
    }

    private static bool IsDown(int vk)
    {
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    /// <summary>
    /// 键码 → 字符。只有白名单字符会回传；同时 Ctrl+A 翻译成 '\u0001' 作为口令前缀。
    /// 不查键盘布局：口令只用 ASCII 可见字符，与用户键盘布局无关。
    /// </summary>
    private char? Translate(int vk)
    {
        bool ctrl = IsDown(VkControl);
        bool shift = IsDown(VkShift);

        if (vk == VkReturn)
        {
            return '\r';
        }

        // 口令前缀：Ctrl+A 在 Windows 里对应字符 1，界面上什么也不会显示。
        if (ctrl && !shift && vk == 0x41)
        {
            return '\u0001';
        }

        if (ctrl)
        {
            // 其它 Ctrl 组合不进隐藏通道：它们不是口令字符，进去只会污染状态机。
            return null;
        }

        if (vk >= 0x41 && vk <= 0x5A)
        {
            return shift ? (char)vk : char.ToLowerInvariant((char)vk);
        }

        if (vk >= 0x30 && vk <= 0x39)
        {
            return (char)vk;
        }

        if (vk == VkSpace)
        {
            return ' ';
        }

        return null;
    }

    // ── 鼠标 ──────────────────────────────────────────────────

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && !_disposed)
        {
            uint msg = unchecked((uint)wParam.ToInt64());

            if (msg is LButtonDown or RButtonDown or MButtonDown or XButtonDown or MouseWheel or MouseHWheel)
            {
                // 只拦"点到别处"。本进程自己的窗口必须放行，否则界面点不动。
                if (!IsOwnWindowUnderCursor())
                {
                    return (IntPtr)1;
                }
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private bool IsOwnWindowUnderCursor()
    {
        if (!GetCursorPos(out NativePoint pt))
        {
            // 拿不到坐标就保守放行：吞掉自己窗口的点击会让界面完全失效。
            return true;
        }

        IntPtr hwnd = WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        GetWindowThreadProcessId(hwnd, out uint pid);

        // 桌面的 SysListView/WorkerW 属于 explorer 进程，不是我们，会被吞 —— 这是想要的效果。
        return pid == _ownPid;
    }
}
