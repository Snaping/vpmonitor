using System.Diagnostics;
using System.Drawing.Drawing2D;
using Newtonsoft.Json;
using VPMonitor.Controls;
using VPMonitor.Models;
using VPMonitor.Services;

namespace VPMonitor;

public partial class Form1 : Form
{
    private LogService _logService = null!;
    private ProcessMonitorService? _monitorService;
    private ThresholdManager? _thresholdManager;
    private StressTestService _stressTestService = null!;
    private IpcService? _ipcService;
    private System.Windows.Forms.Timer _monitorTimer = null!;

    private WaveformChart _cpuChart = null!;
    private WaveformChart _memoryChart = null!;
    private WaveformChart _diskChart = null!;

    private TextBox _txtProcessPath = null!;
    private NumericUpDown _numCpuThreshold = null!;
    private NumericUpDown _numCpuDuration = null!;
    private ComboBox _cmbCpuAction = null!;
    private NumericUpDown _numMemoryThreshold = null!;
    private NumericUpDown _numMemoryDuration = null!;
    private ComboBox _cmbMemoryAction = null!;
    private CheckBox _chkThresholdEnabled = null!;

    private Label _lblPid = null!;
    private Label _lblProcessName = null!;
    private Label _lblThreadCount = null!;
    private Label _lblHandleCount = null!;
    private Label _lblChildCount = null!;
    private Label _lblStatus = null!;

    private Button _btnStartProcess = null!;
    private Button _btnAttachProcess = null!;
    private Button _btnSuspend = null!;
    private Button _btnResume = null!;
    private Button _btnTerminate = null!;
    private Button _btnStartTestProcess = null!;

    private Button _btnStartSelfStress = null!;
    private Button _btnStopSelfStress = null!;
    private NumericUpDown _numSelfStressLevel = null!;
    private Button _btnAllocateMemory = null!;
    private NumericUpDown _numAllocateMB = null!;

    private RichTextBox _txtLog = null!;
    private Process? _testProcess;
    private string _ipcPipeName = "VPMonitor_IPC_Pipe";

    public Form1()
    {
        InitializeComponent();
        InitializeServices();
        BuildUI();
        Load += Form1_Load;
        FormClosing += Form1_FormClosing;
    }

    private void InitializeServices()
    {
        _logService = new LogService();
        _stressTestService = new StressTestService(_logService);
        _logService.LogAdded += LogService_LogAdded;

        _monitorTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000,
            Enabled = false
        };
        _monitorTimer.Tick += MonitorTimer_Tick;
    }

    private void Form1_Load(object? sender, EventArgs e)
    {
        _logService.LogInfo("Application started");
        UpdateUIState();
    }

    private void Form1_FormClosing(object? sender, FormClosingEventArgs e)
    {
        try
        {
            _monitorTimer.Stop();
            _monitorService?.Dispose();
            _ipcService?.Dispose();
            _stressTestService.Dispose();
            _testProcess?.Dispose();
            _logService.Dispose();
        }
        catch
        {
        }
    }

    private void BuildUI()
    {
        var mainSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 1000,
            FixedPanel = FixedPanel.Panel2,
            BorderStyle = BorderStyle.None
        };
        Controls.Add(mainSplit);

        BuildLeftPanel(mainSplit.Panel1);
        BuildRightPanel(mainSplit.Panel2);
    }

    private void BuildLeftPanel(Control parent)
    {
        var leftPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(10)
        };
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
        parent.Controls.Add(leftPanel);

        _cpuChart = CreateChart("CPU 使用率", "%", Color.FromArgb(52, 152, 219), 0, 100);
        _memoryChart = CreateChart("内存使用量", "MB", Color.FromArgb(46, 204, 113), 0, 4096);
        _diskChart = CreateChart("磁盘读写速率", "KB/s", Color.FromArgb(230, 126, 34), 0, 10000);

        leftPanel.Controls.Add(CreateChartPanel(_cpuChart, "CPU"), 0, 0);
        leftPanel.Controls.Add(CreateChartPanel(_memoryChart, "内存"), 0, 1);
        leftPanel.Controls.Add(CreateChartPanel(_diskChart, "磁盘"), 0, 2);
    }

    private WaveformChart CreateChart(string title, string unit, Color color, double min, double max)
    {
        return new WaveformChart
        {
            Dock = DockStyle.Fill,
            Title = title,
            Unit = unit,
            LineColor = color,
            MinValue = min,
            MaxValue = max,
            Padding = new Padding(5)
        };
    }

    private Panel CreateChartPanel(Control chart, string title)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(5)
        };

        var groupBox = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = title,
            Font = new Font("Arial", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(44, 62, 80)
        };
        groupBox.Controls.Add(chart);
        panel.Controls.Add(groupBox);

        return panel;
    }

    private void BuildRightPanel(Control parent)
    {
        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 5,
            ColumnCount = 1,
            Padding = new Padding(5)
        };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 200));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 180));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        parent.Controls.Add(rightPanel);

        rightPanel.Controls.Add(BuildProcessStartPanel(), 0, 0);
        rightPanel.Controls.Add(BuildProcessInfoPanel(), 0, 1);
        rightPanel.Controls.Add(BuildThresholdPanel(), 0, 2);
        rightPanel.Controls.Add(BuildStressTestPanel(), 0, 3);
        rightPanel.Controls.Add(BuildLogPanel(), 0, 4);
    }

    private GroupBox BuildProcessStartPanel()
    {
        var groupBox = CreateGroupBox("进程控制");
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 3,
            Padding = new Padding(5)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));

        panel.Controls.Add(new Label { Text = "路径:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        _txtProcessPath = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right, PlaceholderText = "选择或输入exe路径..." };
        panel.Controls.Add(_txtProcessPath, 1, 0);
        var btnBrowse = new Button { Text = "浏览...", Anchor = AnchorStyles.Left };
        btnBrowse.Click += BtnBrowse_Click;
        panel.Controls.Add(btnBrowse, 2, 0);

        _btnStartProcess = CreateButton("启动进程", Color.FromArgb(52, 152, 219));
        _btnStartProcess.Click += BtnStartProcess_Click;
        panel.Controls.Add(_btnStartProcess, 0, 1);
        panel.SetColumnSpan(_btnStartProcess, 2);

        _btnAttachProcess = CreateButton("附加进程", Color.FromArgb(155, 89, 182));
        _btnAttachProcess.Click += BtnAttachProcess_Click;
        panel.Controls.Add(_btnAttachProcess, 2, 1);

        _btnStartTestProcess = CreateButton("创建测试进程", Color.FromArgb(230, 126, 34));
        _btnStartTestProcess.Click += BtnStartTestProcess_Click;
        panel.Controls.Add(_btnStartTestProcess, 0, 2);
        panel.SetColumnSpan(_btnStartTestProcess, 3);

        groupBox.Controls.Add(panel);
        return groupBox;
    }

    private GroupBox BuildProcessInfoPanel()
    {
        var groupBox = CreateGroupBox("进程信息");
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 6,
            ColumnCount = 2,
            Padding = new Padding(5)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _lblPid = CreateValueLabel("-");
        _lblProcessName = CreateValueLabel("-");
        _lblThreadCount = CreateValueLabel("-");
        _lblHandleCount = CreateValueLabel("-");
        _lblChildCount = CreateValueLabel("-");
        _lblStatus = CreateValueLabel("未监控");

        panel.Controls.Add(new Label { Text = "PID:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        panel.Controls.Add(_lblPid, 1, 0);
        panel.Controls.Add(new Label { Text = "进程名:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        panel.Controls.Add(_lblProcessName, 1, 1);
        panel.Controls.Add(new Label { Text = "线程数:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        panel.Controls.Add(_lblThreadCount, 1, 2);
        panel.Controls.Add(new Label { Text = "句柄数:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        panel.Controls.Add(_lblHandleCount, 1, 3);
        panel.Controls.Add(new Label { Text = "子进程:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        panel.Controls.Add(_lblChildCount, 1, 4);
        panel.Controls.Add(new Label { Text = "状态:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 5);
        panel.Controls.Add(_lblStatus, 1, 5);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(5)
        };

        _btnSuspend = CreateButton("挂起", Color.FromArgb(241, 196, 15));
        _btnSuspend.Click += BtnSuspend_Click;
        _btnResume = CreateButton("恢复", Color.FromArgb(46, 204, 113));
        _btnResume.Click += BtnResume_Click;
        _btnTerminate = CreateButton("终止", Color.FromArgb(231, 76, 60));
        _btnTerminate.Click += BtnTerminate_Click;

        buttonPanel.Controls.Add(_btnSuspend);
        buttonPanel.Controls.Add(_btnResume);
        buttonPanel.Controls.Add(_btnTerminate);

        groupBox.Controls.Add(panel);
        groupBox.Controls.Add(buttonPanel);
        return groupBox;
    }

    private GroupBox BuildThresholdPanel()
    {
        var groupBox = CreateGroupBox("阈值设置");
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 5,
            Padding = new Padding(5)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 60));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _chkThresholdEnabled = new CheckBox
        {
            Text = "启用阈值检测",
            Checked = true,
            AutoSize = true
        };
        _chkThresholdEnabled.CheckedChanged += (s, e) =>
        {
            if (_thresholdManager != null)
                _thresholdManager.Config.Enabled = _chkThresholdEnabled.Checked;
        };
        panel.Controls.Add(_chkThresholdEnabled, 0, 0);
        panel.SetColumnSpan(_chkThresholdEnabled, 5);

        panel.Controls.Add(new Label { Text = "CPU阈值:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        _numCpuThreshold = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 70, DecimalPlaces = 0, Width = 60 };
        panel.Controls.Add(_numCpuThreshold, 1, 1);
        panel.Controls.Add(new Label { Text = "% 持续:", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 1);
        _numCpuDuration = new NumericUpDown { Minimum = 1, Maximum = 300, Value = 10, DecimalPlaces = 0, Width = 60 };
        panel.Controls.Add(_numCpuDuration, 3, 1);
        _cmbCpuAction = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbCpuAction.Items.AddRange(new object[] { "无动作", "降低优先级", "挂起", "终止" });
        _cmbCpuAction.SelectedIndex = 1;
        panel.Controls.Add(_cmbCpuAction, 4, 1);

        panel.Controls.Add(new Label { Text = "内存阈值:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        _numMemoryThreshold = new NumericUpDown { Minimum = 1, Maximum = 16384, Value = 1024, DecimalPlaces = 0, Width = 60 };
        panel.Controls.Add(_numMemoryThreshold, 1, 2);
        panel.Controls.Add(new Label { Text = "MB 持续:", AutoSize = true, Anchor = AnchorStyles.Left }, 2, 2);
        _numMemoryDuration = new NumericUpDown { Minimum = 1, Maximum = 300, Value = 10, DecimalPlaces = 0, Width = 60 };
        panel.Controls.Add(_numMemoryDuration, 3, 2);
        _cmbMemoryAction = new ComboBox { Width = 100, DropDownStyle = ComboBoxStyle.DropDownList };
        _cmbMemoryAction.Items.AddRange(new object[] { "无动作", "降低优先级", "挂起", "终止" });
        _cmbMemoryAction.SelectedIndex = 1;
        panel.Controls.Add(_cmbMemoryAction, 4, 2);

        var btnApply = CreateButton("应用设置", Color.FromArgb(52, 152, 219));
        btnApply.Click += BtnApplyThreshold_Click;
        panel.Controls.Add(btnApply, 0, 3);
        panel.SetColumnSpan(btnApply, 5);

        groupBox.Controls.Add(panel);
        return groupBox;
    }

    private GroupBox BuildStressTestPanel()
    {
        var groupBox = CreateGroupBox("压力测试");
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 4,
            Padding = new Padding(5)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));

        panel.Controls.Add(new Label { Text = "自身压力测试", AutoSize = true, Font = new Font("Arial", 9, FontStyle.Bold) }, 0, 0);
        panel.SetColumnSpan(panel.Controls[panel.Controls.Count - 1], 4);

        _btnStartSelfStress = CreateButton("启动CPU压力", Color.FromArgb(231, 76, 60));
        _btnStartSelfStress.Click += BtnStartSelfStress_Click;
        panel.Controls.Add(_btnStartSelfStress, 0, 1);

        _btnStopSelfStress = CreateButton("停止压力", Color.FromArgb(46, 204, 113));
        _btnStopSelfStress.Click += BtnStopSelfStress_Click;
        panel.Controls.Add(_btnStopSelfStress, 1, 1);

        panel.Controls.Add(new Label { Text = "压力等级:", AutoSize = true, Anchor = AnchorStyles.Right }, 2, 1);
        _numSelfStressLevel = new NumericUpDown { Minimum = 10, Maximum = 100, Value = 70, DecimalPlaces = 0, Dock = DockStyle.Fill };
        panel.Controls.Add(_numSelfStressLevel, 3, 1);

        _btnAllocateMemory = CreateButton("分配内存", Color.FromArgb(155, 89, 182));
        _btnAllocateMemory.Click += BtnAllocateMemory_Click;
        panel.Controls.Add(_btnAllocateMemory, 0, 2);
        panel.SetColumnSpan(_btnAllocateMemory, 2);

        panel.Controls.Add(new Label { Text = "大小(MB):", AutoSize = true, Anchor = AnchorStyles.Right }, 2, 2);
        _numAllocateMB = new NumericUpDown { Minimum = 10, Maximum = 2048, Value = 256, DecimalPlaces = 0, Dock = DockStyle.Fill };
        panel.Controls.Add(_numAllocateMB, 3, 2);

        var btnFreeMemory = CreateButton("释放内存", Color.FromArgb(52, 152, 219));
        btnFreeMemory.Click += (s, e) => _stressTestService.FreeMemory();
        panel.Controls.Add(btnFreeMemory, 0, 3);
        panel.SetColumnSpan(btnFreeMemory, 4);

        groupBox.Controls.Add(panel);
        return groupBox;
    }

    private GroupBox BuildLogPanel()
    {
        var groupBox = CreateGroupBox("事件日志");
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(5)
        };

        _txtLog = new RichTextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            ReadOnly = true,
            Font = new Font("Consolas", 8),
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.FromArgb(220, 220, 220)
        };

        panel.Controls.Add(_txtLog);
        groupBox.Controls.Add(panel);
        return groupBox;
    }

    private GroupBox CreateGroupBox(string title)
    {
        return new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = title,
            Font = new Font("Arial", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(44, 62, 80),
            Padding = new Padding(5)
        };
    }

    private Label CreateValueLabel(string value)
    {
        return new Label
        {
            Text = value,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Font = new Font("Arial", 9, FontStyle.Bold),
            ForeColor = Color.FromArgb(41, 128, 185)
        };
    }

    private Button CreateButton(string text, Color color)
    {
        return new Button
        {
            Text = text,
            Height = 30,
            MinimumSize = new Size(80, 30),
            BackColor = color,
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Arial", 9, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(2)
        };
    }

    private void LogService_LogAdded(object? sender, LogEntry e)
    {
        if (IsDisposed) return;
        Invoke(() =>
        {
            var color = e.EventType switch
            {
                LogEventType.Error => Color.FromArgb(231, 76, 60),
                LogEventType.Warning => Color.FromArgb(241, 196, 15),
                LogEventType.ThresholdExceeded => Color.FromArgb(230, 126, 34),
                LogEventType.ActionTaken => Color.FromArgb(155, 89, 182),
                _ => Color.FromArgb(220, 220, 220)
            };

            _txtLog.SelectionStart = _txtLog.TextLength;
            _txtLog.SelectionColor = color;
            _txtLog.AppendText(e.ToString() + Environment.NewLine);
            _txtLog.ScrollToCaret();
        });
    }

    private void BtnBrowse_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "可执行文件 (*.exe)|*.exe|所有文件 (*.*)|*.*",
            Title = "选择要监控的进程"
        };
        if (dialog.ShowDialog() == DialogResult.OK)
        {
            _txtProcessPath.Text = dialog.FileName;
        }
    }

    private void BtnStartProcess_Click(object? sender, EventArgs e)
    {
        var path = _txtProcessPath.Text.Trim();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            MessageBox.Show("请选择有效的可执行文件路径", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        try
        {
            StopMonitoring();
            _monitorService = new ProcessMonitorService(path, string.Empty, _logService);
            StartMonitoring();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"启动进程失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnAttachProcess_Click(object? sender, EventArgs e)
    {
        using var dialog = new Form
        {
            Text = "选择进程",
            Width = 400,
            Height = 500,
            StartPosition = FormStartPosition.CenterParent
        };

        var listView = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false
        };
        listView.Columns.Add("PID", 60);
        listView.Columns.Add("进程名", 150);
        listView.Columns.Add("内存(MB)", 80);

        foreach (var p in Process.GetProcesses().OrderByDescending(p => p.WorkingSet64).Take(50))
        {
            try
            {
                var item = new ListViewItem(p.Id.ToString());
                item.SubItems.Add(p.ProcessName);
                item.SubItems.Add((p.WorkingSet64 / (1024.0 * 1024.0)).ToString("F1"));
                item.Tag = p.Id;
                listView.Items.Add(item);
            }
            catch { }
        }

        var btnOk = new Button { Text = "确定", Dock = DockStyle.Bottom, Height = 30, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "取消", Dock = DockStyle.Bottom, Height = 30, DialogResult = DialogResult.Cancel };

        dialog.Controls.Add(listView);
        dialog.Controls.Add(btnOk);
        dialog.Controls.Add(btnCancel);
        dialog.AcceptButton = btnOk;
        dialog.CancelButton = btnCancel;

        if (dialog.ShowDialog(this) == DialogResult.OK && listView.SelectedItems.Count > 0)
        {
            var tag = listView.SelectedItems[0].Tag;
            if (tag == null) return;
            var pid = (int)tag;
            try
            {
                StopMonitoring();
                _monitorService = new ProcessMonitorService(pid, _logService);
                StartMonitoring();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"附加进程失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void BtnStartTestProcess_Click(object? sender, EventArgs e)
    {
        try
        {
            StopMonitoring();
            StopIpcService();

            _ipcPipeName = $"VPMonitor_IPC_Pipe_{Guid.NewGuid():N}";
            _ipcService = new IpcService(_ipcPipeName, _logService);
            _ipcService.MessageReceived += IpcService_MessageReceived;
            _ipcService.ClientConnected += IpcService_ClientConnected;
            _ipcService.ClientDisconnected += IpcService_ClientDisconnected;
            _ipcService.StartServer();

            var testProcessPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "VPMonitor.TestProcess.exe");
            if (!File.Exists(testProcessPath))
            {
                testProcessPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..",
                    "VPMonitor.TestProcess", "bin", "Debug", "net9.0", "VPMonitor.TestProcess.exe");
            }

            if (!File.Exists(testProcessPath))
            {
                MessageBox.Show("测试进程可执行文件不存在，请先编译测试项目", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _testProcess = Process.Start(new ProcessStartInfo(testProcessPath, _ipcPipeName)
            {
                UseShellExecute = false
            });

            if (_testProcess != null)
            {
                _monitorService = new ProcessMonitorService(_testProcess.Id, _logService);
                StartMonitoring();
                _logService.LogInfo("测试进程已启动，等待IPC连接...", _testProcess.Id);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"创建测试进程失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void IpcService_MessageReceived(object? sender, IpcMessage e)
    {
        if (e.Command == IpcCommandType.StatusResponse && e.Payload != null)
        {
            try
            {
                var status = JsonConvert.DeserializeObject<TestProcessStatus>(e.Payload);
                if (status != null)
                {
                    _logService.LogInfo($"测试进程状态: {status.Status}, 线程数: {status.ThreadCount}", status.ProcessId);
                }
            }
            catch { }
        }
    }

    private void IpcService_ClientConnected(object? sender, EventArgs e)
    {
        _logService.LogInfo("测试进程IPC已连接");
        Invoke(UpdateUIState);
    }

    private void IpcService_ClientDisconnected(object? sender, EventArgs e)
    {
        _logService.LogInfo("测试进程IPC已断开");
        Invoke(UpdateUIState);
    }

    private void StopIpcService()
    {
        if (_ipcService != null)
        {
            _ipcService.MessageReceived -= IpcService_MessageReceived;
            _ipcService.ClientConnected -= IpcService_ClientConnected;
            _ipcService.ClientDisconnected -= IpcService_ClientDisconnected;
            _ipcService.Dispose();
            _ipcService = null;
        }
        _testProcess?.Dispose();
        _testProcess = null;
    }

    private void StartMonitoring()
    {
        if (_monitorService == null) return;

        _thresholdManager = new ThresholdManager(_monitorService, _logService);
        _thresholdManager.ThresholdViolation += ThresholdManager_ThresholdViolation;
        _thresholdManager.ActionExecuted += ThresholdManager_ActionExecuted;
        ApplyThresholdSettings();

        _monitorTimer.Start();
        _cpuChart.ClearData();
        _memoryChart.ClearData();
        _diskChart.ClearData();

        UpdateUIState();
    }

    private void StopMonitoring()
    {
        _monitorTimer.Stop();

        if (_thresholdManager != null)
        {
            _thresholdManager.ThresholdViolation -= ThresholdManager_ThresholdViolation;
            _thresholdManager.ActionExecuted -= ThresholdManager_ActionExecuted;
        }

        _monitorService?.Dispose();
        _monitorService = null;
        _thresholdManager = null;

        UpdateUIState();
    }

    private void MonitorTimer_Tick(object? sender, EventArgs e)
    {
        if (_monitorService == null) return;

        try
        {
            if (!_monitorService.IsProcessRunning())
            {
                _logService.LogWarning("监控的进程已退出");
                StopMonitoring();
                return;
            }

            var metrics = _monitorService.GetMetrics();

            _cpuChart.AddDataPoint(metrics.CpuUsage);
            _memoryChart.AddDataPoint(metrics.MemoryUsageMB);
            var diskTotal = (metrics.DiskReadBytesPerSec + metrics.DiskWriteBytesPerSec) / 1024.0;
            _diskChart.AddDataPoint(diskTotal);

            _thresholdManager?.CheckThresholds(metrics);

            UpdateProcessInfo(metrics);
        }
        catch (Exception ex)
        {
            _logService.LogError($"监控循环错误: {ex.Message}");
        }
    }

    private void UpdateProcessInfo(ProcessMetrics metrics)
    {
        if (IsDisposed) return;
        Invoke(() =>
        {
            _lblPid.Text = metrics.ProcessId.ToString();
            _lblProcessName.Text = metrics.ProcessName;
            _lblThreadCount.Text = metrics.ThreadCount.ToString();
            _lblHandleCount.Text = metrics.HandleCount.ToString();
            _lblChildCount.Text = metrics.ChildProcessCount.ToString();
            _lblStatus.Text = metrics.IsSuspended ? "已挂起" : "运行中";
            _lblStatus.ForeColor = metrics.IsSuspended ? Color.FromArgb(241, 196, 15) : Color.FromArgb(46, 204, 113);
        });
    }

    private void ThresholdManager_ThresholdViolation(object? sender, string e)
    {
        if (IsDisposed) return;
        Invoke(() =>
        {
            this.BackColor = Color.FromArgb(255, 235, 235);
            Task.Delay(200).ContinueWith(_ =>
            {
                if (!IsDisposed) Invoke(() => this.BackColor = Color.FromArgb(245, 245, 247));
            });
        });
    }

    private void ThresholdManager_ActionExecuted(object? sender, ThresholdAction e)
    {
        UpdateUIState();
    }

    private void BtnApplyThreshold_Click(object? sender, EventArgs e)
    {
        ApplyThresholdSettings();
        _logService.LogInfo("阈值设置已更新");
    }

    private void ApplyThresholdSettings()
    {
        if (_thresholdManager == null) return;

        _thresholdManager.Config = new ThresholdConfig
        {
            CpuThreshold = (double)_numCpuThreshold.Value,
            CpuDurationSeconds = (int)_numCpuDuration.Value,
            CpuAction = (ThresholdAction)_cmbCpuAction.SelectedIndex,
            MemoryThresholdMB = (double)_numMemoryThreshold.Value,
            MemoryDurationSeconds = (int)_numMemoryDuration.Value,
            MemoryAction = (ThresholdAction)_cmbMemoryAction.SelectedIndex,
            Enabled = _chkThresholdEnabled.Checked
        };
    }

    private void BtnSuspend_Click(object? sender, EventArgs e)
    {
        try
        {
            _monitorService?.SuspendProcess();
            UpdateUIState();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"挂起进程失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnResume_Click(object? sender, EventArgs e)
    {
        try
        {
            _monitorService?.ResumeProcess();
            UpdateUIState();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"恢复进程失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void BtnTerminate_Click(object? sender, EventArgs e)
    {
        if (MessageBox.Show("确定要终止该进程吗？此操作不可撤销。", "确认",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
        {
            try
            {
                _monitorService?.TerminateProcess();
                StopMonitoring();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"终止进程失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    private void BtnStartSelfStress_Click(object? sender, EventArgs e)
    {
        _stressTestService.StartCpuStress((int)_numSelfStressLevel.Value);
        UpdateUIState();
    }

    private void BtnStopSelfStress_Click(object? sender, EventArgs e)
    {
        _stressTestService.StopCpuStress();
        UpdateUIState();
    }

    private void BtnAllocateMemory_Click(object? sender, EventArgs e)
    {
        _stressTestService.AllocateMemory((int)_numAllocateMB.Value);
    }

    private void UpdateUIState()
    {
        var hasMonitor = _monitorService != null;
        var isSuspended = _monitorService?.CurrentMetrics.IsSuspended ?? false;

        _btnSuspend.Enabled = hasMonitor && !isSuspended;
        _btnResume.Enabled = hasMonitor && isSuspended;
        _btnTerminate.Enabled = hasMonitor;

        _btnStartSelfStress.Enabled = !_stressTestService.IsCpuStressRunning;
        _btnStopSelfStress.Enabled = _stressTestService.IsCpuStressRunning;

        if (!hasMonitor)
        {
            _lblStatus.Text = "未监控";
            _lblStatus.ForeColor = Color.Gray;
            _lblPid.Text = "-";
            _lblProcessName.Text = "-";
            _lblThreadCount.Text = "-";
            _lblHandleCount.Text = "-";
            _lblChildCount.Text = "-";
        }
    }
}
