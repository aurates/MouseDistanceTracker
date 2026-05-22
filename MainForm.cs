using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MouseDistanceTracker;

public sealed class MainForm : Form
{
    private const int WM_INPUT = 0x00FF;
    private const int RID_INPUT = 0x10000003;
    private const int RIM_TYPEMOUSE = 0;
    private const int RIDEV_INPUTSINK = 0x00000100;
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int VK_TAB = 0x09;
    private const int HintResetButtonSpacing = 12;

    private readonly Label _statusLabel;
    private readonly Label _distanceLabel;
    private readonly Label _deltaLabel;
    private readonly Button _resetButton;

    private bool _tracking;
    private long _rawX;
    private long _rawY;
    private double _totalDistance;
    private IntPtr _keyboardHook = IntPtr.Zero;
    private LowLevelKeyboardProc? _keyboardProc;

    public MainForm()
    {
        Text = "Mouse Distance Tracker";
        Width = 520;
        Height = 260;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        var title = new Label
        {
            Text = "Mouse Distance Tracker",
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            AutoSize = true,
            Left = 24,
            Top = 22
        };

        _statusLabel = new Label
        {
            Text = "Status: stopped — press Tab to start",
            AutoSize = true,
            Left = 24,
            Top = 70
        };

        _distanceLabel = new Label
        {
            Text = "Total distance: 0 raw counts",
            Font = new Font(Font.FontFamily, 12, FontStyle.Regular),
            AutoSize = true,
            Left = 24,
            Top = 105
        };

        _deltaLabel = new Label
        {
            Text = "Net movement: X = 0, Y = 0",
            AutoSize = true,
            Left = 24,
            Top = 140
        };

        var hint = new Label
        {
            Text = "Tab toggles tracking while this app is running. Distance is based on raw mouse input deltas.",
            AutoSize = true,
            Left = 24,
            Top = 172
        };

        _resetButton = new Button
        {
            Text = "Reset",
            Width = 90,
            Height = 32,
            Left = 390,
            Top = 172
        };
        hint.MaximumSize = new Size(_resetButton.Left - hint.Left - HintResetButtonSpacing, 0);
        _resetButton.Click += (_, _) => ResetCounters();

        Controls.Add(title);
        Controls.Add(_statusLabel);
        Controls.Add(_distanceLabel);
        Controls.Add(_deltaLabel);
        Controls.Add(hint);
        Controls.Add(_resetButton);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RegisterRawMouseInput();
        InstallKeyboardHook();
        UpdateLabels();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        base.OnFormClosed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_INPUT)
        {
            HandleRawInput(m.LParam);
        }

        base.WndProc(ref m);
    }

    private void ToggleTracking()
    {
        _tracking = !_tracking;
        UpdateLabels();
    }

    private void ResetCounters()
    {
        _rawX = 0;
        _rawY = 0;
        _totalDistance = 0;
        UpdateLabels();
    }

    private void UpdateLabels()
    {
        _statusLabel.Text = _tracking
            ? "Status: tracking — press Tab to stop"
            : "Status: stopped — press Tab to start";

        _distanceLabel.Text = $"Total distance: {_totalDistance:N2} raw counts";
        _deltaLabel.Text = $"Net movement: X = {_rawX:N0}, Y = {_rawY:N0}";
    }

    private void HandleRawInput(IntPtr hRawInput)
    {
        uint size = 0;
        GetRawInputData(hRawInput, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>());
        if (size == 0)
        {
            return;
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            uint read = GetRawInputData(hRawInput, RID_INPUT, buffer, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>());
            if (read != size)
            {
                return;
            }

            var raw = Marshal.PtrToStructure<RAWINPUT>(buffer);
            if (raw.header.dwType != RIM_TYPEMOUSE || !_tracking)
            {
                return;
            }

            int dx = raw.mouse.lLastX;
            int dy = raw.mouse.lLastY;

            if (dx == 0 && dy == 0)
            {
                return;
            }

            _rawX += dx;
            _rawY += dy;
            _totalDistance += Math.Sqrt((dx * dx) + (dy * dy));
            UpdateLabels();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private void RegisterRawMouseInput()
    {
        var rid = new RAWINPUTDEVICE
        {
            usUsagePage = 0x01,
            usUsage = 0x02,
            dwFlags = RIDEV_INPUTSINK,
            hwndTarget = Handle
        };

        if (!RegisterRawInputDevices([rid], 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>()))
        {
            throw new InvalidOperationException("Failed to register raw mouse input.");
        }
    }

    private void InstallKeyboardHook()
    {
        _keyboardProc = KeyboardHookCallback;
        using Process currentProcess = Process.GetCurrentProcess();
        using ProcessModule? currentModule = currentProcess.MainModule;

        if (currentModule is null)
        {
            throw new InvalidOperationException("Failed to read current process module.");
        }

        _keyboardHook = SetWindowsHookEx(
            WH_KEYBOARD_LL,
            _keyboardProc,
            GetModuleHandle(currentModule.ModuleName),
            0);

        if (_keyboardHook == IntPtr.Zero)
        {
            throw new InvalidOperationException("Failed to install keyboard hook.");
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
        {
            var keyInfo = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (keyInfo.vkCode == VK_TAB)
            {
                BeginInvoke(ToggleTracking);
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage;
        public ushort usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType;
        public uint dwSize;
        public IntPtr hDevice;
        public IntPtr wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUT
    {
        public RAWINPUTHEADER header;
        public RAWMOUSE mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWMOUSE
    {
        public ushort usFlags;
        public uint ulButtons;
        public uint ulRawButtons;
        public int lLastX;
        public int lLastY;
        public uint ulExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] RAWINPUTDEVICE[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr hRawInput,
        uint uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
