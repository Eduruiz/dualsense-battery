using System.Drawing.Imaging;
using HidLibrary;
using System.Linq;
using System.Threading;

namespace DualSenseBattery.App;

public partial class Form1 : Form
{
    private const int VENDOR_ID = 0x054C;
    private const int PRODUCT_ID = 0x0CE6;

    private HidDevice? _controller;
    private bool _lowBatteryAlertSent = false;
    private bool _criticalBatteryAlertSent = false;
    private System.Windows.Forms.Timer _reconnectTimer;
    private bool _isBluetooth = false;
    private bool _controllerWasConnected = false;

    // UI Elements
    private GroupBox? gbControllerInfo;
    private Label? lblStatus;
    private Label? lblBattery;
    private ProgressBar? pbBattery;
    private ToolStripMenuItem? showToolStripMenuItem;


    public Form1()
    {
        InitializeComponent();
        SetupUI();

        _reconnectTimer = new System.Windows.Forms.Timer();
        _reconnectTimer.Interval = 5000; // Check for controllers every 5 seconds
        _reconnectTimer.Tick += ReconnectTimer_Tick;
        _reconnectTimer.Start();
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
        
        // Tray Icon Menu
        showToolStripMenuItem = new ToolStripMenuItem("Show");
        showToolStripMenuItem.Click += ShowToolStripMenuItem_Click;
        contextMenuStrip.Items.Insert(0, showToolStripMenuItem);

        notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
    }

    private void ShowWindow()
    {
        this.Show();
        this.WindowState = FormWindowState.Normal;
        this.BringToFront();
    }

    private void ShowToolStripMenuItem_Click(object? sender, EventArgs e)
    {
        ShowWindow();
    }

    private void NotifyIcon_DoubleClick(object? sender, EventArgs e)
    {
        ShowWindow();
    }

    private void Form1_Load(object sender, EventArgs e)
    {
        this.WindowState = FormWindowState.Minimized;
        this.ShowInTaskbar = false;
        notifyIcon.Icon = SystemIcons.Application; // Default icon
        notifyIcon.Text = "Searching for controller...";
        notifyIcon.Visible = true;
        timer.Start();
        TryConnectController();
        this.Hide();
    }
    
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Instead of closing, hide the window
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            this.Hide();
        }
        else
        {
            base.OnFormClosing(e);
        }
    }


    private void Form1_FormClosing(object sender, FormClosingEventArgs e)
    {
        notifyIcon.Visible = false;
        _controller?.CloseDevice();
        _reconnectTimer.Stop();
    }

    private void ReconnectTimer_Tick(object? sender, EventArgs e)
    {
        if (_controller == null || !_controller.IsConnected)
        {
            TryConnectController();
        }
    }

    private void TryConnectController()
    {
        _controller?.CloseDevice(); // Close any existing connection
        _controller = HidDevices.Enumerate(VENDOR_ID, PRODUCT_ID).FirstOrDefault();

        if (_controller == null)
        {
            UpdateUIForDisconnect();
        }
        else
        {
            _controller.OpenDevice();
            timer.Start();
            if (lblStatus != null) lblStatus.Text = "Status: Connected";
            notifyIcon.Text = "DualSense controller connected.";
        }
    }

    private void timer_Tick(object sender, EventArgs e)
    {
        if (_controller != null && _controller.IsConnected)
        {
            if (!_controllerWasConnected)
            {
                _controllerWasConnected = true;
                ShowConnectionNotification();
            }

            HidReport report = _controller.ReadReport();

            _isBluetooth = report.ReportId == 0x31;
            int batteryDataIndex = _isBluetooth ? 54 : 53;

            if (report.Data.Length > batteryDataIndex)
            {
                int battery = report.Data[batteryDataIndex];
                int batteryLevel = (battery & 0x0F) * 10;
                bool isCharging = (battery & 0x10) != 0;

                UpdateBatteryLevel(batteryLevel, isCharging);

                if (batteryLevel <= 20 && !isCharging && !_lowBatteryAlertSent)
                {
                    notifyIcon.ShowBalloonTip(3000, "DualSense Low Battery", $"Your DualSense controller battery is at {batteryLevel}%!", ToolTipIcon.Info);
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
        }
        else
        {
            if (_controllerWasConnected)
            {
                _controllerWasConnected = false;
                notifyIcon.ShowBalloonTip(3000, "Controller Disconnected", "The DualSense controller has been disconnected.", ToolTipIcon.Warning);
            }
            UpdateUIForDisconnect();
        }
    }

    private void UpdateUIForDisconnect()
    {
        timer.Stop();
        if (lblStatus != null) lblStatus.Text = "Status: Disconnected";
        if (lblBattery != null) lblBattery.Text = "Battery: N/A";
        if (pbBattery != null) pbBattery.Value = 0;
        notifyIcon.Icon = SystemIcons.Application;
        notifyIcon.Text = "DualSense controller disconnected.";
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

            IntPtr hIcon = bitmap.GetHicon();
            try
            {
                notifyIcon.Icon = Icon.FromHandle(hIcon);
            }
            finally
            {
                DestroyIcon(hIcon);
            }
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    extern static bool DestroyIcon(IntPtr handle);

    private void ShowConnectionNotification()
    {
        notifyIcon.ShowBalloonTip(3000, "Controller Connected", "DualSense controller has been detected.", ToolTipIcon.Info);
    }
}
