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
    private const ushort MOUSE_MOVE_ABSOLUTE = 0x0001;

    private readonly Label _statusLabel;
    private readonly TextBox _metricsBox;
    private readonly Label _hintLabel;
    private readonly Button _resetButton;
    private readonly System.Windows.Forms.Timer _uiRefreshTimer;

    private bool _tracking;
    private bool _uiDirty = true;
    private long _totalAbsX;
    private long _totalAbsY;
    private long _eventCount;
    private double _totalDistance;
    private IntPtr _keyboardHook = IntPtr.Zero;
    private LowLevelKeyboardProc? _keyboardProc;

    public MainForm()
    {
        Text = "Mouse Distance Tracker";
        ClientSize = new Size(760, 430);
        MinimumSize = new Size(760, 430);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        DoubleBuffered = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(24),
            AutoSize = false
        };

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        var title = new Label
        {
            Text = "Mouse Distance Tracker",
            Font = new Font(Font.FontFamily, 16, FontStyle.Bold),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false
        };

        _statusLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false
        };

        _metricsBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font(FontFamily.GenericMonospace, 10.5f),
            TabStop = false,
            WordWrap = false
        };

        _hintLabel = new Label
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.TopLeft,
            AutoEllipsis = false,
            Padding = new Padding(0, 8, 0, 0),
            Text = "Distance is based on WM_INPUT / Raw Input mouse deltas. It does not use cursor position, screen pixels, or Windows pointer acceleration. X/Y totals are absolute movement counters, so they never go negative."
        };

        var bottomPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0)
        };

        _resetButton = new Button
        {
            Text = "Reset",
            Width = 112,
            Height = 32,
            TabStop = false
        };
        _resetButton.Click += (_, _) => ResetCounters();
        bottomPanel.Controls.Add(_resetButton);

        root.Controls.Add(title, 0, 0);
        root.Controls.Add(_statusLabel, 0, 1);
        root.Controls.Add(_metricsBox, 0, 2);
        root.Controls.Add(_hintLabel, 0, 3);
        root.Controls.Add(bottomPanel, 0, 4);
        Controls.Add(root);

        _uiRefreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 50 // 20 FPS; prevents repainting the UI for every mouse packet.
        };
        _uiRefreshTimer.Tick += (_, _) => UpdateLabelsIfDirty();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        RegisterRawMouseInput();
        InstallKeyboardHook();
        UpdateLabels(force: true);
        _uiRefreshTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _uiRefreshTimer.Stop();
        _uiRefreshTimer.Dispose();

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
        UpdateLabels(force: true);
    }

    private void ResetCounters()
    {
        _totalAbsX = 0;
        _totalAbsY = 0;
        _eventCount = 0;
        _totalDistance = 0;
        UpdateLabels(force: true);
    }

    private void UpdateLabelsIfDirty()
    {
        if (_uiDirty)
        {
            UpdateLabels(force: true);
        }
    }

    private void MarkUiDirty()
    {
        _uiDirty = true;
    }

    private void UpdateLabels(bool force = false)
    {
        if (!force && !_uiDirty)
        {
            return;
        }

        _statusLabel.Text = _tracking
            ? "Status: tracking raw mouse input — press Tab to stop"
            : "Status: stopped — press Tab to start";

        _metricsBox.Text =
            $"Total path distance : {_totalDistance:N2} raw counts{Environment.NewLine}" +
            $"Total X movement    : {_totalAbsX:N0} raw counts{Environment.NewLine}" +
            $"Total Y movement    : {_totalAbsY:N0} raw counts{Environment.NewLine}" +
            $"Raw input events    : {_eventCount:N0}";

        _uiDirty = false;
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

            // This tracker is intended for normal relative mouse movement.
            // Absolute devices use normalized coordinates, not movement counts, so ignore them here.
            if ((raw.mouse.usFlags & MOUSE_MOVE_ABSOLUTE) == MOUSE_MOVE_ABSOLUTE)
            {
                return;
            }

            int dx = raw.mouse.lLastX;
            int dy = raw.mouse.lLastY;

            if (dx == 0 && dy == 0)
            {
                return;
            }

            _eventCount++;
            _totalAbsX += Math.Abs((long)dx);
            _totalAbsY += Math.Abs((long)dy);
            _totalDistance += Math.Sqrt(((double)dx * dx) + ((double)dy * dy));

            // Do not repaint labels for every WM_INPUT packet; high-polling-rate mice can flood this.
            MarkUiDirty();
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
