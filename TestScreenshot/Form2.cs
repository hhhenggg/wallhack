using Capture;
using Capture.Hook;
using Capture.Interface;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace TestScreenshot
{
    public partial class Form2 : Form
    {
        public Form2()
        {
            InitializeComponent();
        }

        private void Form2_Load(object sender, EventArgs e)
        {
        }

        private void btnInject_Click(object sender, EventArgs e)
        {
            if (_captureProcess == null)
            {
                btnInject.Enabled = false;
                AttachProcess();
                btnInject.Enabled = true;
            }
            else
            {
                HookManager.RemoveHookedProcess(_captureProcess.Process.Id);
                _captureProcess.CaptureInterface.Disconnect();
                _captureProcess = null;
            }

            if (_captureProcess != null)
            {
                btnInject.Text = "关闭";
            }
            else
            {
                btnInject.Text = "开启";
            }
        }

        int processId = 0;
        Process _process;
        CaptureProcess _captureProcess;
        private void AttachProcess()
        {
            string exeName = Path.GetFileNameWithoutExtension("Europa_Client.exe");

            Process[] processes = Process.GetProcessesByName(exeName);
            foreach (Process process in processes)
            {
                // Simply attach to the first one found.

                // If the process doesn't have a mainwindowhandle yet, skip it (we need to be able to get the hwnd to set foreground etc)
                if (process.MainWindowHandle == IntPtr.Zero)
                {
                    continue;
                }

                // Skip if the process is already hooked (and we want to hook multiple applications)
                if (HookManager.IsHooked(process.Id))
                {
                    continue;
                }

                Direct3DVersion direct3DVersion = Direct3DVersion.AutoDetect;

                CaptureConfig cc = new CaptureConfig()
                {
                    Direct3DVersion = direct3DVersion,
                    ShowOverlay = true,
                    WallhackEnabled = checkBox1.Checked,
                    WeaponEnabled = checkBox2.Checked
                };

                processId = process.Id;
                _process = process;

                var captureInterface = new CaptureInterface();
                captureInterface.RemoteMessage += new MessageReceivedEvent(CaptureInterface_RemoteMessage);
                _captureProcess = new CaptureProcess(process, cc, captureInterface);

                break;
            }
            Thread.Sleep(10);

            if (_captureProcess == null)
            {
                MessageBox.Show("No executable found matching: '" + exeName + "'");
            }
        }


        void CaptureInterface_RemoteMessage(MessageReceivedEventArgs message)
        {
        }
        private void Form1_FormClosed(object sender, FormClosedEventArgs e)
        {
            if (_captureProcess != null)
            {
                HookManager.RemoveHookedProcess(_captureProcess.Process.Id);
                _captureProcess.CaptureInterface.Disconnect();
                _captureProcess = null;
            }
        }
    }
}
