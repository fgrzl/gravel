using System;
using System.Text;

namespace Gravel.TestHelpers;

public static class TestBytes
{
    public static ReadOnlyMemory<byte> B(string s)
    {
        return Encoding.UTF8.GetBytes(s);
    }
}