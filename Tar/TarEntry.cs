

namespace HNIdesu.Compression.Tar
{
    public class TarEntry
    {
    

        public string? Key { get; set; } = null;
        public long FileSize { get; set; }
        public int ChunkCount
        {
            get {
                int x = (int)(FileSize % 512);
                int y = (int)(FileSize / 512);
                return 1+(x == 0 ? y : y + 1);
            }
        }

    }
}
