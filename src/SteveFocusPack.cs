using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Threading;

namespace DeskMate
{
    // Optional data only. Pin the 18 complete authored frame banks; this
    // installer never runs code, trusts paths from the ZIP, or touches saves.
    internal sealed class SteveFocusPack
    {
        sealed class Spec
        {
            internal readonly int Bytes;
            internal readonly string Hash;
            internal Spec(int bytes,string hash){Bytes=bytes;Hash=hash;}
        }

        static readonly Dictionary<string,Spec> Files=new Dictionary<string,Spec>(StringComparer.Ordinal) {
            {"fork-check",new Spec(2614419,"a96d68259d59a8d6fa302aedb0e04f45c67d217696399a56cc5efbafc8284e45")},
            {"fork-coal",new Spec(3024518,"270821ea30fbb8ac5197d44d9573324f547392cc91c4e0f236a9a90233c5a2d8")},
            {"fork-emerald",new Spec(3032388,"0cdcff15e8839c27a72e693c28c665ce4e24e99a1bc3e03e5e7c09391f25b476")},
            {"fork-gold",new Spec(3042502,"81133cbc26591de831388134eb124c79d5729a6089e5da48024e2adf09649826")},
            {"fork-iron",new Spec(3029109,"a748ccc18c3329338e6a8982bb4188e6a071aef9c9428f84ee39e8d17d3a3374")},
            {"fork-pass",new Spec(2571026,"35a28b1b6384cd3c1636fc3aac47eb632651b16c9561e0f5c3aa7629d81de83a")},
            {"stairs-check",new Spec(2633008,"238512cc399cff25777267df180c9c7c91d7fab2bc5c97b31e861199803e2489")},
            {"stairs-coal",new Spec(3024561,"57faf19744db1e1297a48773b4e725de321f0e453c5fc78d6f161c2d8bc0a6a0")},
            {"stairs-diamond",new Spec(3042520,"bcf4a95169385bee8312a8d4b9c611f06e99c7609fd07e284b3c602ebee6fb64")},
            {"stairs-iron",new Spec(3031752,"141e49f4df0efc28c268b7da9b527ccf2ee75366c70af37face8c86e4f1c820a")},
            {"stairs-pass",new Spec(2596331,"341edb99a654b440c8085f0b0c5255f947f769045deee4d033726885b6eba960")},
            {"stairs-red",new Spec(3034502,"37a8926dd74f50c2edacaa2770a10f1e30a1d3206eb536aa2051962c97452146")},
            {"torch-check",new Spec(3134632,"6e281ce9026677f0813e056e19cc3f5b98444db3f1781e932884ae0ecca37ecc")},
            {"torch-coal",new Spec(3660186,"9763b461ef301ddfa61c52e1c07d30f65983b0efa351dd086384d9bdbbfc7c1e")},
            {"torch-diamond",new Spec(3688003,"a138bc6e12aa8eb16a4c7d447d79184a29c59c5f97d589fc2e3ae8503469835d")},
            {"torch-lapis",new Spec(3687339,"9f17d285cf22c7f5c741720d47d937baa8ffdb7213b0a1b2bc331fc6d0eeee02")},
            {"torch-pass",new Spec(3089078,"f9a5bda32371f5fd514e9393066b48869ac56cb6697147f6f3484b635178f288")},
            {"torch-red",new Spec(3678424,"64161e0fb726beb0086eca534329e67e73e836f7a05e04a8fd7a90d1f7b46caf")},
        };
        const long MaxArchiveBytes=65L*1024*1024;
        readonly string root,installed;
        readonly object gate=new object();
        int available;
        internal bool Available { get { return Volatile.Read(ref available)!=0; } }

        internal SteveFocusPack(string userFolder)
        {
            root=Path.Combine(userFolder,"expansions");
            installed=Path.Combine(root,"steve-focus-1");
            if(Complete(installed))available=1;
        }

        internal static string FileName(string key) { return "motion-steve-focuscave-"+key+".dca"; }
        internal string Locate(string file){return Path.Combine(installed,file);}
        internal void DisableBroken(){Interlocked.Exchange(ref available,0);}

        static bool Complete(string folder)
        {
            try {
                foreach(var pair in Files)
                {
                    var f=new FileInfo(Path.Combine(folder,FileName(pair.Key)));
                    if(!f.Exists || f.Length!=pair.Value.Bytes)return false;
                }
                return true;
            }catch(IOException){return false;}catch(UnauthorizedAccessException){return false;}
        }

        string UniquePath(string purpose){return Path.Combine(root,".steve-focus-"+purpose+"-"+Guid.NewGuid().ToString("N"));}
        static void Clean(string path)
        {
            if(path==null || !Directory.Exists(path))return;
            try{Directory.Delete(path,true);}catch(IOException){}catch(UnauthorizedAccessException){}
        }

        internal void Install(string archive)
        {
            lock(gate)
            {
                // Zip structure and all payload bytes must match before
                // changing the active pack. Failed installs leave it alone.
                Directory.CreateDirectory(root);
                string incoming=UniquePath("install"), previous=null;
                Directory.CreateDirectory(incoming);
                try
                {
                    using(var input=File.OpenRead(archive))
                    {
                        if(input.Length>MaxArchiveBytes)throw new InvalidDataException("文件过大，不是对应的史蒂夫专注动画包。");
                        using(var zip=new ZipArchive(input,ZipArchiveMode.Read))
                        using(var sha=SHA256.Create())
                        {
                            if(zip.Entries.Count!=Files.Count)throw new InvalidDataException("史蒂夫专注动画包的文件不完整，或选到了普通人物包。");
                            var entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.Ordinal);
                            foreach(var entry in zip.Entries)
                            {
                                if(entries.ContainsKey(entry.FullName))throw new InvalidDataException("动画包里有重复文件。");
                                entries.Add(entry.FullName,entry);
                            }
                            var buffer=new byte[65536];
                            foreach(var pair in Files)
                            {
                                string file=FileName(pair.Key);
                                ZipArchiveEntry entry;
                                if(!entries.TryGetValue(file,out entry) || entry.Length!=pair.Value.Bytes)
                                    throw new InvalidDataException("动画包的素材版本不匹配或文件已损坏。");
                                string output=Path.Combine(incoming,file);
                                using(var source=entry.Open())
                                using(var dest=File.Create(output))
                                {
                                    int remaining=pair.Value.Bytes;
                                    while(remaining>0)
                                    {
                                        int n=source.Read(buffer,0,Math.Min(remaining,buffer.Length));
                                        if(n<=0)throw new InvalidDataException("动画包文件提前结束。");
                                        dest.Write(buffer,0,n);remaining-=n;
                                    }
                                    if(source.ReadByte()!=-1)throw new InvalidDataException("动画包文件大小不正确。");
                                }
                                using(var saved=File.OpenRead(output))
                                {
                                    string fingerprint=BitConverter.ToString(sha.ComputeHash(saved)).Replace("-","").ToLowerInvariant();
                                    if(fingerprint!=pair.Value.Hash)throw new InvalidDataException("动画包校验失败，请重新下载。");
                                }
                            }
                        }
                    }
                    if(Directory.Exists(installed))
                    {
                        previous=UniquePath("previous");
                        Directory.Move(installed,previous);
                    }
                    try { Directory.Move(incoming,installed); }
                    catch
                    {
                        if(previous!=null)
                        {
                            Directory.Move(previous,installed);
                            previous=null;
                        }
                        throw;
                    }
                    Interlocked.Exchange(ref available,1);
                    Clean(previous);
                }
                finally { Clean(incoming); }
            }
        }

        internal void Uninstall()
        {
            lock(gate)
            {
                if(!Directory.Exists(installed)){DisableBroken();return;}
                string staged=UniquePath("removal");
                Directory.Move(installed,staged);
                DisableBroken();
                Clean(staged);
            }
        }
    }
}
