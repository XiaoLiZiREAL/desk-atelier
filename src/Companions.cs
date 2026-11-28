using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DeskMate
{
    internal sealed class CompanionInfo
    {
        public string Id, Name, Description;
    }

    internal static class CompanionArchive
    {
        const int MetadataLimit = 16384, ImageLimit = 20 * 1024 * 1024;
        internal const int MaxPets = 64;
        internal static readonly string[] BuiltinIds={"rem","kurisu","steve","hitori","mutsumi","kuroneko"};
        internal static bool IsBuiltin(string id){return Array.IndexOf(BuiltinIds,id)>=0;}
        internal static bool IsId(string id)
        {
            return id != null && id.Length == 71 && id.StartsWith("custom-", StringComparison.Ordinal) &&
                id.Skip(7).All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));
        }

        static InvalidDataException Bad(string reason) { return new InvalidDataException(reason); }
        static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = MetadataLimit, RecursionLimit = 12 }; }

        static byte[] ReadEntry(ZipArchiveEntry entry, int limit)
        {
            if (entry.Length <= 0 || entry.Length > limit) throw Bad("角色文件超过了支持的大小。");
            byte[] data = new byte[(int)entry.Length];
            using (Stream stream = entry.Open())
            {
                ReadExactly(stream, data);
                if (stream.ReadByte() != -1) throw Bad("压缩包内的文件长度不匹配。");
            }
            return data;
        }

        static void ReadExactly(Stream stream, byte[] target)
        {
            int offset = 0;
            while (offset < target.Length)
            {
                int got = stream.Read(target, offset, target.Length - offset);
                if (got == 0) throw Bad("角色包内容不完整。");
                offset += got;
            }
        }

        // Bound the central directory before ZipArchive materializes its entry list.
        static void CheckDirectory(FileStream stream)
        {
            if (stream.Length < 22 || stream.Length > 24 * 1024 * 1024) throw Bad("角色 ZIP 须小于 24 MB。");
            int tailSize = (int)Math.Min(stream.Length, 65557);
            long start = stream.Length - tailSize;
            stream.Position = start;
            byte[] tail = new byte[tailSize]; ReadExactly(stream, tail);
            for (int i = tail.Length - 22; i >= 0; i--)
            {
                if (BitConverter.ToUInt32(tail, i) != 0x06054b50U ||
                    i + 22 + BitConverter.ToUInt16(tail, i + 20) != tail.Length) continue;
                ushort count = BitConverter.ToUInt16(tail, i + 10);
                uint size = BitConverter.ToUInt32(tail, i + 12), offset = BitConverter.ToUInt32(tail, i + 16);
                if (BitConverter.ToUInt32(tail, i + 4) != 0 || BitConverter.ToUInt16(tail, i + 8) != count ||
                    count < 2 || count > 64 || size > 65536 || (long)offset + size != start + i)
                    throw Bad("角色包需要一个标准 ZIP，且最多包含 64 项。");
                stream.Position = 0;
                return;
            }
            throw Bad("无法读取这个 ZIP 文件。");
        }

        static string EntryPath(ZipArchiveEntry entry) { return entry.FullName.Replace('\\','/'); }
        static string Leaf(string path) { return path.Substring(path.LastIndexOf('/') + 1); }

        static Dictionary<string, object> Manifest(byte[] bytes)
        {
            string text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
            try
            {
                var data = Json().DeserializeObject(text) as Dictionary<string,object>;
                if (data != null) return data;
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            throw Bad("pet.json 不是有效的角色说明。");
        }

        static string Field(Dictionary<string,object> fields, string key)
        {
            object raw; return fields.TryGetValue(key,out raw) ? (raw as string ?? "").Trim() : "";
        }

        static string CleanText(string value, int max)
        {
            string cleaned = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (cleaned.Length <= max) return cleaned;
            int cut = char.IsHighSurrogate(cleaned[max - 1]) ? max - 1 : max;
            return cleaned.Substring(0, cut).TrimEnd();
        }

        internal static CompanionInfo InspectInstalled(string folder)
        {
            string id = Path.GetFileName(folder);
            if (!IsId(id)) return null;
            try
            {
                string meta = Path.Combine(folder,"pet.json");
                if (!File.Exists(meta) || new FileInfo(meta).Length > MetadataLimit ||
                    !File.Exists(Path.Combine(folder,"atlas.png")) || !File.Exists(Path.Combine(folder,"portrait.png"))) return null;
                var m = Manifest(File.ReadAllBytes(meta));
                string name = CleanText(Field(m,"displayName"),32);
                return name.Length == 0 ? null : new CompanionInfo { Id=id, Name=name, Description=CleanText(Field(m,"description"),220) };
            }
            catch (IOException) { return null; }
            catch (InvalidDataException) { return null; }
            catch (DecoderFallbackException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        static int Big32(byte[] bytes, int i)
        { return bytes[i] << 24 | bytes[i + 1] << 16 | bytes[i + 2] << 8 | bytes[i + 3]; }

        static MemoryStream ImageChunks(byte[] bytes)
        {
            var stream=new MemoryStream(bytes.Length);
            stream.Write(bytes,0,8);
            int offset=8, count=0;
            while(offset<=bytes.Length-12 && ++count<=8192)
            {
                int length=Big32(bytes,offset), kind=Big32(bytes,offset+4);
                if(length<0 || length>bytes.Length-offset-12)break;
                // Keep only PNG's image data, palette and transparency. Text, profiles and other
                // ancillary payloads need no parsing or decompression for a desktop sprite.
                if(kind==0x49484452 || kind==0x504c5445 || kind==0x74524e53 || kind==0x49444154 || kind==0x49454e44)
                    stream.Write(bytes,offset,length+12);
                else if((bytes[offset+4]&0x20)==0 || kind==0x6163544c)
                { stream.Dispose();throw Bad("请选择标准的单张 PNG 图集。"); }
                if(kind==0x49454e44 && length==0) { stream.Position=0;return stream; }
                offset+=length+12;
            }
            stream.Dispose();throw Bad("PNG 文件不完整或包含过多区块。");
        }

        static byte[] Pixels(byte[] bytes, out int height,bool action=false)
        {
            if (bytes.Length < 33 || bytes.Length > ImageLimit || BitConverter.ToUInt64(bytes,0) != 0x0A1A0A0D474E5089UL ||
                Big32(bytes,8) != 13 || Big32(bytes,12) != 0x49484452)
                throw Bad("角色图集需要使用 PNG 格式。");
            height = Big32(bytes,20);
            int width=action?1152:1536;
            if(action && (Big32(bytes,16)!=width || height!=208))throw Bad("生活动作需要六幅横排姿势，共 1152 × 208 像素。");
            if (!action && (Big32(bytes,16) != width || (height != 1872 && height != 2288)))
                throw Bad("图集需要为 1536 × 1872 或 1536 × 2288 像素。");
            // Rebuild from decoded pixels only. Ancillary PNG metadata is never installed.
            using (var stream = ImageChunks(bytes))
            {
                var decoder = new PngBitmapDecoder(stream,BitmapCreateOptions.IgnoreColorProfile,BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count != 1) throw Bad("请选择单张 PNG 图集。");
                var frame = new FormatConvertedBitmap(decoder.Frames[0],PixelFormats.Bgra32,null,0);
                if (frame.PixelWidth != width || frame.PixelHeight != height) throw Bad("PNG 尺寸不一致。");
                byte[] rgba = new byte[width * 4 * height];
                frame.CopyPixels(rgba,width * 4,0);
                return rgba;
            }
        }

        static string Fingerprint(byte[] pixels)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(pixels)).Replace("-","").ToLowerInvariant();
        }

        static Int32Rect CheckFrames(byte[] pixels,int height)
        {
            int[] required = { 6,8,8,4,5,8,6,6,6,8,8 };
            int left=192,right=-1,top=208,bottom=-1;
            for (int r=0;r<height/208;r++)
                for (int f=0;f<required[r];f++)
                {
                    int visible=0, transparent=0;
                    for (int y=0;y<208;y++)
                    {
                        int offset = ((r * 208 + y) * 1536 + f * 192) * 4 + 3;
                        for (int x=0;x<192;x++,offset+=4)
                        {
                            byte a=pixels[offset]; if (a<6) transparent++;
                            if (a<=15) continue;
                            visible++;
                            if (r==0 && f==0)
                            { left=Math.Min(left,x);right=Math.Max(right,x);top=Math.Min(top,y);bottom=Math.Max(bottom,y); }
                        }
                    }
                    if (visible < 8 || transparent == 0) throw Bad("图集中有空白帧或缺少透明背景。");
                }
            return new Int32Rect(left,top,right-left+1,bottom-top+1);
        }

        static void Encode(string path, BitmapSource frame)
        {
            var enc=new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(frame));
            using (FileStream target=File.Create(path)) enc.Save(target);
        }

        internal static string Import(string zipPath, string root, Func<string,string> matchBuiltin,Func<string,IDictionary<string,byte[]>,bool> sameActions)
        {
            byte[] bytes; Dictionary<string,object> meta;
            var actions=new SortedDictionary<string,byte[]>(StringComparer.Ordinal);
            using (var source=File.OpenRead(zipPath))
            {
                CheckDirectory(source);
                using (var zip=new ZipArchive(source,ZipArchiveMode.Read))
                {
                    if (zip.Entries.Count>64) throw Bad("角色包内的文件过多。");
                    var manifests=zip.Entries.Where(e=>Leaf(EntryPath(e)).Equals("pet.json",StringComparison.OrdinalIgnoreCase)).ToList();
                    if (manifests.Count!=1) throw Bad("每个 ZIP 需要包含一份 pet.json。");
                    string manifestPath=EntryPath(manifests[0]);
                    meta=Manifest(ReadEntry(manifests[0],MetadataLimit));
                    string leaf=Field(meta,"spritesheetPath");
                    if (leaf.StartsWith("./",StringComparison.Ordinal)) leaf=leaf.Substring(2);
                    if (!leaf.EndsWith(".png",StringComparison.OrdinalIgnoreCase) || leaf.Length>160 ||
                        leaf.IndexOfAny(Path.GetInvalidFileNameChars())>=0 || leaf.Contains("/") || leaf.Contains("\\"))
                        throw Bad("pet.json 应指定同一目录下的 PNG 文件名。");
                    string expected=manifestPath.Substring(0,manifestPath.LastIndexOf('/')+1)+leaf;
                    var images=zip.Entries.Where(e=>EntryPath(e).Equals(expected,StringComparison.OrdinalIgnoreCase)).ToList();
                    if (images.Count!=1) throw Bad("找不到角色说明指定的唯一图集。");
                    bytes=ReadEntry(images[0],ImageLimit);
                    string actionBase=manifestPath.Substring(0,manifestPath.LastIndexOf('/')+1)+"actions/";
                    foreach(string part in ActionScore.Names)
                    {
                        var extras=zip.Entries.Where(e=>EntryPath(e).Equals(actionBase+part+".png",StringComparison.OrdinalIgnoreCase)).ToList();
                        if(extras.Count>1)throw Bad("同一个生活动作只能有一张图集。");
                        if(extras.Count==0)continue;
                        int actionHeight;
                        byte[] picture=Pixels(ReadEntry(extras[0],2*1024*1024),out actionHeight,true);
                        for(int cell=0;cell<6;cell++)
                        {
                            int solid=0,clear=0;
                            for(int y=0;y<208;y++)for(int x=0;x<192;x++)
                            {
                                byte alpha=picture[(y*1152+cell*192+x)*4+3];
                                if(alpha>16)solid++;else clear++;
                            }
                            if(solid<128 || clear<128)throw Bad(part+" 动作含有空白帧或没有透明背景。");
                        }
                        actions.Add(part,picture);
                    }
                }
            }
            string name=CleanText(Field(meta,"displayName"),32);
            if (name.Length==0) throw Bad("pet.json 需要填写 displayName。");
            string description=CleanText(Field(meta,"description"),220);
            int height; byte[] pixels=Pixels(bytes,out height);
            string hash=Fingerprint(pixels), builtin=matchBuiltin(hash);
            if (builtin!=null && (actions.Count==0 || sameActions(builtin,actions))) return builtin;
            if(actions.Count>0)
                hash=Fingerprint(Encoding.UTF8.GetBytes(hash+String.Join("",actions.Select(a=>a.Key+Fingerprint(a.Value)))));
            // Exact pixel signatures of the pre-1.6 classic Steve atlas and its
            // complete official action pack. Edited drawings keep their own ID.
            if(hash=="9522fd4640276df8e660655e741332af0aa69673027fa4c6a94b903de6188564" ||
               hash=="da7cb07e35a372befbc7c2b33ee935d667041fba398b534737e45cc72c225715")return "steve";
            string id="custom-"+hash;
            Directory.CreateDirectory(root);
            string target=Path.Combine(root,id);
            if (InspectInstalled(target)!=null) return id;
            if (!Directory.Exists(target) && Directory.EnumerateDirectories(root,"custom-*").Take(MaxPets).Count()>=MaxPets)
                throw Bad("角色册已收录 64 位伙伴。");
            Int32Rect crop=CheckFrames(pixels,height);
            string temp=Path.Combine(root,".import-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            var files=new List<string>{"atlas.png","portrait.png","pet.json"};
            try
            {
                var image=BitmapSource.Create(1536,height,96,96,PixelFormats.Bgra32,null,pixels,1536*4);
                Encode(Path.Combine(temp,files[0]),image);
                Encode(Path.Combine(temp,files[1]),new CroppedBitmap(image,crop));
                string json=Json().Serialize(new Dictionary<string,string> { {"displayName",name},{"description",description} });
                File.WriteAllText(Path.Combine(temp,files[2]),json,new UTF8Encoding(false));
                foreach(var part in actions)
                {
                    string filename=part.Key+".png";files.Add(filename);
                    var imagePart=BitmapSource.Create(1152,208,96,96,PixelFormats.Bgra32,null,part.Value,1152*4);
                    Encode(Path.Combine(temp,filename),imagePart);
                }
                if (!Directory.Exists(target)) Directory.Move(temp,target);
                else foreach (string file in files)
                {
                    string old=Path.Combine(target,file), fresh=Path.Combine(temp,file);
                    if (File.Exists(old)) File.Replace(fresh,old,null,true); else File.Move(fresh,old);
                }
                return id;
            }
            finally
            {
                if (Directory.Exists(temp))
                {
                    foreach (string file in files) File.Delete(Path.Combine(temp,file));
                    Directory.Delete(temp);
                }
            }
        }

        internal static string HashFile(string path)
        {
            int height; return Fingerprint(Pixels(File.ReadAllBytes(path),out height));
        }
        internal static bool SameActionPixels(string file,byte[] pixels)
        {
            if(!File.Exists(file))return false;
            int height;return Pixels(File.ReadAllBytes(file),out height,true).SequenceEqual(pixels);
        }
    }

    public partial class Studio
    {
        readonly object companionLock=new object();
        readonly object installLock=new object();
        readonly Dictionary<string,string> builtinHashes=new Dictionary<string,string>();
        List<CompanionInfo> installed;
        string CompanionFolder { get { return Path.Combine(folder,"companions"); } }

        internal List<CompanionInfo> Companions()
        {
            lock(companionLock)
            {
                if (installed==null)
                {
                    var items=new List<CompanionInfo>();
                    if (Directory.Exists(CompanionFolder)) foreach (string dir in Directory.EnumerateDirectories(CompanionFolder,"custom-*").Take(CompanionArchive.MaxPets))
                    { var item=CompanionArchive.InspectInstalled(dir);if(item!=null)items.Add(item); }
                    installed=items.OrderBy(c=>c.Name,StringComparer.CurrentCulture).ToList();
                }
                return installed.Where(c=>File.Exists(Path.Combine(CompanionFolder,c.Id,"atlas.png")) &&
                    File.Exists(Path.Combine(CompanionFolder,c.Id,"portrait.png"))).ToList();
            }
        }

        public string CustomName(string id)
        {
            if (!CompanionArchive.IsId(id)) return null;
            var item=Companions().FirstOrDefault(c=>c.Id==id);
            return item==null?null:item.Name;
        }

        public string LookupCustomAsset(string filename)
        {
            if (filename==null) return null;
            string suffix=filename.EndsWith("-still.png",StringComparison.Ordinal)?"-still.png":".png";
            if (!filename.EndsWith(suffix,StringComparison.Ordinal)) return null;
            string id=filename.Substring(0,filename.Length-suffix.Length);
            if (!CompanionArchive.IsId(id)) return null;
            string path=Path.Combine(CompanionFolder,id,suffix=="-still.png"?"portrait.png":"atlas.png");
            return File.Exists(path)?path:null;
        }

        public void VerifyCompanion()
        {
            if(Data.Character=="kuroneko" && (!File.Exists(Asset("kuroneko.png")) || !File.Exists(Asset("kuroneko-still.png")))){Data.Character="rem";Save();return;}
            if(CompanionArchive.IsBuiltin(Data.Character))return;
            if(CustomName(Data.Character)==null || !File.Exists(LookupCustomAsset(Data.Character+".png")) ||
               !File.Exists(LookupCustomAsset(Data.Character+"-still.png")))
            { Data.Character="rem"; Save(); }
        }

        string MatchBuiltin(string hash)
        {
            foreach (string id in CompanionArchive.BuiltinIds)
            {
                if(!File.Exists(Asset(id+".png")))continue;
                string fingerprint;
                if (!builtinHashes.TryGetValue(id,out fingerprint))
                { fingerprint=CompanionArchive.HashFile(Asset(id+".png")); builtinHashes.Add(id,fingerprint); }
                if (fingerprint==hash) return id;
            }
            return null;
        }

        public string ImportCompanion(string path)
        {
            lock(installLock)
            {
                string id=CompanionArchive.Import(path,CompanionFolder,MatchBuiltin,
                    (builtIn,poses)=>poses.All(a=>CompanionArchive.SameActionPixels(Asset("motion-"+builtIn+"-"+a.Key+".png"),a.Value)));
                lock(companionLock) installed=null;
                return id;
            }
        }
    }

    public partial class StudioWindow
    {
        bool importingCompanion;
        public UIElement ImportedCompanionCard()
        {
            var panel=new StackPanel();
            var import=Ui.Button(importingCompanion?"正在读取角色包…":"导入角色 ZIP",ChooseCompanionFile);
            import.IsEnabled=!importingCompanion;import.HorizontalAlignment=HorizontalAlignment.Left;
            panel.Children.Add(import);
            panel.Children.Add(Ui.Text("角色包需包含 pet.json 和透明 PNG 动作图集。",12,Ui.Muted));
            foreach(var entry in app.Companions())
            {
                string id=entry.Id;
                var name=Ui.Heading(entry.Name,15);
                var labels=Ui.Stack(name,Ui.Text(entry.Description,12,Ui.Muted));
                var row=new DockPanel { Margin=new Thickness(0,8,0,0) };
                var face=Ui.Picture(app.LookupCustomAsset(id+"-still.png"),44,54);face.Margin=new Thickness(0,0,10,0);
                DockPanel.SetDock(face,Dock.Left);row.Children.Add(face);
                var button=Ui.Button(app.Data.Character==id?"当前伙伴":"选择",delegate { SelectCompanion(id); });
                button.IsEnabled=app.Data.Character!=id;button.VerticalAlignment=VerticalAlignment.Center;
                DockPanel.SetDock(button,Dock.Right);row.Children.Add(button);row.Children.Add(labels);
                panel.Children.Add(row);
            }
            return panel;
        }

        void SelectCompanion(string id)
        {
            app.Data.Character=id;app.VerifyCompanion();app.Save();app.Pet.Sync();app.Pet.Reveal();
            RefreshPage();app.Interact("wave");
        }

        async void ChooseCompanionFile()
        {
            if(importingCompanion) return;
            var dialog=new Microsoft.Win32.OpenFileDialog { Title="导入桌宠角色包", Filter="桌宠 ZIP 文件|*.zip",CheckFileExists=true };
            if(dialog.ShowDialog(this)!=true)return;
            string path=dialog.FileName;
            importingCompanion=true; RefreshPage();
            string id=null, error=null;
            try { id=await Task.Run(()=>app.ImportCompanion(path)); }
            catch(Exception ex) { error=ex.GetBaseException().Message; }
            importingCompanion=false;
            if(app.Exiting)return;
            if(error!=null) { RefreshPage();MessageBox.Show(this,error,"无法导入角色包",MessageBoxButton.OK,MessageBoxImage.Information);return; }
            SelectCompanion(id);ShowStatus("已选用"+app.CharacterName+"。");
        }
    }
}
