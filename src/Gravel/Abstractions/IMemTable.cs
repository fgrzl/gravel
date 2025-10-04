namespace Gravel.Abstractions;

public interface IMemTable
{
    void Insert(string key, string value);
    string? Get(string key);
    void Delete(string key);
    void Clear();
}
