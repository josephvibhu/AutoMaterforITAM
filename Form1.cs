using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ITAS_QC_Tool
{
    public partial class Form1 : Form
    {
        // ==========================================
        // 1. LOW-LEVEL KEYBOARD HOOK SETUP
        // ==========================================
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        // UI Elements
        private RichTextBox reportBox;
        private Button exitButton;

        public Form1()
        {
            // Set up the full-screen trap UI
            this.WindowState = FormWindowState.Maximized;
            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true; // Stays on top of everything
            this.BackColor = System.Drawing.Color.FromArgb(30, 30, 30);
            this.ForeColor = System.Drawing.Color.White;

            reportBox = new RichTextBox
            {
                Location = new System.Drawing.Point(50, 50),
                Size = new System.Drawing.Size(800, 600),
                Font = new System.Drawing.Font("Consolas", 12),
                BackColor = System.Drawing.Color.FromArgb(40, 40, 40),
                ForeColor = System.Drawing.Color.LightGreen,
                ReadOnly = true
            };

            exitButton = new Button
            {
                Text = "QC Passed (Exit & Unlock)",
                Location = new System.Drawing.Point(50, 670),
                Size = new System.Drawing.Size(250, 50),
                BackColor = System.Drawing.Color.SteelBlue,
                FlatStyle = FlatStyle.Flat
            };
            exitButton.Click += (s, e) => Application.Exit();

            this.Controls.Add(reportBox);
            this.Controls.Add(exitButton);

            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            // Activate the keyboard trap and run hardware scan
            _hookID = SetHook(_proc);
            GenerateHardwareReport();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // Release the keyboard trap when the app closes
            UnhookWindowsHookEx(_hookID);
        }

        // ==========================================
        // 2. KEYBOARD TRAP LOGIC
        // ==========================================
        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                Keys key = (Keys)vkCode;

                // BLOCK: Windows Key, Alt, Control, and Escape combinations
                if (key == Keys.LWin || key == Keys.RWin || 
                    (Control.ModifierKeys == Keys.Alt && key == Keys.Tab) || 
                    (Control.ModifierKeys == Keys.Alt && key == Keys.Escape) ||
                    (Control.ModifierKeys == Keys.Control && key == Keys.Escape))
                {
                    return (IntPtr)1; // Returning 1 eats the keystroke, ignoring it
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // ==========================================
        // 3. HARDWARE QC SCANNER
        // ==========================================
        private void GenerateHardwareReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("========================================");
            sb.AppendLine("        ITAS C# HARDWARE REPORT         ");
            sb.AppendLine("========================================\n");

            // --- SYSTEM & SERIAL ---
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        sb.AppendLine($"Serial Number: {obj["SerialNumber"]}");
                    }
                }
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        sb.AppendLine($"CPU: {obj["Name"]}");
                    }
                }
            }
            catch { sb.AppendLine("System Info: Failed to read WMI."); }

            // --- STORAGE HEALTH (Native MSFT_PhysicalDisk) ---
            sb.AppendLine("\n--- STORAGE HEALTH ---");
            try
            {
                // Note: Requires Admin rights to read Microsoft\Windows\Storage
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = obj["FriendlyName"]?.ToString();
                        long size = Convert.ToInt64(obj["Size"]) / (1024 * 1024 * 1024);
                        sb.AppendLine($"Drive: {name} | Size: {size} GB");
                    }
                }
            }
            catch { sb.AppendLine("Storage: Run App as Admin for deep disk info."); }

            // --- BATTERY HEALTH ---
            sb.AppendLine("\n--- BATTERY STATUS ---");
            try
            {
                uint design = 0;
                uint full = 0;

                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM BatteryStaticData"))
                {
                    foreach (ManagementObject obj in searcher.Get()) { design = Convert.ToUInt32(obj["DesignedCapacity"]); }
                }
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM BatteryFullChargedCapacity"))
                {
                    foreach (ManagementObject obj in searcher.Get()) { full = Convert.ToUInt32(obj["FullChargedCapacity"]); }
                }

                if (design > 0)
                {
                    double health = Math.Round(((double)full / design) * 100, 2);
                    if (health > 100) health = 100;
                    sb.AppendLine($"Battery Health: {health}%");
                }
                else { sb.AppendLine("Battery: No battery detected."); }
            }
            catch { sb.AppendLine("Battery: Run App as Admin for WMI battery data."); }

            reportBox.Text = sb.ToString();
        }
    }
}