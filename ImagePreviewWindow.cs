using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DesktopStickyNotes
{
    public sealed class ImagePreviewWindow : Window
    {
        [DllImport("user32.dll", SetLastError = true)] static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll", SetLastError = true)] static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        readonly Canvas viewport;
        readonly Image image;
        readonly TextBlock percentage;
        readonly BitmapSource source;
        double scale = 1;
        Point offset;
        Point? dragStart;
        Point dragOffset;

        const string Markup = @"
<Grid xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
 <Grid.Resources>
  <Style TargetType='Button'>
   <Setter Property='Width' Value='32'/><Setter Property='Height' Value='32'/>
   <Setter Property='Background' Value='Transparent'/><Setter Property='BorderBrush' Value='Transparent'/>
   <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
    <Border x:Name='B' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='5'>
     <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
    </Border>
    <ControlTemplate.Triggers>
     <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='B' Property='Background' Value='#35FFFFFF'/></Trigger>
     <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='B' Property='BorderBrush' Value='#BDEFFF'/></Trigger>
    </ControlTemplate.Triggers>
   </ControlTemplate></Setter.Value></Setter>
  </Style>
 </Grid.Resources>
 <Grid.RowDefinitions><RowDefinition Height='48'/><RowDefinition Height='*'/></Grid.RowDefinitions>
 <StackPanel x:Name='Tools' Orientation='Horizontal' HorizontalAlignment='Center' VerticalAlignment='Center'>
  <Button x:Name='Move' AutomationProperties.AutomationId='PreviewMoveButton' AutomationProperties.Name='移动预览窗口' ToolTip='拖动这里移动窗口' Cursor='SizeAll'/>
  <Button x:Name='Out' AutomationProperties.AutomationId='ZoomOutButton' AutomationProperties.Name='缩小图片' ToolTip='缩小图片'/>
  <TextBlock x:Name='Percentage' Width='48' Foreground='#F8FAFC' FontFamily='Segoe UI' FontSize='12' TextAlignment='Center' VerticalAlignment='Center'>
   <TextBlock.Effect><DropShadowEffect Color='Black' ShadowDepth='0' BlurRadius='3' Opacity='1'/></TextBlock.Effect>
  </TextBlock>
  <Button x:Name='In' AutomationProperties.AutomationId='ZoomInButton' AutomationProperties.Name='放大图片' ToolTip='放大图片'/>
  <Button x:Name='Fit' AutomationProperties.AutomationId='FitImageButton' AutomationProperties.Name='完整显示图片' ToolTip='完整显示图片'/>
  <Button x:Name='Actual' AutomationProperties.AutomationId='ActualSizeButton' AutomationProperties.Name='原始大小' ToolTip='原始大小（1:1）'/>
  <Button x:Name='Close' AutomationProperties.AutomationId='ClosePreviewButton' AutomationProperties.Name='关闭图片预览' ToolTip='关闭图片预览（Esc）'/>
 </StackPanel>
 <Canvas x:Name='Viewport' Grid.Row='1' ClipToBounds='True' Background='Transparent'>
  <Image x:Name='PreviewImage' AutomationProperties.AutomationId='PreviewImage' Stretch='Fill' Cursor='Hand'/>
 </Canvas>
</Grid>";

        public ImagePreviewWindow(Window owner, BitmapSource bitmap, string name)
        {
            source = bitmap; Owner = owner; Title = "图片预览";
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; Topmost = owner.Topmost;
            var area = SystemParameters.WorkArea;
            Width = Math.Min(1000, area.Width - 48); Height = Math.Min(760, area.Height - 48);
            Left = area.Left + (area.Width - Width) / 2; Top = area.Top + (area.Height - Height) / 2;
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AppIcon"))
                Icon = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var layout = (Grid)XamlReader.Parse(Markup); Content = layout;
            viewport = (Canvas)layout.FindName("Viewport"); image = (Image)layout.FindName("PreviewImage");
            percentage = (TextBlock)layout.FindName("Percentage"); image.Source = source;
            AutomationProperties.SetName(image, name);
            Button move = SetIcon(layout, "Move", "M 12,2 L 12,22 M 2,12 L 22,12 M 8,6 L 12,2 L 16,6 M 8,18 L 12,22 L 16,18 M 6,8 L 2,12 L 6,16 M 18,8 L 22,12 L 18,16");
            SetIcon(layout, "Out", "M 5,12 L 19,12").Click += delegate { Zoom(scale / 1.2, Center()); };
            SetIcon(layout, "In", "M 5,12 L 19,12 M 12,5 L 12,19").Click += delegate { Zoom(scale * 1.2, Center()); };
            SetIcon(layout, "Fit", "M 8,3 L 3,3 L 3,8 M 16,3 L 21,3 L 21,8 M 3,16 L 3,21 L 8,21 M 16,21 L 21,21 L 21,16").Click += delegate { Fit(); };
            SetIcon(layout, "Actual", "M 4,4 L 20,4 L 20,20 L 4,20 Z M 8,8 L 16,8 L 16,16 L 8,16 Z").Click += delegate { CenterAt(1); };
            SetIcon(layout, "Close", "M 5,5 L 19,19 M 19,5 L 5,19").Click += delegate { Close(); };
            move.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { DragMove(); e.Handled = true; };
            viewport.MouseWheel += delegate(object sender, MouseWheelEventArgs e) {
                Zoom(scale * Math.Pow(1.2, e.Delta / 120.0), e.GetPosition(viewport)); e.Handled = true;
            };
            image.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
                dragStart = e.GetPosition(viewport); dragOffset = offset; image.CaptureMouse(); e.Handled = true;
            };
            image.MouseMove += delegate(object sender, MouseEventArgs e) {
                if (!dragStart.HasValue) return;
                var delta = e.GetPosition(viewport) - dragStart.Value;
                offset = dragOffset + delta; Render();
            };
            image.MouseLeftButtonUp += delegate { dragStart = null; image.ReleaseMouseCapture(); };
            image.LostMouseCapture += delegate { dragStart = null; };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
            SourceInitialized += delegate {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (SetWindowLong(hwnd, -20, (GetWindowLong(hwnd, -20) | 0x80) & ~0x40000) == 0)
                    throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            };
            Loaded += delegate { Fit(); };
        }

        static Button SetIcon(Grid layout, string name, string geometry)
        {
            var button = (Button)layout.FindName(name);
            button.Content = new Viewbox { Width = 18, Height = 18, Child = new System.Windows.Shapes.Path {
                Data = Geometry.Parse(geometry), Stroke = Brushes.White, StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, BlurRadius = 3, ShadowDepth = 0, Opacity = 1 } } };
            return button;
        }
        Point Center() { return new Point(viewport.ActualWidth / 2, viewport.ActualHeight / 2); }
        void Fit() { CenterAt(Math.Min(1, Math.Min(viewport.ActualWidth / source.PixelWidth, viewport.ActualHeight / source.PixelHeight))); }
        void CenterAt(double value)
        {
            scale = value; var center = Center();
            offset = new Point(center.X - source.PixelWidth * scale / 2, center.Y - source.PixelHeight * scale / 2); Render();
        }
        void Zoom(double value, Point anchor)
        {
            value = Math.Max(1.0 / Math.Max(source.PixelWidth, source.PixelHeight), Math.Min(20, value)); double ratio = value / scale;
            offset = new Point(anchor.X - (anchor.X - offset.X) * ratio, anchor.Y - (anchor.Y - offset.Y) * ratio);
            scale = value; Render();
        }
        void Render()
        {
            image.Width = source.PixelWidth * scale; image.Height = source.PixelHeight * scale;
            Canvas.SetLeft(image, offset.X); Canvas.SetTop(image, offset.Y);
            percentage.Text = Math.Round(scale * 100) + "%";
        }
    }
}
