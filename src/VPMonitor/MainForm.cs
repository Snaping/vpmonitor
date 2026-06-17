using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using VPMonitor.Core;
using VPMonitor.IPC;
using VPMonitor.Models;
using VPMonitor.Monitoring;
using VPMonitor.UI;

namespace VPMonitor;

public class MainForm : Form
{
    private readonly Logger _logger;
    private readonly ProcessManager _processManager;
    private readonly PerformanceMonitor _performanceMonitor;
    private readonly ThresholdMonitor _thresholdMonitor;
    private readonly StressTester _stressTester;
    private IpcClient? _ipcClient;
    private string _testProcessPipeName = "VPMonitor_TestProcess";

    private WaveformChart _cpuChart = null!;
    private WaveformChart _memoryChart = null!;
    private WaveformChart _diskChart = null!;

    private TextBox _txtExePath = null!;
    private TextBox _txtArguments = null!;
    private TextBox _txtProcessId = null!;
    private NumericUpDown _nudCpuThreshold = null!;
    private NumericUpDown _nudCpuDuration = null!;
    private NumericUpDown _nudMemoryThreshold = null!;
    private NumericUpDown _nudMemoryDuration = null!;
    private ComboBox _cmbCpuAction = null!;
    private ComboBox _cmbMemoryAction = null!;
    private NumericUpDown _nudCpuLoad = null!;
    private NumericUpDown _nudMemoryLoad = null!;
    private RichTextBox _rtbLog = null!;
    private Label _lblProcessInfo = null!;
    private Label _lblThreadsHandles = null!;
    private Label _lblChildProcesses = null!;
    private Label _lblStatus = null!;
    private Button _btnStartProcess = null!;
    private Button _btnAttachProcess = null!;
    private Button _btnStopProcess = null!;
    private Button _btnSuspendProcess = null!;
    private Button _btnResumeProcess = null!;
    private Button _btnLowerPriority = null!;
    private Button _btnRestorePriority = null!;
    private Button _btnStartSelfCpu = null!;
    private Button _btnStopSelfCpu = null!;
    private Button _btnStartSelfMemory = null!;
    private Button _btnStopSelfMemory = null!;
    private Button _btnStartTestProcess = null!;
    private Button _btnConnectIpc = null!;
    private Button _btnTestHighCpu = null!;
    private Button _btnTestHighMemory = null!;
    private Button _btnTestDisk = null!;
    private Button _btnTestChild = null!;
    private Button _btnTestCrash = null!;
    private Button _btnTestHang = null!;
    private Button _btnTestStopAll = null!;
    private CheckBox _chkCpuThresholdEnabled = null!;
    private CheckBox _chkMemoryThresholdEnabled = null!;
    private System.Windows.Forms.Timer _childProcessTimer = null!;

    public MainForm()
    {
        _logger = new Logger();
        _processManager = new ProcessManager(_logger);
        _performanceMonitor = new PerformanceMonitor(_processManager, _logger);
        _thresholdMonitor = new ThresholdMonitor(_processManager, _performanceMonitor, _logger);
        _stressTester = new StressTester(_logger);

        _logger.LogAdded += OnLogAdded;
        _performanceMonitor.MetricsUpdated += OnMetricsUpdated;
        _processManager.ProcessExited += OnProcessExited;
        _thresholdMonitor.ThresholdTriggered += OnThresholdTriggered;

        InitializeComponent();
        InitializeUI();
        LoadSettings();
    }

    private void InitializeComponent()
    {
        Text = "虚拟机进程监控与资源限制工具 - VP Monitor";
        Size = new Size(1400, 900);
        MinimumSize = new Size(1200, 800);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(45, 45, 48);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);

        var mainContainer = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 900,
            FixedPanel = FixedPanel.Panel2,
            BackColor = Color.FromArgb(30, 30, 30),
            SplitterWidth = 5
        };

        var leftPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 48) };
        var rightPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 48) };

        mainContainer.Panel1.Controls.Add(leftPanel);
        mainContainer.Panel2.Controls.Add(rightPanel);
        Controls.Add(mainContainer);

        CreateCharts(leftPanel);
        CreateControlPanel(rightPanel);

        _childProcessTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _childProcessTimer.Tick += (s, e) =>
        {
            if (_processManager.IsProcessRunning)
            {
                _processManager.RefreshChildProcesses();
                UpdateProcessInfo();
            }
        };
        _childProcessTimer.Start();

        FormClosing += OnFormClosing;
    }

    private void CreateCharts(Panel parent)
    {
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(45, 45, 48)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));

        var headerPanel = CreateHeaderPanel();
        mainLayout.Controls.Add(headerPanel, 0, 0);

        _cpuChart = new WaveformChart
        {
            Dock = DockStyle.Fill,
            Title = "CPU 使用率",
            Unit = "%",
            MaxValue = 100,
            LineColor = Color.FromArgb(244, 67, 54),
            WarningColor = Color.FromArgb(255, 193, 7)
        };
        mainLayout.Controls.Add(_cpuChart, 0, 1);

        _memoryChart = new WaveformChart
        {
            Dock = DockStyle.Fill,
            Title = "内存使用量",
            Unit = "MB",
            MaxValue = 2000,
            LineColor = Color.FromArgb(33, 150, 243)
        };
        mainLayout.Controls.Add(_memoryChart, 0, 2);

        _diskChart = new WaveformChart
        {
            Dock = DockStyle.Fill,
            Title = "磁盘读写速率",
            Unit = "KB/s",
            MaxValue = 10000,
            LineColor = Color.FromArgb(76, 175, 80)
        };
        mainLayout.Controls.Add(_diskChart, 0, 3);

        parent.Controls.Add(mainLayout);
    }

    private Panel CreateHeaderPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(45, 45, 48) };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 2,
            Padding = new Padding(5)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

        _lblProcessInfo = CreateInfoLabel("进程: 未启动", "进程信息");
        _lblThreadsHandles = CreateInfoLabel("线程: 0 | 句柄: 0", "线程/句柄");
        _lblChildProcesses = CreateInfoLabel("子进程: 0", "子进程");
        _lblStatus = CreateInfoLabel("状态: 就绪", "状态");

        layout.Controls.Add(_lblProcessInfo, 0, 0);
        layout.Controls.Add(_lblThreadsHandles, 1, 0);
        layout.Controls.Add(_lblChildProcesses, 2, 0);
        layout.Controls.Add(_lblStatus, 3, 0);

        panel.Controls.Add(layout);
        return panel;
    }

    private Label CreateInfoLabel(string text, string title)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10f, FontStyle.Bold),
            Padding = new Padding(5)
        };
    }

    private void CreateControlPanel(Panel parent)
    {
        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 6,
            ColumnCount = 1,
            Padding = new Padding(10),
            BackColor = Color.FromArgb(45, 45, 48)
        };
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        mainLayout.Controls.Add(CreateProcessControlPanel(), 0, 0);
        mainLayout.Controls.Add(CreateThresholdPanel(), 0, 1);
        mainLayout.Controls.Add(CreateSelfStressPanel(), 0, 2);
        mainLayout.Controls.Add(CreateTestProcessPanel(), 0, 3);
        mainLayout.Controls.Add(CreateTestCommandsPanel(), 0, 4);
        mainLayout.Controls.Add(CreateLogPanel(), 0, 5);

        parent.Controls.Add(mainLayout);
    }

    private GroupBox CreateProcessControlPanel()
    {
        var groupBox = new GroupBox
        {
            Text = "进程控制",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Padding = new Padding(10),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 3,
            Padding = new Padding(5)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));

        _txtExePath = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "可执行文件路径",
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9f)
        };
        _txtArguments = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "命令行参数",
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9f)
        };
        _btnStartProcess = CreateButton("启动进程", Color.FromArgb(76, 175, 80));
        _btnStartProcess.Click += (s, e) => StartProcess();

        _txtProcessId = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "进程ID (PID)",
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9f)
        };
        _btnAttachProcess = CreateButton("附加进程", Color.FromArgb(33, 150, 243));
        _btnAttachProcess.Click += (s, e) => AttachProcess();
        _btnStopProcess = CreateButton("终止进程", Color.FromArgb(244, 67, 54));
        _btnStopProcess.Click += (s, e) => StopProcess();
        _btnStopProcess.Enabled = false;

        _btnSuspendProcess = CreateButton("挂起", Color.FromArgb(255, 152, 0));
        _btnSuspendProcess.Click += (s, e) => SuspendProcess();
        _btnSuspendProcess.Enabled = false;
        _btnResumeProcess = CreateButton("恢复", Color.FromArgb(255, 152, 0));
        _btnResumeProcess.Click += (s, e) => ResumeProcess();
        _btnResumeProcess.Enabled = false;
        _btnLowerPriority = CreateButton("降低优先级", Color.FromArgb(156, 39, 176));
        _btnLowerPriority.Click += (s, e) => LowerPriority();
        _btnLowerPriority.Enabled = false;
        _btnRestorePriority = CreateButton("恢复优先级", Color.FromArgb(156, 39, 176));
        _btnRestorePriority.Click += (s, e) => RestorePriority();
        _btnRestorePriority.Enabled = false;

        layout.Controls.Add(_txtExePath, 0, 0);
        layout.Controls.Add(_txtArguments, 1, 0);
        layout.Controls.Add(_btnStartProcess, 2, 0);
        layout.Controls.Add(_txtProcessId, 0, 1);
        layout.Controls.Add(_btnAttachProcess, 1, 1);
        layout.Controls.Add(_btnStopProcess, 2, 1);
        layout.Controls.Add(_btnSuspendProcess, 0, 2);
        layout.Controls.Add(_btnResumeProcess, 1, 2);
        layout.Controls.Add(_btnLowerPriority, 0, 2);
        layout.SetColumnSpan(_btnLowerPriority, 1);
        layout.Controls.Add(_btnRestorePriority, 1, 2);

        var buttonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        buttonRow.Controls.Add(_btnSuspendProcess, 0, 0);
        buttonRow.Controls.Add(_btnResumeProcess, 1, 0);
        layout.Controls.Add(buttonRow, 0, 2);

        var buttonRow2 = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        buttonRow2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        buttonRow2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        buttonRow2.Controls.Add(_btnLowerPriority, 0, 0);
        buttonRow2.Controls.Add(_btnRestorePriority, 1, 0);
        layout.Controls.Add(buttonRow2, 1, 2);

        layout.Controls.Add(_btnStopProcess, 2, 2);

        groupBox.Controls.Add(layout);
        return groupBox;
    }

    private GroupBox CreateThresholdPanel()
    {
        var groupBox = new GroupBox
        {
            Text = "阈值设置",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Padding = new Padding(10),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 5,
            Padding = new Padding(5)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 30));

        _chkCpuThresholdEnabled = new CheckBox
        {
            Text = "CPU",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Checked = true,
            Font = new Font("Segoe UI", 9f)
        };
        _chkCpuThresholdEnabled.CheckedChanged += (s, e) =>
        {
            _thresholdMonitor.CpuConfig.Enabled = _chkCpuThresholdEnabled.Checked;
            _cpuChart.WarningThreshold = _chkCpuThresholdEnabled.Checked ? (double)_nudCpuThreshold.Value : double.NaN;
        };

        _chkMemoryThresholdEnabled = new CheckBox
        {
            Text = "内存",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Checked = true,
            Font = new Font("Segoe UI", 9f)
        };
        _chkMemoryThresholdEnabled.CheckedChanged += (s, e) =>
        {
            _thresholdMonitor.MemoryConfig.Enabled = _chkMemoryThresholdEnabled.Checked;
            _memoryChart.WarningThreshold = _chkMemoryThresholdEnabled.Checked ? (double)_nudMemoryThreshold.Value : double.NaN;
        };

        _nudCpuThreshold = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 10,
            Maximum = 100,
            Value = 70,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f)
        };
        _nudCpuThreshold.ValueChanged += (s, e) =>
        {
            _thresholdMonitor.CpuConfig.CpuPercentage = (double)_nudCpuThreshold.Value;
            if (_chkCpuThresholdEnabled.Checked) _cpuChart.WarningThreshold = (double)_nudCpuThreshold.Value;
        };

        _nudCpuDuration = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 1,
            Maximum = 300,
            Value = 10,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f)
        };
        _nudCpuDuration.ValueChanged += (s, e) =>
        {
            _thresholdMonitor.CpuConfig.DurationSeconds = (int)_nudCpuDuration.Value;
        };

        _cmbCpuAction = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f)
        };
        _cmbCpuAction.Items.AddRange(new object[] { "仅记录", "降低优先级", "挂起进程", "终止进程" });
        _cmbCpuAction.SelectedIndex = 0;
        _cmbCpuAction.SelectedIndexChanged += (s, e) =>
        {
            _thresholdMonitor.CpuConfig.Action = (ThresholdAction)_cmbCpuAction.SelectedIndex;
        };

        _nudMemoryThreshold = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 50,
            Maximum = 16384,
            Value = 500,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f)
        };
        _nudMemoryThreshold.ValueChanged += (s, e) =>
        {
            _thresholdMonitor.MemoryConfig.MemoryMb = (double)_nudMemoryThreshold.Value;
            if (_chkMemoryThresholdEnabled.Checked) _memoryChart.WarningThreshold = (double)_nudMemoryThreshold.Value;
        };

        _nudMemoryDuration = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 1,
            Maximum = 300,
            Value = 10,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f)
        };
        _nudMemoryDuration.ValueChanged += (s, e) =>
        {
            _thresholdMonitor.MemoryConfig.DurationSeconds = (int)_nudMemoryDuration.Value;
        };

        _cmbMemoryAction = new ComboBox
        {
            Dock = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f)
        };
        _cmbMemoryAction.Items.AddRange(new object[] { "仅记录", "降低优先级", "挂起进程", "终止进程" });
        _cmbMemoryAction.SelectedIndex = 0;
        _cmbMemoryAction.SelectedIndexChanged += (s, e) =>
        {
            _thresholdMonitor.MemoryConfig.Action = (ThresholdAction)_cmbMemoryAction.SelectedIndex;
        };

        var lbl1 = new Label { Text = "阈值", Dock = DockStyle.Fill, ForeColor = Color.LightGray, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 8f) };
        var lbl2 = new Label { Text = "持续(s)", Dock = DockStyle.Fill, ForeColor = Color.LightGray, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 8f) };
        var lbl3 = new Label { Text = "动作", Dock = DockStyle.Fill, ForeColor = Color.LightGray, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 8f) };

        layout.Controls.Add(new Label(), 0, 0);
        layout.Controls.Add(lbl1, 1, 0);
        layout.Controls.Add(lbl2, 2, 0);
        layout.Controls.Add(lbl3, 3, 0);

        layout.Controls.Add(_chkCpuThresholdEnabled, 0, 1);
        layout.Controls.Add(_nudCpuThreshold, 1, 1);
        layout.Controls.Add(_nudCpuDuration, 2, 1);
        layout.Controls.Add(_cmbCpuAction, 3, 1);

        layout.Controls.Add(_chkMemoryThresholdEnabled, 0, 2);
        layout.Controls.Add(_nudMemoryThreshold, 1, 2);
        layout.Controls.Add(_nudMemoryDuration, 2, 2);
        layout.Controls.Add(_cmbMemoryAction, 3, 2);

        groupBox.Controls.Add(layout);
        return groupBox;
    }

    private GroupBox CreateSelfStressPanel()
    {
        var groupBox = new GroupBox
        {
            Text = "自身压力测试",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Padding = new Padding(10),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 4,
            Padding = new Padding(5)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

        var lblCpu = new Label { Text = "CPU负载(%)", Dock = DockStyle.Fill, ForeColor = Color.LightGray, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 8f) };
        var lblMem = new Label { Text = "内存(MB)", Dock = DockStyle.Fill, ForeColor = Color.LightGray, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 8f) };

        _nudCpuLoad = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 10,
            Maximum = 100,
            Value = 70,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f)
        };
        _nudCpuLoad.ValueChanged += (s, e) => _stressTester.CpuLoadLevel = (int)_nudCpuLoad.Value;

        _nudMemoryLoad = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = 50,
            Maximum = 2048,
            Value = 200,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9f)
        };
        _nudMemoryLoad.ValueChanged += (s, e) => _stressTester.MemorySizeMb = (int)_nudMemoryLoad.Value;

        _btnStartSelfCpu = CreateButton("开始CPU", Color.FromArgb(244, 67, 54));
        _btnStartSelfCpu.Click += (s, e) =>
        {
            _stressTester.CpuLoadLevel = (int)_nudCpuLoad.Value;
            _stressTester.StartCpuStress();
            _btnStartSelfCpu.Enabled = false;
            _btnStopSelfCpu.Enabled = true;
        };

        _btnStopSelfCpu = CreateButton("停止CPU", Color.FromArgb(76, 175, 80));
        _btnStopSelfCpu.Enabled = false;
        _btnStopSelfCpu.Click += (s, e) =>
        {
            _stressTester.StopCpuStress();
            _btnStartSelfCpu.Enabled = true;
            _btnStopSelfCpu.Enabled = false;
        };

        _btnStartSelfMemory = CreateButton("开始内存", Color.FromArgb(33, 150, 243));
        _btnStartSelfMemory.Click += (s, e) =>
        {
            _stressTester.MemorySizeMb = (int)_nudMemoryLoad.Value;
            _stressTester.StartMemoryStress();
            _btnStartSelfMemory.Enabled = false;
            _btnStopSelfMemory.Enabled = true;
        };

        _btnStopSelfMemory = CreateButton("停止内存", Color.FromArgb(76, 175, 80));
        _btnStopSelfMemory.Enabled = false;
        _btnStopSelfMemory.Click += (s, e) =>
        {
            _stressTester.StopMemoryStress();
            _btnStartSelfMemory.Enabled = true;
            _btnStopSelfMemory.Enabled = false;
        };

        layout.Controls.Add(lblCpu, 0, 0);
        layout.Controls.Add(lblMem, 1, 0);
        layout.Controls.Add(new Label(), 2, 0);
        layout.Controls.Add(new Label(), 3, 0);
        layout.Controls.Add(_nudCpuLoad, 0, 1);
        layout.Controls.Add(_nudMemoryLoad, 1, 1);

        var btnCpuLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        btnCpuLayout.Controls.Add(_btnStartSelfCpu, 0, 0);
        btnCpuLayout.Controls.Add(_btnStopSelfCpu, 0, 1);
        layout.Controls.Add(btnCpuLayout, 2, 1);

        var btnMemLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        btnMemLayout.Controls.Add(_btnStartSelfMemory, 0, 0);
        btnMemLayout.Controls.Add(_btnStopSelfMemory, 0, 1);
        layout.Controls.Add(btnMemLayout, 3, 1);

        groupBox.Controls.Add(layout);
        return groupBox;
    }

    private GroupBox CreateTestProcessPanel()
    {
        var groupBox = new GroupBox
        {
            Text = "测试进程控制",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Padding = new Padding(10),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 3,
            Padding = new Padding(5)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));

        var txtPipeName = new TextBox
        {
            Dock = DockStyle.Fill,
            PlaceholderText = "管道名称",
            Text = _testProcessPipeName,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Segoe UI", 9f)
        };
        txtPipeName.TextChanged += (s, e) => _testProcessPipeName = txtPipeName.Text;

        _btnStartTestProcess = CreateButton("启动测试进程", Color.FromArgb(76, 175, 80));
        _btnStartTestProcess.Click += async (s, e) => await StartTestProcess();

        _btnConnectIpc = CreateButton("连接IPC", Color.FromArgb(33, 150, 243));
        _btnConnectIpc.Click += async (s, e) => await ConnectIpc();
        _btnConnectIpc.Enabled = false;

        layout.Controls.Add(txtPipeName, 0, 0);
        layout.Controls.Add(_btnStartTestProcess, 1, 0);
        layout.Controls.Add(_btnConnectIpc, 2, 0);

        groupBox.Controls.Add(layout);
        return groupBox;
    }

    private GroupBox CreateTestCommandsPanel()
    {
        var groupBox = new GroupBox
        {
            Text = "测试命令 (IPC)",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Padding = new Padding(10),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 4,
            Padding = new Padding(5)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

        _btnTestHighCpu = CreateButton("高CPU", Color.FromArgb(244, 67, 54));
        _btnTestHighCpu.Click += async (s, e) =>
        {
            if (_ipcClient != null) await _ipcClient.StartHighCpuAsync(70);
        };
        _btnTestHighCpu.Enabled = false;

        _btnTestHighMemory = CreateButton("高内存", Color.FromArgb(33, 150, 243));
        _btnTestHighMemory.Click += async (s, e) =>
        {
            if (_ipcClient != null) await _ipcClient.StartHighMemoryAsync(300);
        };
        _btnTestHighMemory.Enabled = false;

        _btnTestDisk = CreateButton("磁盘活动", Color.FromArgb(76, 175, 80));
        _btnTestDisk.Click += async (s, e) =>
        {
            if (_ipcClient != null) await _ipcClient.StartDiskActivityAsync();
        };
        _btnTestDisk.Enabled = false;

        _btnTestChild = CreateButton("创建子进程", Color.FromArgb(156, 39, 176));
        _btnTestChild.Click += async (s, e) =>
        {
            if (_ipcClient != null) await _ipcClient.CreateChildProcessAsync();
        };
        _btnTestChild.Enabled = false;

        _btnTestCrash = CreateButton("崩溃", Color.FromArgb(183, 28, 28));
        _btnTestCrash.Click += async (s, e) =>
        {
            if (_ipcClient != null) await _ipcClient.CrashProcessAsync();
            _ipcClient?.Dispose();
            _ipcClient = null;
            UpdateIpcButtons(false);
        };
        _btnTestCrash.Enabled = false;

        _btnTestHang = CreateButton("挂起", Color.FromArgb(255, 152, 0));
        _btnTestHang.Click += async (s, e) =>
        {
            if (_ipcClient != null) await _ipcClient.HangProcessAsync();
        };
        _btnTestHang.Enabled = false;

        _btnTestStopAll = CreateButton("停止所有", Color.FromArgb(76, 175, 80));
        _btnTestStopAll.Click += async (s, e) =>
        {
            if (_ipcClient != null) await _ipcClient.StopAllAsync();
        };
        _btnTestStopAll.Enabled = false;

        var btnExit = CreateButton("退出", Color.FromArgb(244, 67, 54));
        btnExit.Click += async (s, e) =>
        {
            if (_ipcClient != null) await _ipcClient.ExitProcessAsync();
            _ipcClient?.Dispose();
            _ipcClient = null;
            UpdateIpcButtons(false);
        };
        btnExit.Enabled = false;

        layout.Controls.Add(_btnTestHighCpu, 0, 0);
        layout.Controls.Add(_btnTestHighMemory, 1, 0);
        layout.Controls.Add(_btnTestDisk, 2, 0);
        layout.Controls.Add(_btnTestChild, 3, 0);
        layout.Controls.Add(_btnTestCrash, 0, 1);
        layout.Controls.Add(_btnTestHang, 1, 1);
        layout.Controls.Add(_btnTestStopAll, 2, 1);
        layout.Controls.Add(btnExit, 3, 1);

        groupBox.Controls.Add(layout);
        return groupBox;
    }

    private GroupBox CreateLogPanel()
    {
        var groupBox = new GroupBox
        {
            Text = "日志",
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Padding = new Padding(10),
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var buttonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1
        };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));

        var btnClearLog = CreateButton("清空日志", Color.FromArgb(100, 100, 100));
        btnClearLog.Click += (s, e) => _rtbLog.Clear();

        var btnOpenLog = CreateButton("打开日志文件", Color.FromArgb(100, 100, 100));
        btnOpenLog.Click += (s, e) =>
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", _logger.GetLogFilePath());
            }
            catch { }
        };

        var lblLogPath = new Label
        {
            Text = Path.GetFileName(_logger.GetLogFilePath()),
            Dock = DockStyle.Fill,
            ForeColor = Color.LightGray,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 8f)
        };

        buttonRow.Controls.Add(btnClearLog, 0, 0);
        buttonRow.Controls.Add(btnOpenLog, 1, 0);
        buttonRow.Controls.Add(lblLogPath, 2, 0);

        _rtbLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(20, 20, 20),
            ForeColor = Color.White,
            Font = new Font("Consolas", 9f),
            ReadOnly = true,
            BorderStyle = BorderStyle.FixedSingle
        };

        layout.Controls.Add(buttonRow, 0, 0);
        layout.Controls.Add(_rtbLog, 0, 1);

        groupBox.Controls.Add(layout);
        return groupBox;
    }

    private Button CreateButton(string text, Color color)
    {
        return new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            BackColor = color,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(2),
            Padding = new Padding(3)
        };
    }

    private void InitializeUI()
    {
        _cpuChart.WarningThreshold = (double)_nudCpuThreshold.Value;
        _memoryChart.WarningThreshold = (double)_nudMemoryThreshold.Value;
    }

    private void LoadSettings()
    {
        _thresholdMonitor.CpuConfig.CpuPercentage = (double)_nudCpuThreshold.Value;
        _thresholdMonitor.CpuConfig.DurationSeconds = (int)_nudCpuDuration.Value;
        _thresholdMonitor.CpuConfig.Action = (ThresholdAction)_cmbCpuAction.SelectedIndex;
        _thresholdMonitor.CpuConfig.Enabled = _chkCpuThresholdEnabled.Checked;

        _thresholdMonitor.MemoryConfig.MemoryMb = (double)_nudMemoryThreshold.Value;
        _thresholdMonitor.MemoryConfig.DurationSeconds = (int)_nudMemoryDuration.Value;
        _thresholdMonitor.MemoryConfig.Action = (ThresholdAction)_cmbMemoryAction.SelectedIndex;
        _thresholdMonitor.MemoryConfig.Enabled = _chkMemoryThresholdEnabled.Checked;
    }

    private void StartProcess()
    {
        var exePath = _txtExePath.Text.Trim();
        if (string.IsNullOrEmpty(exePath))
        {
            MessageBox.Show("请输入可执行文件路径", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (!File.Exists(exePath))
        {
            MessageBox.Show("文件不存在", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (_processManager.StartProcess(exePath, _txtArguments.Text))
        {
            StartMonitoring();
        }
    }

    private void AttachProcess()
    {
        if (int.TryParse(_txtProcessId.Text, out var pid))
        {
            if (_processManager.AttachToProcess(pid))
            {
                StartMonitoring();
            }
        }
        else
        {
            MessageBox.Show("请输入有效的进程ID", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void StartMonitoring()
    {
        _performanceMonitor.Start();
        _thresholdMonitor.Start();

        _btnStartProcess.Enabled = false;
        _btnAttachProcess.Enabled = false;
        _btnStopProcess.Enabled = true;
        _btnSuspendProcess.Enabled = true;
        _btnResumeProcess.Enabled = false;
        _btnLowerPriority.Enabled = true;
        _btnRestorePriority.Enabled = false;

        UpdateProcessInfo();
        _lblStatus.Text = "状态: 监控中";
        _lblStatus.ForeColor = Color.FromArgb(76, 175, 80);
    }

    private void StopProcess()
    {
        _performanceMonitor.Stop();
        _thresholdMonitor.Stop();
        _processManager.StopProcess();

        _cpuChart.Clear();
        _memoryChart.Clear();
        _diskChart.Clear();

        _btnStartProcess.Enabled = true;
        _btnAttachProcess.Enabled = true;
        _btnStopProcess.Enabled = false;
        _btnSuspendProcess.Enabled = false;
        _btnResumeProcess.Enabled = false;
        _btnLowerPriority.Enabled = false;
        _btnRestorePriority.Enabled = false;

        UpdateProcessInfo();
        _lblStatus.Text = "状态: 已停止";
        _lblStatus.ForeColor = Color.FromArgb(244, 67, 54);
    }

    private void SuspendProcess()
    {
        if (_processManager.SuspendProcess())
        {
            _btnSuspendProcess.Enabled = false;
            _btnResumeProcess.Enabled = true;
            UpdateProcessInfo();
        }
    }

    private void ResumeProcess()
    {
        if (_processManager.ResumeProcess())
        {
            _btnSuspendProcess.Enabled = true;
            _btnResumeProcess.Enabled = false;
            UpdateProcessInfo();
        }
    }

    private void LowerPriority()
    {
        if (_processManager.LowerPriority())
        {
            _btnLowerPriority.Enabled = false;
            _btnRestorePriority.Enabled = true;
        }
    }

    private void RestorePriority()
    {
        if (_processManager.RestorePriority())
        {
            _btnLowerPriority.Enabled = true;
            _btnRestorePriority.Enabled = false;
        }
    }

    private async System.Threading.Tasks.Task StartTestProcess()
    {
        try
        {
            var testProcessPath = Path.Combine(
                Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                "..", "..", "..", "..", "VPMonitor.TestProcess", "bin", "Debug", "net9.0", "VPMonitor.TestProcess.exe");

            if (!File.Exists(testProcessPath))
            {
                testProcessPath = Path.Combine(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                    "VPMonitor.TestProcess.exe");
            }

            if (!File.Exists(testProcessPath))
            {
                MessageBox.Show("请先编译测试进程项目", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            _testProcessPipeName = "VPMonitor_TestProcess_" + Guid.NewGuid().ToString("N").Substring(0, 8);

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = testProcessPath,
                Arguments = _testProcessPipeName,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            var process = System.Diagnostics.Process.Start(startInfo);
            if (process != null)
            {
                _logger.LogInfo("测试进程已启动", $"PID: {process.Id}, Pipe: {_testProcessPipeName}");
                await System.Threading.Tasks.Task.Delay(1000);
                _btnConnectIpc.Enabled = true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("启动测试进程失败", ex.Message);
            MessageBox.Show($"启动失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async System.Threading.Tasks.Task ConnectIpc()
    {
        try
        {
            _ipcClient?.Dispose();
            _ipcClient = new IpcClient(_testProcessPipeName, _logger);

            if (await _ipcClient.ConnectAsync(5000))
            {
                if (await _ipcClient.PingAsync())
                {
                    UpdateIpcButtons(true);
                    _logger.LogInfo("已连接到测试进程IPC");
                }
                else
                {
                    MessageBox.Show("连接成功但Ping失败", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            else
            {
                MessageBox.Show("连接测试进程IPC失败", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("连接IPC失败", ex.Message);
            MessageBox.Show($"连接失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateIpcButtons(bool connected)
    {
        _btnTestHighCpu.Enabled = connected;
        _btnTestHighMemory.Enabled = connected;
        _btnTestDisk.Enabled = connected;
        _btnTestChild.Enabled = connected;
        _btnTestCrash.Enabled = connected;
        _btnTestHang.Enabled = connected;
        _btnTestStopAll.Enabled = connected;
    }

    private void OnMetricsUpdated(object? sender, ProcessMetrics metrics)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnMetricsUpdated(sender, metrics)));
            return;
        }

        _cpuChart.AddDataPoint(metrics.CpuUsage);
        _memoryChart.AddDataPoint(metrics.MemoryUsageMb);
        _diskChart.AddDataPoint(metrics.DiskReadRate + metrics.DiskWriteRate);

        UpdateProcessInfo();
    }

    private void UpdateProcessInfo()
    {
        if (_processManager.TargetProcess != null && !_processManager.TargetProcess.HasExited)
        {
            var proc = _processManager.TargetProcess;
            var metrics = _performanceMonitor.CurrentMetrics;

            _lblProcessInfo.Text = $"进程: {proc.ProcessName} (PID: {proc.Id})";
            _lblThreadsHandles.Text = $"线程: {metrics.ThreadCount} | 句柄: {metrics.HandleCount}";
            _lblChildProcesses.Text = $"子进程: {_processManager.ChildProcesses.Count}";

            var statusText = _processManager.IsSuspended ? "已挂起" : "运行中";
            _lblStatus.Text = $"状态: {statusText}";
            _lblStatus.ForeColor = _processManager.IsSuspended ? Color.FromArgb(255, 152, 0) : Color.FromArgb(76, 175, 80);
        }
        else
        {
            _lblProcessInfo.Text = "进程: 未启动";
            _lblThreadsHandles.Text = "线程: 0 | 句柄: 0";
            _lblChildProcesses.Text = "子进程: 0";
            _lblStatus.Text = "状态: 就绪";
            _lblStatus.ForeColor = Color.White;
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnProcessExited(sender, e)));
            return;
        }

        _performanceMonitor.Stop();
        _thresholdMonitor.Stop();

        _btnStartProcess.Enabled = true;
        _btnAttachProcess.Enabled = true;
        _btnStopProcess.Enabled = false;
        _btnSuspendProcess.Enabled = false;
        _btnResumeProcess.Enabled = false;
        _btnLowerPriority.Enabled = false;
        _btnRestorePriority.Enabled = false;

        UpdateProcessInfo();
        _lblStatus.Text = "状态: 已退出";
        _lblStatus.ForeColor = Color.FromArgb(244, 67, 54);
    }

    private void OnThresholdTriggered(object? sender, ThresholdViolation violation)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnThresholdTriggered(sender, violation)));
            return;
        }

        if (violation.ActionTaken)
        {
            _lblStatus.Text = $"警报: {violation.Type}超限 - {violation.ActionDescription}";
            _lblStatus.ForeColor = Color.FromArgb(244, 67, 54);
        }
    }

    private void OnLogAdded(object? sender, LogEntry entry)
    {
        if (InvokeRequired)
        {
            Invoke(new Action(() => OnLogAdded(sender, entry)));
            return;
        }

        var color = entry.Level switch
        {
            "ALERT" => Color.FromArgb(255, 100, 100),
            "ERROR" => Color.FromArgb(244, 67, 54),
            "WARN" => Color.FromArgb(255, 193, 7),
            _ => Color.White
        };

        _rtbLog.SelectionStart = _rtbLog.TextLength;
        _rtbLog.SelectionColor = color;
        _rtbLog.AppendText(entry.ToString() + Environment.NewLine);
        _rtbLog.ScrollToCaret();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        try
        {
            _childProcessTimer.Stop();
            _performanceMonitor.Stop();
            _thresholdMonitor.Stop();
            _stressTester.Dispose();
            _ipcClient?.Dispose();
            _processManager.Dispose();
            _performanceMonitor.Dispose();
            _thresholdMonitor.Dispose();
            _logger.Dispose();
        }
        catch { }
    }
}
