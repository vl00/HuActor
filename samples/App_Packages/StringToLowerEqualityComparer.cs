using System;
using System.Collections.Generic;
using System.Text;

namespace Common
{
    public class StringToLowerEqualityComparer : IEqualityComparer<string>
    {
        bool IEqualityComparer<string>.Equals(string x, string y)
        {
            return string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
        }

        int IEqualityComparer<string>.GetHashCode(string obj)
        {
            return obj.ToLower().GetHashCode();
        }
    }
}
