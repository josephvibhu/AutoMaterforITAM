using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace ITAS_QC_Tool
{
    public partial class Form1 : Form
    {
        // ==========================================
        // 1. KEYBOARD HOOK SETUP (OS-Level Interception)
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
        private Panel keyboardPanel;
        
        // Maps the physical Keys to their UI Label
        private Dictionary<Keys, Label> keyLabels = new Dictionary<Keys, Label>();
        public static Form1 Instance; // Allows the static hook to update the UI

        public Form1()
        {
            Instance = this;
            this.WindowState = FormWindowState.Maximized;
            this.FormBorderStyle = FormBorderStyle.None;
            this.TopMost = true; // Traps the technician inside the app
            this.BackColor = Color.FromArgb(30, 30, 30);
            this.ForeColor = Color.White;
            this.KeyPreview = true; // Allows form to read keystrokes before controls do

            // Setup Hardware Report Box (Left Side)
            reportBox = new RichTextBox
            {
                Location = new Point(30, 30),
                Size = new Size(500, 600),
                Font = new Font("Consolas", 11),
                BackColor = Color.FromArgb(40, 40, 40),
                ForeColor = Color.LightGreen,
                ReadOnly = true,
                BorderStyle = BorderStyle.None
            };

            // Setup Visual Keyboard Panel (Right Side)
            keyboardPanel = new Panel
            {
                Location = new Point(560, 30),
                Size = new Size(1300, 400),
                BackColor = Color.FromArgb(20, 20, 20)
            };

            // Setup Exit Button
            exitButton = new Button
            {
                Text = "QC Passed (Exit & Unlock)",
                Location = new Point(30, 650),
                Size = new Size(500, 60),
                BackColor = Color.SeaGreen,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 14, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            exitButton.FlatAppearance.BorderSize = 0;
            exitButton.Click += (s, e) => Application.Exit();

            this.Controls.Add(reportBox);
            this.Controls.Add(keyboardPanel);
            this.Controls.Add(exitButton);

            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;
            this.KeyDown += Form1_KeyDown; // Handle standard keys
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            BuildVisualKeyboard();
            _hookID = SetHook(_proc);
            GenerateHardwareReport();
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            UnhookWindowsHookEx(_hookID);
        }

        // ==========================================
        // 2. THE VISUAL KEYBOARD RENDERER
        // ==========================================
        private void BuildVisualKeyboard()
        {
            // A simplified grid layout of the keys
            Keys[][] layout = new Keys[][]
            {
                new Keys[] { Keys.Escape, Keys.F1, Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7, Keys.F8, Keys.F9, Keys.F10, Keys.F11, Keys.F12 },
                new Keys[] { Keys.Oemtilde, Keys.D1, Keys.D2, Keys.D3, Keys.D4, Keys.D5, Keys.D6, Keys.D7, Keys.D8, Keys.D9, Keys.D0, Keys.OemMinus, Keys.Oemplus, Keys.Back },
                new Keys[] { Keys.Tab, Keys.Q, Keys.W, Keys.E, Keys.R, Keys.T, Keys.Y, Keys.U, Keys.I, Keys.O, Keys.P, Keys.OemOpenBrackets, Keys.OemCloseBrackets, Keys.OemPipe },
                new Keys[] { Keys.Capital, Keys.A, Keys.S, Keys.D, Keys.F, Keys.G, Keys.H, Keys.J, Keys.K, Keys.L, Keys.OemSemicolon, Keys.OemQuotes, Keys.Enter },
                new Keys[] { Keys.LShiftKey, Keys.Z, Keys.X, Keys.C, Keys.V, Keys.B, Keys.N, Keys.M, Keys.Oemcomma, Keys.OemPeriod, Keys.OemQuestion, Keys.RShiftKey },
                new Keys[] { Keys.LControlKey, Keys.LWin, Keys.LMenu, Keys.Space, Keys.RMenu, Keys.RWin, Keys.Apps, Keys.RControlKey }
            };

            int startX = 20, startY = 20;
            int keySize = 50, spacing = 5;

            for (int row = 0; row < layout.Length; row++)
            {
                int currentX = startX;
                for (int col = 0; col < layout[row].Length; col++)
                {
                    Keys key = layout[row][col];
                    
                    // Dynamic width for special keys
                    int width = keySize;
                    if (key == Keys.Back || key == Keys.Tab || key == Keys.Capital || key == Keys.Enter || key == Keys.LShiftKey || key == Keys.RShiftKey) width = 90;
                    if (key == Keys.Space) width = 350;

                    Label lbl = new Label
                    {
                        Text = GetKeyDisplayName(key),
                        Size = new Size(width, keySize),
                        Location = new Point(currentX, startY + (row * (keySize + spacing))),
                        BackColor = Color.FromArgb(60, 60, 60),
                        ForeColor = Color.White,
                        TextAlign = ContentAlignment.MiddleCenter,
                        Font = new Font("Segoe UI", 9, FontStyle.Bold),
                        BorderStyle = BorderStyle.FixedSingle
                    };

                    keyLabels[key] = lbl;
                    keyboardPanel.Controls.Add(lbl);
                    currentX += width + spacing;
                }
            }
        }

        private string GetKeyDisplayName(Keys key)
        {
            // Translates ugly enum names into clean keyboard text
            switch (key)
            {
                case Keys.Oemtilde: return "~";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.OemCloseBrackets: return "]";
                case Keys.OemPipe: return "\\";
                case Keys.OemSemicolon: return ";";
                case Keys.OemQuotes: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                case Keys.LShiftKey: case Keys.RShiftKey: return "Shift";
                case Keys.LControlKey: case Keys.RControlKey: return "Ctrl";
                case Keys.LMenu: case Keys.RMenu: return "Alt";
                case Keys.LWin: case Keys.RWin: return "Win";
                case Keys.Apps: return "Menu";
                case Keys.Capital: return "Caps";
                case Keys.Back: return "Backspace";
                case (Keys.D0): return "0";
                case (Keys.D1): return "1"; // Handles D1-D9 cleanly enough for the prototype
                default: return key.ToString().Replace("D", ""); // strips 'D' from 'D2', etc.
            }
        }

        // ==========================================
        // 3. EVENT HANDLERS (Standard & Hooked)
        // ==========================================
        
        // This handles standard keys (A, B, C, Enter, etc.)
        private void Form1_KeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true; // Prevent default Windows ding sounds
            HighlightKey(e.KeyCode);
        }

        // This allows the static Hook to update the UI
        public void HighlightKey(Keys key)
        {
            if (keyLabels.ContainsKey(key))
            {
                keyLabels[key].BackColor = Color.LimeGreen;
                keyLabels[key].ForeColor = Color.Black;
            }
        }

        // The OS Hook intercepts keys BEFORE Windows processes them
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

                // 1. Tell the UI to turn the key green (even for blocked keys)
                if (Instance != null) { Instance.HighlightKey(key); }

                // 2. The Absolute Trap: Block Windows Key, Alt-Tab, Ctrl-Esc, Alt-F4
                if (key == Keys.LWin || key == Keys.RWin || 
                    (Control.ModifierKeys == Keys.Alt && key == Keys.Tab) || 
                    (Control.ModifierKeys == Keys.Alt && key == Keys.F4) || 
                    (Control.ModifierKeys == Keys.Control && key == Keys.Escape))
                {
                    return (IntPtr)1; // Destroys the keystroke
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        // ==========================================
        // 4. HARDWARE QC SCANNER (Direct WMI)
        // ==========================================
        private void GenerateHardwareReport()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("========================================");
            sb.AppendLine("        ITAS C# HARDWARE REPORT         ");
            sb.AppendLine("========================================\n");
            sb.AppendLine($"Date: {DateTime.Now}");
            sb.AppendLine($"System: {Environment.MachineName}\n");

            // --- SYSTEM INFO ---
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem"))
                    foreach (ManagementObject o in s.Get()) sb.AppendLine($"Model: {o["Model"]}");
                    
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT * FROM Win32_BIOS"))
                    foreach (ManagementObject o in s.Get()) sb.AppendLine($"Serial Number: {o["SerialNumber"]}");
                    
                using (ManagementObjectSearcher s = new ManagementObjectSearcher("SELECT * FROM Win32_Processor"))
                    foreach (ManagementObject o in s.Get()) sb.AppendLine($"CPU: {o["Name"]}");
            }
            catch { sb.AppendLine("System Info: Failed to read WMI."); }

            // --- STORAGE & BATTERY ---
            sb.AppendLine("\n--- STORAGE HEALTH ---");
            try
            {
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage", "SELECT * FROM MSFT_PhysicalDisk"))
                    foreach (ManagementObject o in s.Get())
                    {
                        if (o["Size"] != null) {
                            long size = Convert.ToInt64(o["Size"]) / (1024 * 1024 * 1024);
                            sb.AppendLine($"Drive: {o["FriendlyName"]} | {size} GB");
                        }
                    }
            }
            catch { sb.AppendLine("Storage: Requires 'requireAdministrator' in app.manifest"); }

            sb.AppendLine("\n--- BATTERY STATUS ---");
            try
            {
                uint design = 0, full = 0;
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\wmi", "SELECT DesignedCapacity FROM BatteryStaticData"))
                    foreach (ManagementObject o in s.Get()) design = Convert.ToUInt32(o["DesignedCapacity"]);
                    
                using (ManagementObjectSearcher s = new ManagementObjectSearcher(@"root\wmi", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
                    foreach (ManagementObject o in s.Get()) full = Convert.ToUInt32(o["FullChargedCapacity"]);

                if (design > 0)
                {
                    double health = Math.Round(((double)full / design) * 100, 2);
                    sb.AppendLine($"Design: {design} mWh | Current Max: {full} mWh");
                    sb.AppendLine($"Battery Health: {(health > 100 ? 100 : health)}%");
                }
                else sb.AppendLine("Battery: No battery detected.");
            }
            catch { sb.AppendLine("Battery: Requires 'requireAdministrator' in app.manifest"); }

            reportBox.Text = sb.ToString();
        }
    }
}