using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace DesktopStickyNotes
{
    public sealed class ImageStore
    {
        readonly string directory;
        public ImageStore(string dataDirectory) { directory = Path.Combine(dataDirectory, "Images"); }

        public static BitmapSource Decode(Stream stream)
        {
            var image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            image.Freeze(); return image;
        }

        public NoteImage Import(string filePath)
        {
            using (var stream = File.OpenRead(filePath)) return Add(Decode(stream), Path.GetFileName(filePath));
        }

        public NoteImage Add(BitmapSource image, string displayName)
        {
            if (image == null) throw new InvalidOperationException("剪贴板中没有可读取的图片。");
            Directory.CreateDirectory(directory);
            var reference = new NoteImage { FileName = Guid.NewGuid().ToString("N") + ".png", DisplayName = displayName };
            reference.Normalize();
            string path = Path.Combine(directory, reference.FileName);
            try
            {
                using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
                    encoder.Save(stream); stream.Flush(true);
                }
            }
            catch { if (File.Exists(path)) File.Delete(path); throw; }
            return reference;
        }

        public BitmapSource Load(NoteImage reference)
        {
            reference.Normalize();
            using (var stream = File.OpenRead(Path.Combine(directory, reference.FileName))) return Decode(stream);
        }
    }
}
