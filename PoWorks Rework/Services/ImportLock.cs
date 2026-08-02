namespace PoWorks_Rework.Services
{
    /// <summary>
    /// Thread synchronization utility to prevent concurrent data import operations.
    /// Ensures only one import process runs at a time using a semaphore.
    /// </summary>
    public static class ImportLock
    {
        /// <summary>
        /// Semaphore that acts as a gate to control concurrent access to import operations.
        /// Initialized with 1 permit to allow only one importer at a time.
        /// </summary>
        public static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
    }
}