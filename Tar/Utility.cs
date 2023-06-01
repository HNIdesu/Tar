namespace HNIdesu.Compression.Tar
{
    internal static class Utility
    {
        public static bool IsEmpty(this byte[] buffer) => buffer.All(b => b == 0);
        
    }

}
