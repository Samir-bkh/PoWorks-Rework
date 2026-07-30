namespace PoWorks_Rework.Services
{
    public static class ImportLock
    {
        public static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
    }
}