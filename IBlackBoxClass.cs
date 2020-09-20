using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace IBlackBox
{
    public interface IBlackBox
    {
        event Action<object, string> Output1;
        event Action<object, string> Output2;
        void output1(string data);
        void output2(string data);
        void Input1(string data);
        void Input2(string data);
    }
}
