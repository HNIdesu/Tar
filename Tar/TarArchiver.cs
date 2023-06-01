using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace HNIdesu.Compression.Tar
{

    public class TarArchiver
    {
        
        private string FileName;
        private ICollection<TarEntry> TarEntryList { get; set; } = new LinkedList<TarEntry>();

        [Flags]
        public enum FileMode
        {
            [Description("TUREAD")]
            TUREAD=400,        /* 拥有者可读 */
            [Description("TUWRITE")]
            TUWRITE =200,        /* 拥有者可写 */
            [Description("TUEXEC")]
            TUEXEC =100,        /* 拥有者可执行/搜索 */
            [Description("TGREAD")]
            TGREAD =40,       /* 同组用户可读 */
            [Description("TGWRITE")]
            TGWRITE =20,      /* 同组用户可写 */
            [Description("TGEXEC")]
            TGEXEC =10,     /* 同组用户可执行/搜索 */
            [Description("TOREAD")]
            TOREAD =4,    /* 其他用户可读 */
            [Description("TOWRITE")]
            TOWRITE=2,   /* 其他用户可写 */
            [Description("TOEXEC")]
            TOEXEC =1  /* 其他用户可执行/搜索 */
        }
        [StructLayout(LayoutKind.Sequential,Pack =1)]
        private struct TarFileHeader
        {
            
            [MarshalAs(UnmanagedType.ByValArray,SizeConst =100)]
            private byte[] mFileName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            private byte[] mPrivilege;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            private byte[] mUid;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            private byte[] mGid;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            private byte[] mFileSize;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)]
            private byte[] mLastModifiedTime;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            private byte[] mCheckSum;
            private byte mFileType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 100)]
            private byte[] mLinkTargetName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
            private byte[] mMagic;
            private short mVersion;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            private byte[] mOwnerName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            private byte[] mGroupName;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            private byte[] mDevMajor;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            private byte[] mDevMinor;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 167)]
            private byte[] mExtra;
            public string FileName
            {
                get=> GetString(mFileName);
            }
            public FileMode Privilege
            {
                get => (FileMode)int.Parse(GetString(mPrivilege));
            }

            public string OwnerName
            {
                get => GetString(mOwnerName);
            }
            public string GroupName
            {
                get => GetString(mGroupName);
            }

            public long FileSize
            {
                get => Convert.ToInt64(GetString(mFileSize),8);
                set{
                    byte[] data = new byte[12];
                    Encoding.ASCII.GetBytes(Convert.ToString(value, 8).PadLeft(11,'0')).CopyTo(data,0);
                    mFileSize = data;
                } 
            }
            public int Uid
            {
                get => Convert.ToInt32(GetString(mUid),8);
            }
            public int Gid
            {
                get => Convert.ToInt32(GetString(mGid),8);
            }
            public DateTime LastModifiedTime
            {
                get => DateTime.UnixEpoch.AddSeconds(Convert.ToInt64(GetString(mLastModifiedTime), 8)).AddHours(8);
            }
            public long CheckSum
            {
                get=> Convert.ToInt64(GetString(mCheckSum),8);
                set
                {
                    byte[] data = new byte[12];
                    Encoding.ASCII.GetBytes(Convert.ToString(value, 8).PadLeft(6, '0')).CopyTo(data, 0);
                    data[6] = 0;
                    data[7] = 0x20;
                    mCheckSum = data;
                }
            }
            public bool IsEmpty() => mFileName.IsEmpty();
            public long CalculateCheckSum()
            {
                mCheckSum = new byte[8];
                IntPtr intPtr= Marshal.AllocHGlobal(Marshal.SizeOf<TarFileHeader>());
                Marshal.StructureToPtr(this, intPtr, false);
                long sum = 0;
                byte[] buffer = new byte[512];
                Marshal.Copy(intPtr, buffer, 0, 512);
                Marshal.FreeHGlobal(intPtr);

                // 读取文件内容并计算校验和
                foreach (byte b in buffer)
                    sum += b;
                sum += 0x20 * 8;
                return sum;
            }
        }



        public long SeekEntryDataPosition(string key)
        {
            int x = 0;
            foreach(TarEntry entry in TarEntryList)
            {
                if (entry.Key == key)
                    return x*512;
                x+=entry.ChunkCount;
            }
            return -1;
            
        }

        private static string GetString(byte[] bytes)
        {
            int count = 0;
            while (bytes[count] != 0)
                count++;
            return Encoding.UTF8.GetString(new ReadOnlySequence<byte>(bytes, 0, count));
        }

        public static TarArchiver FromFile(string filename)
        {
            if (!File.Exists(filename))
                throw new FileNotFoundException($"Can not find file path \"{filename}\"");
            
            TarArchiver archiver = new TarArchiver();
            archiver.FileName = filename;
            using (var br=new BinaryOperator.BinaryReader(File.OpenRead(filename)))
            {
                while (true)
                {
                    TarFileHeader header = br.ReadMarshal<TarFileHeader>(Marshal.SizeOf<TarFileHeader>());
                    if (header.IsEmpty())
                        break;
                    TarEntry tarEntry = new TarEntry();
                    tarEntry.FileSize = header.FileSize;
                    tarEntry.Key = header.FileName;
                    archiver.TarEntryList.Add(tarEntry);
                    br.BaseStream.Seek((tarEntry.ChunkCount-1) * 512, SeekOrigin.Current);
                }
            }
            return archiver;
        }

        public Stream OpenEntry(TarEntry entry)
        {
            long position = SeekEntryDataPosition(entry.Key)+512;
            MemoryStream ms;
            using (Stream stream = File.OpenRead(FileName))
            {
                stream.Seek(position, SeekOrigin.Begin);
                byte[] buffer = new byte[entry.FileSize];
                stream.Read(buffer);
                ms = new MemoryStream(buffer);
            }

            return ms;
        }

        public void ReplaceEntry(TarEntry entry,Stream data)
        {
            if (FileName == null)
                throw new NotSupportedException();
            var reader =new BinaryOperator.BinaryReader(File.OpenRead(FileName));
            string temppath = Path.Combine(Environment.GetEnvironmentVariable("TEMP"), Path.GetFileName(FileName) + ".temp");
            var writer = new BinaryOperator.BinaryWriter(File.OpenWrite(temppath));
            long position = SeekEntryDataPosition(entry.Key);
            byte[] buffer = new byte[position];
            reader.BaseStream.Read(buffer);
            writer.BaseStream.Write(buffer);//读取目标entry之前的数据并写入到临时文件
            TarFileHeader header = reader.ReadMarshal<TarFileHeader>(Marshal.SizeOf<TarFileHeader>());
            header.FileSize = data.Length;
            header.CheckSum = header.CalculateCheckSum();
            writer.WriteMarshal(header);
            data.CopyTo(writer.BaseStream);
            if (data.Length % 512 != 0)
                writer.BaseStream.Write(new byte[512 - (data.Length% 512)]);
            long skip = (entry.ChunkCount - 1) * 512;
            reader.BaseStream.Seek(skip, SeekOrigin.Current);
            entry.FileSize = data.Length;
            reader.BaseStream.CopyTo(writer.BaseStream);
            reader.Close();
            writer.Close();
            File.Delete(FileName);
            File.Copy(temppath, FileName);
            File.Delete(temppath);
            

            
        }


        public TarArchiver()
        {

        }

        public IEnumerable<TarEntry> EnumerateEntries()
        {
            foreach (TarEntry entry in TarEntryList)
                yield return entry;
        }

    }
}