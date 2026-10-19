using System;
using System.IO;
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
        readonly ImageStore images;
        readonly StackPanel imageLinks;
        readonly ScrollViewer imageScroll;
        ImagePreviewWindow preview;
        NoteImage previewImage;
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
   <Setter Property='Width' Value='28'/><Setter Property='Height' Value='28'/>
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
  <Grid.RowDefinitions><RowDefinition Height='32'/><RowDefinition Height='*'/><RowDefinition Height='Auto'/><RowDefinition Height='24'/></Grid.RowDefinitions>
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
    <Button x:Name='ImageButton' AutomationProperties.AutomationId='AddImageButton' AutomationProperties.Name='添加图片' ToolTip='添加图片；也可直接 Ctrl+V 粘贴图片'>
     <Viewbox Width='17' Height='17'>
      <Path x:Name='ImageIcon' Data='M 3,4 L 21,4 L 21,20 L 3,20 Z M 3,16 L 8,11 L 13,16 L 17,12 L 21,16 M 14,8 A 1.5,1.5 0 1 0 17,8 A 1.5,1.5 0 1 0 14,8' StrokeThickness='1.5' StrokeLineJoin='Round'/>
     </Viewbox>
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
    VerticalScrollBarVisibility='Hidden' HorizontalScrollBarVisibility='Disabled'
    SelectionBrush='#609AE4F3' CaretBrush='#A8F0FC' SpellCheck.IsEnabled='False'/>
   <TextBlock x:Name='Placeholder' Text='记下此刻要做的事…' IsHitTestVisible='False' FontFamily='Microsoft YaHei UI' FontSize='17' Foreground='#95DCE8F0'/>
  </Grid>
  <ScrollViewer x:Name='ImageScroll' Grid.Row='2' Visibility='Collapsed' VerticalScrollBarVisibility='Hidden' HorizontalScrollBarVisibility='Disabled' Margin='0,2,0,4'>
   <StackPanel x:Name='ImageLinks'/>
  </ScrollViewer>
  <TextBlock x:Name='Status' Grid.Row='3' Text='已保存' FontSize='10' Foreground='#B8DCE8F0' VerticalAlignment='Center' IsHitTestVisible='False'/>
  <Thumb x:Name='Grip' Grid.Row='3' AutomationProperties.AutomationId='ResizeGrip' AutomationProperties.Name='调整便签大小'
   Width='22' Height='22' HorizontalAlignment='Right' VerticalAlignment='Bottom' Cursor='SizeNWSE'>
   <Thumb.Template><ControlTemplate TargetType='Thumb'><Grid Background='Transparent'>
    <Path Data='M 10,18 L 18,10 M 14,18 L 18,14' Stroke='#A0DAEBF8' StrokeThickness='1.3'/>
   </Grid></ControlTemplate></Thumb.Template>
  </Thumb>
 </Grid>
</Border>";

        public NoteWindow(NoteData data, ImageStore imageStore)
        {
            note = data; images = imageStore;
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
            imageLinks = (StackPanel)surface.FindName("ImageLinks");
            imageScroll = (ScrollViewer)surface.FindName("ImageScroll");
            editor.Text = note.Text;
            ApplyAppearance();
            CommandManager.AddPreviewCanExecuteHandler(editor, delegate(object sender, CanExecuteRoutedEventArgs e) {
                if (e.Command != ApplicationCommands.Paste) return;
                try { if (HasClipboardImage(Clipboard.GetDataObject())) { e.CanExecute = true; e.Handled = true; } }
                catch (ExternalException) { }
            });
            CommandManager.AddPreviewExecutedHandler(editor, delegate(object sender, ExecutedRoutedEventArgs e) {
                if (e.Command != ApplicationCommands.Paste) return;
                try { if (PasteImage(Clipboard.GetDataObject())) e.Handled = true; }
                catch (ExternalException ex) { e.Handled = true; ImageError(ex); }
            });
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
            ((Button)surface.FindName("ImageButton")).Click += delegate { PickImages(); };
            ((Button)surface.FindName("ThroughButton")).Click += delegate { SetClickThrough(true); };
            pin.Click += delegate { note.Topmost = !note.Topmost; Topmost = note.Topmost; ApplyAppearance(); Raise(Changed); };
            ((Button)surface.FindName("HideButton")).Click += delegate { HideNote(); };
            var options = (Button)surface.FindName("OptionsButton");
            options.Click += delegate { options.ContextMenu = BuildMenu(); options.ContextMenu.PlacementTarget = options; options.ContextMenu.IsOpen = true; };
            SizeChanged += delegate { note.Width = Width; note.Height = Height; LayoutImages(); Raise(Changed); };
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
                else ClosePreview();
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.N) { Raise(NewRequested); e.Handled = true; }
                if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { Raise(SaveRequested); e.Handled = true; }
            };
        }

        void Raise(EventHandler handler) { if (handler != null) handler(this, EventArgs.Empty); }
        public void SetStatus(string text) { status.Text = text; }
        public void ShowNote() { SetClickThrough(false); note.Visible = true; Show(); Activate(); editor.Focus(); Raise(Changed); }
        public void HideNote() { ClosePreview(); note.Visible = false; Hide(); Raise(Changed); }

        void ClosePreview() { if (preview != null) preview.Close(); }

        void AddImage(NoteImage image)
        {
            note.Images.Add(image); RefreshImages(); Raise(Changed);
        }

        void PickImages()
        {
            using (var dialog = new System.Windows.Forms.OpenFileDialog { Title = "选择图片", Multiselect = true,
                Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff", CheckFileExists = true })
            {
                if (dialog.ShowDialog(new NativeOwner(new WindowInteropHelper(this).Handle)) != System.Windows.Forms.DialogResult.OK) return;
                try { foreach (string file in dialog.FileNames) AddImage(images.Import(file)); }
                catch (Exception ex) { ImageError(ex); }
            }
        }

        sealed class NativeOwner : System.Windows.Forms.IWin32Window
        {
            public IntPtr Handle { get; private set; }
            public NativeOwner(IntPtr handle) { Handle = handle; }
        }

        static bool HasClipboardImage(IDataObject data)
        {
            return data != null && (data.GetDataPresent("PNG") || data.GetDataPresent("image/png") || data.GetDataPresent(DataFormats.Bitmap) || data.GetDataPresent(DataFormats.FileDrop));
        }

        bool PasteImage(IDataObject data)
        {
            if (!HasClipboardImage(data)) return false;
            try
            {
                if (data.GetDataPresent("PNG") || data.GetDataPresent("image/png"))
                {
                    var stream = data.GetData(data.GetDataPresent("PNG") ? "PNG" : "image/png") as Stream;
                    if (stream == null) throw new InvalidOperationException("剪贴板图片格式无法读取，请通过图片图标选择文件。");
                    if (stream.CanSeek) stream.Position = 0;
                    AddImage(images.Add(ImageStore.Decode(stream), "图片 " + DateTime.Now.ToString("MM-dd HH:mm:ss")));
                }
                // Screenshot DIB alpha bytes can be unused; PNG carries real transparency.
                else if (data.GetDataPresent(DataFormats.Bitmap)) AddImage(images.Add(
                    new FormatConvertedBitmap(Clipboard.GetImage(), PixelFormats.Bgr32, null, 0), "图片 " + DateTime.Now.ToString("MM-dd HH:mm:ss")));
                else foreach (string file in (string[])data.GetData(DataFormats.FileDrop)) AddImage(images.Import(file));
            }
            catch (Exception ex) { ImageError(ex); }
            return true;
        }

        void ImageError(Exception ex)
        {
            MessageBox.Show(this, "图片无法读取或保存，已有便签内容保留。\n" + ex.Message, "透明便签", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        void RefreshImages()
        {
            imageLinks.Children.Clear();
            foreach (var entry in note.Images)
            {
                var image = entry;
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var link = new Button { Width = double.NaN, Height = 28, HorizontalAlignment = HorizontalAlignment.Left,
                    Foreground = editor.Foreground, FontSize = note.FontSize, Opacity = note.TextOpacity, ToolTip = "点击预览：" + image.DisplayName,
                    Content = new TextBlock { Text = image.DisplayName, TextDecorations = TextDecorations.Underline,
                        TextTrimming = TextTrimming.CharacterEllipsis, Width = Math.Max(40, Width - 90) } };
                System.Windows.Automation.AutomationProperties.SetAutomationId(link, "ImageLink-" + image.FileName);
                System.Windows.Automation.AutomationProperties.SetName(link, image.DisplayName);
                link.Click += delegate {
                    try
                    {
                        var bitmap = images.Load(image); ClosePreview();
                        previewImage = image; preview = new ImagePreviewWindow(this, bitmap, image.DisplayName);
                        preview.Closed += delegate { preview = null; previewImage = null; };
                        preview.Show();
                    }
                    catch (Exception ex) { ImageError(ex); }
                };
                row.Children.Add(link);
                var remove = new Button { Width = 24, Height = 28, Foreground = status.Foreground, Content = "×", ToolTip = "移除图片链接" };
                System.Windows.Automation.AutomationProperties.SetAutomationId(remove, "RemoveImage-" + image.FileName);
                System.Windows.Automation.AutomationProperties.SetName(remove, "移除图片链接：" + image.DisplayName);
                remove.Click += delegate {
                    if (previewImage == image) ClosePreview();
                    note.Images.Remove(image); RefreshImages(); Raise(Changed);
                };
                Grid.SetColumn(remove, 1); row.Children.Add(remove); imageLinks.Children.Add(row);
            }
            imageScroll.Visibility = note.Images.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            LayoutImages();
        }

        void LayoutImages()
        {
            if (imageScroll == null) return;
            imageScroll.MaxHeight = Math.Max(28, (Height - 120) * 0.45);
            foreach (Grid row in imageLinks.Children)
                ((TextBlock)((Button)row.Children[0]).Content).Width = Math.Max(40, Width - 90);
        }

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
            if (enabled) ClosePreview();
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
            editor.Opacity = note.TextOpacity;
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
            ((System.Windows.Shapes.Path)surface.FindName("ImageIcon")).Stroke = uiForeground;
            ((System.Windows.Shapes.Path)surface.FindName("ThroughIcon")).Stroke = uiForeground;
            var slash = (System.Windows.Shapes.Path)surface.FindName("PinSlash");
            slash.Stroke = uiForeground; slash.Visibility = note.Topmost ? Visibility.Collapsed : Visibility.Visible;
            pin.Background = note.Topmost ? new SolidColorBrush(Color.FromArgb(28, 150, 225, 245)) : Brushes.Transparent;
            pin.ToolTip = note.Topmost ? "已置顶；点击取消" : "未置顶；点击置顶";
            RefreshImages();
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
            var opacityPanel = new DockPanel { Width = 180 };
            var opacityValue = new TextBlock { Width = 40, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(opacityValue, Dock.Right); opacityPanel.Children.Add(opacityValue);
            var opacitySlider = new Slider { Minimum = 0, Maximum = 100, Value = Math.Round((1 - note.TextOpacity) * 100),
                TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 10, VerticalAlignment = VerticalAlignment.Center };
            opacityValue.Text = opacitySlider.Value + "%";
            System.Windows.Automation.AutomationProperties.SetAutomationId(opacitySlider, "TextTransparencySlider");
            System.Windows.Automation.AutomationProperties.SetName(opacitySlider, "文字透明度");
            opacitySlider.ToolTip = "文字透明度：0% 不透明，100% 完全透明";
            opacitySlider.ValueChanged += delegate {
                note.TextOpacity = 1 - opacitySlider.Value / 100;
                opacityValue.Text = opacitySlider.Value + "%"; ApplyAppearance(); Raise(Changed);
            };
            opacityPanel.Children.Add(opacitySlider);
            var opacityItem = new MenuItem { Header = opacityPanel, StaysOpenOnClick = true, ToolTip = opacitySlider.ToolTip,
                Icon = new Viewbox { Width = 17, Height = 17, Child = new System.Windows.Shapes.Path {
                    Data = Geometry.Parse("M 2,21 L 8,3 L 14,21 M 4,15 L 12,15 M 21,5 A 7,7 0 1 0 21,19 Z M 21,5 L 21,19"),
                    Stroke = Brushes.Black, StrokeThickness = 1.5, StrokeLineJoin = PenLineJoin.Round } } };
            System.Windows.Automation.AutomationProperties.SetName(opacityItem, "文字透明度");
            menu.Items.Add(opacityItem);
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
