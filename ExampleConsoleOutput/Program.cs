using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ExampleConsoleOutput
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("hi");
            Console.WriteLine("hi2");
            Console.Error.WriteLine("error");
            throw new Exception("crash");
        }
    }
}
