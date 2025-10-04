namespace Gravel.Abstractions;

public interface IGravelFactory
{
    IGravelDb Open(string path);
}
