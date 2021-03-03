using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SimplifySvg
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                var svg = new Svg(args[0]);
                svg.Simplify();
                svg.WriteTo(args[1]);
            }
            catch (Exception err)
            {
                Console.WriteLine(err.ToString());
            }

            if (System.Diagnostics.Debugger.IsAttached)
            {
                Console.WriteLine("Press any key to exit.");
                Console.ReadKey();
            }
        }
    }
}
