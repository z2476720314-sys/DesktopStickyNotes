using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DesktopStickyNotes;

static class Checks
{
    static void Check(bool pass, string message)
    {
        if (!pass) throw new Exception("FAIL: " + message);
        Console.WriteLine("PASS: " + message);
    }

    static int Main(string[] args)
    {
        string directory = Path.Combine(args[0], "store-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new NoteStore(directory);
            Check(store.Load().Count == 0, "first start has no previous notes");
            var note = new NoteData { Text = "采购清单\r\n手机壳 × 2 📝", Left = 215, Top = 126,
                Width = 420, Height = 260, BackgroundOpacity = 0.15, FontSize = 22, Topmost = false };
            store.Save(new List<NoteData> { note });
            Check(File.Exists(store.FilePath), "save creates a persistent file");
            var saved = new NoteStore(directory).Load();
            Check(saved.Count == 1 && saved[0].Text == "采购清单\r\n手机壳 × 2 📝", "Chinese, newlines and emoji survive restart");
            Check(saved[0].Left == 215 && saved[0].Top == 126 && saved[0].Width == 420 &&
                saved[0].Height == 260 && saved[0].BackgroundOpacity == 0.15 &&
                saved[0].FontSize == 22 && !saved[0].Topmost, "position, size and appearance survive restart");
            note.Text = "第二次编辑";
            store.Save(new List<NoteData> { note, new NoteData { Text = "另一个便签", Visible = false } });
            Check(store.Load().Count == 2 && !store.Load()[1].Visible, "multiple notes and hidden state persist");
            Check(File.Exists(store.FilePath + ".bak"), "last good save is retained as a backup");
            File.WriteAllText(store.FilePath, "<broken");
            var recovery = new NoteStore(directory);
            Check(recovery.Load()[0].Text == "采购清单\r\n手机壳 × 2 📝" && recovery.RecoveredFromBackup,
                "damaged primary recovers the previous save");
            Check(Directory.GetFiles(directory, "notes.xml.corrupt-*").Length == 1, "damaged file is preserved for recovery");
            note.Text = "恢复后编辑";
            recovery.Save(new List<NoteData> { note });
            Check(recovery.Load()[0].Text == "恢复后编辑", "normal saving continues after recovery");
            var invalidDirectory = Path.Combine(directory, "invalid");
            Directory.CreateDirectory(invalidDirectory);
            File.WriteAllText(Path.Combine(invalidDirectory, "notes.xml"), "<broken");
            bool rejected = false;
            try { new NoteStore(invalidDirectory).Load(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && File.ReadAllText(Path.Combine(invalidDirectory, "notes.xml")) == "<broken",
                "unrecoverable data is rejected without overwriting it");
            string blocked = Path.Combine(directory, "blocked");
            File.WriteAllText(blocked, "occupied");
            bool failed = false;
            try { new NoteStore(blocked).Save(new List<NoteData> { note }); } catch (IOException) { failed = true; }
            Check(failed && store.Load()[0].Text == "恢复后编辑", "write failure is reported and existing notes remain readable");
            File.WriteAllText(store.FilePath, "<DesktopStickyNotes xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" xsi:nil=\"true\"/>");
            var nilRecovery = new NoteStore(directory);
            Check(nilRecovery.Load()[0].Text == "采购清单\r\n手机壳 × 2 📝" && nilRecovery.RecoveredFromBackup,
                "nil XML root recovers a valid backup instead of crashing");
            var malformed = new NoteData { Width = double.NaN, Height = -1, FontSize = double.PositiveInfinity,
                BackgroundOpacity = -2, Left = double.PositiveInfinity, Text = null };
            store.Save(new List<NoteData> { malformed });
            var normalized = store.Load()[0];
            Check(normalized.Width == 330 && normalized.Height == 180 && normalized.FontSize == 17 &&
                normalized.BackgroundOpacity == 1.0 / 255 && double.IsNaN(normalized.Left) && normalized.Text == "",
                "malformed appearance values cannot create an unusable window");
            store.Save(new List<NoteData> { new NoteData { TextColor = "#126ACB" }, new NoteData { DarkText = true }, new NoteData { TextColor = "invalid" } });
            var colors = store.Load();
            Check(colors[0].TextColor == "#126ACB", "custom RGB font color survives save and restart");
            Check(colors[1].TextColor == "#182732" && colors[2].TextColor == "#F8FAFC", "old dark text and invalid font color get readable defaults");
            File.WriteAllText(store.FilePath, "<DesktopStickyNotes><Notes><NoteData><Text>穿透便签</Text><ClickThrough>true</ClickThrough></NoteData></Notes></DesktopStickyNotes>");
            store.Save(store.Load());
            Check(File.ReadAllText(store.FilePath).Contains("<ClickThrough>true</ClickThrough>"), "click-through state survives a save and reload");
            File.WriteAllText(store.FilePath, "<DesktopStickyNotes><Notes><NoteData><Text>旧便签</Text></NoteData></Notes></DesktopStickyNotes>");
            store.Save(store.Load());
            Check(File.ReadAllText(store.FilePath).Contains("<ClickThrough>false</ClickThrough>"), "legacy notes default to editable mode");
            File.WriteAllText(store.FilePath, "<DesktopStickyNotes><Notes><NoteData><Text>图片说明</Text><Images><NoteImage><FileName>0123456789abcdef0123456789abcdef.png</FileName><DisplayName>参考图片.png</DisplayName></NoteImage></Images></NoteData></Notes></DesktopStickyNotes>");
            store.Save(store.Load());
            var imageRoundtrip = System.Xml.Linq.XDocument.Load(store.FilePath);
            Check(imageRoundtrip.Descendants("FileName").FirstOrDefault() != null &&
                imageRoundtrip.Descendants("FileName").First().Value == "0123456789abcdef0123456789abcdef.png" &&
                imageRoundtrip.Descendants("DisplayName").First().Value == "参考图片.png" &&
                imageRoundtrip.Descendants("Text").First().Value == "图片说明", "image links and text survive save and reload together");
            var invalidImageDirectory = Path.Combine(directory, "invalid-image");
            Directory.CreateDirectory(invalidImageDirectory);
            File.WriteAllText(Path.Combine(invalidImageDirectory, "notes.xml"), "<DesktopStickyNotes><Notes><NoteData><Images><NoteImage><FileName>../outside.png</FileName></NoteImage></Images></NoteData></Notes></DesktopStickyNotes>");
            bool invalidImageRejected = false;
            try { new NoteStore(invalidImageDirectory).Load(); } catch (InvalidOperationException) { invalidImageRejected = true; }
            Check(invalidImageRejected, "image links cannot refer to files outside their own data directory");
            store.Save(new List<NoteData> { new NoteData { Text = "移除图片后" } });
            Check(System.Xml.Linq.XDocument.Load(store.FilePath).Descendants("NoteImage").Count() == 0 &&
                System.Xml.Linq.XDocument.Load(store.FilePath + ".bak").Descendants("NoteImage").Count() == 1,
                "removing image links preserves the preceding references in the XML backup");
            Console.WriteLine("All persistence checks passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }
}
