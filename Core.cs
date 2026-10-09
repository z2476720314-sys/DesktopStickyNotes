using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Serialization;

namespace DesktopStickyNotes
{
    public sealed class NoteData
    {
        public string Text = "";
        public string TextColor;
        public double Left = double.NaN, Top = double.NaN;
        public double Width = 330, Height = 310, BackgroundOpacity = 0.22, FontSize = 17;
        public bool Topmost = true, Visible = true, DarkText = false, ClickThrough = false;

        public void Normalize()
        {
            Text = Text ?? "";
            if (TextColor == null || !Regex.IsMatch(TextColor, "^#[0-9a-fA-F]{6}([0-9a-fA-F]{2})?$"))
                TextColor = DarkText ? "#182732" : "#F8FAFC";
            else TextColor = (TextColor.Length == 9 ? "#" + TextColor.Substring(3) : TextColor).ToUpperInvariant();
            Width = Bounded(Width, 330, 260, 1600);
            Height = Bounded(Height, 310, 180, 1600);
            BackgroundOpacity = Bounded(BackgroundOpacity, 0.22, 1.0 / 255, 1);
            FontSize = Bounded(FontSize, 17, 12, 32);
            if (double.IsInfinity(Left)) Left = double.NaN;
            if (double.IsInfinity(Top)) Top = double.NaN;
        }

        static double Bounded(double value, double fallback, double min, double max)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
        }
    }

    [XmlRoot("DesktopStickyNotes")]
    public sealed class NotesFile
    {
        public List<NoteData> Notes = new List<NoteData>();
    }

    public sealed class NoteStore
    {
        public readonly string FilePath;
        public bool RecoveredFromBackup { get; private set; }
        public NoteStore(string directory) { FilePath = System.IO.Path.Combine(directory, "notes.xml"); }
        static readonly XmlSerializer Serializer = new XmlSerializer(typeof(NotesFile));

        List<NoteData> Read(string path)
        {
            using (var reader = XmlReader.Create(path, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }))
            {
                var file = (NotesFile)Serializer.Deserialize(reader);
                if (file == null || file.Notes == null || file.Notes.Exists(n => n == null))
                    throw new InvalidOperationException("便签文件内容无效。");
                foreach (var note in file.Notes) note.Normalize();
                return file.Notes;
            }
        }

        public List<NoteData> Load()
        {
            if (!File.Exists(FilePath) && !File.Exists(FilePath + ".bak")) return new List<NoteData>();
            try { if (File.Exists(FilePath)) return Read(FilePath); }
            catch (InvalidOperationException) { if (!File.Exists(FilePath + ".bak")) throw; }
            var recovered = Read(FilePath + ".bak");
            if (File.Exists(FilePath))
                File.Copy(FilePath, FilePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"), false);
            File.Copy(FilePath + ".bak", FilePath, true);
            RecoveredFromBackup = true;
            return recovered;
        }

        public void Save(List<NoteData> notes)
        {
            if (notes == null || notes.Exists(n => n == null)) throw new ArgumentException("便签内容无效。", "notes");
            foreach (var note in notes) note.Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temporary = FilePath + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    using (var writer = XmlWriter.Create(stream, new XmlWriterSettings {
                        Encoding = new UTF8Encoding(false), Indent = true, NewLineHandling = NewLineHandling.Entitize }))
                        Serializer.Serialize(writer, new NotesFile { Notes = notes });
                    stream.Flush(true);
                }
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak");
                else File.Move(temporary, FilePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
