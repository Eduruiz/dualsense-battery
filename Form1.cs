using System.Runtime.InteropServices;
using HidLibrary;

namespace DualSenseBattery.App;

public partial class Form1 : Form
{
    private const int VENDOR_ID = 0x054C;
    private const int PRODUCT_ID = 0x0CE6;

    // Windows messages for device change notifications
    private const int WM_DEVICECHANGE = 0x0219;
    private const int DBT_DEVICEARRIVAL = 0x8000;
    private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
    private const int DBT_DEVTYP_DEVICEINTERFACE = 0x00000005;

    private static readonly Guid GUID_DEVINTERFACE_HID = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

    private HidDevice? _controller;
    private bool _lowBatteryAlertSent = false;
    private bool _criticalBatteryAlertSent = false;
    private bool _controllerWasConnected = false;
    private bool _isBluetooth = false;
    private ToolStripMenuItem? showToolStripMenuItem;
    private IntPtr _deviceNotificationHandle;
    
    private GroupBox? gbControllerInfo;
    private Label? lblStatus;
    private Label? lblBattery;
    private ProgressBar? pbBattery;
    
    // Track the current tray icon handle so we can destroy the old one when updating
    private IntPtr _trayIconHandle = IntPtr.Zero;

    private void Log(string message)
    {
        string logFile = Path.Combine(Path.GetTempPath(), "DualSenseBattery.log");
        File.AppendAllText(logFile, $"{DateTime.Now:HH:mm:ss.fff} - {message}\n");
    }


    [StructLayout(LayoutKind.Sequential)]
    private struct DEV_BROADCAST_DEVICEINTERFACE
    {
        public int dbcc_size;
        public int dbcc_devicetype;
        public int dbcc_reserved;
        public Guid dbcc_classguid;
        public short dbcc_name;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr NotificationFilter, int Flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterDeviceNotification(IntPtr Handle);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);

    public Form1()
    {
        InitializeComponent();
        SetupUI();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Log("OnHandleCreated: Registering for device notifications");
        RegisterDeviceChangeNotification();
    }

    private void RegisterDeviceChangeNotification()
    {
        // Unregister any previous registration (handles can be recreated on WindowState changes)
        if (_deviceNotificationHandle != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_deviceNotificationHandle);
            _deviceNotificationHandle = IntPtr.Zero;
        }

        DEV_BROADCAST_DEVICEINTERFACE dbi = new DEV_BROADCAST_DEVICEINTERFACE
        {
            dbcc_size = Marshal.SizeOf(typeof(DEV_BROADCAST_DEVICEINTERFACE)),
            dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE,
            dbcc_reserved = 0,
            dbcc_classguid = GUID_DEVINTERFACE_HID,
            dbcc_name = 0
        };

        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf(dbi));
        Marshal.StructureToPtr(dbi, buffer, true);

        const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
        const int DEVICE_NOTIFY_ALL_INTERFACE_CLASSES = 0x00000004;

        _deviceNotificationHandle = RegisterDeviceNotification(this.Handle, buffer, DEVICE_NOTIFY_WINDOW_HANDLE | DEVICE_NOTIFY_ALL_INTERFACE_CLASSES);

        Marshal.FreeHGlobal(buffer);

        Log($"RegisterDeviceChangeNotification: Handle = {_deviceNotificationHandle}, Window Handle = {this.Handle}");
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_DEVICECHANGE)
        {
            int wParam = m.WParam.ToInt32();
            Log($"WndProc: WM_DEVICECHANGE received, wParam = 0x{wParam:X} ({wParam}) (ARRIVAL=0x{DBT_DEVICEARRIVAL:X}, REMOVE=0x{DBT_DEVICEREMOVECOMPLETE:X})");
            
            if (wParam == DBT_DEVICEARRIVAL)
            {
                Log("WndProc: Device ARRIVAL detected");
                // Delay slightly to allow device enumeration to update
                Task.Delay(500).ContinueWith(_ => 
                {
                    if (!this.IsDisposed)
                    {
                        try
                        {
                            this.Invoke(new Action(() =>
                            {
                                CheckControllerConnection();
                            }));
                        }
                        catch (Exception ex)
                        {
                            Log($"WndProc ARRIVAL Invoke error: {ex.Message}");
                        }
                    }
                });
            }
            else if (wParam == DBT_DEVICEREMOVECOMPLETE)
            {
                Log("WndProc: Device REMOVE detected");
                // Delay slightly to allow device enumeration to update
                Task.Delay(500).ContinueWith(_ => 
                {
                    if (!this.IsDisposed)
                    {
                        try
                        {
                            this.Invoke(new Action(() =>
                            {
                                CheckControllerConnection();
                            }));
                        }
                        catch (Exception ex)
                        {
                            Log($"WndProc REMOVE Invoke error: {ex.Message}");
                        }
                    }
                });
            }
        }
        
        base.WndProc(ref m);
    }

    private void CheckControllerConnection()
    {
        var devices = HidDevices.Enumerate(VENDOR_ID, PRODUCT_ID).ToList();
        bool controllerPresent = devices.Any();

        Log($"CheckControllerConnection: controllerPresent={controllerPresent}, _controllerWasConnected={_controllerWasConnected}");

        if (controllerPresent && !_controllerWasConnected)
        {
            // Controller connected - try to connect and show notification
            Log("CheckControllerConnection: Controller CONNECTED");

            if (TryConnectController())
            {
                _controllerWasConnected = true;

                // Read battery info for the notification and update UI
                string batteryInfo = "";
                if (_controller != null)
                {
                    var report = _controller.ReadReport(1000);
                    if (report.ReadStatus == HidDeviceData.ReadStatus.Success && report.Data.Length > 0)
                    {
                        _isBluetooth = report.Data.Length == 77; // BT extended report = 77 data bytes; USB = 63
                        int battIdx = _isBluetooth ? 53 : 52;

                        if (report.Data.Length > battIdx)
                        {
                            int battery = report.Data[battIdx];
                            int batteryLevel = (battery & 0x0F) * 10;
                            bool isCharging = (battery & 0x10) != 0;
                            batteryInfo = $" Battery: {batteryLevel}%" + (isCharging ? " (Charging)" : "");
                            UpdateBatteryLevel(batteryLevel, isCharging);
                        }
                    }
                }

                if (lblStatus != null) lblStatus.Text = "Status: Connected";
                notifyIcon.ShowBalloonTip(3000, "Controller Connected", $"DualSense controller has been detected.{batteryInfo}", ToolTipIcon.Info);
                Log("CheckControllerConnection: CONNECT notification shown");
            }
        }
        else if (!controllerPresent && _controllerWasConnected)
        {
            // Controller disconnected
            Log("CheckControllerConnection: Controller DISCONNECTED");
            
            _controllerWasConnected = false;
            UpdateUIForDisconnect();
            notifyIcon.ShowBalloonTip(3000, "Controller Disconnected", "The DualSense controller has been disconnected.", ToolTipIcon.Warning);
            Log("CheckControllerConnection: DISCONNECT notification shown");
        }
        else
        {
            Log($"CheckControllerConnection: No state change");
        }
    }

    private void SetupUI()
    {
        // Form settings
        this.Text = "DualSense Battery";
        this.Size = new System.Drawing.Size(320, 200);
        this.FormBorderStyle = FormBorderStyle.FixedSingle;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;

        // GroupBox
        gbControllerInfo = new GroupBox
        {
            Text = "Controller Information",
            Location = new System.Drawing.Point(10, 10),
            Size = new System.Drawing.Size(280, 140)
        };
        this.Controls.Add(gbControllerInfo);

        // Status Label
        lblStatus = new Label
        {
            Text = "Status: Searching...",
            Location = new System.Drawing.Point(15, 30),
            AutoSize = true
        };
        gbControllerInfo.Controls.Add(lblStatus);

        // Battery Label
        lblBattery = new Label
        {
            Text = "Battery: N/A",
            Location = new System.Drawing.Point(15, 60),
            AutoSize = true
        };
        gbControllerInfo.Controls.Add(lblBattery);

        // Battery ProgressBar
        pbBattery = new ProgressBar
        {
            Location = new System.Drawing.Point(15, 90),
            Size = new System.Drawing.Size(250, 20)
        };
        gbControllerInfo.Controls.Add(pbBattery);
        
        // Tray Icon Menu - Insert Show at the beginning
        showToolStripMenuItem = new ToolStripMenuItem("Show");
        showToolStripMenuItem.Click += ShowToolStripMenuItem_Click;
        contextMenuStrip.Items.Insert(0, showToolStripMenuItem);
        
        // Force re-associate context menu to notify icon
        notifyIcon.ContextMenuStrip = contextMenuStrip;
        
        // Setup double-click handler
        notifyIcon.DoubleClick += NotifyIcon_DoubleClick;

        notifyIcon.Text = "DualSense Battery - Searching...";
    }

    private void NotifyIcon_DoubleClick(object? sender, EventArgs e)
    {
        ShowWindow();
    }

    private void ShowWindow()
    {
        this.Show();
        this.WindowState = FormWindowState.Normal;
        this.ShowInTaskbar = true; // Make it appear in the taskbar when shown
        this.BringToFront();
    }

    private void ShowToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        ShowWindow();
    }

    private void Form1_Load(object sender, EventArgs e)
    {
        Log("=== APP STARTED ===");
        
        this.WindowState = FormWindowState.Minimized;
        this.ShowInTaskbar = false;
        
        Log("Form1_Load: Starting timer");
        timer.Start();
    }
    
    private void Form1_Shown(object sender, EventArgs e)
    {
        Log("Form1_Shown: Hiding form");
        this.Hide();
        
        // Check if controller is already connected on startup
        Log("Form1_Shown: Checking for already-connected controller");
        var devices = HidDevices.Enumerate(VENDOR_ID, PRODUCT_ID).ToList();
        
        if (devices.Any())
        {
            Log("Form1_Load: Controller detected on startup - connecting");
            if (TryConnectController())
            {
                _controllerWasConnected = true;
                
                // Read battery info for the notification
                string batteryInfo = "";
                if (_controller != null)
                {
                    var report = _controller.ReadReport(1000);
                    if (report.ReadStatus == HidDeviceData.ReadStatus.Success && report.Data.Length > 0)
                    {
                        _isBluetooth = report.Data.Length == 77; // BT extended report = 77 data bytes; USB = 63
                        int battIdx = _isBluetooth ? 53 : 52;
                        
                        if (report.Data.Length > battIdx)
                        {
                            int battery = report.Data[battIdx];
                            int batteryLevel = (battery & 0x0F) * 10;
                            bool isCharging = (battery & 0x10) != 0;
                            batteryInfo = $" Battery: {batteryLevel}%" + (isCharging ? " (Charging)" : "");
                            
                            // Update UI immediately
                            UpdateBatteryLevel(batteryLevel, isCharging);
                        }
                    }
                }
                
                if (lblStatus != null) lblStatus.Text = "Status: Connected";
                notifyIcon.ShowBalloonTip(3000, "Controller Connected", $"DualSense controller is connected.{batteryInfo}", ToolTipIcon.Info);
                Log("Form1_Load: Controller connected on startup, notification shown");
            }
            else
            {
                Log("Form1_Load: Controller detected but failed to connect");
            }
        }
        else
        {
            Log("Form1_Load: No controller detected on startup");
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Instead of closing, hide the window
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            this.Hide();
            this.ShowInTaskbar = false; // Hide from taskbar
        }
        else
        {
            base.OnFormClosing(e);
        }
    }


    private void Form1_FormClosing(object sender, FormClosingEventArgs e)
    {
        if (_deviceNotificationHandle != IntPtr.Zero)
        {
            UnregisterDeviceNotification(_deviceNotificationHandle);
            _deviceNotificationHandle = IntPtr.Zero;
        }
        notifyIcon.Visible = false;
        _controller?.CloseDevice();
        timer?.Stop();
        timer?.Dispose();
        if (_trayIconHandle != IntPtr.Zero)
        {
            DestroyIcon(_trayIconHandle);
            _trayIconHandle = IntPtr.Zero;
        }
    }

    private bool TryConnectController()
    {
        Log("TryConnectController: Starting");
        _controller?.CloseDevice();
        _controller = null;
        
        var devices = HidDevices.Enumerate(VENDOR_ID, PRODUCT_ID).ToList();
        Log($"TryConnectController: Found {devices.Count} devices");
        
        if (devices.Any())
        {
            // Try each device until we find one that actually works
            foreach (var device in devices)
            {
                Log($"TryConnectController: Trying device {device.Description}");
                device.OpenDevice();
                
                if (device.IsConnected)
                {
                    Log("TryConnectController: Device opened, attempting read");
                    // Try to read from the device to verify it's actually responding (not a phantom)
                    var report = device.ReadReport(1000); // 1 second timeout
                    
                    Log($"TryConnectController: Read status={report.ReadStatus}, DataLength={report.Data.Length}");
                    
                    if (report.ReadStatus == HidDeviceData.ReadStatus.Success && report.Data.Length > 0)
                    {
                        // This is a real, responding controller!
                        Log("TryConnectController: Found working controller!");
                        _controller = device;
                        Log("TryConnectController: SUCCESS - returning true");
                        return true;
                    }
                    else
                    {
                        // Phantom device - close and try next
                        Log("TryConnectController: Device didn't respond, closing");
                        device.CloseDevice();
                    }
                }
                else
                {
                    Log("TryConnectController: Device not connected");
                    device.CloseDevice();
                }
            }
        }
        
        Log($"TryConnectController: FAILED - returning false");
        return false;
    }

    private void timer_Tick(object sender, EventArgs e)
    {
        if (_controller == null || !_controllerWasConnected)
            return;

        HidReport report = _controller.ReadReport(1000);

        if (report.ReadStatus != HidDeviceData.ReadStatus.Success || report.Data.Length == 0)
        {
            Log($"timer_Tick: Read FAILED! Status={report.ReadStatus} - waiting for Windows disconnect event");
            return;
        }

        _isBluetooth = report.Data.Length == 77; // BT extended report = 77 data bytes; USB = 63
        int battIdx = _isBluetooth ? 53 : 52;

        if (report.Data.Length <= battIdx)
            return;

        int battery = report.Data[battIdx];
        int batteryNibble = battery & 0x0F;
        bool isCharging = (battery & 0x10) != 0;

        if (batteryNibble > 10)
        {
            Log($"timer_Tick: Invalid battery nibble {batteryNibble} (raw=0x{battery:X2}), skipping");
            return;
        }

        int batteryLevel = batteryNibble * 10;
        UpdateBatteryLevel(batteryLevel, isCharging);

        if (batteryLevel <= 20 && !isCharging && !_lowBatteryAlertSent)
        {
            notifyIcon.ShowBalloonTip(3000, "DualSense Low Battery", $"Your DualSense controller battery is at {batteryLevel}%!", ToolTipIcon.Warning);
            _lowBatteryAlertSent = true;
        }
        else if (batteryLevel > 20 || isCharging)
        {
            _lowBatteryAlertSent = false;
        }

        if (batteryLevel <= 5 && !isCharging && !_criticalBatteryAlertSent)
        {
            notifyIcon.ShowBalloonTip(3000, "DualSense Critical Battery", $"Your DualSense controller battery is CRITICALLY LOW at {batteryLevel}%!", ToolTipIcon.Error);
            _criticalBatteryAlertSent = true;
        }
        else if (batteryLevel > 5 || isCharging)
        {
            _criticalBatteryAlertSent = false;
        }
    }

    private void UpdateUIForDisconnect()
    {
        Log("UpdateUIForDisconnect: CALLED - Clearing ALL state");
        
        // Close and clear controller
        _controller?.CloseDevice();
        _controller = null;
        
        _isBluetooth = false;
        _lowBatteryAlertSent = false;
        _criticalBatteryAlertSent = false;
        
        // Update UI
        if (lblStatus != null) lblStatus.Text = "Status: Disconnected";
        if (lblBattery != null) lblBattery.Text = "Battery: N/A";
        if (pbBattery != null) pbBattery.Value = 0;
        if (_trayIconHandle != IntPtr.Zero)
        {
            DestroyIcon(_trayIconHandle);
            _trayIconHandle = IntPtr.Zero;
        }
        notifyIcon.Icon = SystemIcons.Application;
        notifyIcon.Text = "DualSense Battery - Searching...";
        
        Log("UpdateUIForDisconnect: All state cleared");
    }


    private void exitToolStripMenuItem_Click(object sender, EventArgs e)
    {
        Application.Exit();
    }

    private void colorToolStripMenuItem_Click(object sender, EventArgs e)
    {
        if (colorDialog.ShowDialog() == DialogResult.OK)
        {
            if (_controller != null && _controller.IsConnected)
            {
                byte[] outputReport = new byte[78];
                if (_isBluetooth)
                {
                    outputReport[0] = 0x31;
                    outputReport[1] = 0x02;
                    outputReport[3] = 0x01 | 0x02 | 0x04;
                    outputReport[48] = colorDialog.Color.R;
                    outputReport[49] = colorDialog.Color.G;
                    outputReport[50] = colorDialog.Color.B;
                }
                else
                {
                    outputReport[0] = 0x02;
                    outputReport[1] = 0x01 | 0x02 | 0x04;
                    outputReport[45] = colorDialog.Color.R;
                    outputReport[46] = colorDialog.Color.G;
                    outputReport[47] = colorDialog.Color.B;
                }

                _controller.Write(outputReport);
            }
        }
    }

    private void UpdateBatteryLevel(int batteryLevel, bool isCharging)
    {
        string chargingStatus = isCharging ? " (Charging)" : "";
        notifyIcon.Text = $"DualSense Battery: {batteryLevel}%{chargingStatus}";
        if (lblBattery != null) lblBattery.Text = $"Battery: {batteryLevel}%{chargingStatus}";
        if (pbBattery != null) pbBattery.Value = batteryLevel;


        using (var bitmap = new Bitmap(32, 32))
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.Transparent);

            graphics.DrawRectangle(Pens.Black, 0, 5, 30, 20);
            graphics.FillRectangle(Brushes.Black, 30, 10, 2, 10);

            Brush batteryBrush = Brushes.Red;
            if (batteryLevel > 20) batteryBrush = Brushes.Green;
            else if (batteryLevel > 10) batteryBrush = Brushes.Yellow;

            graphics.FillRectangle(batteryBrush, 2, 7, 26 * batteryLevel / 100, 16);

            if (isCharging)
            {
                Point[] points = { new Point(12, 0), new Point(12, 10), new Point(16, 10), new Point(10, 20), new Point(10, 10), new Point(6, 10) };
                graphics.FillPolygon(Brushes.Yellow, points);
            }

            // Create new icon handle BEFORE destroying the old one, so the tray icon is
            // never pointing at an invalid handle. Icon.FromHandle does not take ownership
            // of the handle — we must call DestroyIcon ourselves when we're done with it.
            IntPtr newHandle = bitmap.GetHicon();
            IntPtr oldHandle = _trayIconHandle;
            _trayIconHandle = newHandle;
            notifyIcon.Icon = Icon.FromHandle(newHandle);
            if (oldHandle != IntPtr.Zero)
                DestroyIcon(oldHandle);
        }
    }
}