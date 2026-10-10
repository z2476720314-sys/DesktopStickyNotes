using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Xml.Linq;
using Forms = System.Windows.Forms;

static class UiChecks
{
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct KEYINPUT { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] struct INPUTDATA { [FieldOffset(0)] public KEYINPUT Key; [FieldOffset(0)] public MOUSEINPUT Mouse; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint Type; public INPUTDATA Data; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct SHELLINFO
    {
        public IntPtr Icon; public int Index; public uint Attributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string Type;
    }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attributes, ref SHELLINFO info, uint size, uint flags);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern void SHChangeNotify(uint eventId, uint flags, string path, string other);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, INPUT[] input, int size);

    static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); Console.WriteLine("PASS: " + message); }
    static void Pause(int ms) { var timer = Stopwatch.StartNew(); while (timer.ElapsedMilliseconds < ms) { Forms.Application.DoEvents(); Thread.Sleep(30); } }
    static void Wait(Func<bool> condition, string message) { for (int i = 0; i < 60; i++) { if (condition()) return; Pause(100); } throw new Exception("Timeout: " + message); }
    static void Shortcut(params ushort[] keys)
    {
        var input = new INPUT[keys.Length * 2];
        for (int i = 0; i < keys.Length; i++)
        {
            input[i] = new INPUT { Type = 1, Data = new INPUTDATA { Key = new KEYINPUT { Key = keys[i] } } };
            input[keys.Length + i] = new INPUT { Type = 1, Data = new INPUTDATA { Key = new KEYINPUT { Key = keys[keys.Length - i - 1], Flags = 2 } } };
        }
        if (SendInput((uint)input.Length, input, Marshal.SizeOf(typeof(INPUT))) != input.Length)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        Pause(150);
    }
    static AutomationElement Window(int pid)
    {
        return AutomationElement.RootElement.FindFirst(TreeScope.Children, new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, pid),
            new PropertyCondition(AutomationElement.NameProperty, "透明便签")));
    }
    static AutomationElement Find(int pid, string id)
    {
        var parents = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, pid));
        foreach (AutomationElement parent in parents)
        {
            var found = parent.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, id));
            if (found != null) return found;
        }
        throw new Exception("Control not found: " + id);
    }
    static AutomationElement PreviewWindow(int pid)
    {
        var hwnd = FindWindow(null, "图片预览");
        if (hwnd == IntPtr.Zero) return null;
        var window = AutomationElement.FromHandle(hwnd);
        return window.Current.ProcessId == pid ? window : null;
    }
    static void Invoke(int pid, string id) { ((InvokePattern)Find(pid, id).GetCurrentPattern(InvokePattern.Pattern)).Invoke(); Pause(150); }
    static RECT Bounds(IntPtr hwnd) { RECT value; GetWindowRect(hwnd, out value); return value; }
    static void Drag(int x, int y, int dx, int dy)
    {
        SetCursorPos(x, y); Pause(180); mouse_event(2, 0, 0, 0, UIntPtr.Zero); Pause(120);
        for (int i = 1; i <= 12; i++) { SetCursorPos(x + dx * i / 12, y + dy * i / 12); Pause(20); }
        mouse_event(4, 0, 0, 0, UIntPtr.Zero); Pause(200);
    }
    static void Click(int x, int y)
    {
        SetCursorPos(x, y); mouse_event(2, 0, 0, 0, UIntPtr.Zero); Pause(50);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero); Pause(250);
    }
    static void CheckChrome(IntPtr hwnd, bool visible, string path, string message)
    {
        using (var bitmap = Capture(Bounds(hwnd), path))
        {
            int bright = 0;
            for (int y = 8; y < 40; y++) for (int x = 14; x < bitmap.Width - 14; x++)
            { var pixel = bitmap.GetPixel(x, y); if (pixel.R > 235 && pixel.G > 235 && pixel.B > 235) bright++; }
            Check(visible ? bright > 20 : bright == 0, message);
        }
    }
    static Bitmap Capture(RECT rect, string path)
    {
        var bitmap = new Bitmap(rect.Right - rect.Left, rect.Bottom - rect.Top);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(rect.Left, rect.Top, 0, 0, bitmap.Size);
        bitmap.Save(path, ImageFormat.Png); return bitmap;
    }
    static Process Start(string exe, string directory)
    {
        return Process.Start(new ProcessStartInfo(exe, "--data-dir \"" + directory + "\"") { UseShellExecute = false });
    }
    static XDocument Read(string directory) { return XDocument.Load(Path.Combine(directory, "notes.xml")); }
    static void Exit(int pid) { Invoke(pid, "OptionsButton"); Invoke(pid, "ExitMenu"); }
    static int ContentColorPixels(Bitmap bitmap, RECT rect, System.Windows.Rect area)
    {
        int count = 0;
        for (int y = Math.Max(0, (int)area.Top - rect.Top); y < Math.Min(bitmap.Height, (int)area.Bottom - rect.Top); y++)
            for (int x = Math.Max(0, (int)area.Left - rect.Left); x < Math.Min(bitmap.Width, (int)area.Right - rect.Left); x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                if (pixel.R < 40 && pixel.G > 85 && pixel.G < 125 && pixel.B > 185) count++;
            }
        return count;
    }
    static void CheckUiColor(int pid, IntPtr hwnd, string evidence)
    {
        var rect = Bounds(hwnd);
        using (var bitmap = Capture(rect, evidence))
        {
            int tinted = 0;
            foreach (string id in new[] { "NewNoteButton", "PinButton", "ColorButton", "AddImageButton", "MouseThroughButton", "OptionsButton", "HideButton", "Status" })
                tinted += ContentColorPixels(bitmap, rect, Find(pid, id).Current.BoundingRectangle);
            var title = Window(pid).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "便签"));
            tinted += ContentColorPixels(bitmap, rect, title.Current.BoundingRectangle);
            Check(tinted == 0, "custom content color does not tint toolbar icons, title or save status");
        }
    }
    static void ShellIcon(string path, string label)
    {
        SHChangeNotify(0x2000, 5, path, null);
        var info = new SHELLINFO();
        Check(SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf(typeof(SHELLINFO)), 0x100) != IntPtr.Zero && info.Icon != IntPtr.Zero,
            label + " is supplied by Windows Shell");
        try
        {
            using (var icon = Icon.FromHandle(info.Icon))
            using (var bitmap = icon.ToBitmap())
            {
                int teal = 0;
                for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
                { var pixel = bitmap.GetPixel(x, y); if (Math.Abs(pixel.R - 45) < 8 && Math.Abs(pixel.G - 93) < 8 && Math.Abs(pixel.B - 115) < 8) teal++; }
                Check(teal > bitmap.Width * bitmap.Height / 6, label + " uses the custom note design");
                bitmap.Save(Path.Combine(Path.GetDirectoryName(typeof(UiChecks).Assembly.Location), "evidence", label + ".png"), ImageFormat.Png);
            }
        }
        finally { DestroyIcon(info.Icon); }
    }
    static AutomationElement TrayIcon(AutomationElement taskbar)
    {
        var condition = new PropertyCondition(AutomationElement.NameProperty, "透明便签 · 双击恢复编辑");
        var icon = taskbar.FindFirst(TreeScope.Descendants, condition);
        if (icon != null && !icon.Current.IsOffscreen) return icon;
        foreach (string cls in new[] { "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland" })
        {
            var hwnd = FindWindow(cls, null);
            if (hwnd == IntPtr.Zero || !IsWindowVisible(hwnd)) continue;
            icon = AutomationElement.FromHandle(hwnd).FindFirst(TreeScope.Descendants, condition);
            if (icon != null && !icon.Current.IsOffscreen) return icon;
        }
        return null;
    }
    static void CheckTray(int pid, IntPtr hwnd, AutomationElement taskbar)
    {
        POINT oldCursor; GetCursorPos(out oldCursor);
        bool opened = false;
        AutomationElement chevron = null;
        try
        {
            Invoke(pid, "MouseThroughButton");
            Check((GetWindowLong(hwnd, -20) & 0x20) != 0, "live note can enter mouse pass-through mode");
            Invoke(pid, "HideButton"); Check(!IsWindowVisible(hwnd), "live note can hide while its process stays running");
            var icon = TrayIcon(taskbar);
            if (icon == null)
            {
                chevron = taskbar.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1502"));
                Check(chevron != null, "notification overflow control is available");
                ((InvokePattern)chevron.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); Pause(250); opened = true;
                Wait(() => TrayIcon(taskbar) != null, "note tray icon"); icon = TrayIcon(taskbar);
            }
            Check(icon != null, "note icon remains in the notification area");
            var area = icon.Current.BoundingRectangle;
            using (var bitmap = Capture(new RECT { Left = (int)area.Left, Top = (int)area.Top, Right = (int)area.Right, Bottom = (int)area.Bottom },
                Path.Combine(Path.GetDirectoryName(typeof(UiChecks).Assembly.Location), "evidence", "tray-icon.png"))) { }
            SetCursorPos((int)(area.Left + area.Width / 2), (int)(area.Top + area.Height / 2));
            for (int i = 0; i < 2; i++) { mouse_event(2, 0, 0, 0, UIntPtr.Zero); mouse_event(4, 0, 0, 0, UIntPtr.Zero); Pause(70); }
            Wait(() => IsWindowVisible(hwnd), "tray double-click restores note");
            Check(IsWindowVisible(hwnd) && Find(pid, "NoteEditor").Current.IsEnabled, "double-clicking the actual tray icon restores the editable note");
            Check((GetWindowLong(hwnd, -20) & 0x20) == 0, "actual tray double-click cancels pass-through and restores mouse editing");
        }
        finally
        {
            if (opened && chevron != null && TrayIcon(taskbar) != null)
                ((InvokePattern)chevron.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
            if (!IsWindowVisible(hwnd)) using (var live = Process.GetProcessById(pid)) { using (var second = Process.Start(live.MainModule.FileName)) second.WaitForExit(5000); }
            SetCursorPos(oldCursor.X, oldCursor.Y);
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 2 && (args[0] == "--inspect" || args[0] == "--close" || args[0] == "--through"))
        {
            try
            {
                int pid = int.Parse(args[1]);
                if (args[0] == "--close")
                {
                    using (var live = Process.GetProcessById(pid)) { Exit(pid); Check(live.WaitForExit(5000), "live application saves and exits normally"); }
                    return 0;
                }
                if (args[0] == "--through")
                {
                    var roots = AutomationElement.RootElement.FindAll(TreeScope.Children, new AndCondition(
                        new PropertyCondition(AutomationElement.ProcessIdProperty, pid), new PropertyCondition(AutomationElement.NameProperty, "透明便签")));
                    Check(roots.Count > 0, "live desktop notes are available");
                    foreach (AutomationElement root in roots)
                    {
                        var button = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "MouseThroughButton"));
                        ((InvokePattern)button.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                        var noteHwnd = new IntPtr(root.Current.NativeWindowHandle);
                        Wait(() => (GetWindowLong(noteHwnd, -20) & 0x20) != 0 && IsWindowVisible(noteHwnd), "live mouse pass-through mode");
                        Check((GetWindowLong(noteHwnd, -20) & 0x20) != 0 && IsWindowVisible(noteHwnd), "live note remains visible and passes mouse clicks through");
                    }
                    Pause(900); return 0;
                }
                Check(Window(pid) != null, "default desktop entry has a visible note window");
                Check(Find(pid, "NoteEditor").Current.IsEnabled, "default desktop entry has an accessible editor");
                Check(Find(pid, "PinButton") != null, "default desktop entry has the pin icon control");
                IntPtr liveHwnd = new IntPtr(Window(pid).Current.NativeWindowHandle);
                Check((GetWindowLong(liveHwnd, -20) & 0x80) != 0, "default note stays out of Alt+Tab as a tool window");
                Check(GetWindow(liveHwnd, 4) != IntPtr.Zero, "default note is owned and hidden from taskbar");
                Check(SendMessage(liveHwnd, 0x7F, new IntPtr(1), IntPtr.Zero) != IntPtr.Zero, "default note exposes its application icon to Windows");
                using (var windowIcon = Icon.FromHandle(SendMessage(liveHwnd, 0x7F, new IntPtr(1), IntPtr.Zero)))
                using (var bitmap = windowIcon.ToBitmap())
                {
                    string iconEvidence = Path.Combine(Path.GetDirectoryName(typeof(UiChecks).Assembly.Location), "evidence", "window-icon.png");
                    bitmap.Save(iconEvidence, ImageFormat.Png);
                    int teal = 0;
                    for (int y = 0; y < bitmap.Height; y++) for (int x = 0; x < bitmap.Width; x++)
                    { var pixel = bitmap.GetPixel(x, y); if (Math.Abs(pixel.R - 45) < 8 && Math.Abs(pixel.G - 93) < 8 && Math.Abs(pixel.B - 115) < 8) teal++; }
                    Check(teal > bitmap.Width * bitmap.Height / 6, "actual window icon matches the custom note design");
                }
                var taskbar = AutomationElement.FromHandle(FindWindow("Shell_TrayWnd", null));
                AutomationElement ownButton = null;
                foreach (AutomationElement element in taskbar.FindAll(TreeScope.Descendants, Condition.TrueCondition))
                {
                    var bounds = element.Current.BoundingRectangle;
                    if (element.Current.Name.Contains("透明便签") && !element.Current.Name.Contains("双击恢复编辑") &&
                        bounds.Width >= 20 && bounds.Width < 260 && bounds.Height >= 20 && bounds.Height < 150)
                    { ownButton = element; break; }
                }
                Check(ownButton == null, "Windows taskbar has no running note entry");
                CheckTray(pid, liveHwnd, taskbar);
                using (var live = Process.GetProcessById(pid)) ShellIcon(live.MainModule.FileName, "explorer-exe-icon");
                ShellIcon(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "透明便签.lnk"), "desktop-shortcut-icon");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
        }
        string directory = Path.Combine(args[1], "ui-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string evidence = Path.Combine(args[1], "evidence"); Directory.CreateDirectory(evidence);
        POINT oldCursor; GetCursorPos(out oldCursor);
        Process process = null;
        Forms.Form backdrop = null;
        try
        {
            Check(File.Exists(args[0]), "release EXE exists");
            process = Start(args[0], directory);
            Wait(() => Window(process.Id) != null, "main note window");
            var root = Window(process.Id);
            IntPtr hwnd = new IntPtr(root.Current.NativeWindowHandle);
            Check((GetWindowLong(hwnd, -20) & 0x80000) != 0, "actual window uses per-pixel transparency");
            Check((GetWindowLong(hwnd, -20) & 8) != 0, "actual note is always on top");
            Check(GetWindow(hwnd, 4) != IntPtr.Zero, "visible note is hidden from taskbar");
            Check((GetWindowLong(hwnd, -20) & 0x80) != 0, "visible note stays out of Alt+Tab as a tool window");
            Check(SendMessage(hwnd, 0x7F, new IntPtr(1), IntPtr.Zero) != IntPtr.Zero, "window supplies an application icon");
            string text = "今天的便签\n联系供应商\n确认明天的发货清单";
            ((ValuePattern)Find(process.Id, "NoteEditor").GetCurrentPattern(ValuePattern.Pattern)).SetValue(text);
            Pause(950);
            Check(Read(directory).Descendants("Text").First().Value.Replace("\r\n", "\n") == text, "editing Chinese in real window auto-saves");

            var screen = Forms.Screen.PrimaryScreen.Bounds;
            backdrop = new Forms.Form { FormBorderStyle = Forms.FormBorderStyle.None, StartPosition = Forms.FormStartPosition.Manual,
                Bounds = screen, BackColor = Color.FromArgb(34, 66, 88), TopMost = true };
            backdrop.Controls.Add(new Forms.Panel { Location = new Point(480 - screen.Left, 0), Size = new Size(screen.Width, screen.Height), BackColor = Color.FromArgb(182, 207, 217) });
            backdrop.Show(); Pause(100);
            SetWindowPos(hwnd, new IntPtr(-1), 310, 230, 0, 0, 0x11); Pause(250);
            SetForegroundWindow(hwnd); Pause(150);
            var before = Bounds(hwnd);
            SetCursorPos(before.Left + 30, before.Top + 75); Pause(400);
            CheckChrome(hwnd, false, Path.Combine(evidence, "hover-hidden-note.png"), "hover does not reveal the toolbar");
            var title = Window(process.Id).FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, "便签")).Current.BoundingRectangle;
            Drag((int)(title.Left + 15), (int)(title.Top + title.Height / 2), 70, 40);
            var moved = Bounds(hwnd);
            Console.WriteLine("Drag delta: " + (moved.Left - before.Left) + "," + (moved.Top - before.Top));
            Check(Math.Abs(moved.Left - before.Left - 70) < 5 && Math.Abs(moved.Top - before.Top - 40) < 5, "title strip moves actual window with the mouse");
            CheckChrome(hwnd, false, Path.Combine(evidence, "drag-hidden-note.png"), "dragging works while the toolbar stays hidden");
            Click(moved.Left + 35, moved.Top + 85);
            CheckChrome(hwnd, true, Path.Combine(evidence, "clicked-note.png"), "clicking the note reveals the toolbar");
            Click(screen.Left + 50, screen.Top + 70);
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13); Pause(150);
            CheckChrome(hwnd, false, Path.Combine(evidence, "inactive-note.png"), "clicking outside hides the toolbar");
            SetCursorPos(moved.Left + 30, moved.Top + 75); Pause(400);
            CheckChrome(hwnd, false, Path.Combine(evidence, "hover-again-hidden-note.png"), "hover after leaving does not reveal the toolbar");
            Click((int)(title.Left + 85), (int)(title.Top + 40 + title.Height / 2));
            CheckChrome(hwnd, true, Path.Combine(evidence, "title-clicked-note.png"), "a title click also reveals the toolbar");
            var grip = Find(process.Id, "ResizeGrip").Current.BoundingRectangle;
            Drag((int)(grip.Left + grip.Width / 2), (int)(grip.Top + grip.Height / 2), 90, 45);
            var resized = Bounds(hwnd);
            Check(resized.Right - resized.Left >= moved.Right - moved.Left + 80 && resized.Bottom - resized.Top >= moved.Bottom - moved.Top + 35,
                "corner grip resizes actual window with the mouse");
            Pause(850);
            using (var bitmap = Capture(resized, Path.Combine(evidence, "transparent-note.png")))
            {
                var dark = bitmap.GetPixel(70, bitmap.Height - 65);
                var light = bitmap.GetPixel(bitmap.Width - 65, bitmap.Height - 65);
                Check(light.R - dark.R > 70 && light.G - dark.G > 70, "different backgrounds remain visible through the note");
            }
            Invoke(process.Id, "OptionsButton");
            ((ExpandCollapsePattern)Find(process.Id, "TransparencyMenu").GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand(); Pause(200);
            Invoke(process.Id, "Opacity100"); Pause(900);
            Check(Math.Abs(double.Parse(Read(directory).Descendants("BackgroundOpacity").First().Value, System.Globalization.CultureInfo.InvariantCulture) - 1.0 / 255) < 0.00001,
                "text-only transparency selection is saved");
            using (var bitmap = Capture(resized, Path.Combine(evidence, "text-only-note.png")))
            {
                var dark = bitmap.GetPixel(70, bitmap.Height - 65);
                Check(Math.Abs(dark.R - 34) < 5 && Math.Abs(dark.G - 66) < 5 && Math.Abs(dark.B - 88) < 5,
                    "text-only mode leaves the underlying background visually unchanged");
                int bright = 0;
                for (int y = 55; y < 140; y++) for (int x = 20; x < 150; x++)
                { var pixel = bitmap.GetPixel(x, y); if (pixel.R > 220 && pixel.G > 220 && pixel.B > 220) bright++; }
                Check(bright > 30, "text stays bright and readable when background is transparent");
            }
            Invoke(process.Id, "PinButton");
            Check((GetWindowLong(hwnd, -20) & 8) == 0, "pin control can cancel always-on-top");
            Invoke(process.Id, "PinButton");
            Invoke(process.Id, "HideButton"); Pause(800);
            Check(!IsWindowVisible(hwnd) && Read(directory).Descendants("Visible").First().Value == "false", "hide keeps saved note without deleting it");
            using (var second = Start(args[0], directory)) { Check(second.WaitForExit(5000) && second.ExitCode == 0, "second launch exits instead of creating another instance"); }
            Wait(() => IsWindowVisible(hwnd), "second launch shows existing note");
            Check(IsWindowVisible(hwnd), "desktop shortcut can recover a hidden note");
            Invoke(process.Id, "ColorButton");
            AutomationElement picker = null;
            Wait(() => {
                foreach (AutomationElement candidate in AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id)))
                    if (candidate.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "706")) != null) { picker = candidate; return true; }
                return false;
            }, "native font color picker");
            foreach (var channel in new[] { new[] { "706", "18" }, new[] { "707", "106" }, new[] { "708", "203" } })
            {
                var edit = picker.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, channel[0]));
                ((ValuePattern)edit.GetCurrentPattern(ValuePattern.Pattern)).SetValue(channel[1]);
            }
            var acceptColor = picker.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "1"));
            ((InvokePattern)acceptColor.GetCurrentPattern(InvokePattern.Pattern)).Invoke(); Pause(1000);
            var colorBounds = Bounds(hwnd); Click(colorBounds.Left + 25, colorBounds.Top + 65);
            Check(Read(directory).Descendants("TextColor").First().Value == "#126ACB", "native color picker applies and saves an arbitrary RGB font color");
            using (var bitmap = Capture(Bounds(hwnd), Path.Combine(evidence, "colored-note.png")))
            {
                int blue = 0;
                for (int y = 55; y < 140; y++) for (int x = 20; x < 150; x++)
                { var pixel = bitmap.GetPixel(x, y); if (pixel.R < 40 && pixel.G > 85 && pixel.G < 125 && pixel.B > 185) blue++; }
                Check(blue > 25, "selected font color is rendered in the actual note");
            }
            CheckUiColor(process.Id, hwnd, Path.Combine(evidence, "content-color-only.png"));
            Pause(800);
            var finalBounds = Bounds(hwnd);
            Exit(process.Id);
            Check(process.WaitForExit(5000) && process.ExitCode == 0, "normal exit closes the process after saving");
            process.Dispose(); process = Start(args[0], directory);
            Wait(() => Window(process.Id) != null, "restored window");
            root = Window(process.Id); hwnd = new IntPtr(root.Current.NativeWindowHandle);
            var restored = Bounds(hwnd);
            Check(((ValuePattern)Find(process.Id, "NoteEditor").GetCurrentPattern(ValuePattern.Pattern)).Current.Value.Replace("\r\n", "\n") == text,
                "actual EXE restores Chinese note after restart");
            Check(Read(directory).Descendants("TextColor").First().Value == "#126ACB", "custom font color survives an actual EXE restart");
            Click(restored.Left + 25, restored.Top + 65);
            CheckUiColor(process.Id, hwnd, Path.Combine(evidence, "restored-content-color-only.png"));
            Check(Math.Abs(restored.Left - finalBounds.Left) < 3 && Math.Abs(restored.Top - finalBounds.Top) < 3 &&
                Math.Abs((restored.Right - restored.Left) - (finalBounds.Right - finalBounds.Left)) < 3,
                "actual EXE restores position and resized width");
            string previousTheme = Read(directory).Descendants("DarkText").First().Value;
            Invoke(process.Id, "OptionsButton"); Invoke(process.Id, "TextColorMenu"); Pause(800);
            Check(Read(directory).Descendants("TextColor").First().Value == "#182732" &&
                Read(directory).Descendants("DarkText").First().Value == previousTheme,
                "dark text preset changes content without changing the UI theme");
            Invoke(process.Id, "OptionsButton"); Invoke(process.Id, "TextColorMenu"); Pause(800);
            Check(Read(directory).Descendants("TextColor").First().Value == "#F8FAFC" &&
                Read(directory).Descendants("DarkText").First().Value == previousTheme,
                "white text preset changes content without changing the UI theme");
            int underneathClicks = 0;
            var noteBounds = Bounds(hwnd);
            var underneath = new Forms.Button { Text = "下方测试按钮", Bounds = new Rectangle(noteBounds.Left + 20 - screen.Left, noteBounds.Top + 75 - screen.Top, 140, 40) };
            underneath.Click += delegate { underneathClicks++; };
            backdrop.Controls.Add(underneath); underneath.BringToFront();
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            Click(noteBounds.Left + 55, noteBounds.Top + 90);
            Check(underneathClicks == 0, "editable note receives clicks instead of the window underneath");
            var throughControl = Find(process.Id, "MouseThroughButton").Current.BoundingRectangle;
            Click((int)(throughControl.Left + throughControl.Width / 2), (int)(throughControl.Top + throughControl.Height / 2)); Pause(800);
            Check((GetWindowLong(hwnd, -20) & 0x20) != 0 && Read(directory).Descendants("ClickThrough").First().Value == "true",
                "clicking the cursor icon enables and saves native mouse pass-through");
            Click(noteBounds.Left + 55, noteBounds.Top + 90);
            Check(underneathClicks == 1 && IsWindowVisible(hwnd), "actual mouse click reaches a separate process underneath while the note remains visible");
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            underneath.Bounds = new Rectangle(noteBounds.Left + 20 - screen.Left, noteBounds.Top + 8 - screen.Top, 90, 28);
            Click(noteBounds.Left + 55, noteBounds.Top + 20);
            Check(underneathClicks == 2, "hidden title and toolbar area also pass clicks to the underlying window");
            Exit(process.Id); Check(process.WaitForExit(5000), "pass-through note still saves and exits normally");
            process.Dispose(); process = Start(args[0], directory);
            Wait(() => Window(process.Id) != null, "restored pass-through note");
            hwnd = new IntPtr(Window(process.Id).Current.NativeWindowHandle);
            Check((GetWindowLong(hwnd, -20) & 0x20) != 0, "pass-through mode survives an actual EXE restart");
            using (var second = Start(args[0], directory)) { Check(second.WaitForExit(5000) && second.ExitCode == 0, "repeat launch requests edit recovery from the existing process"); }
            Wait(() => (GetWindowLong(hwnd, -20) & 0x20) == 0, "repeat launch restores mouse editing"); Pause(800);
            Check(Read(directory).Descendants("ClickThrough").First().Value == "false", "recovering editing clears the saved pass-through state");
            SetWindowPos(hwnd, new IntPtr(-1), 0, 0, 0, 0, 0x13);
            underneath.Bounds = new Rectangle(noteBounds.Left + 20 - screen.Left, noteBounds.Top + 75 - screen.Top, 140, 40);
            Click(noteBounds.Left + 55, noteBounds.Top + 90);
            Check(underneathClicks == 2 && Find(process.Id, "NoteEditor").Current.IsEnabled, "recovered note accepts mouse input again");
            backdrop.Controls.Remove(underneath); underneath.Dispose();
            var originalClipboard = Forms.Clipboard.GetDataObject();
            try
            {
                byte[] png;
                using (var fixture = new Bitmap(160, 100, PixelFormat.Format32bppArgb))
                using (var graphics = Graphics.FromImage(fixture))
                using (var stream = new MemoryStream())
                using (var fill = new SolidBrush(Color.FromArgb(36, 198, 146)))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.FillRectangle(fill, 10, 10, 140, 80);
                    fixture.Save(stream, ImageFormat.Png); png = stream.ToArray();
                }
                var clipboardImage = new Forms.DataObject();
                clipboardImage.SetData("PNG", false, new MemoryStream(png));
                Forms.Clipboard.SetDataObject(clipboardImage, true);
                SetForegroundWindow(hwnd);
                Find(process.Id, "NoteEditor").SetFocus();
                Check(GetForegroundWindow() == hwnd && AutomationElement.FocusedElement.Current.ProcessId == process.Id,
                    "clipboard keyboard input targets the actual note window");
                Shortcut(0x11, 0x56); Pause(1000);
                var imageEntries = Read(directory).Descendants("NoteImage").ToArray();
                Check(imageEntries.Length == 1 && File.Exists(Path.Combine(directory, "Images", imageEntries[0].Element("FileName").Value)),
                    "Ctrl+V saves a real clipboard PNG as an image link without replacing note text");
                Check(((ValuePattern)Find(process.Id, "NoteEditor").GetCurrentPattern(ValuePattern.Pattern)).Current.Value.Replace("\r\n", "\n") == text,
                    "pasting an image preserves existing Chinese text");
                Forms.Clipboard.SetText("普通文字粘贴"); Shortcut(0x11, 0x23); Shortcut(0x11, 0x56); Pause(250);
                Check(((ValuePattern)Find(process.Id, "NoteEditor").GetCurrentPattern(ValuePattern.Pattern)).Current.Value.EndsWith("普通文字粘贴"),
                    "image paste command keeps ordinary text paste working");
                ((ValuePattern)Find(process.Id, "NoteEditor").GetCurrentPattern(ValuePattern.Pattern)).SetValue(text);
                string imageFile = imageEntries[0].Element("FileName").Value;
                var imageLink = Find(process.Id, "ImageLink-" + imageFile);
                Check(imageLink.Current.ControlType == ControlType.Button && imageLink.Current.Name.Length > 0,
                    "image is represented by a named clickable link instead of an inline thumbnail");
                Invoke(process.Id, "ImageLink-" + imageFile);
                Wait(() => PreviewWindow(process.Id) != null, "transparent image preview");
                var previewHwnd = new IntPtr(PreviewWindow(process.Id).Current.NativeWindowHandle);
                Check((GetWindowLong(previewHwnd, -20) & 0x80000) != 0 && (GetWindowLong(previewHwnd, -20) & 0x80) != 0 &&
                    GetWindow(previewHwnd, 4) == hwnd, "image preview is layered, owned and excluded from Alt+Tab");
                foreach (string id in new[] { "ZoomInButton", "ZoomOutButton", "FitImageButton", "ActualSizeButton", "PreviewMoveButton", "ClosePreviewButton" })
                    Check(Find(process.Id, id).Current.ControlType == ControlType.Button && Find(process.Id, id).Current.Name.Length > 0,
                        id + " exposes an accessible icon control");
                var imageArea = Find(process.Id, "PreviewImage").Current.BoundingRectangle;
                Check(Math.Abs(imageArea.Width - 160) < 2 && Math.Abs(imageArea.Height - 100) < 2, "first preview displays the complete image without cropping");
                var previewBounds = Bounds(previewHwnd);
                using (var capture = Capture(previewBounds, Path.Combine(evidence, "transparent-image-preview.png")))
                {
                    var corner = capture.GetPixel(capture.Width - 15, capture.Height - 15);
                    Check(corner.R == 182 && corner.G == 207 && corner.B == 217, "preview background is fully transparent with no tinted overlay");
                    var center = capture.GetPixel((int)(imageArea.Left + imageArea.Width / 2) - previewBounds.Left,
                        (int)(imageArea.Top + imageArea.Height / 2) - previewBounds.Top);
                    Check(Math.Abs(center.R - 36) < 2 && Math.Abs(center.G - 198) < 2 && Math.Abs(center.B - 146) < 2,
                        "preview renders original image colors without note opacity");
                    var alpha = capture.GetPixel((int)imageArea.Left + 2 - previewBounds.Left, (int)imageArea.Top + 2 - previewBounds.Top);
                    Check(alpha.R == 182 && alpha.G == 207 && alpha.B == 217, "transparent PNG pixels keep their transparency in preview");
                }
                SetCursorPos((int)(imageArea.Left + imageArea.Width / 2), (int)(imageArea.Top + imageArea.Height / 2));
                Pause(250);
                mouse_event(0x800, 0, 0, 120, UIntPtr.Zero); Pause(350);
                var enlarged = Find(process.Id, "PreviewImage").Current.BoundingRectangle;
                Check(enlarged.Width > imageArea.Width * 1.15, "real mouse wheel enlarges the image");
                mouse_event(0x800, 0, 0, unchecked((uint)-120), UIntPtr.Zero); Pause(350);
                Check(Math.Abs(Find(process.Id, "PreviewImage").Current.BoundingRectangle.Width - 160) < 2, "real mouse wheel shrinks the image");
                Invoke(process.Id, "ZoomInButton");
                Check(Find(process.Id, "PreviewImage").Current.BoundingRectangle.Width > 180, "zoom icon enlarges the image");
                Invoke(process.Id, "ZoomOutButton"); Invoke(process.Id, "ActualSizeButton");
                imageArea = Find(process.Id, "PreviewImage").Current.BoundingRectangle;
                Drag((int)(imageArea.Left + 80), (int)(imageArea.Top + 50), 60, 25);
                var panned = Find(process.Id, "PreviewImage").Current.BoundingRectangle;
                Check(Math.Abs(panned.Left - imageArea.Left - 60) < 3 && Math.Abs(panned.Top - imageArea.Top - 25) < 3, "dragging the preview pans the image");
                Invoke(process.Id, "FitImageButton");
                Check(Math.Abs(Find(process.Id, "PreviewImage").Current.BoundingRectangle.Left - imageArea.Left) < 3, "fit icon restores the complete centered image");
                var moveControl = Find(process.Id, "PreviewMoveButton").Current.BoundingRectangle;
                Drag((int)(moveControl.Left + moveControl.Width / 2), (int)(moveControl.Top + moveControl.Height / 2), 30, 20);
                var movedPreview = Bounds(previewHwnd);
                Check(Math.Abs(movedPreview.Left - previewBounds.Left - 30) < 3 && Math.Abs(movedPreview.Top - previewBounds.Top - 20) < 3,
                    "move icon drags the transparent preview window");
                Invoke(process.Id, "ClosePreviewButton");
                Wait(() => PreviewWindow(process.Id) == null, "preview closes independently");
                Check(IsWindowVisible(hwnd), "closing preview keeps the note visible");
                const string originalName = "参考图片_这是一个较长的文件名_透明预览与滚轮缩放.png";
                string originalImage = Path.Combine(directory, originalName);
                using (var large = new Bitmap(2400, 1600))
                using (var graphics = Graphics.FromImage(large))
                { graphics.Clear(Color.FromArgb(36, 198, 146)); large.Save(originalImage, ImageFormat.Png); }
                Invoke(process.Id, "AddImageButton");
                AutomationElement fileDialog = null;
                Wait(() => {
                    var dialogHwnd = FindWindow(null, "选择图片");
                    if (dialogHwnd == IntPtr.Zero) return false;
                    fileDialog = AutomationElement.FromHandle(dialogHwnd);
                    return fileDialog.Current.ProcessId == process.Id;
                }, "native image file picker");
                Forms.Clipboard.SetText(originalImage); Shortcut(0x12, 0x4E); Shortcut(0x11, 0x56); Shortcut(0x0D); Pause(1200);
                imageEntries = Read(directory).Descendants("NoteImage").ToArray();
                Check(imageEntries.Length == 2 && imageEntries[1].Element("DisplayName").Value == originalName, "image icon imports a selected local file as a named link");
                var originalNoteBounds = Bounds(hwnd);
                SetWindowPos(hwnd, new IntPtr(-1), originalNoteBounds.Left, originalNoteBounds.Top, 260, 180, 0x10); Pause(350);
                var linkBounds = Find(process.Id, "ImageLink-" + imageEntries[1].Element("FileName").Value).Current.BoundingRectangle;
                var removeBounds = Find(process.Id, "RemoveImage-" + imageEntries[1].Element("FileName").Value).Current.BoundingRectangle;
                Check(linkBounds.Right <= removeBounds.Left && Find(process.Id, "NoteEditor").Current.BoundingRectangle.Height >= 20,
                    "long image names stay separate from removal icons and text remains usable at minimum note size");
                Check(Window(process.Id).FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ScrollBar))
                    .Cast<AutomationElement>().All(element => element.Current.IsOffscreen),
                    "overflowing note text and image links have no visible scrollbars");
                var firstLinkBounds = Find(process.Id, "ImageLink-" + imageFile).Current.BoundingRectangle;
                SetCursorPos((int)(firstLinkBounds.Left + 20), (int)(firstLinkBounds.Top + firstLinkBounds.Height / 2)); Pause(250);
                mouse_event(0x800, 0, 0, unchecked((uint)-120), UIntPtr.Zero); Pause(350);
                Check(!Find(process.Id, "ImageLink-" + imageEntries[1].Element("FileName").Value).Current.IsOffscreen,
                    "mouse wheel reveals overflowing image links with hidden scrollbars");
                var textEditor = Find(process.Id, "NoteEditor");
                ((ValuePattern)textEditor.GetCurrentPattern(ValuePattern.Pattern)).SetValue(string.Join("\n", Enumerable.Range(1, 50).Select(number => "滚动行 " + number)));
                textEditor.SetFocus(); Shortcut(0x11, 0x24); Pause(200);
                var textPattern = (TextPattern)textEditor.GetCurrentPattern(TextPattern.Pattern);
                string visibleBefore = textPattern.GetVisibleRanges()[0].GetText(-1);
                var editorBounds = textEditor.Current.BoundingRectangle;
                SetCursorPos((int)(editorBounds.Left + editorBounds.Width / 2), (int)(editorBounds.Top + editorBounds.Height / 2)); Pause(250);
                mouse_event(0x800, 0, 0, unchecked((uint)-120), UIntPtr.Zero); Pause(350);
                Check(textPattern.GetVisibleRanges()[0].GetText(-1) != visibleBefore, "note text still scrolls with the mouse wheel while scrollbars stay hidden");
                Shortcut(0x11, 0x23); Pause(200);
                Check(textPattern.GetVisibleRanges()[0].GetText(-1).Contains("滚动行 50"), "keyboard navigation reaches the end of text with hidden scrollbars");
                ((ValuePattern)textEditor.GetCurrentPattern(ValuePattern.Pattern)).SetValue(text); Pause(250);
                using (var capture = Capture(Bounds(hwnd), Path.Combine(evidence, "minimum-image-note.png"))) { }
                SetWindowPos(hwnd, new IntPtr(-1), originalNoteBounds.Left, originalNoteBounds.Top,
                    originalNoteBounds.Right - originalNoteBounds.Left, originalNoteBounds.Bottom - originalNoteBounds.Top, 0x10); Pause(250);
                using (var capture = Capture(Bounds(hwnd), Path.Combine(evidence, "image-links-note.png"))) { }
                File.Delete(originalImage);
                string importedFile = imageEntries[1].Element("FileName").Value;
                Invoke(process.Id, "ImageLink-" + importedFile);
                Wait(() => PreviewWindow(process.Id) != null, "preview imported copy");
                var largeImageArea = Find(process.Id, "PreviewImage").Current.BoundingRectangle;
                var largePreviewBounds = Bounds(new IntPtr(PreviewWindow(process.Id).Current.NativeWindowHandle));
                Check(largeImageArea.Width > 150 && Math.Abs(largeImageArea.Width / largeImageArea.Height - 1.5) < 0.01 &&
                    largeImageArea.Width <= largePreviewBounds.Right - largePreviewBounds.Left + 1 &&
                    largeImageArea.Height <= largePreviewBounds.Bottom - largePreviewBounds.Top - 48 + 1,
                    "large imported image fits completely at its original aspect ratio after deleting the source file");
                Shortcut(0x1B); Wait(() => PreviewWindow(process.Id) == null, "Escape closes preview");
                Check(IsWindowVisible(hwnd), "Escape closes only the image preview");
                string widePath = Path.Combine(directory, "超宽图片.png");
                using (var wide = new Bitmap(24000, 120))
                using (var graphics = Graphics.FromImage(wide))
                { graphics.Clear(Color.FromArgb(36, 198, 146)); wide.Save(widePath, ImageFormat.Png); }
                var imageFiles = new System.Collections.Specialized.StringCollection(); imageFiles.Add(widePath);
                Forms.Clipboard.SetFileDropList(imageFiles);
                SetForegroundWindow(hwnd); Find(process.Id, "NoteEditor").SetFocus(); Shortcut(0x11, 0x56); Pause(1000);
                imageEntries = Read(directory).Descendants("NoteImage").ToArray();
                Check(imageEntries.Length == 3 && imageEntries[2].Element("DisplayName").Value == "超宽图片.png",
                    "Ctrl+V imports a copied image file without replacing existing attachments");
                string wideFile = imageEntries[2].Element("FileName").Value;
                Invoke(process.Id, "ImageLink-" + wideFile); Wait(() => PreviewWindow(process.Id) != null, "ultrawide image preview");
                var wideArea = Find(process.Id, "PreviewImage").Current.BoundingRectangle;
                Invoke(process.Id, "ZoomOutButton");
                Check(Find(process.Id, "PreviewImage").Current.BoundingRectangle.Width < wideArea.Width * 0.9,
                    "zoom-out icon shrinks an ultrawide image even when its fitted scale is below five percent");
                Invoke(process.Id, "FitImageButton");
                wideArea = Find(process.Id, "PreviewImage").Current.BoundingRectangle;
                SetCursorPos((int)(wideArea.Left + wideArea.Width / 2), (int)(wideArea.Top + wideArea.Height / 2)); Pause(250);
                mouse_event(0x800, 0, 0, unchecked((uint)-120), UIntPtr.Zero); Pause(350);
                Check(Find(process.Id, "PreviewImage").Current.BoundingRectangle.Width < wideArea.Width * 0.9,
                    "real mouse wheel also shrinks an ultrawide fitted image");
                Invoke(process.Id, "RemoveImage-" + wideFile); Pause(900);
                Check(Read(directory).Descendants("NoteImage").Count() == 2 && PreviewWindow(process.Id) == null,
                    "removing the ultrawide attachment preserves the other image links");
                Invoke(process.Id, "ImageLink-" + importedFile); Wait(() => PreviewWindow(process.Id) != null, "reopen preview before pass-through");
                Invoke(process.Id, "MouseThroughButton");
                Wait(() => PreviewWindow(process.Id) == null, "pass-through also closes owned preview");
                using (var second = Start(args[0], directory)) second.WaitForExit(5000);
                Wait(() => (GetWindowLong(hwnd, -20) & 0x20) == 0, "restore editing after preview");
                Exit(process.Id); Check(process.WaitForExit(5000), "image note saves on normal exit");
                process.Dispose(); process = Start(args[0], directory);
                Wait(() => Window(process.Id) != null, "restored image note"); hwnd = new IntPtr(Window(process.Id).Current.NativeWindowHandle);
                Check(Read(directory).Descendants("NoteImage").Count() == 2 && Find(process.Id, "ImageLink-" + imageFile) != null,
                    "image links and their local files survive actual EXE restart");
                Invoke(process.Id, "ImageLink-" + imageFile); Wait(() => PreviewWindow(process.Id) != null, "restored image preview");
                Invoke(process.Id, "RemoveImage-" + imageFile); Pause(900);
                Check(Read(directory).Descendants("NoteImage").Count() == 1 && PreviewWindow(process.Id) == null &&
                    File.Exists(Path.Combine(directory, "Images", imageFile)), "removing an image link closes its preview while retaining the file for XML backup recovery");
                Check(Read(directory).Descendants("Text").First().Value.Replace("\r\n", "\n") == text, "image attachment operations preserve note text through restart");
            }
            finally { if (originalClipboard != null) Forms.Clipboard.SetDataObject(originalClipboard, true); }
            Invoke(process.Id, "NewNoteButton"); Pause(900);
            Check(Read(directory).Descendants("NoteData").Count() == 2, "new-note control creates a second saved note");
            Exit(process.Id); Check(process.WaitForExit(5000), "all note windows close on exit");
            string snapshot = File.ReadAllText(Path.Combine(directory, "notes.xml"));
            using (var lease = new FileStream(Path.Combine(directory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                process.Dispose(); process = Start(args[0], directory);
                Wait(() => Window(process.Id) != null, "competing writer error dialog");
                var dialog = Window(process.Id);
                Check(dialog.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "NoteEditor")) == null &&
                    File.ReadAllText(Path.Combine(directory, "notes.xml")) == snapshot, "exclusive data lock prevents a competing writer from opening or overwriting notes");
                ((WindowPattern)dialog.GetCurrentPattern(WindowPattern.Pattern)).Close();
                Check(process.WaitForExit(5000) && process.ExitCode == 1, "competing writer reports failure and exits");
            }
            Console.WriteLine("All native UI checks passed. Evidence: " + evidence);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
        finally
        {
            if (process != null) { if (!process.HasExited) { process.Kill(); process.WaitForExit(); } process.Dispose(); }
            if (backdrop != null) backdrop.Dispose();
            SetCursorPos(oldCursor.X, oldCursor.Y);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
