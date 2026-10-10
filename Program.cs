using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

[assembly: AssemblyTitle("透明便签")]
[assembly: AssemblyVersion("1.1.1.0")]
[assembly: AssemblyFileVersion("1.1.1.0")]

namespace DesktopStickyNotes
{
    public sealed class NotesApp : Application
    {
        readonly NoteStore store;
        readonly ImageStore images;
        readonly EventWaitHandle showSignal;
        readonly List<NoteWindow> windows = new List<NoteWindow>();
        List<NoteData> notes;
        readonly DispatcherTimer saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        readonly DispatcherTimer signalTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        Forms.NotifyIcon tray;

        public NotesApp(string directory, EventWaitHandle signal)
        {
            store = new NoteStore(directory); images = new ImageStore(directory); showSignal = signal;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            saveTimer.Tick += delegate { saveTimer.Stop(); Save(false); };
            signalTimer.Tick += delegate { if (showSignal.WaitOne(0)) ShowAll(); };
            SessionEnding += delegate(object sender, SessionEndingCancelEventArgs e) { e.Cancel = !Save(true); };
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            try
            {
                notes = store.Load();
                foreach (var note in notes) CreateWindow(note);
                if (notes.Count == 0) NewNote();
                else if (!notes.Any(n => n.Visible)) ShowAll();
                using (var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AppIcon"))
                    tray = new Forms.NotifyIcon { Icon = new System.Drawing.Icon(iconStream, 32, 32),
                        Text = "透明便签 · 双击恢复编辑", Visible = true };
                var menu = new Forms.ContextMenuStrip();
                menu.Items.Add("显示并恢复编辑", null, delegate { Dispatcher.Invoke(new Action(ShowAll)); });
                menu.Items.Add("鼠标穿透所有便签", null, delegate { Dispatcher.Invoke(new Action(delegate { foreach (var window in windows) window.SetClickThrough(true); })); });
                menu.Items.Add("新建便签", null, delegate { Dispatcher.Invoke(new Action(NewNote)); });
                menu.Items.Add("隐藏所有便签", null, delegate { Dispatcher.Invoke(new Action(delegate { foreach (var window in windows) window.HideNote(); })); });
                menu.Items.Add(new Forms.ToolStripSeparator());
                menu.Items.Add("退出", null, delegate { Dispatcher.Invoke(new Action(ExitApp)); });
                tray.ContextMenuStrip = menu;
                tray.DoubleClick += delegate { Dispatcher.Invoke(new Action(ShowAll)); };
                signalTimer.Start();
                if (store.RecoveredFromBackup)
                    MessageBox.Show("已从上一次备份恢复便签。损坏的原文件已保留在：\n" + Path.GetDirectoryName(store.FilePath), "便签已恢复");
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法启动便签，原有数据已保留。\n" + ex.Message + "\n\n数据位置：" + store.FilePath, "透明便签", MessageBoxButton.OK, MessageBoxImage.Error);
                foreach (var window in windows) window.AllowClose = true;
                Shutdown(1);
            }
        }

        void CreateWindow(NoteData note)
        {
            var window = new NoteWindow(note, images);
            window.Changed += delegate { Dirty(); };
            window.NewRequested += delegate { NewNote(); };
            window.SaveRequested += delegate { Save(true); };
            window.ExitRequested += delegate { ExitApp(); };
            window.DeleteRequested += delegate {
                if (MessageBox.Show(window, "删除这张便签及其内容？", "删除便签", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                int index = notes.IndexOf(note);
                notes.Remove(note);
                if (!Save(true)) { notes.Insert(index, note); return; }
                windows.Remove(window); window.AllowClose = true; window.Close();
            };
            windows.Add(window);
            if (note.Visible) window.Show();
        }

        void NewNote()
        {
            var note = new NoteData();
            if (windows.Count != 0)
            {
                note.Left = windows[windows.Count - 1].Left + 24;
                note.Top = windows[windows.Count - 1].Top + 24;
            }
            notes.Add(note); CreateWindow(note); Dirty();
        }

        void ShowAll() { foreach (var window in windows) window.ShowNote(); }
        void Dirty()
        {
            foreach (var window in windows) window.SetStatus("保存中…");
            saveTimer.Stop(); saveTimer.Start();
        }

        bool Save(bool showError)
        {
            saveTimer.Stop();
            try
            {
                store.Save(notes);
                foreach (var window in windows) window.SetStatus("已保存");
                return true;
            }
            catch (Exception ex)
            {
                foreach (var window in windows) window.SetStatus("保存失败 · Ctrl+S 重试");
                if (showError) MessageBox.Show("保存失败，便签仍保留在窗口中。请检查磁盘空间和目录权限后按 Ctrl+S 重试。\n" + ex.Message + "\n\n" + store.FilePath,
                    "透明便签", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        void ExitApp()
        {
            if (!Save(true)) return;
            foreach (var window in windows) window.AllowClose = true;
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            saveTimer.Stop(); signalTimer.Stop();
            if (tray != null) { var icon = tray.Icon; var menu = tray.ContextMenuStrip; tray.Dispose(); icon.Dispose(); menu.Dispose(); }
            base.OnExit(e);
        }
    }

    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
                if (args.Length != 0)
                {
                    if (args.Length != 2 || args[0] != "--data-dir") throw new ArgumentException("用法：DesktopStickyNotes.exe [--data-dir 数据目录]");
                    directory = Path.GetFullPath(args[1]);
                }
                directory = Path.GetFullPath(directory);
                if (directory.Length > Path.GetPathRoot(directory).Length) directory = directory.TrimEnd(Path.DirectorySeparatorChar);
                string identity;
                using (var hash = SHA256.Create()) identity = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(directory.ToUpperInvariant()))).Replace("-", "");
                bool first;
                using (var mutex = new Mutex(true, "Local\\DesktopStickyNotes-" + identity, out first))
                using (var signal = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\DesktopStickyNotes-Show-" + identity))
                {
                    if (!first) { signal.Set(); return 0; }
                    try
                    {
                        Directory.CreateDirectory(directory);
                        using (var lease = new FileStream(Path.Combine(directory, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                            return new NotesApp(directory, signal).Run();
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "透明便签", MessageBoxButton.OK, MessageBoxImage.Error);
                return 1;
            }
        }
    }
}
