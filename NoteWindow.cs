using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace DesktopStickyNotes
{
    public sealed class NoteWindow : Window
    {
        [DllImport("user32.dll", SetLastError = true)] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", SetLastError = true)] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        readonly NoteData note;
        readonly Border surface;
        readonly Grid layout;
        readonly TextBox editor;
        readonly TextBlock placeholder, status;
        readonly DockPanel toolbar;
        readonly Button pin;
        bool chromeVisible;
        Point? clickStart;
        public bool AllowClose;
        public event EventHandler Changed, NewRequested, DeleteRequested, ExitRequested, SaveRequested;

        const string Markup = @"
<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
 xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' x:Name='Surface' CornerRadius='12' BorderThickness='1'>
 <Border.Resources>
  <Style TargetType='Button'>
   <Setter Property='Width' Value='30'/><Setter Property='Height' Value='28'/>
   <Setter Property='FontSize' Value='16'/><Setter Property='Foreground' Value='#F6FAFF'/>
   <Setter Property='Background' Value='Transparent'/><Setter Property='BorderBrush' Value='Transparent'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
    <Border x:Name='B' Background='{TemplateBinding Background}' CornerRadius='5' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'>
     <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
    </Border>
    <ControlTemplate.Triggers>
     <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Background' Value='#35FFFFFF'/></Trigger>
     <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='B' Property='BorderBrush' Value='#80BDEFFF'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate></Setter.Value></Setter>
  </Style>
 </Border.Resources>
 <Grid x:Name='Layout' Margin='14,8,14,8'>
  <Grid.RowDefinitions><RowDefinition Height='32'/><RowDefinition Height='*'/><RowDefinition Height='24'/></Grid.RowDefinitions>
  <DockPanel x:Name='Toolbar' LastChildFill='True'>
   <StackPanel DockPanel.Dock='Right' Orientation='Horizontal'>
    <Button x:Name='NewButton' AutomationProperties.AutomationId='NewNoteButton' AutomationProperties.Name='新建便签' ToolTip='新建便签（Ctrl+N）' Content='＋'/>
    <Button x:Name='PinButton' AutomationProperties.AutomationId='PinButton' AutomationProperties.Name='切换置顶' ToolTip='切换置顶'>
     <Viewbox Width='17' Height='17'><Grid Width='24' Height='24'>
      <Path x:Name='PinIcon' Data='M 7,3 L 17,3 M 9,3 L 9,9 L 6,13 L 6,15 L 18,15 L 18,13 L 15,9 L 15,3 M 12,15 L 12,22'
       StrokeThickness='1.6' StrokeStartLineCap='Round' StrokeEndLineCap='Round' StrokeLineJoin='Round'/>
      <Path x:Name='PinSlash' Data='M 3,3 L 21,21' StrokeThickness='1.6' StrokeStartLineCap='Round' StrokeEndLineCap='Round'/>
     </Grid></Viewbox>
    </Button>
    <Button x:Name='ColorButton' AutomationProperties.AutomationId='ColorButton' AutomationProperties.Name='更换字体颜色' ToolTip='更换字体颜色'>
     <Viewbox Width='17' Height='17'><Grid Width='24' Height='24'>
      <Path x:Name='ColorIcon' Data='M 12,3 C 6,3 3,7 3,12 C 3,18 7,21 12,21 L 14,21 C 17,21 18,18 15,16 C 13,14 15,13 18,13 C 23,13 22,3 12,3 Z' StrokeThickness='1.5' StrokeLineJoin='Round'/>
      <Ellipse Width='3' Height='3' Fill='#F19C9C' HorizontalAlignment='Left' VerticalAlignment='Top' Margin='7,6,0,0'/>
      <Ellipse Width='3' Height='3' Fill='#A8E4C8' HorizontalAlignment='Left' VerticalAlignment='Top' Margin='13,5,0,0'/>
      <Ellipse Width='3' Height='3' Fill='#A6D9FA' HorizontalAlignment='Left' VerticalAlignment='Top' Margin='17,8,0,0'/>
      <Ellipse Width='3' Height='3' Fill='#F3D79B' HorizontalAlignment='Left' VerticalAlignment='Top' Margin='6,12,0,0'/>
     </Grid></Viewbox>
    </Button>
    <Button x:Name='ThroughButton' AutomationProperties.AutomationId='MouseThroughButton' AutomationProperties.Name='开启鼠标穿透' ToolTip='鼠标穿透：点击下方窗口；双击托盘恢复编辑'>
     <Viewbox Width='17' Height='17'>
      <Path x:Name='ThroughIcon' Data='M 4,3 L 4,18 L 8,14 L 12,22 L 15,20 L 11,13 L 18,13 Z M 16,3 L 22,3 L 22,9'
       StrokeThickness='1.5' StrokeLineJoin='Round'/>
     </Viewbox>
    </Button>
    <Button x:Name='OptionsButton' AutomationProperties.AutomationId='OptionsButton' AutomationProperties.Name='便签设置' ToolTip='透明度、字号与更多选项' Content='⋯'/>
    <Button x:Name='HideButton' AutomationProperties.AutomationId='HideButton' AutomationProperties.Name='隐藏便签' ToolTip='隐藏便签；从托盘或桌面快捷方式找回' Content='×'/>
   </StackPanel>
   <Border x:Name='DragHandle' Background='Transparent' Cursor='SizeAll' ToolTip='拖动这里移动便签'>
    <TextBlock Text='便签' FontFamily='Segoe UI' FontSize='12' VerticalAlignment='Center' Foreground='#DAEBF8'/>
   </Border>
  </DockPanel>
  <Grid Grid.Row='1' Margin='0,8,0,4'>
   <TextBox x:Name='NoteEditor' AutomationProperties.AutomationId='NoteEditor' AutomationProperties.Name='便签内容'
    Background='Transparent' BorderThickness='0' Padding='0' Foreground='#F6FAFF'
    FontFamily='Microsoft YaHei UI' TextWrapping='Wrap' AcceptsReturn='True' AcceptsTab='True'
    VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled'
    SelectionBrush='#609AE4F3' CaretBrush='#A8F0FC' SpellCheck.IsEnabled='False'/>
   <TextBlock x:Name='Placeholder' Text='记下此刻要做的事…' IsHitTestVisible='False' FontFamily='Microsoft YaHei UI' FontSize='17' Foreground='#95DCE8F0'/>
  </Grid>
  <TextBlock x:Name='Status' Grid.Row='2' Text='已保存' FontSize='10' Foreground='#B8DCE8F0' VerticalAlignment='Center' IsHitTestVisible='False'/>
  <Thumb x:Name='Grip' Grid.Row='2' AutomationProperties.AutomationId='ResizeGrip' AutomationProperties.Name='调整便签大小'
   Width='22' Height='22' HorizontalAlignment='Right' VerticalAlignment='Bottom' Cursor='SizeNWSE'>
   <Thumb.Template><ControlTemplate TargetType='Thumb'><Grid Background='Transparent'>
    <Path Data='M 10,18 L 18,10 M 14,18 L 18,14' Stroke='#A0DAEBF8' StrokeThickness='1.3'/>
   </Grid></ControlTemplate></Thumb.Template>
  </Thumb>
 </Grid>
</Border>";

        public NoteWindow(NoteData data)
        {
            note = data;
            note.Normalize();
            Title = "透明便签";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            using (var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AppIcon"))
                Icon = BitmapFrame.Create(iconStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            ResizeMode = ResizeMode.NoResize;
            MinWidth = 260; MinHeight = 180; MaxWidth = 1600; MaxHeight = 1600;
            Width = note.Width; Height = note.Height; Topmost = note.Topmost;
            if (!double.IsNaN(note.Left)) Left = note.Left;
            if (!double.IsNaN(note.Top)) Top = note.Top;
            surface = (Border)XamlReader.Parse(Markup);
            Content = surface;
            layout = (Grid)surface.FindName("Layout");
            editor = (TextBox)surface.FindName("NoteEditor");
            placeholder = (TextBlock)surface.FindName("Placeholder");
            status = (TextBlock)surface.FindName("Status");
            toolbar = (DockPanel)surface.FindName("Toolbar");
            pin = (Button)surface.FindName("PinButton");
            editor.Text = note.Text;
            ApplyAppearance();
            editor.TextChanged += delegate {
                note.Text = editor.Text;
                placeholder.Visibility = note.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                Raise(Changed);
            };
            ((Border)surface.FindName("DragHandle")).MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                if (e.ClickCount == 1) {
                    double left = Left, top = Top;
                    DragMove();
                    if (Math.Abs(Left - left) < SystemParameters.MinimumHorizontalDragDistance &&
                        Math.Abs(Top - top) < SystemParameters.MinimumVerticalDragDistance) { chromeVisible = true; UpdateChrome(); }
                    e.Handled = true;
                }
            };
            ((Thumb)surface.FindName("Grip")).DragDelta += delegate(object sender, DragDeltaEventArgs e) {
                Width = Math.Max(MinWidth, Math.Min(MaxWidth, Width + e.HorizontalChange));
                Height = Math.Max(MinHeight, Math.Min(MaxHeight, Height + e.VerticalChange));
            };
            ((Button)surface.FindName("NewButton")).Click += delegate { Raise(NewRequested); };
            ((Button)surface.FindName("ColorButton")).Click += delegate { PickTextColor(); };
            ((Button)surface.FindName("ThroughButton")).Click += delegate { SetClickThrough(true); };
            pin.Click += delegate { note.Topmost = !note.Topmost; Topmost = note.Topmost; ApplyAppearance(); Raise(Changed); };
            ((Button)surface.FindName("HideButton")).Click += delegate { HideNote(); };
            var options = (Button)surface.FindName("OptionsButton");
            options.Click += delegate { options.ContextMenu = BuildMenu(); options.ContextMenu.PlacementTarget = options; options.ContextMenu.IsOpen = true; };
            SizeChanged += delegate { note.Width = Width; note.Height = Height; Raise(Changed); };
            LocationChanged += delegate { note.Left = Left; note.Top = Top; Raise(Changed); };
            PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { clickStart = PointToScreen(e.GetPosition(this)); };
            PreviewMouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e) {
                if (clickStart.HasValue) {
                    var delta = PointToScreen(e.GetPosition(this)) - clickStart.Value;
                    if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
                        Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) { chromeVisible = true; UpdateChrome(); }
                }
                clickStart = null;
            };
            Deactivated += delegate { chromeVisible = false; UpdateChrome(); };
            SourceInitialized += delegate {
                ApplyWindowStyle(note.ClickThrough);
                RestorePosition();
            };
            Loaded += delegate { editor.Focus(); UpdateChrome(); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) {
                if (!AllowClose) { e.Cancel = true; HideNote(); }
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) { Raise(NewRequested); e.Handled = true; }
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { Raise(SaveRequested); e.Handled = true; }
            };
        }

        void Raise(EventHandler handler) { if (handler != null) handler(this, EventArgs.Empty); }
        public void SetStatus(string text) { status.Text = text; }
        public void ShowNote() { SetClickThrough(false); note.Visible = true; Show(); Activate(); editor.Focus(); Raise(Changed); }
        public void HideNote() { note.Visible = false; Hide(); Raise(Changed); }

        void ApplyWindowStyle(bool clickThrough)
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            int style = (GetWindowLong(handle, -20) | 0x80) & ~0x40000;
            style = clickThrough ? style | 0x20 : style & ~0x20;
            if (SetWindowLong(handle, -20, style) == 0)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        public void SetClickThrough(bool enabled)
        {
            if (note.ClickThrough == enabled) return;
            ApplyWindowStyle(enabled);
            note.ClickThrough = enabled;
            chromeVisible = false; clickStart = null;
            UpdateChrome(); Raise(Changed);
        }

        void RestorePosition()
        {
            var source = (HwndSource)PresentationSource.FromVisual(this);
            var matrix = source.CompositionTarget.TransformFromDevice;
            var areas = System.Windows.Forms.Screen.AllScreens.Select(s => {
                var a = s.WorkingArea;
                return new Rect(matrix.Transform(new Point(a.Left, a.Top)), matrix.Transform(new Point(a.Right, a.Bottom)));
            }).ToArray();
            var primary = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            var area = new Rect(matrix.Transform(new Point(primary.Left, primary.Top)), matrix.Transform(new Point(primary.Right, primary.Bottom)));
            if (!double.IsNaN(note.Left) && !double.IsNaN(note.Top))
            {
                var bounds = new Rect(note.Left, note.Top, Width, Height);
                var match = areas.OrderByDescending(a => {
                    var intersection = Rect.Intersect(a, bounds);
                    return intersection.IsEmpty ? 0 : intersection.Width * intersection.Height;
                }).First();
                if (match.IntersectsWith(bounds)) area = match;
            }
            Width = Math.Max(MinWidth, Math.Min(Width, area.Width));
            Height = Math.Max(MinHeight, Math.Min(Height, area.Height));
            Left = double.IsNaN(note.Left) ? area.Right - Width - 24 : Math.Max(area.Left, Math.Min(area.Right - Width, note.Left));
            Top = double.IsNaN(note.Top) ? area.Top + 72 : Math.Max(area.Top, Math.Min(area.Bottom - Height, note.Top));
        }

        void UpdateChrome()
        {
            bool active = chromeVisible && !note.ClickThrough;
            toolbar.Opacity = active ? 1 : 0;
            ((StackPanel)toolbar.Children[0]).IsHitTestVisible = active;
            ToolTipService.SetIsEnabled((Border)surface.FindName("DragHandle"), active);
            status.Opacity = active ? 0.7 : 0;
            surface.BorderBrush = new SolidColorBrush(Color.FromArgb((byte)(active ? 50 : 0), 220, 235, 248));
        }

        void ApplyAppearance()
        {
            var foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(note.TextColor));
            var uiForeground = note.DarkText ? Brushes.Black : Brushes.White;
            var color = note.DarkText ? Color.FromRgb(234, 242, 248) : Color.FromRgb(25, 43, 57);
            color.A = (byte)Math.Max(1, Math.Round(note.BackgroundOpacity * 255));
            surface.Background = new SolidColorBrush(color);
            editor.Foreground = foreground;
            editor.CaretBrush = foreground;
            editor.FontSize = note.FontSize;
            editor.Effect = note.DarkText ? null : new DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 1, Opacity = 0.7 };
            placeholder.Visibility = note.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            placeholder.Foreground = uiForeground; placeholder.Opacity = 0.5; placeholder.FontSize = note.FontSize;
            foreach (var button in ((StackPanel)toolbar.Children[0]).Children.OfType<Button>()) button.Foreground = uiForeground;
            ((TextBlock)((Border)surface.FindName("DragHandle")).Child).Foreground = uiForeground;
            status.Foreground = uiForeground;
            ((System.Windows.Shapes.Path)surface.FindName("PinIcon")).Stroke = uiForeground;
            ((System.Windows.Shapes.Path)surface.FindName("ColorIcon")).Stroke = uiForeground;
            ((System.Windows.Shapes.Path)surface.FindName("ThroughIcon")).Stroke = uiForeground;
            var slash = (System.Windows.Shapes.Path)surface.FindName("PinSlash");
            slash.Stroke = uiForeground; slash.Visibility = note.Topmost ? Visibility.Collapsed : Visibility.Visible;
            pin.Background = note.Topmost ? new SolidColorBrush(Color.FromArgb(28, 150, 225, 245)) : Brushes.Transparent;
            pin.ToolTip = note.Topmost ? "已置顶；点击取消" : "未置顶；点击置顶";
            UpdateChrome();
        }

        MenuItem Item(string text, string id, Action action)
        {
            var item = new MenuItem { Header = text };
            System.Windows.Automation.AutomationProperties.SetAutomationId(item, id);
            item.Click += delegate { action(); };
            return item;
        }

        void PickTextColor()
        {
            var current = (Color)ColorConverter.ConvertFromString(note.TextColor);
            using (var dialog = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true,
                Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B) })
            {
                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                note.TextColor = "#" + dialog.Color.R.ToString("X2") + dialog.Color.G.ToString("X2") + dialog.Color.B.ToString("X2");
                ApplyAppearance(); Raise(Changed);
            }
        }

        ContextMenu BuildMenu()
        {
            var menu = new ContextMenu();
            var transparency = new MenuItem { Header = "背景透明度" };
            System.Windows.Automation.AutomationProperties.SetAutomationId(transparency, "TransparencyMenu");
            foreach (int percent in new[] { 100, 85, 78, 65, 35, 0 })
            {
                int value = percent;
                double opacity = Math.Max(1.0 / 255, 1 - value / 100.0);
                var item = Item(value == 100 ? "100% · 仅文字" : value + "%", "Opacity" + value,
                    delegate { note.BackgroundOpacity = opacity; ApplyAppearance(); Raise(Changed); });
                item.IsCheckable = true; item.IsChecked = Math.Abs(note.BackgroundOpacity - opacity) < 0.005;
                transparency.Items.Add(item);
            }
            menu.Items.Add(transparency);
            var size = new MenuItem { Header = "文字大小" };
            foreach (int value in new[] { 14, 17, 20, 24, 28 })
            {
                int font = value;
                var item = Item(font + "", "Font" + font, delegate { note.FontSize = font; ApplyAppearance(); Raise(Changed); });
                item.IsCheckable = true; item.IsChecked = note.FontSize == font; size.Items.Add(item);
            }
            menu.Items.Add(size);
            var colorItem = Item("更换字体颜色…", "TextColorPickerMenu", PickTextColor);
            colorItem.Icon = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = editor.Foreground, Stroke = Brushes.Gray, StrokeThickness = 1 };
            menu.Items.Add(colorItem);
            bool useDark = note.TextColor != "#182732";
            menu.Items.Add(Item(useDark ? "使用深色文字" : "使用白色文字", "TextColorMenu",
                delegate { note.TextColor = useDark ? "#182732" : "#F8FAFC"; ApplyAppearance(); Raise(Changed); }));
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("删除这张便签…", "DeleteMenu", delegate { Raise(DeleteRequested); }));
            menu.Items.Add(Item("退出程序", "ExitMenu", delegate { Raise(ExitRequested); }));
            return menu;
        }
    }
}
