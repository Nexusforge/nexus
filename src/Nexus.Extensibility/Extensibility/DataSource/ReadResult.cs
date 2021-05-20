namespace Nexus.Extensibility
{
    public record ReadResult<T>(T[] Dataset, byte[] Status);
}
